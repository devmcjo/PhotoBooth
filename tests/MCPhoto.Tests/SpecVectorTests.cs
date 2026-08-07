using System.Globalization;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.Json;
using MCPhoto.Core.Capture;
using MCPhoto.Core.Frames;
using MCPhoto.Core.Models;
using MCPhoto.Core.Settings;
using MCPhoto.Core.Upload;

namespace MCPhoto.Tests;

/// <summary>
/// 공유 테스트 벡터 교차 검증 — `docs/spec-vectors/*.json`을 **웹 클라이언트와 같은 파일로** 읽어
/// Windows 구현이 같은 결과를 내는지 확인한다. (docs/web-client/10 §3, 11-wbs Step 2)
///
/// <para>
/// 왜 필요한가: 웹(TypeScript)은 정수 나눗셈이 없고 `Math.round`가 half-up이다. 규격의 `round`는
/// C# `Math.Round`(은행가 반올림)이라 그대로 옮기면 슬롯 위치가 1px씩 어긋난다. 이 테스트가
/// **양쪽이 같은 파일을 읽어 동시에 실패하는 장치**다 — 규격을 바꿀 때는 벡터 파일을 먼저 고친다.
/// </para>
/// <para>
/// 불일치 시 진실원은 **이 프로젝트(C#)**다 → 웹 구현을 고친다(10 §3.3).
/// </para>
/// </summary>
public class SpecVectorTests
{
    private const double Tolerance = 1e-9;

    private static readonly string VectorDir = FindVectorDir();

