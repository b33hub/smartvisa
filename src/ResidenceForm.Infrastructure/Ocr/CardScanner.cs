using Microsoft.Extensions.Logging;
using ResidenceForm.Application.Abstractions;
using ResidenceForm.Domain.CivilIds;
using ResidenceForm.Domain.Passports;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace ResidenceForm.Infrastructure.Ocr;

/// <summary>
/// Reads one side of an ID card. The printed-text reader finds every text line and reads digits and English; the same boxes
/// are then cropped and read again with Tesseract's Arabic model, because the first reader has no Arabic letters.
/// Images are processed in memory only and never stored.
/// </summary>
public sealed class CardScanner : ICardScanner
{
    private const int ScanWidth = 1600;
    private const int MaxLines = 80;
    private const int ArabicCropHeight = 64;            // Tesseract reads Arabic best when the text line is about this tall
    private const float CropPadding = 4f;

    private readonly IPrintedTextReader _printedText;
    private readonly TesseractTextReader _tesseract;
    private readonly ILogger<CardScanner> _logger;

    public CardScanner(IOptions<AssetOptions> assets, IPrintedTextReader printedText, ILogger<CardScanner> logger)
    {
        _printedText = printedText;
        _tesseract = new TesseractTextReader(assets.Value.TessdataPath);
        _logger = logger;
    }

    public CardScan Scan(byte[] imageBytes)
    {
        using var upright = ImageOperations.DecodeUpright(imageBytes);
        using var card = ImageOperations.ScaleToWidth(upright, ScanWidth);

        var words = _printedText.Read(ImageOperations.ToPng(card));
        if (words is null)
        {
            return new CardScan(Array.Empty<CardLine>(), $"The text reader isn't available ({_printedText.LastError}).");
        }

        var boxes = words.Take(MaxLines).ToList();
        var arabicTexts = ReadArabic(card, boxes, out var problem);
        var lines = boxes.Select((box, i) => new CardLine(box, box.Text, arabicTexts[i])).ToList();
        return new CardScan(lines, problem);
    }

    /// <summary>The Arabic reading of every box (empty strings when the Arabic reader fails, which is reported, not fatal).</summary>
    private IReadOnlyList<string> ReadArabic(SKBitmap card, IReadOnlyList<OcrWord> boxes, out string? problem)
    {
        problem = null;
        var empty = boxes.Select(_ => "").ToList();
        try
        {
            var crops = new List<byte[]>(boxes.Count);
            foreach (var box in boxes)
            {
                using var crop = ImageOperations.Crop(card, box.X1 - CropPadding, box.Y1 - CropPadding, box.X2 + CropPadding, box.Y2 + CropPadding);
                if (crop is null) { crops.Add(BlankPng()); continue; }

                using var scaled = ImageOperations.ScaleTowardsHeight(crop, ArabicCropHeight);
                crops.Add(ImageOperations.ToPng(scaled));
            }
            return _tesseract.ReadLines(crops, TesseractTextReader.Arabic);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The Arabic reader (Tesseract) failed");
            problem = $"The Arabic reader isn't available ({ex.GetType().Name}: {ex.Message}), so Arabic text wasn't read.";
            return empty;
        }
    }

    private static byte[] BlankPng()
    {
        using var blank = new SKBitmap(8, 8);
        blank.Erase(SKColors.White);
        return ImageOperations.ToPng(blank);
    }
}
