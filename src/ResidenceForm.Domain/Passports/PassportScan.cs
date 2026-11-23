namespace ResidenceForm.Domain.Passports;

/// <summary>
/// The raw output of reading a passport image, before any interpretation: the two code lines (if found), the printed text
/// boxes (if the printed-text reader worked) and every printed date seen.
/// </summary>
/// <param name="CodeLines">The name line and number line of the machine-readable zone, scored.</param>
/// <param name="PrintedWords">Printed text with positions; null when the printed-text reader was unavailable.</param>
/// <param name="VisualDates">All printed dates found on the page (used for the issue date and the century of birth/expiry).</param>
/// <param name="PrintedReaderProblem">Why the printed-text reader was unavailable, for display.</param>
/// <param name="CodeLineReaderProblem">Why the code-line reader (Tesseract) was unavailable, for display.</param>
public sealed record PassportScan(
    Mrz.MrzPair? CodeLines,
    IReadOnlyList<OcrWord>? PrintedWords,
    IReadOnlyCollection<DateOnly> VisualDates,
    string? PrintedReaderProblem = null,
    string? CodeLineReaderProblem = null);
