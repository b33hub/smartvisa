using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidenceForm.Application.Abstractions;
using ResidenceForm.Domain.Passports;
using SkiaSharp;
using Tesseract;

namespace ResidenceForm.Infrastructure.Ocr;

/// <summary>
/// Reads a passport image in two ways and returns the raw findings:
/// 1. the two machine-readable lines at the bottom (Tesseract, restricted alphabet; this also finds the page rotation);
/// 2. the printed text with positions (PP-OCR), a second opinion on the code lines plus place of birth, issue date, etc.
/// Images are processed in memory only and never stored.
/// </summary>
public sealed class PassportScanner : IPassportScanner
{
    private const string CodeLineCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<";
    private const int CodeLineScanWidth = 2200;
    private const int PrintedTextWidth = 1600;
    private static readonly int[] RotationsToTry = { 0, 90, 270, 180 };

    private readonly TesseractTextReader _tesseract;
    private readonly IPrintedTextReader _printedText;
    private readonly ILogger<PassportScanner> _logger;

    public PassportScanner(IOptions<AssetOptions> assets, IPrintedTextReader printedText, ILogger<PassportScanner> logger)
    {
        _logger = logger;
        _tesseract = new TesseractTextReader(assets.Value.TessdataPath);
        _printedText = printedText;
    }

    public PassportScan Scan(byte[] imageBytes)
    {
        using var upright = ImageOperations.DecodeUpright(imageBytes);

        var (codeLines, page, codeLineProblem) = FindCodeLines(upright);
        using (page)
        {
            var printedWords = ReadPrintedText(page);
            codeLines = CombineWithPrintedText(codeLines, printedWords);

            var visualDates = printedWords is not null
                ? Mrz.VisualDates(string.Join("\n", printedWords.Select(w => w.Text)))
                : ReadDatesWithTesseract(page);

            return new PassportScan(codeLines, printedWords, visualDates, printedWords is null ? _printedText.LastError : null, codeLineProblem);
        }
    }

    // ------------------------------------------------------------------ 1. code lines

    /// <summary>Tries the page upright, then rotated; returns the best code-line pair and the scaled page it was found on.</summary>
    private (Mrz.MrzPair? Best, SKBitmap Page, string? Problem) FindCodeLines(SKBitmap upright)
    {
        Mrz.MrzPair? best = null;
        SKBitmap? bestPage = null;

        foreach (var rotation in RotationsToTry)
        {
            using var rotated = ImageOperations.Rotate(upright, rotation);
            var scaled = ImageOperations.ScaleToWidth(rotated, CodeLineScanWidth);
            var keepScaled = false;
            try
            {
                foreach (var fraction in CropFractions(rotation))
                {
                    using var crop = ImageOperations.CropBottom(scaled, fraction);
                    string text;
                    try
                    {
                        text = _tesseract.Read(ImageOperations.ToPng(crop), PageSegMode.SingleBlock, CodeLineCharacters);
                    }
                    catch (Exception ex)
                    {
                        // e.g. a missing native library: carry on with the printed-text reader instead of failing the whole reading
                        _logger.LogWarning(ex, "The code-line reader (Tesseract) failed");
                        var page = bestPage ?? ImageOperations.ScaleToWidth(upright, CodeLineScanWidth);
                        return (best, page, Describe(ex));
                    }
                    var pair = Mrz.FindPair(Mrz.LongLines(text));

                    if (pair is not null && (best is null || pair.Score > best.Score))
                    {
                        best = pair;
                        if (!keepScaled)
                        {
                            bestPage?.Dispose();
                            bestPage = scaled;
                            keepScaled = true;
                        }
                    }
                    if (IsFullyVerified(best)) break;
                }
            }
            finally
            {
                if (!keepScaled) scaled.Dispose();
            }

            if (IsFullyVerified(best) || best?.L2 is { Score: >= 6 }) break;      // looks like a real code line: no need to try other rotations
        }

        return (best, bestPage ?? ImageOperations.ScaleToWidth(upright, CodeLineScanWidth), null);
    }

    // upright photos: look at the bottom third, then bigger areas; rotated photos: bottom third, then everything
    private static double[] CropFractions(int rotation) => rotation == 0 ? new[] { 0.30, 0.45, 1.0 } : new[] { 0.35, 1.0 };

    private static bool IsFullyVerified(Mrz.MrzPair? pair) => pair?.L2 is { NumberOk: true, DobOk: true, ExpiryOk: true };

    // ------------------------------------------------------------------ 2. printed text

    private IReadOnlyList<OcrWord>? ReadPrintedText(SKBitmap page)
    {
        using var smaller = ImageOperations.ScaleToWidth(page, PrintedTextWidth);
        return _printedText.Read(ImageOperations.ToPng(smaller));
    }

    /// <summary>The printed text often contains the code lines too; use them when they score better, and borrow the name line if ours is missing.</summary>
    private static Mrz.MrzPair? CombineWithPrintedText(Mrz.MrzPair? fromTesseract, IReadOnlyList<OcrWord>? printedWords)
    {
        if (printedWords is null) return fromTesseract;

        var text = string.Join("\n", printedWords.OrderBy(w => w.Cy).Select(w => w.Text));
        var fromPrinted = Mrz.FindPair(Mrz.LongLines(text));
        if (fromPrinted is null) return fromTesseract;

        var best = fromTesseract is null || fromPrinted.Score > fromTesseract.Score ? fromPrinted : fromTesseract;
        return best.L1 is null && fromPrinted.L1 is not null ? best with { L1 = fromPrinted.L1 } : best;
    }

    /// <summary>Fallback when the printed-text reader is unavailable: find printed dates with plain Tesseract.</summary>
    private HashSet<DateOnly> ReadDatesWithTesseract(SKBitmap page)
    {
        try
        {
            return ReadDates(page);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading printed dates with Tesseract failed");
            return new HashSet<DateOnly>();
        }
    }

    private HashSet<DateOnly> ReadDates(SKBitmap page)
    {
        var dates = Mrz.VisualDates(_tesseract.Read(ImageOperations.ToPng(page), PageSegMode.SparseText));
        using var lowerPart = ImageOperations.CropBottom(page, 0.65);
        dates.UnionWith(Mrz.VisualDates(_tesseract.Read(ImageOperations.ToPng(lowerPart), PageSegMode.SingleBlock)));
        return dates;
    }

    private static string Describe(Exception ex) =>
        ex.InnerException is null ? $"{ex.GetType().Name}: {ex.Message}" : $"{ex.GetType().Name}: {ex.Message} ({ex.InnerException.Message})";
}
