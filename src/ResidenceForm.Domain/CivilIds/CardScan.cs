using System.Text.RegularExpressions;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Domain.CivilIds;

/// <summary>
/// One line of text found on an ID card. The same box is read twice because no single reader handles both scripts:
/// <see cref="Latin"/> (digits and English, very reliable) and <see cref="Arabic"/> (Arabic letters and labels).
/// </summary>
public sealed partial record CardLine(OcrWord Box, string Latin, string Arabic)
{
    /// <summary>Only the digits of the Latin reading (the Latin reader is the better one for numbers).</summary>
    public string Digits => NonDigitRegex().Replace(Latin, "");

    /// <summary>Latin text as upper-case letters and digits only, for label matching.</summary>
    public string LatinKey => NonAlphanumericRegex().Replace(Latin.ToUpperInvariant(), "");

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitRegex();

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphanumericRegex();
}

/// <summary>The raw reading of one side of an ID card.</summary>
/// <param name="Problem">Why part of the reading is unavailable (for example the Arabic reader), for display.</param>
public sealed record CardScan(IReadOnlyList<CardLine> Lines, string? Problem = null);
