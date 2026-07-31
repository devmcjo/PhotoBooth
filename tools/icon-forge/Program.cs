using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconForge;

/// <summary>
/// SVG 심볼 + 배경을 합성해 Windows 앱 아이콘(.ico/.png)을 굽는다.
/// SVG path mini-language는 WPF Geometry.Parse와 대체로 호환되므로 외부 렌더러 없이 처리한다.
/// </summary>
internal static class Program
{
    private sealed record Design(
        string Name,
        string Svg,
        Color Bg1,
        Color Bg2,
        Color Symbol,
        bool Highlight,
        Color? Ring = null);

    private static readonly Design[] Designs =
    {
        // 로즈 그라디언트 + 흰 카메라 — 앱 Accent(#FF4D79) 계승, 어두운 작업표시줄에서 가장 잘 보인다.
        new("01-rose-camera", "ms_photo_camera",
            Rgb(0xFF6B8F), Rgb(0xE1315E), Colors.White, true),

        // 딥 플럼 배경 + 로즈 카메라 — Text.Primary(#241F2B) 계열, 차분한 톤.
        new("02-plum-camera", "ms_photo_camera",
            Rgb(0x39303F), Rgb(0x1C1722), Rgb(0xFF4D79), true),

        // 로즈 그라디언트 + 흰 셔터(조리개) — 더 추상적/브랜드적.
        new("03-rose-shutter", "ms_camera",
            Rgb(0xFF6B8F), Rgb(0xE1315E), Colors.White, true),

        // 딥 플럼 + 흰 셔터.
        new("04-plum-shutter", "ms_camera",
            Rgb(0x39303F), Rgb(0x1C1722), Colors.White, true),

        // 밝은 톤: 화이트 배경 + 로즈 카메라 + 얇은 테두리.
        new("05-light-camera", "ms_photo_camera",
            Rgb(0xFFFFFF), Rgb(0xFFEFF3), Rgb(0xE43C67), false, Rgb(0xF3D3DC)),

        // 로즈 그라디언트 + 흰 포토 라이브러리(사진 묶음) — 포토부스 결과물 은유.
        new("06-rose-library", "ms_photo_library",
            Rgb(0xFF6B8F), Rgb(0xE1315E), Colors.White, true),

        // 딥 플럼 + 민트 카메라 — Accent2(#37C9B0) 조합.
        new("07-plum-mint", "ms_photo_camera",
            Rgb(0x39303F), Rgb(0x1C1722), Rgb(0x37C9B0), true),

        // 부트스트랩 카메라(둥근 형태) + 로즈.
        new("08-rose-bscam", "bs_camera_fill",
            Rgb(0xFF6B8F), Rgb(0xE1315E), Colors.White, true),
    };

    private static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string svgDir = args.Length > 0 ? args[0] : "icons";
            string outDir = args.Length > 1 ? args[1] : "out";
            Directory.CreateDirectory(outDir);

            var geoms = new Dictionary<string, Geometry>();
            foreach (var f in Directory.GetFiles(svgDir, "*.svg"))
                geoms[Path.GetFileNameWithoutExtension(f)] = ParseSvg(f);

            foreach (var d in Designs)
            {
                if (!geoms.TryGetValue(d.Svg, out var sym))
                {
                    Console.WriteLine($"SKIP {d.Name}: svg '{d.Svg}' 없음");
                    continue;
                }

                var frames = new List<(int size, BitmapSource bmp)>();
                foreach (int s in IcoSizes)
                    frames.Add((s, Render(d, sym, s)));

                WriteIco(Path.Combine(outDir, d.Name + ".ico"), frames);
                File.WriteAllBytes(Path.Combine(outDir, d.Name + "-256.png"),
                    EncodePng(Render(d, sym, 256)));
                Console.WriteLine($"OK   {d.Name}.ico  ({frames.Count} frames)");
            }

            BuildSheet(geoms, Path.Combine(outDir, "_preview-sheet.png"));
            Console.WriteLine("OK   _preview-sheet.png");

