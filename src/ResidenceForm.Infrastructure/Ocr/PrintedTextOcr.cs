using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using ResidenceForm.Application.Abstractions;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Infrastructure.Ocr;

/// <summary>
/// Reads the printed text of a passport page with the PP-OCRv4 models (PaddleOCR, Apache-2.0, ONNX format) on ONNX Runtime.
/// Pure NuGet, no native DLL to install, runs on the CPU. Detection (DB) finds text lines, recognition (CTC) reads each one.
/// If the models can't be loaded, Read returns null and the app falls back to the code lines read by Tesseract.
/// </summary>
public sealed class PrintedTextOcr : IPrintedTextReader, IDisposable
{
    private readonly string _modelDirectory;
    private readonly ILogger<PrintedTextOcr> _logger;
    private readonly object _gate = new();
    private InferenceSession? _det, _rec;
    private string[] _dict = Array.Empty<string>();
    private bool _failed;

    public string? LastError { get; private set; }

    public PrintedTextOcr(IOptions<AssetOptions> assets, ILogger<PrintedTextOcr> logger)
    {
        _modelDirectory = assets.Value.OcrModelsPath;
        _logger = logger;
    }

    // PaddleOCR training used BGR channel order with these ImageNet statistics
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std  = { 0.229f, 0.224f, 0.225f };

    private bool EnsureLoaded()
    {
        lock (_gate)
        {
            if (_failed) return false;
            if (_det != null) return true;
            try
            {
                _det = new InferenceSession(Path.Combine(_modelDirectory, "det.onnx"));
                _rec = new InferenceSession(Path.Combine(_modelDirectory, "rec.onnx"));
                var chars = File.ReadAllText(Path.Combine(_modelDirectory, "dict.txt")).Split('\n');
                _dict = new[] { "blank" }.Concat(chars).Concat(new[] { " " }).ToArray();       // CTC blank first, space last
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                LastError = ex.Message;
                _logger.LogWarning(ex, "Printed-text models could not be loaded from {Directory}", _modelDirectory);
                return false;
            }
        }
    }

