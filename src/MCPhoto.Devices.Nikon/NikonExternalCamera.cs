using MCPhoto.Core.Devices;
using MCPhoto.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MCPhoto.Devices.Nikon;

/// <summary>
/// Nikon 외부 카메라 어댑터(오케스트레이션). (it23 §3.4)
/// <para>
/// <b>SDK 타입이 등장하지 않는다</b> — <see cref="INikonSdkShim"/>만 호출한다. 대신 SDK 유무와 무관하게
/// 지금 검증 가능한 것을 전부 담는다: 연결 상태머신·단일 비행·타임아웃·저장 노출값 재적용·
/// 탈락 이벤트 중계·Shutdown 보장. 그래서 SDK가 도착했을 때 "고칠 파일 1개"가
/// "다시 읽고 이해할 파일 1개"가 되지 않는다.
/// </para>
/// <para>
/// DI Singleton으로 등록한다: 물리 장치는 1대이고, SDK 모듈 수명(Shutdown 필요)이 앱 수명과 일치해야 한다.
/// 웹캠 <c>ICameraService</c> Singleton 제약과 동형이다.
/// </para>
/// 크래시 금지 관례: 모든 실패는 예외가 아니라 false/null이다.
/// </summary>
/// <remarks>
/// ⚠️ <see cref="IDisposable"/>을 <see cref="IAsyncDisposable"/>과 <b>함께</b> 구현한다.
/// <c>App.OnExit</c>는 동기 메서드이므로 컨테이너 정리는 <c>ServiceProvider.Dispose()</c>(동기)로 일어나는데,
/// IAsyncDisposable만 구현한 싱글턴이 있으면 그 호출이 InvalidOperationException을 던진다.
/// 동기 경로를 제공해 그 함정을 막되, <b>UI 스레드를 블로킹하지 않는다</b>
/// (동기 Dispose는 shim의 동기 해제만 호출하고 async를 기다리지 않는다).
/// </remarks>
public sealed class NikonExternalCamera : IExternalCamera, IAsyncDisposable, IDisposable
{
    /// <summary>연결 상태머신. Lost는 "연결됐다가 탈락"으로, Disconnected와 사유 문구가 다르다.</summary>
    private enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Lost
    }

    private readonly INikonSdkShim _shim;
    private readonly ISettingsService _settings;
    private readonly SdkRuntimeProbe _probe;
    private readonly ILogger? _logger;

    /// <summary>상태·캐시·단일 비행 필드 보호. 짧은 임계구역만(내부에서 await 하지 않는다).</summary>
    private readonly object _gate = new();

    private ConnectionState _state = ConnectionState.Disconnected;
    private Task<bool>? _connectInFlight;
    private string? _modelName;
    private string? _unavailableReason;
    private ExternalCameraCapabilities? _capabilities;
    private ExposureDomain? _exposureDomain;

    /// <summary>캡처 단일 비행 플래그(0=유휴, 1=진행 중). Interlocked로만 만진다.</summary>
    private int _capturing;

    private bool _disposed;

    public NikonExternalCamera(
        INikonSdkShim shim,
        ISettingsService settings,
        ILogger? logger = null,
        SdkRuntimeProbe? probe = null)
    {
        _shim = shim;
        _settings = settings;
        _probe = probe ?? new SdkRuntimeProbe();
        _logger = logger;

        // 어댑터는 Singleton(앱 수명)이므로 이 구독은 실질 누수가 아니지만, 해제 경로는 반드시 둔다
        // (DisposeAsync) — "수명이 같으니 안 떼도 된다"는 예외를 만들면 다음 사람이 그 예외를 확장한다.
        _shim.DeviceLost += OnDeviceLost;
    }

    public bool IsAvailable
    {
        get { lock (_gate) return _state == ConnectionState.Connected; }
    }

    public string? ModelName
    {
        get { lock (_gate) return _state == ConnectionState.Connected ? _modelName : null; }
    }

    public string? UnavailableReason
    {
        get { lock (_gate) return _state == ConnectionState.Connected ? null : _unavailableReason; }
    }

    public event EventHandler<ExternalCameraConnectionChange>? ConnectionChanged;

    /// <summary>
    /// 로컬 전제 검사(it24 §5.1 ⓐⓑⓒ). <b>USB·SDK를 접촉하지 않는다</b> — shim 플래그 조회 + 파일 존재 검사뿐이다.
    /// <list type="number">
    /// <item>ⓐ shim이 부재 구현이면 <c>(false, W10)</c> — <b>파일이 있어도</b> false다. 부재 shim으로는
    ///       모듈을 열 수 없으니, 그 실패를 장치 부재의 근거로 쓸 수 없다(it24 R1).</item>
    /// <item>ⓑ 런타임 파일이 없으면 <c>(false, W11)</c> — 사유에 파일 경로가 들어가 그것이 곧 조치 안내다.</item>
    /// <item>ⓒ 둘 다 통과하면 <c>(true, null)</c> — 이때부터 연결 실패를 "찾지 못했다"로 말할 자격이 생긴다.</item>
    /// </list>
    /// 사유 문구는 <see cref="NikonCameraReasons"/> 상수만 쓴다(같은 원인이 화면마다 다르게 설명되는 것을 막는다).
    /// </summary>
    public ExternalCameraReadiness CheckReadiness()
    {
        if (!_shim.IsOperational)
            return new ExternalCameraReadiness(false, NikonCameraReasons.SdkMissing);

        var model = ExternalCameraModels.Resolve(_settings.Current.ExternalCameraModel);
        var (fileOk, fileReason) = _probe.Probe(model);   // 예외를 던지지 않는다(부재 취급 — it24 E20)
        if (!fileOk)
            return new ExternalCameraReadiness(false, fileReason ?? NikonCameraReasons.SdkMissing);

        return new ExternalCameraReadiness(true, null);
    }

    // ── 연결 ──

    /// <summary>
    /// 연결 시도. 이미 연결돼 있으면 즉시 true, 진행 중이면 <b>같은 Task를 공유</b>한다(단일 비행).
    /// <para>
    /// 단일 비행이 필요한 이유: 촬영 진입과 컷 실패 재연결이 겹칠 수 있고, 모듈 로드를 두 번 시도하면
    /// 네이티브 초기화가 중복돼 무엇이 실패했는지조차 알 수 없게 된다(it20에서 얻은 교훈과 같은 형태).
    /// </para>
    /// </summary>
    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(false);
            if (_state == ConnectionState.Connected) return Task.FromResult(true);

            // ??= 이므로 진행 중 Task가 있으면 그것을 그대로 돌려준다(shim.OpenAsync 1회 보장).
            _connectInFlight ??= RunConnectAsync(ct);
            return _connectInFlight;
        }
    }

    private async Task<bool> RunConnectAsync(CancellationToken ct)
    {
        // ⚠️ 첫 await로 즉시 양보한다. 이 호출은 ConnectAsync의 lock 안에서 시작되므로, 동기적으로
        //    끝까지 달리면 아래 finally가 아직 대입되지 않은 _connectInFlight를 null로 지우고
        //    직후에 완료된 Task가 필드에 남아 "영구 진행 중" 상태가 된다.
        await Task.Yield();

        lock (_gate)
        {
            _state = ConnectionState.Connecting;
            // 재연결이므로 이전 세션의 캐시는 버린다(다른 바디·다른 모드일 수 있다).
            _capabilities = null;
            _exposureDomain = null;
            _unavailableReason = null;
        }

        try
        {
            var model = ExternalCameraModels.Resolve(_settings.Current.ExternalCameraModel);
            lock (_gate) _modelName = model.DisplayName;

            // ① 파일 프로브 선행 — 없으면 shim을 호출하지 않는다.
            var (fileOk, fileReason) = _probe.Probe(model);
            if (!fileOk)
            {
                Fail(fileReason ?? NikonCameraReasons.SdkMissing);
                _logger?.LogInformation("외부 카메라 연결 강등: {Reason}", fileReason);
                return false;
            }

            // ② 모듈 로드 + 장치 대기(ConnectTimeout).
            var (openOk, openReason) = await OpenWithTimeoutAsync(_probe.Md3Path(model), ct).ConfigureAwait(false);
            if (!openOk)
            {
                Fail(openReason ?? NikonCameraReasons.NotConnected);
                _logger?.LogInformation("외부 카메라 연결 실패: {Reason}", openReason);
                return false;
            }

            lock (_gate)
            {
                _state = ConnectionState.Connected;
                _unavailableReason = null;
            }

            // ③ capability 프로브 1회(캐시). 매 촬영마다 조회하지 않는다 — SDK 왕복 비용·실패 확률 모두 미지수다.
            var caps = await ProbeCapabilitiesSafeAsync(ct).ConfigureAwait(false);
            lock (_gate) _capabilities = caps;

            // ④ 노출 도메인 조회 + 저장값 재적용(§10.2). 실패는 무음 스킵 — 촬영을 막지 않는다.
            var domain = await ReadExposureDomainSafeAsync(ct).ConfigureAwait(false);
            lock (_gate) _exposureDomain = domain;
            await ApplyStoredExposureAsync(ct).ConfigureAwait(false);

            _logger?.LogInformation("외부 카메라 연결됨: {Model}", model.DisplayName);
            RaiseConnectionChanged(true, null);
            return true;
        }
        catch (OperationCanceledException)
        {
            // 호출측 취소(화면 이탈 등) — 실패로 취급하되 사유는 "미연결"로 통일한다.
            Fail(NikonCameraReasons.NotConnected);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "외부 카메라 연결 중 예외(강등)");
            Fail(NikonCameraReasons.NotConnected);
            return false;
        }
        finally
        {
            lock (_gate) _connectInFlight = null;
        }
    }

    private async Task<(bool ok, string? reason)> OpenWithTimeoutAsync(string md3Path, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ExternalCapturePolicy.ConnectTimeout);
        try
        {
            return await _shim.OpenAsync(md3Path, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 타임아웃(호출측 취소가 아님) → 미연결로 강등. 예외를 위로 던지지 않는다.
            _logger?.LogWarning("외부 카메라 연결 타임아웃({Seconds}s)", ExternalCapturePolicy.ConnectTimeout.TotalSeconds);
            return (false, NikonCameraReasons.NotConnected);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SDK 모듈 로드 실패");
            return (false, NikonCameraReasons.SdkMissing);
        }
    }

    /// <summary>상태를 미연결로 되돌리고 사유를 확정(연결 실패 공통 처리).</summary>
    private void Fail(string reason)
    {
        lock (_gate)
        {
            _state = ConnectionState.Disconnected;
            _unavailableReason = reason;
            _capabilities = null;
            _exposureDomain = null;
        }
    }

    // ── 캡처 ──

    /// <summary>
    /// 스틸 1컷 캡처. 미연결·타임아웃·수신 실패는 모두 null이다(호출측이 재시도 1회 → 웹캠 강등을 판단).
    /// <para>
    /// 진행 중 재진입은 즉시 null이다(§6.2 단일 비행): 셔터가 겹치면 어느 바이트가 어느 컷인지
    /// 판정할 방법이 없다. 촬영 화면과 테스트 모달이 같은 Singleton을 공유하므로 방어가 필요하다.
    /// </para>
    /// </summary>
    public async Task<byte[]?> CaptureAsync(CancellationToken ct = default)
    {
        if (!IsAvailable) return null;

        if (Interlocked.CompareExchange(ref _capturing, 1, 0) != 0)
        {
            _logger?.LogWarning("외부 카메라 캡처 재진입 — 무시(진행 중인 수신이 있다)");
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ExternalCapturePolicy.CaptureTimeout);
            try
            {
                var bytes = await _shim.CaptureImageAsync(cts.Token).ConfigureAwait(false);
                if (bytes is null || bytes.Length == 0)
                {
                    _logger?.LogWarning("외부 카메라 캡처 실패(빈 수신)");
                    return null;
                }
                return bytes;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 수신 타임아웃(§11 E6). 예외 대신 null — 컷 루프가 재시도·강등을 결정한다.
                _logger?.LogWarning("외부 카메라 수신 타임아웃({Seconds}s)", ExternalCapturePolicy.CaptureTimeout.TotalSeconds);
                return null;
            }
            catch (OperationCanceledException)
            {
                // 호출측 취소(화면 이탈)는 그대로 전파한다 — 웹캠 CaptureStillAsync와 동형이라
                // 컷 루프의 기존 취소 처리가 그대로 적용된다.
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "외부 카메라 캡처 중 예외(컷 실패로 강등)");
                return null;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _capturing, 0);
        }
    }

    // ── capability · 노출 ──

    public Task<ExternalCameraCapabilities?> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            // 미연결이면 null(= "아직 물어볼 대상이 없다"). 연결됐지만 프로브가 실패했다면
            // 전 항목 Unknown이 캐시돼 있어 게이트는 닫히고 사유는 "확인 불가"가 된다(§4.1).
            return Task.FromResult(_state == ConnectionState.Connected ? _capabilities : null);
        }
    }

    public Task<ExposureDomain?> GetExposureDomainAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_state == ConnectionState.Connected ? _exposureDomain : null);
        }
    }

    /// <summary>
    /// 노출값 적용. 도메인을 알고 있으면 <b>정확 일치만</b> 통과시키고(근사 매칭 금지), 모르면 shim에 맡긴다.
    /// 반환 false는 "카메라 현재값 유지"이며 촬영을 막지 않는다(§11 E9).
    /// </summary>
    public async Task<bool> SetExposureAsync(ExposureParameter parameter, string value, CancellationToken ct = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(value)) return false;

        ExposureDomainEntry? entry;
        CapabilityState exposureState;
        lock (_gate)
        {
            entry = _exposureDomain?[parameter];
            exposureState = _capabilities?.ExposureControl ?? CapabilityState.Unknown;
        }

        // capability가 확정적으로 미지원이면 왕복하지 않는다(Unknown은 시도해 본다 — 프로브만 실패했을 수 있다).
        if (exposureState == CapabilityState.Unsupported) return false;

        // 도메인을 아는데 값이 목록에 없으면 shim을 부르지 않는다 — 몰래 근사값이 적용되는 것을 원천 차단.
        if (entry is not null && entry.IndexOf(value) < 0)
        {
            _logger?.LogInformation("노출 적용 스킵({Parameter}={Value}): 카메라 도메인에 없는 값", parameter, value);
            return false;
        }

        try
        {
            var ok = await _shim.WriteExposureAsync(parameter, value.Trim(), ct).ConfigureAwait(false);
            if (ok) await RefreshExposureDomainAsync(ct).ConfigureAwait(false);
            return ok;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "노출 적용 중 예외({Parameter}={Value})", parameter, value);
            return false;
        }
    }

    /// <summary>
    /// 물리 플래시 발광 모드 시도. capability가 Supported가 아니면 <b>shim을 부르지 않고</b> false.
    /// 현재 프로덕션에서는 caps가 null(MissingShim)이라 이 게이트가 항상 닫혀 있다 — 화면 플래시가 유일 활성 경로다.
    /// </summary>
    public async Task<bool> TrySetPhysicalFlashAsync(bool enabled, CancellationToken ct = default)
    {
        if (!IsAvailable) return false;

        CapabilityState flashState;
        lock (_gate) flashState = _capabilities?.PhysicalFlash ?? CapabilityState.Unknown;
        if (!ExternalCapturePolicy.IsOpen(flashState)) return false;

        try
        {
            return await _shim.WritePhysicalFlashAsync(enabled, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "물리 플래시 설정 중 예외(무시 — 화면 플래시는 이미 동작)");
            return false;
        }
    }

    private async Task<ExternalCameraCapabilities?> ProbeCapabilitiesSafeAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ExternalCapturePolicy.ConnectTimeout);
            // shim이 null을 주면(조회 실패) 전 항목 Unknown으로 승격 — 게이트는 닫히지만
            // "확인 못 함"과 "미지원"이 화면에서 구분된다(§11 E10).
            return await _shim.ProbeCapabilitiesAsync(cts.Token).ConfigureAwait(false)
                   ?? ExternalCameraCapabilities.AllUnknown;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "capability 프로브 실패 — 전 항목 Unknown으로 처리");
            return ExternalCameraCapabilities.AllUnknown;
        }
    }

    private async Task<ExposureDomain?> ReadExposureDomainSafeAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ExternalCapturePolicy.ConnectTimeout);
            return await _shim.ReadExposureDomainAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "노출 도메인 조회 실패(슬라이더 비활성으로 강등)");
            return null;
        }
    }

    private async Task RefreshExposureDomainAsync(CancellationToken ct)
    {
        var domain = await ReadExposureDomainSafeAsync(ct).ConfigureAwait(false);
        lock (_gate) _exposureDomain = domain;
    }

    /// <summary>
    /// 연결 직후 ini 저장 노출값 3종을 재적용(§10.2). 부스 조명은 고정적이므로 운영자가 맞춘 노출이
    /// 재시작 후에도 유지되어야 한다. 빈 값은 "미지정"이라 건너뛰고, 도메인 불일치는 무음 스킵 + 로그다.
    /// 컷마다 재적용하지 않는다 — 셔터 직전 SDK 왕복은 수신 지연만 늘린다.
    /// </summary>
    private async Task ApplyStoredExposureAsync(CancellationToken ct)
    {
        var s = _settings.Current;
        var wanted = new (ExposureParameter Parameter, string Value)[]
        {
            (ExposureParameter.ShutterSpeed, s.ExternalShutterSpeed),
            (ExposureParameter.Aperture, s.ExternalAperture),
            (ExposureParameter.Iso, s.ExternalIso),
        };

        foreach (var (parameter, value) in wanted)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;   // 미지정 → 카메라 현재값 유지
            var ok = await SetExposureAsync(parameter, value, ct).ConfigureAwait(false);
            if (!ok)
                _logger?.LogInformation("저장 노출값 미적용({Parameter}={Value}) — 카메라 현재값 유지", parameter, value);
        }
    }

    // ── 탈락 이벤트 중계 ──

    /// <summary>
    /// shim 탈락 통지 → <see cref="ConnectionChanged"/> 재발행 + 사용 불가 확정(§11 E5).
    /// ⚠️ 임의 스레드에서 호출된다 — 마샬링하지 않는다(계약이 그렇게 못박혀 있고, UI 마샬링은 VM의 책임이다).
    /// </summary>
    private void OnDeviceLost(string? reason)
    {
        bool wasConnected;
        lock (_gate)
        {
            wasConnected = _state == ConnectionState.Connected;
            _state = ConnectionState.Lost;
            _unavailableReason = reason ?? NikonCameraReasons.NotConnected;
            _capabilities = null;
            _exposureDomain = null;
        }

        _logger?.LogWarning("외부 카메라 탈락: {Reason}", reason ?? "(사유 없음)");
        // 연결 전 탈락 통지는 상태 변화가 아니므로 재발행하지 않는다(중복 배너 방지).
        if (wasConnected)
            RaiseConnectionChanged(false, reason ?? NikonCameraReasons.NotConnected);
    }

    private void RaiseConnectionChanged(bool connected, string? reason)
    {
        try
        {
            ConnectionChanged?.Invoke(this, new ExternalCameraConnectionChange(connected, reason));
        }
        catch (Exception ex)
        {
            // 구독자(VM) 예외가 장치 계층을 죽이지 않게 격리. 임의 스레드에서 올라오면 잡을 곳이 없다.
            _logger?.LogWarning(ex, "ConnectionChanged 구독자 예외(무시)");
        }
    }

    // ── 해제 ──

    /// <summary>
    /// 연결 해제. <b>재연결 가능한 상태</b>로 되돌린다(shim은 Close만 — Dispose는 앱 종료 1회).
    /// 테스트 모달 닫기·세션 종료가 이 경로를 타므로, 여기서 Dispose를 부르면 다음 세션이 영구 강등된다.
    /// </summary>
    public async Task DisconnectAsync()
    {
        bool needsClose;
        lock (_gate)
        {
            needsClose = _state is ConnectionState.Connected or ConnectionState.Lost or ConnectionState.Connecting;
            _state = ConnectionState.Disconnected;
            _capabilities = null;
            _exposureDomain = null;
        }

        if (!needsClose) return;

        try { await _shim.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _logger?.LogWarning(ex, "외부 카메라 해제 중 예외(무시)"); }
    }

    /// <summary>
    /// 앱 종료 시 1회. shim <c>DisposeAsync</c>까지 반드시 통과시킨다 —
    /// 벤더 SDK는 Shutdown 미호출 시 드라이버가 불안정해진다는 경고가 있다(설계 §1.3).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!MarkDisposed()) return;

        try { await _shim.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger?.LogWarning(ex, "SDK shim 해제 중 예외(무시)"); }
    }

    /// <summary>
    /// 동기 해제(컨테이너 종료 경로 — <c>ServiceProvider.Dispose()</c>).
    /// shim의 <b>동기</b> 해제만 호출한다 — 여기서 async를 기다리면 UI 스레드가 종료 시점에 멈춘다.
    /// shim이 동기 경로를 제공하지 않으면 구독 해제까지만 하고 넘어간다(프로세스가 곧 끝난다).
    /// </summary>
    public void Dispose()
    {
        if (!MarkDisposed()) return;

        try { (_shim as IDisposable)?.Dispose(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "SDK shim 동기 해제 중 예외(무시)"); }
    }

    /// <summary>해제 진입 1회 보장 + 구독 해제(생성자 구독의 대칭). 이미 해제됐으면 false.</summary>
    private bool MarkDisposed()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            _disposed = true;
            _state = ConnectionState.Disconnected;
        }

        _shim.DeviceLost -= OnDeviceLost;
        return true;
    }
}