    /// <summary>테스트 출력 폴더에서 위로 올라가며 `docs/spec-vectors`를 찾는다(빌드 구성 무의존).</summary>
    private static string FindVectorDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "spec-vectors");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "docs/spec-vectors 를 찾을 수 없습니다. `cd webclient && npx vite-node scripts/genVectors.ts` 로 생성하세요.");
    }

    private static JsonElement LoadCases(string name)
    {
        var path = Path.Combine(VectorDir, name + ".json");
        Assert.True(File.Exists(path), $"벡터 파일 없음: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        // JsonDocument는 Dispose 후 접근할 수 없으므로 복제해 돌려준다.
        return doc.RootElement.GetProperty("cases").Clone();
    }

    /// <summary>벡터 파일의 <b>루트</b>를 돌려준다(`cases` 밖의 절도 읽어야 하는 벡터용).</summary>
    private static JsonElement LoadVectorRoot(string name)
    {
        var path = Path.Combine(VectorDir, name + ".json");
        Assert.True(File.Exists(path), $"벡터 파일 없음: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static Slot ReadSlot(JsonElement e) => new()
    {
        Index = e.GetProperty("index").GetInt32(),
        X = e.GetProperty("x").GetInt32(),
        Y = e.GetProperty("y").GetInt32(),
        Width = e.GetProperty("width").GetInt32(),
        Height = e.GetProperty("height").GetInt32()
    };

    private static List<Slot> ReadSlots(JsonElement array)
    {
        var list = new List<Slot>();
        foreach (var e in array.EnumerateArray()) list.Add(ReadSlot(e));
        return list;
    }

    private static void AssertSlot(JsonElement expected, Slot actual, string label)
    {
        Assert.Equal(expected.GetProperty("index").GetInt32(), actual.Index);
        Assert.Equal(expected.GetProperty("x").GetInt32(), actual.X);
        Assert.Equal(expected.GetProperty("y").GetInt32(), actual.Y);
        Assert.Equal(expected.GetProperty("width").GetInt32(), actual.Width);
        Assert.Equal(expected.GetProperty("height").GetInt32(), actual.Height);
        _ = label;
    }

    /// <summary>벡터의 국면 문자열 → enum. C# enum 이름과 **정확히 같은 철자**여야 한다(대소문자 구분).</summary>
    private static FrameLoadPhase ReadPhase(JsonElement e)
        => Enum.Parse<FrameLoadPhase>(e.GetString()!, ignoreCase: false);

    private static void AssertRect(JsonElement expected, CropRect actual, string label)
    {
        Assert.Equal(expected.GetProperty("x").GetInt32(), actual.X);
        Assert.Equal(expected.GetProperty("y").GetInt32(), actual.Y);
        Assert.Equal(expected.GetProperty("width").GetInt32(), actual.Width);
        Assert.Equal(expected.GetProperty("height").GetInt32(), actual.Height);
        _ = label;
    }

    [Fact]
    public void All_Vector_Files_Exist()
    {
        string[] expected =
        {
            "center-crop", "auto-arrange", "scale-slots", "clamp-slot", "overlap",
            "editor-transform", "role-matrix", "copy-name", "session-id", "timelapse-speed",
            "settings-clamp", "cut-count", "qr-normalize", "slots-file", "frame-load-policy"
        };
        foreach (var name in expected)
            Assert.True(File.Exists(Path.Combine(VectorDir, name + ".json")), $"누락: {name}.json");

        Assert.Equal(expected.Length, Directory.GetFiles(VectorDir, "*.json").Length);
    }

    [Fact]
    public void CenterCrop_Matches_Vector()
    {
        foreach (var c in LoadCases("center-crop").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var actual = CropCalculator.CenterCrop(
                i.GetProperty("srcWidth").GetInt32(),
                i.GetProperty("srcHeight").GetInt32(),
                i.GetProperty("targetAspect").GetDouble());
            AssertRect(c.GetProperty("expected"), actual, i.ToString());
        }
    }

    [Fact]
    public void AutoArrange_Matches_Vector()
    {
        foreach (var c in LoadCases("auto-arrange").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var aspectNode = i.GetProperty("targetAspect");
            double? aspect = aspectNode.ValueKind == JsonValueKind.Null ? null : aspectNode.GetDouble();

            var actual = SlotLayout.AutoArrange(
                i.GetProperty("slotCount").GetInt32(),
                i.GetProperty("frameW").GetInt32(),
                i.GetProperty("frameH").GetInt32(),
                aspect);

            var expected = c.GetProperty("expected");
            Assert.Equal(expected.GetArrayLength(), actual.Count);
            for (int k = 0; k < actual.Count; k++)
                AssertSlot(expected[k], actual[k], i.ToString());
        }
    }

    [Fact]
    public void ScaleSlots_Matches_Vector()
    {
        foreach (var c in LoadCases("scale-slots").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var actual = SlotLayout.ScaleSlots(
                ReadSlots(i.GetProperty("baseSlots")),
                i.GetProperty("factor").GetDouble(),
                i.GetProperty("frameW").GetInt32(),
                i.GetProperty("frameH").GetInt32());

            var expected = c.GetProperty("expected");
            Assert.Equal(expected.GetArrayLength(), actual.Count);
            for (int k = 0; k < actual.Count; k++)
                AssertSlot(expected[k], actual[k], i.ToString());
        }
    }

    [Fact]
    public void ClampSlot_Matches_Vector_For_Both_Formulas()
    {
        foreach (var c in LoadCases("clamp-slot").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var slot = ReadSlot(i.GetProperty("slot"));
            int frameW = i.GetProperty("frameW").GetInt32();
            int frameH = i.GetProperty("frameH").GetInt32();
            var expected = c.GetProperty("expected");

            AssertSlot(expected.GetProperty("editor"), SlotLayout.ClampToFrame(slot, frameW, frameH), "editor");
            AssertRect(expected.GetProperty("composition"), SlotPlacement.ClampSlotToFrame(slot, frameW, frameH), "composition");
        }
    }

    [Fact]
    public void Overlap_Matches_Vector()
    {
        foreach (var c in LoadCases("overlap").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var expected = c.GetProperty("expected");

            if (c.GetProperty("kind").GetString() == "pair")
            {
                var a = ReadSlot(i.GetProperty("a"));
                var b = ReadSlot(i.GetProperty("b"));
                Assert.Equal(expected.GetProperty("overlaps").GetBoolean(), SlotLayout.Overlaps(a, b));
            }
            else
            {
                var slots = ReadSlots(i.GetProperty("slots"));
                int frameW = i.GetProperty("frameW").GetInt32();
                int frameH = i.GetProperty("frameH").GetInt32();
                Assert.Equal(expected.GetProperty("hasAnyOverlap").GetBoolean(), SlotLayout.HasAnyOverlap(slots));
                Assert.Equal(expected.GetProperty("isValid").GetBoolean(), SlotLayout.IsValid(slots, frameW, frameH));
            }
        }
    }

    [Fact]
    public void EditorTransform_Matches_Vector()
    {
        foreach (var c in LoadCases("editor-transform").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var t = EditorTransform.Compute(
                i.GetProperty("canvasW").GetDouble(),
                i.GetProperty("canvasH").GetDouble(),
                i.GetProperty("frameW").GetInt32(),
                i.GetProperty("frameH").GetInt32());
            var expected = c.GetProperty("expected");

            if (c.GetProperty("kind").GetString() == "compute")
            {
                Assert.InRange(Math.Abs(expected.GetProperty("scale").GetDouble() - t.Scale), 0, Tolerance);
                Assert.InRange(Math.Abs(expected.GetProperty("originX").GetDouble() - t.OriginX), 0, Tolerance);
                Assert.InRange(Math.Abs(expected.GetProperty("originY").GetDouble() - t.OriginY), 0, Tolerance);
                Assert.InRange(Math.Abs(expected.GetProperty("displayWidth").GetDouble() - t.DisplayWidth), 0, Tolerance);
                Assert.InRange(Math.Abs(expected.GetProperty("displayHeight").GetDouble() - t.DisplayHeight), 0, Tolerance);
            }
            else
            {
                var (cx, cy) = t.FrameToCanvas(i.GetProperty("fx").GetDouble(), i.GetProperty("fy").GetDouble());
                var canvas = expected.GetProperty("canvas");
                Assert.InRange(Math.Abs(canvas.GetProperty("x").GetDouble() - cx), 0, Tolerance);
                Assert.InRange(Math.Abs(canvas.GetProperty("y").GetDouble() - cy), 0, Tolerance);

                var (fx2, fy2) = t.CanvasToFrame(cx, cy);
                var frame = expected.GetProperty("frame");
                Assert.InRange(Math.Abs(frame.GetProperty("x").GetDouble() - fx2), 0, Tolerance);
                Assert.InRange(Math.Abs(frame.GetProperty("y").GetDouble() - fy2), 0, Tolerance);
            }
        }
    }

    [Fact]
    public void RoleMatrix_Matches_Vector()
    {
        foreach (var c in LoadCases("role-matrix").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var expected = c.GetProperty("expected");

            if (i.TryGetProperty("actor", out var actorNode))
            {
                var actor = UserRoleExtensions.ParseRole(actorNode.GetString());
                var current = UserRoleExtensions.ParseRole(i.GetProperty("current").GetString());

                var actualRoles = RoleChangePolicy.AssignableRoles(actor, current)
                    .Select(r => r.ToFirestoreValue()).ToArray();
                var expectedRoles = expected.GetProperty("assignableRoles")
                    .EnumerateArray().Select(e => e.GetString()).ToArray();
                Assert.Equal(expectedRoles, actualRoles);

                Assert.Equal(expected.GetProperty("canManage").GetBoolean(), actor.CanManage(current));
                Assert.Equal(expected.GetProperty("canResetPin").GetBoolean(), actor.CanResetPin(current));
            }
            else
            {
                var role = UserRoleExtensions.ParseRole(i.GetProperty("role").GetString());
                Assert.Equal(expected.GetProperty("isPower").GetBoolean(), role.IsPower());
                Assert.Equal(expected.GetProperty("canWriteFrames").GetBoolean(), role.CanWriteFrames());
                Assert.Equal(expected.GetProperty("hierarchyRank").GetInt32(), role.HierarchyRank());
            }
        }
    }

    [Fact]
    public void CopyName_Matches_Vector()
    {
        foreach (var c in LoadCases("copy-name").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var expected = c.GetProperty("expected").GetString();

            if (c.GetProperty("kind").GetString() == "nextCopyName")
            {
                var baseNode = i.GetProperty("baseName");
                string? baseName = baseNode.ValueKind == JsonValueKind.Null ? null : baseNode.GetString();
                var existing = i.GetProperty("existingNames").EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty).ToArray();
                Assert.Equal(expected, FrameNaming.NextCopyName(baseName, existing));
            }
            else
            {
                Assert.Equal(expected, FrameNaming.StripCopySuffix(i.GetProperty("name").GetString()!));
            }
        }
    }

    [Fact]
    public void SessionId_And_Paths_Match_Vector()
    {
        foreach (var c in LoadCases("session-id").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var expected = c.GetProperty("expected");

            switch (c.GetProperty("kind").GetString())
            {
                case "stamp":
                {
                    var local = new DateTime(
                        i.GetProperty("year").GetInt32(), i.GetProperty("month").GetInt32(),
                        i.GetProperty("day").GetInt32(), i.GetProperty("hour").GetInt32(),
                        i.GetProperty("minute").GetInt32(), i.GetProperty("second").GetInt32());
                    var stamp = UploadContract.StampPrefix(local);
                    Assert.Equal(expected.GetProperty("stampPrefix").GetString(), stamp);
                    // NewSessionId는 GUID를 자체 생성하므로 조립 규칙만 검증한다(형식 계약 = M13).
                    Assert.Equal(
                        expected.GetProperty("sessionId").GetString(),
                        $"{stamp}_{i.GetProperty("uuid").GetString()}");
                    break;
                }
                case "paths":
                {
                    var sessionId = i.GetProperty("sessionId").GetString()!;
                    var format = i.GetProperty("format").GetString() == "Png" ? OutputFormat.Png : OutputFormat.Jpg;
                    Assert.Equal(expected.GetProperty("finalImagePath").GetString(),
                        UploadContract.FinalImagePath(sessionId, format));
                    Assert.Equal(expected.GetProperty("timelapsePath").GetString(),
                        UploadContract.TimelapsePath(sessionId));
                    break;
                }
                case "urls":
                {
                    Assert.Equal(expected.GetProperty("tokenDownloadUrl").GetString(),
                        UploadContract.TokenDownloadUrl(
                            i.GetProperty("bucket").GetString()!,
                            i.GetProperty("storagePath").GetString()!,
                            i.GetProperty("downloadToken").GetString()!));
                    Assert.Equal(expected.GetProperty("downloadPageUrl").GetString(),
                        UploadContract.DownloadPageUrl(
                            i.GetProperty("hostingBaseUrl").GetString()!,
                            i.GetProperty("token").GetString()!));
                    break;
                }
                case "expiresAt":
                {
                    var created = DateTimeOffset.FromUnixTimeMilliseconds(
                        i.GetProperty("createdAtEpochMs").GetInt64()).UtcDateTime;
                    var actual = UploadContract.ComputeExpiresAt(created, i.GetProperty("retentionHours").GetInt32());
                    var actualMs = new DateTimeOffset(actual, TimeSpan.Zero).ToUnixTimeMilliseconds();
                    Assert.Equal(expected.GetProperty("expiresAtEpochMs").GetInt64(), actualMs);
                    break;
                }
                default:
                    throw new InvalidOperationException("알 수 없는 kind");
            }
        }
    }

    [Fact]
    public void TimelapseSpeed_Matches_Vector()
    {
        foreach (var c in LoadCases("timelapse-speed").EnumerateArray())
        {
            var seconds = c.GetProperty("input").GetProperty("sessionSeconds").GetDouble();
            var expected = c.GetProperty("expected").GetProperty("factor").GetDouble();
            Assert.InRange(Math.Abs(expected - FfmpegArgs.ComputeSpeedFactor(seconds)), 0, Tolerance);
        }
    }

    [Fact]
    public void SettingsClamp_Matches_Vector()
    {
        foreach (var c in LoadCases("settings-clamp").EnumerateArray())
        {
            var settings = new AppSettings();
            foreach (var patch in c.GetProperty("input").EnumerateObject())
                ApplyPatch(settings, patch);

            settings.Clamp();

            foreach (var expected in c.GetProperty("expected").EnumerateObject())
                AssertSetting(settings, expected);
        }
    }

    private static void ApplyPatch(AppSettings s, JsonProperty p)
    {
        switch (p.Name)
        {
            case "CutCount": s.CutCount = p.Value.GetInt32(); break;
            case "CountdownSec": s.CountdownSec = p.Value.GetInt32(); break;
            case "RetakeLimit": s.RetakeLimit = p.Value.GetInt32(); break;
            case "RetentionHours": s.RetentionHours = p.Value.GetInt32(); break;
            case "HostingBaseUrl": s.HostingBaseUrl = p.Value.GetString()!; break;
            case "BackendBaseUrl": s.BackendBaseUrl = p.Value.GetString()!; break;
            case "GoogleClientId": s.GoogleClientId = p.Value.GetString()!; break;
            case "EnableQrDelivery": s.EnableQrDelivery = p.Value.GetBoolean(); break;
            case "SendPhoto": s.SendPhoto = p.Value.GetBoolean(); break;
            case "SendTimelapse": s.SendTimelapse = p.Value.GetBoolean(); break;
            default: throw new InvalidOperationException($"벡터에 알 수 없는 설정 키: {p.Name}");
        }
    }

    private static void AssertSetting(AppSettings s, JsonProperty p)
    {
        switch (p.Name)
        {
            case "CutCount": Assert.Equal(p.Value.GetInt32(), s.CutCount); break;
            case "CountdownSec": Assert.Equal(p.Value.GetInt32(), s.CountdownSec); break;
            case "RetakeLimit": Assert.Equal(p.Value.GetInt32(), s.RetakeLimit); break;
            case "RetentionHours": Assert.Equal(p.Value.GetInt32(), s.RetentionHours); break;
            case "HostingBaseUrl": Assert.Equal(p.Value.GetString(), s.HostingBaseUrl); break;
            case "BackendBaseUrl": Assert.Equal(p.Value.GetString(), s.BackendBaseUrl); break;
            case "GoogleClientId": Assert.Equal(p.Value.GetString(), s.GoogleClientId); break;
            case "EnableQrDelivery": Assert.Equal(p.Value.GetBoolean(), s.EnableQrDelivery); break;
            case "SendPhoto": Assert.Equal(p.Value.GetBoolean(), s.SendPhoto); break;
            case "SendTimelapse": Assert.Equal(p.Value.GetBoolean(), s.SendTimelapse); break;
            default: throw new InvalidOperationException($"벡터에 알 수 없는 설정 키: {p.Name}");
        }
    }

    [Fact]
    public void CutCount_Matches_Vector()
    {
        foreach (var c in LoadCases("cut-count").EnumerateArray())
        {
            var i = c.GetProperty("input");
            int configured = i.GetProperty("configured").GetInt32();
            int slotCount = i.GetProperty("slotCount").GetInt32();
            var expected = c.GetProperty("expected");

            Assert.Equal(expected.GetProperty("resolved").GetInt32(), CutCountPolicy.Resolve(configured, slotCount));
            Assert.Equal(expected.GetProperty("isAuto").GetBoolean(), CutCountPolicy.IsAuto(configured));
        }
    }

    /// <summary>
    /// it20 프레임 로딩 정책 교차 검증. 이 벡터는 **웹 구현이 아니라 이 프로젝트의 판정**에서 나왔다
    /// (<see cref="FrameLoadPolicyTests"/> 13건을 옮긴 것). 그러므로 이 테스트가 통과한다는 것은
    /// "웹이 참조하는 기대값이 Windows 구현과 일치한다"는 증명이다 — 웹 쪽 vectors.test.ts가 같은 파일을 읽는다.
    /// 값 하나를 틀리면 **양쪽이 동시에 실패**해야 한다(10 §3.3).
    /// </summary>
    [Fact]
    public void FrameLoadPolicy_Matches_Vector()
    {
        int classify = 0, finalize = 0, deadline = 0, notice = 0, constants = 0;

        foreach (var c in LoadCases("frame-load-policy").EnumerateArray())
        {
            var i = c.GetProperty("input");
            var expected = c.GetProperty("expected");

            switch (c.GetProperty("kind").GetString())
            {
                case "classify":
                    Assert.Equal(
                        ReadPhase(expected.GetProperty("phase")),
                        FrameLoadPolicy.Classify(
                            i.GetProperty("frameCount").GetInt32(),
                            i.GetProperty("waitInterrupted").GetBoolean()));
                    classify++;
                    break;

                case "finalize":
                    Assert.Equal(
                        ReadPhase(expected.GetProperty("phase")),
                        FrameLoadPolicy.Finalize(
                            ReadPhase(i.GetProperty("current")),
                            i.GetProperty("frameCount").GetInt32(),
                            i.GetProperty("waitInterrupted").GetBoolean(),
                            i.GetProperty("quiet").GetBoolean()));
                    finalize++;
                    break;

                case "nextDeadline":
                    Assert.Equal(
                        expected.GetProperty("nextDeadlineMs").GetInt64(),
                        (long)FrameLoadPolicy
                            .NextDeadline(TimeSpan.FromMilliseconds(i.GetProperty("elapsedMs").GetInt64()))
                            .TotalMilliseconds);
                    deadline++;
                    break;

                case "notice":
                    Assert.Equal(
                        expected.GetProperty("notice").GetString(),
                        FrameLoadPolicy.NoticeFor(ReadPhase(i.GetProperty("phase"))));
                    notice++;
                    break;

                case "constants":
                    // 상한 상수는 `const`라 xUnit2000이 'expected' 자리에 오기를 요구한다. 진실원이 이쪽이므로
                    // (벡터가 이 값에서 나왔다) 순서를 그대로 두는 것이 실패 메시지도 정확하다 — 틀린 쪽이 actual로 찍힌다.
                    Assert.Equal(FrameLoadPolicy.NoProgressTimeoutSeconds,
                        expected.GetProperty("noProgressTimeoutSeconds").GetInt32());
                    Assert.Equal(FrameLoadPolicy.MaxTotalWaitSeconds,
                        expected.GetProperty("maxTotalWaitSeconds").GetInt32());
                    Assert.Equal(FrameLoadPolicy.IdleWarningReferenceSeconds,
                        expected.GetProperty("idleWarningReferenceSeconds").GetInt32());
                    // enum 0번 값 = Loading (ViewModel 초기 상태 안전 보장)
                    Assert.Equal(default(FrameLoadPhase), ReadPhase(expected.GetProperty("defaultPhase")));
                    constants++;
                    break;

                default:
                    throw new InvalidOperationException("알 수 없는 kind");
            }
        }

        // 케이스가 통째로 빠져도 "전부 통과"로 보이므로 종류별 개수를 고정한다.
        Assert.Equal(7, classify);
        Assert.Equal(32, finalize);
        Assert.Equal(8, deadline);
        Assert.Equal(4, notice);
        Assert.Equal(1, constants);
    }

    [Fact]
    public void QrNormalize_Matches_Vector()
    {
        foreach (var c in LoadCases("qr-normalize").EnumerateArray())
        {
            var expected = c.GetProperty("expected");

            if (c.GetProperty("kind").GetString() == "normalize")
            {
                var i = c.GetProperty("input");
                var (enableQr, sendPhoto, sendTimelapse) = QrDeliveryPolicy.Normalize(
                    i.GetProperty("enableQrDelivery").GetBoolean(),
                    i.GetProperty("sendPhoto").GetBoolean(),
                    i.GetProperty("sendTimelapse").GetBoolean());

                Assert.Equal(expected.GetProperty("enableQrDelivery").GetBoolean(), enableQr);
                Assert.Equal(expected.GetProperty("sendPhoto").GetBoolean(), sendPhoto);
                Assert.Equal(expected.GetProperty("sendTimelapse").GetBoolean(), sendTimelapse);
            }
            else
            {
                var (photo, timelapse) = QrDeliveryPolicy.OnReEnabled();
                Assert.Equal(expected.GetProperty("sendPhoto").GetBoolean(), photo);
                Assert.Equal(expected.GetProperty("sendTimelapse").GetBoolean(), timelapse);
            }
        }
    }

    /// <summary>
    /// `.slots` **v2** 포맷 벡터. 서명 키는 플랫폼 내장이라 벡터에 담을 수 없으므로,
    /// 공유 계약은 <b>payload 텍스트 규격</b>이다 — 각 클라이언트가 이 형태로 payload를 만들고
    /// 자기 키로 서명한다. 여기서는 ① Encode가 규격 payload를 만드는지 ② Decode 왕복이 보존되는지
    /// ③ 거부 케이스가 지정된 상태를 내는지를 고정한다.
    /// </summary>
    [Fact]
    public void SlotsFile_Matches_Vector()
    {
        var root = LoadVectorRoot("slots-file");

        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var content = c.GetProperty("content");
            var slots = content.GetProperty("slots").EnumerateArray()
                .Select(s => new Slot
                {
                    Index = s.GetProperty("index").GetInt32(),
                    X = s.GetProperty("x").GetInt32(),
                    Y = s.GetProperty("y").GetInt32(),
                    Width = s.GetProperty("width").GetInt32(),
                    Height = s.GetProperty("height").GetInt32(),
                }).ToList();

            var sizeNode = content.GetProperty("imageSize");
            var dbIdNode = content.GetProperty("dbId");
            var input = new SlotsFileContent(
                content.GetProperty("owner").GetString()!,
                new ImageSize
                {
                    Width = sizeNode.GetProperty("width").GetInt32(),
                    Height = sizeNode.GetProperty("height").GetInt32()
                },
                slots,
                dbIdNode.ValueKind == JsonValueKind.Null ? null : dbIdNode.GetString());

            // ① payload 규격 — base64를 풀고 #sig 줄을 떼면 벡터의 expectedPayload와 정확히 같아야 한다.
            var encoded = SlotsFileCodec.Encode(input);
            var decodedText = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var payload = decodedText[..decodedText.LastIndexOf("\n#sig=", StringComparison.Ordinal)];
            Assert.Equal(c.GetProperty("expectedPayload").GetString(), payload);

            // ② 왕복 보존
            Assert.Equal(SlotsDecodeStatus.Ok, SlotsFileCodec.Decode(encoded, out var back));
            Assert.NotNull(back);
            Assert.Equal(input.Owner, back!.Owner);
            Assert.Equal(input.ImageSize.Width, back.ImageSize.Width);
            Assert.Equal(input.ImageSize.Height, back.ImageSize.Height);
            Assert.Equal(input.DbId, back.DbId);
            Assert.Equal(input.Slots.Count, back.Slots.Count);
        }

        // ③ 거부 케이스
        foreach (var c in root.GetProperty("rejectCases").EnumerateArray())
        {
            var expected = Enum.Parse<SlotsDecodeStatus>(c.GetProperty("expectedStatus").GetString()!);
            var actual = SlotsFileCodec.Decode(c.GetProperty("fileText").GetString(), out _);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Vector_Files_Are_Culture_Invariant()
    {
        // 벡터의 실수 표기는 `.` 소수점이다. 테스트가 다른 문화권에서 돌아도 파싱이 깨지지 않아야 한다.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // 소수점이 ','인 문화권
            CenterCrop_Matches_Vector();
            TimelapseSpeed_Matches_Vector();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
