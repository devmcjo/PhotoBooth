using System.Globalization;
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
    /// `.slots` 파서는 <see cref="LocalFrameStore"/> 내부에 있으므로(private) 임시 폴더에 실제 파일을
    /// 써서 <c>LoadPublic</c>으로 관측한다. 리플렉션을 쓰지 않는 이유: 파일 레이아웃 규약(공용=접두 없음,
    /// `#dbid`가 있으면 그 값이 Id)까지 함께 고정되기 때문이다.
    /// </summary>
    [Fact]
    public void SlotsFile_Matches_Vector()
    {
        foreach (var c in LoadCases("slots-file").EnumerateArray())
        {
            var text = c.GetProperty("input").GetProperty("text").GetString()!;
            var expected = c.GetProperty("expected");

            var root = Path.Combine(Path.GetTempPath(), "mcphoto-slots-vec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                // 공용 규약: 이름에 '_'가 없어야 LoadPublic이 집계한다.
                File.WriteAllBytes(Path.Combine(root, "vec.png"), new byte[] { 1, 2, 3 });
                File.WriteAllText(Path.Combine(root, "vec.slots"), text);

                var frames = new LocalFrameStore(root).LoadPublic();
                Assert.Single(frames);
                var frame = frames[0];

                var size = expected.GetProperty("imageSize");
                Assert.Equal(size.GetProperty("width").GetInt32(), frame.ImageSize.Width);
                Assert.Equal(size.GetProperty("height").GetInt32(), frame.ImageSize.Height);

                var slots = expected.GetProperty("slots");
                Assert.Equal(slots.GetArrayLength(), frame.Slots.Count);
                for (int k = 0; k < frame.Slots.Count; k++)
                    AssertSlot(slots[k], frame.Slots[k], text);

                // dbId: 있으면 Id가 그 값, 없으면 `local:{파일명}`.
                var dbIdNode = expected.GetProperty("dbId");
                var expectedId = dbIdNode.ValueKind == JsonValueKind.Null || dbIdNode.GetString()!.Length == 0
                    ? "local:vec"
                    : dbIdNode.GetString();
                Assert.Equal(expectedId, frame.Id);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { /* 정리 실패는 무시 */ }
            }
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
