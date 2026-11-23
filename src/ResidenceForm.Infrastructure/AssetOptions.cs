namespace ResidenceForm.Infrastructure;

/// <summary>Where the files the app needs live. Set once by the composition root (the Web project).</summary>
public sealed class AssetOptions
{
    public string RootPath { get; set; } = "Assets";

    public string BlankFormPath => Path.Combine(RootPath, "blank.pdf");
    public string FontPath => Path.Combine(RootPath, "Amiri-Regular.ttf");
    public string TranslationsPath => Path.Combine(RootPath, "translations.json");
    public string TessdataPath => Path.Combine(RootPath, "tessdata");
    public string OcrModelsPath => Path.Combine(RootPath, "ocr");
}
