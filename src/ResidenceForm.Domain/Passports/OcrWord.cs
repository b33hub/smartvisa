using System.Text.RegularExpressions;

namespace ResidenceForm.Domain.Passports;

/// <summary>One recognised text line/box with its position in image pixels (origin top-left).</summary>
public sealed record OcrWord(string Text, float X1, float Y1, float X2, float Y2)
{
    /// <summary>The recognised text without control characters (a stray carriage return would split words and break every pattern).</summary>
    public string Text { get; init; } = RemoveControlCharacters(Text);

    private static string RemoveControlCharacters(string text) => string.Concat(text.Where(c => !char.IsControl(c)));

    public float Cx => (X1 + X2) / 2;
    public float Cy => (Y1 + Y2) / 2;
    public float H => Y2 - Y1;
    public float W => X2 - X1;
    /// <summary>Upper-case letters and digits only (OCR often drops spaces, so comparisons ignore them).</summary>
    public string Key => Regex.Replace(Text.ToUpperInvariant(), "[^A-Z0-9]", "");
}
