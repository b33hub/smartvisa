using Tesseract;

namespace ResidenceForm.Infrastructure.Ocr;

/// <summary>Plain-text OCR with Tesseract. A new engine per call: the engine is not thread-safe and starts in milliseconds.</summary>
internal sealed class TesseractTextReader(string tessdataDirectory)
{
    public const string English = "eng";
    public const string Arabic = "ara";

    /// <param name="allowedCharacters">Restricts the alphabet (for the code lines); null for no restriction.</param>
    public string Read(byte[] png, PageSegMode layout, string? allowedCharacters = null, string language = English)
    {
        using var engine = CreateEngine(language, allowedCharacters);
        return ReadWith(engine, png, layout);
    }

    /// <summary>Reads many small images (one text line each) with a single engine; much faster than one engine per image.</summary>
    public IReadOnlyList<string> ReadLines(IReadOnlyList<byte[]> pngs, string language)
    {
        using var engine = CreateEngine(language, null);
        return pngs.Select(png => ReadWith(engine, png, PageSegMode.SingleLine).Trim()).ToList();
    }

    private TesseractEngine CreateEngine(string language, string? allowedCharacters)
    {
        var engine = new TesseractEngine(tessdataDirectory, language, EngineMode.LstmOnly);
        if (allowedCharacters is not null) engine.SetVariable("tessedit_char_whitelist", allowedCharacters);
        return engine;
    }

    private static string ReadWith(TesseractEngine engine, byte[] png, PageSegMode layout)
    {
        using var image = Pix.LoadFromMemory(png);
        using var page = engine.Process(image, layout);
        return page.GetText() ?? "";
    }
}