    public IReadOnlyList<OcrWord>? Read(byte[] image)
    {
        if (!EnsureLoaded()) return null;
        try
        {
            using var decoded = SKBitmap.Decode(image);
            if (decoded == null) return null;
            using var bmp = decoded.ColorType == SKColorType.Bgra8888 ? decoded.Copy() : decoded.Copy(SKColorType.Bgra8888);

            var boxes = Detect(bmp);
            var words = new List<OcrWord>();
            foreach (var b in boxes)
            {
                var (text, score) = Recognise(bmp, b);
                if (!string.IsNullOrWhiteSpace(text) && score > 0.3f)
                    words.Add(new OcrWord(text.Trim(), b.x0, b.y0, b.x1, b.y1));
            }
            return words;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "Reading printed text failed");
            return null;
        }
    }

    // ------------------------------------------------------------------ detection (DB)

    private List<(float x0, float y0, float x1, float y1)> Detect(SKBitmap src)
    {
        const int maxSide = 1600, minSide = 736;
        const float thresh = 0.3f, boxThresh = 0.5f, unclip = 1.6f;

        int h = src.Height, w = src.Width;
        double s = 1.0;
        if (Math.Max(h, w) > maxSide) s = maxSide / (double)Math.Max(h, w);
        else if (Math.Min(h, w) < minSide) s = minSide / (double)Math.Min(h, w);
        int nh = Math.Max(32, (int)Math.Round(h * s / 32.0) * 32);
        int nw = Math.Max(32, (int)Math.Round(w * s / 32.0) * 32);

        using var resized = src.Resize(src.Info.WithSize(nw, nh), SKFilterQuality.High) ?? src.Copy();
        var px = resized.GetPixelSpan();
        int stride = resized.RowBytes;

        var input = new float[3 * nh * nw];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int i = y * stride + x * 4;                              // B, G, R, A
                for (int c = 0; c < 3; c++)
                    input[c * nh * nw + y * nw + x] = (px[i + c] / 255f - Mean[c]) / Std[c];
            }

        using var run = _det!.Run(new[] { NamedOnnxValue.CreateFromTensor("x", new DenseTensor<float>(input, new[] { 1, 3, nh, nw })) });
        var prob = run.First().AsTensor<float>().ToArray();             // [1,1,nh,nw]

        var boxes = new List<(float, float, float, float)>();
        var visited = new bool[nh * nw];
        var stack = new int[nh * nw];
        for (int start = 0; start < prob.Length; start++)
        {
            if (visited[start] || prob[start] <= thresh) continue;

            int sp = 0; stack[sp++] = start; visited[start] = true;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
            double sum = 0;
            while (sp > 0)                                               // 8-connected flood fill
            {
                int p = stack[--sp];
                int y = p / nw, x = p - y * nw;
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                sum += prob[p]; count++;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= nw || ny >= nh) continue;
                        int q = ny * nw + nx;
                        if (!visited[q] && prob[q] > thresh) { visited[q] = true; stack[sp++] = q; }
                    }
            }

            int bw = maxX - minX + 1, bh = maxY - minY + 1;
            if (count < 10 || sum / count < boxThresh || Math.Min(bw, bh) < 3) continue;

            float d = bw * bh * unclip / (2f * (bw + bh));               // DB "unclip": grow the shrunken text kernel back
            float x0 = Math.Max(0, minX - d), y0 = Math.Max(0, minY - d), x1 = Math.Min(nw, maxX + 1 + d), y1 = Math.Min(nh, maxY + 1 + d);
            boxes.Add((x0 * w / nw, y0 * h / nh, x1 * w / nw, y1 * h / nh));
        }
        return boxes.OrderBy(b => (int)Math.Round(b.Item2 / 10.0)).ThenBy(b => b.Item1).ToList();
    }

    // ------------------------------------------------------------------ recognition (CTC)

    private (string text, float score) Recognise(SKBitmap src, (float x0, float y0, float x1, float y1) b)
    {
        int x0 = Math.Max(0, (int)Math.Round(b.x0)), y0 = Math.Max(0, (int)Math.Round(b.y0));
        int x1 = Math.Min(src.Width, (int)Math.Round(b.x1)), y1 = Math.Min(src.Height, (int)Math.Round(b.y1));
        int cw = x1 - x0, ch = y1 - y0;
        if (cw < 4 || ch < 4) return ("", 0);

        using var crop = new SKBitmap();
        if (!src.ExtractSubset(crop, new SKRectI(x0, y0, x1, y1))) return ("", 0);

        const int H = 48;
        double maxRatio = Math.Max(320.0 / 48.0, cw / (double)ch);
        int W = (int)(H * maxRatio);
        int rw = Math.Min(W, (int)Math.Ceiling(H * cw / (double)ch));

        using var resized = crop.Resize(new SKImageInfo(rw, H, SKColorType.Bgra8888, SKAlphaType.Unpremul), SKFilterQuality.High);
        if (resized == null) return ("", 0);
        var px = resized.GetPixelSpan();
        int stride = resized.RowBytes;

        var input = new float[3 * H * W];                                // zero = mid-grey padding on the right
        for (int y = 0; y < H; y++)
            for (int x = 0; x < rw; x++)
            {
                int i = y * stride + x * 4;
                for (int c = 0; c < 3; c++) input[c * H * W + y * W + x] = (px[i + c] / 255f - 0.5f) / 0.5f;
            }

        using var run = _rec!.Run(new[] { NamedOnnxValue.CreateFromTensor("x", new DenseTensor<float>(input, new[] { 1, 3, H, W })) });
        var t = run.First().AsTensor<float>();
        int steps = t.Dimensions[1], classes = t.Dimensions[2];
        var flat = t.ToArray();

        var sb = new System.Text.StringBuilder();
        double sum = 0; int n = 0, prev = -1;
        for (int step = 0; step < steps; step++)
        {
            int best = 0; float bestP = float.MinValue;
            for (int k = 0; k < classes; k++)
            {
                float v = flat[step * classes + k];
                if (v > bestP) { bestP = v; best = k; }
            }
            if (best != prev && best != 0 && best < _dict.Length) { sb.Append(_dict[best]); sum += bestP; n++; }
            prev = best;
        }
        return (sb.ToString(), n == 0 ? 0f : (float)(sum / n));
    }

    public void Dispose() { _det?.Dispose(); _rec?.Dispose(); }
}