            BuildZoomSheet(geoms, "03-rose-shutter", Path.Combine(outDir, "_zoom-03.png"));
            Console.WriteLine("OK   _zoom-03.png");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex);
            return 1;
        }
    }

    // ---------- SVG ----------

    private static Geometry ParseSvg(string file)
    {
        string xml = File.ReadAllText(file);
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };

        foreach (Match m in Regex.Matches(xml, @"<path\b[^>]*?/?>", RegexOptions.Singleline))
        {
            string tag = m.Value;
            var fill = Regex.Match(tag, @"fill\s*=\s*""([^""]*)""");
            if (fill.Success && fill.Groups[1].Value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                continue; // 투명 히트박스 path (Material 구형 아이콘) 제외
            var d = Regex.Match(tag, @"\bd\s*=\s*""([^""]*)""", RegexOptions.Singleline);
            if (!d.Success) continue;
            group.Children.Add(Geometry.Parse(NormalizePath(d.Groups[1].Value)));
        }

        foreach (Match m in Regex.Matches(xml, @"<circle\b[^>]*?/?>", RegexOptions.Singleline))
        {
            double cx = Attr(m.Value, "cx"), cy = Attr(m.Value, "cy"), r = Attr(m.Value, "r");
            if (r > 0) group.Children.Add(new EllipseGeometry(new Point(cx, cy), r, r));
        }

        if (group.Children.Count == 0)
            throw new InvalidOperationException($"{file}: 그릴 도형이 없다");
        return group;
    }

    private static double Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*""([^""]*)""");
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>
    /// SVG는 "a.5.5 0" 처럼 소수점을 구분자로 쓴 연속 숫자를 허용하지만 WPF 파서는 못 읽는다.
    /// 이미 소수점이 있는 숫자 뒤에 다시 '.'가 오면 공백을 끼워 토큰을 분리한다.
    /// </summary>
    private static string NormalizePath(string d)
    {
        var sb = new StringBuilder(d.Length + 16);
        bool inNum = false, hasDot = false;
        foreach (char c in d)
        {
            if (char.IsDigit(c)) { inNum = true; sb.Append(c); }
            else if (c == '.')
            {
                if (inNum && hasDot) { sb.Append(' '); hasDot = false; }
                sb.Append(c);
                inNum = true; hasDot = true;
            }
            else { inNum = false; hasDot = false; sb.Append(c); }
        }
        return sb.ToString();
    }

    // ---------- 렌더 ----------

    private static Color Rgb(int hex) =>
        Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    private static BitmapSource Render(Design d, Geometry sym, int size)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetEdgeMode(dv, EdgeMode.Unspecified);

        using (var dc = dv.RenderOpen())
        {
            var rect = new Rect(0, 0, size, size);
            double r = size * 0.205;

            var bg = new LinearGradientBrush(d.Bg1, d.Bg2, new Point(0.15, 0), new Point(0.85, 1));
            Pen ring = d.Ring is Color rc
                ? new Pen(new SolidColorBrush(rc), Math.Max(1.0, size * 0.016))
                : null;
            dc.DrawRoundedRectangle(bg, ring, rect, r, r);

            if (d.Highlight)
            {
                // 상단 미세 광택 — 큰 사이즈에서만 의미가 있고 작은 사이즈에선 탁해지므로 제한.
                if (size >= 32)
                {
                    dc.PushClip(new RectangleGeometry(rect, r, r));
                    var hl = new LinearGradientBrush(
                        Color.FromArgb(40, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255), 90);
                    dc.DrawRectangle(hl, null, new Rect(0, 0, size, size * 0.6));
                    dc.Pop();
                }
            }

            // 작은 사이즈일수록 심볼을 키운다. 셔터처럼 내부 간격이 있는 심볼은
            // 16px에서 날개 사이 여백이 1px 미만으로 무너지므로 여백을 최대한 회수한다.
            double pad = size <= 20 ? 0.07
                       : size <= 32 ? 0.10
                       : size <= 48 ? 0.145
                       : 0.175;
            var b = sym.Bounds;
            double avail = size * (1 - 2 * pad);
            double scale = Math.Min(avail / b.Width, avail / b.Height);

            var tg = new TransformGroup();
            tg.Children.Add(new TranslateTransform(-(b.X + b.Width / 2), -(b.Y + b.Height / 2)));
            tg.Children.Add(new ScaleTransform(scale, scale));
            tg.Children.Add(new TranslateTransform(size / 2.0, size / 2.0));

            var geo = sym.Clone();
            geo.Transform = tg;
            dc.DrawGeometry(new SolidColorBrush(d.Symbol), null, geo);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ---------- 인코딩 ----------

    private static byte[] EncodePng(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>32bpp DIB(BITMAPINFOHEADER + bottom-up BGRA + 빈 AND 마스크).</summary>
    private static byte[] EncodeDib(BitmapSource src, int size)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int stride = size * 4;
        var px = new byte[stride * size];
        conv.CopyPixels(px, stride, 0);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40);            // biSize
        bw.Write(size);          // biWidth
        bw.Write(size * 2);      // biHeight (XOR + AND)
        bw.Write((ushort)1);     // biPlanes
        bw.Write((ushort)32);    // biBitCount
        bw.Write(0);             // biCompression = BI_RGB
        bw.Write(stride * size); // biSizeImage
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        for (int y = size - 1; y >= 0; y--) bw.Write(px, y * stride, stride);
        bw.Write(new byte[((size + 31) / 32) * 4 * size]); // AND 마스크(알파 채널이 대신함)
        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteIco(string path, List<(int size, BitmapSource bmp)> frames)
    {
        // 64px 이상은 PNG 압축(용량), 이하는 DIB(구형 셸 호환).
        var blobs = new List<byte[]>();
        foreach (var (s, bmp) in frames)
            blobs.Add(s >= 64 ? EncodePng(bmp) : EncodeDib(bmp, s));

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)frames.Count);

        int offset = 6 + 16 * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            int s = frames[i].size;
            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(blobs[i].Length);
            bw.Write(offset);
            offset += blobs[i].Length;
        }
        foreach (var b in blobs) bw.Write(b);
    }

    // ---------- 비교 시트 ----------

    private static void BuildSheet(Dictionary<string, Geometry> geoms, string path)
    {
        const int cell = 300, colW = cell, rowH = 360;
        int cols = Designs.Length;
        int w = cols * colW, h = rowH * 2;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Rgb(0xF2F2F4)), null, new Rect(0, 0, w, rowH));
            dc.DrawRectangle(new SolidColorBrush(Rgb(0x1F1F22)), null, new Rect(0, rowH, w, rowH));

            for (int i = 0; i < cols; i++)
            {
                var d = Designs[i];
                if (!geoms.TryGetValue(d.Svg, out var sym)) continue;
                double x = i * colW;

                for (int row = 0; row < 2; row++)
                {
                    double y = row * rowH;
                    DrawAt(dc, Render(d, sym, 192), x + 20, y + 20, 192);
                    DrawAt(dc, Render(d, sym, 48), x + 20, y + 232, 48);
                    DrawAt(dc, Render(d, sym, 32), x + 84, y + 232, 32);
                    DrawAt(dc, Render(d, sym, 24), x + 132, y + 232, 24);
                    DrawAt(dc, Render(d, sym, 16), x + 172, y + 232, 16);

                    var txt = new FormattedText(d.Name,
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 15,
                        new SolidColorBrush(row == 0 ? Rgb(0x333333) : Rgb(0xDDDDDD)),
                        1.0);
                    dc.DrawText(txt, new Point(x + 20, y + 296));
                }
            }
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        File.WriteAllBytes(path, EncodePng(rtb));
    }

    private static void DrawAt(DrawingContext dc, BitmapSource bmp, double x, double y, int s) =>
        dc.DrawImage(bmp, new Rect(x, y, s, s));

    /// <summary>작은 사이즈를 nearest-neighbor로 확대해 픽셀 단위 뭉갬을 눈으로 검증한다.</summary>
    private static void BuildZoomSheet(Dictionary<string, Geometry> geoms, string name, string path)
    {
        var d = Array.Find(Designs, x => x.Name == name);
        if (d == null || !geoms.TryGetValue(d.Svg, out var sym)) return;

        int[] sizes = { 16, 20, 24, 32, 48 };
        const int zoom = 9, gap = 16;
        int w = gap, h = 48 * zoom + gap * 2 + 24;
        foreach (int s in sizes) w += s * zoom + gap;

        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Rgb(0x1F1F22)), null, new Rect(0, 0, w, h));
            double x = gap;
            foreach (int s in sizes)
            {
                var bmp = Render(d, sym, s);
                double side = s * zoom;
                dc.DrawImage(bmp, new Rect(x, gap, side, side));
                var txt = new FormattedText($"{s}px", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 15,
                    new SolidColorBrush(Rgb(0xDDDDDD)), 1.0);
                dc.DrawText(txt, new Point(x, gap + side + 6));
                x += side + gap;
            }
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        File.WriteAllBytes(path, EncodePng(rtb));
    }
}
