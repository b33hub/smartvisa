using System.Text.RegularExpressions;
using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Dropdowns;
using ResidenceForm.Domain.Forms;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Domain.CivilIds;

/// <summary>
/// Turns the text lines read from a Kuwaiti civil ID into form values. Numbers come from the Latin reader; Arabic labels
/// (المهنة, شارع, القطعة ...) identify what each line is, so the rules do not depend on exact positions. Everything read is marked
/// <see cref="Confidence.Read"/>: a card has nothing to verify it against, so the person always reviews it.
/// </summary>
public sealed partial class CivilIdInterpreter(CountryResolver countries, IDropdownCatalog dropdowns)
{
    // labels in their normalised form (see ArabicText.Normalise)
    private const string NameLabel = "الاسم";
    private const string ProfessionLabel = "المهنه";
    private const string StreetLabel = "شارع";
    private const string BlockLabel = "القطعه";
    private const string PlotLabel = "قسيمه";
    private const string FloorLabel = "الدور";
    private const string UnitLabel = "الوحده";
    private const string PaciLabel = "الرقم الالي";
    private const string SerialLabel = "المسلسل";
    private const string BloodTypeLabel = "فصيله";

    private const int CivilIdLength = 12;
    private const int PaciLength = 8;

    // words that mark a line as something other than a governorate or area name
    private static readonly string[] KnownLabels =
        { ProfessionLabel, StreetLabel, BlockLabel, PlotLabel, FloorLabel, UnitLabel, "الرقم", SerialLabel, BloodTypeLabel, "الدم", "بلوك" };

    private static readonly string[] GovernorateWords = { "محافظه", "محافطه", "المحافظه", "المحافطه", "المنطقه" };

    // ------------------------------------------------------------------ front

    public CivilIdData InterpretFront(CardScan scan)
    {
        var data = new CivilIdData(CivilIdSide.Front);
        AddScanProblem(data, scan);

        var civilIdLine = FindCivilIdLine(scan.Lines);
        data.CivilId = civilIdLine is null ? Finding.None : Read(CivilIdDigits(civilIdLine));
        if (civilIdLine is null) data.AddNote("The civil ID number couldn't be read. Please type it.");

        data.ArabicName = ReadHolderName(scan.Lines, civilIdLine);
        if (!data.ArabicName.HasValue) data.AddNote("The Arabic name couldn't be read. Please type or paste it.");

        data.Nationality = ReadNationality(scan.Lines);
        if (!data.Nationality.HasValue) data.AddNote("The nationality couldn't be matched to the form's list. Please choose it yourself.");
        return data;
    }

    private Finding ReadHolderName(IReadOnlyList<CardLine> lines, CardLine? civilIdLine)
    {
        var englishNameLine = lines.FirstOrDefault(l => l.LatinKey.StartsWith("NAME", StringComparison.Ordinal));
        var top = civilIdLine?.Box.Cy ?? float.MinValue;
        var bottom = englishNameLine?.Box.Cy ?? float.MaxValue;

        // the Arabic name row sits between the civil ID row and the English name row; take the widest Arabic line there
        var candidates = lines
            .Where(l => ArabicText.HasArabicLetters(l.Arabic) && l.Box.Cy > top && l.Box.Cy < bottom)
            .Select(l => (Line: l, Name: CleanName(l.Arabic)))
            .Where(c => ArabicText.Words(c.Name).Length >= 1 && c.Name.Length >= 3)
            .OrderByDescending(c => c.Line.Box.W)
            .ToList();

        return candidates.Count == 0
            ? Finding.None
            : Read(candidates[0].Name, "taken from the card; check every letter");
    }

    private static string CleanName(string arabic)
    {
        var withoutLabel = ArabicText.RemoveWords(arabic, NameLabel);
        return DigitsAndPunctuationRegex().Replace(withoutLabel, " ").Trim();
    }

    private Finding ReadNationality(IReadOnlyList<CardLine> lines)
    {
        // "Nationality KWT": the code is the text after the English label, or a separate box on the same row
        var labelLine = lines.FirstOrDefault(l => l.LatinKey.Contains("NATIONALITY", StringComparison.Ordinal));
        if (labelLine is null) return Finding.None;

        var tail = labelLine.LatinKey[(labelLine.LatinKey.IndexOf("NATIONALITY", StringComparison.Ordinal) + "NATIONALITY".Length)..];
        var sameRow = lines.Where(l => l != labelLine && Math.Abs(l.Box.Cy - labelLine.Box.Cy) < Math.Max(labelLine.Box.H, l.Box.H));
        var candidates = new[] { tail }.Concat(sameRow.Select(l => l.LatinKey)).Where(t => t.Length >= 3);

        foreach (var candidate in candidates)
        {
            var option = candidate.Length == 3
                ? countries.FromIso3(candidate, FieldNames.EmployerNationality)
                : countries.FromText(candidate, FieldNames.EmployerNationality);
            if (option is not null) return new Finding(option.Raw, option.Display, Confidence.Read, "read from the card's country code");
        }
        return Finding.None;
    }

    // ------------------------------------------------------------------ back

    public CivilIdData InterpretBack(CardScan scan)
    {
        var data = new CivilIdData(CivilIdSide.Back);
        AddScanProblem(data, scan);

        var civilIdLine = FindCivilIdLine(scan.Lines);
        data.CivilId = civilIdLine is null ? Finding.None : Read(CivilIdDigits(civilIdLine));

        data.Profession = ReadAfterLabel(scan.Lines, ProfessionLabel);
        data.Street = ReadAfterLabel(scan.Lines, StreetLabel);
        data.Block = ReadNumberAfterLabel(scan.Lines, BlockLabel);
        data.PaciNumber = ReadPaciNumber(scan.Lines);
        ReadGovernorateAndArea(data, scan.Lines);

        if (!data.PaciNumber.HasValue) data.AddNote("The address PACI number (8 digits) couldn't be read. Please type it.");
        if (!data.Profession.HasValue) data.AddNote("The profession couldn't be read. Please type it.");
        return data;
    }

    private static Finding ReadAfterLabel(IReadOnlyList<CardLine> lines, string label)
    {
        foreach (var line in lines)
        {
            var value = ArabicText.AfterLabel(line.Arabic, label);
            if (!string.IsNullOrWhiteSpace(value)) return Read(DigitsAndPunctuationRegex().Replace(value, " ").Trim(), "taken from the card; check every letter");
        }
        return Finding.None;
    }

    private static Finding ReadNumberAfterLabel(IReadOnlyList<CardLine> lines, string label)
    {
        foreach (var line in lines.Where(l => ArabicText.ContainsLabel(l.Arabic, label)))
        {
            // digits come from the Latin reader when it found any, otherwise from the Arabic reading
            var digits = line.Digits.Length > 0 ? line.Digits : NonDigitRegex().Replace(ArabicText.Normalise(line.Arabic), "");
            if (digits.Length > 0) return Read(digits);
        }
        return Finding.None;
    }

    private Finding ReadPaciNumber(IReadOnlyList<CardLine> lines)
    {
        // the automated address number is the only 8-digit number on the back; prefer the line that carries its label
        var eightDigits = lines.Where(l => l.Digits.Length == PaciLength).ToList();
        var labelled = eightDigits.FirstOrDefault(l => ArabicText.ContainsLabel(l.Arabic, PaciLabel));
        var chosen = labelled ?? eightDigits.FirstOrDefault();
        return chosen is null ? Finding.None : Read(chosen.Digits);
    }

    private void ReadGovernorateAndArea(CivilIdData data, IReadOnlyList<CardLine> lines)
    {
        var governorates = dropdowns.For(FieldNames.EmployerGovernorate);
        var areas = dropdowns.For(FieldNames.EmployerArea);

        foreach (var line in lines.Where(l => ArabicText.HasArabicLetters(l.Arabic) && !IsKnownLabelLine(l.Arabic)))
        {
            var text = ArabicText.RemoveWords(line.Arabic, GovernorateWords);

            // a governorate name also exists as an area name (Hawalli), so a governorate match is not repeated as an area
            var governorate = data.Governorate.HasValue ? null : MatchOption(text, governorates);
            if (governorate is not null)
            {
                data.Governorate = new Finding(governorate.Raw, governorate.Display, Confidence.Read, "matched the card's text to the form's list");
                continue;
            }

            var area = data.Area.HasValue ? null : MatchOption(text, areas);
            if (area is not null)
            {
                data.Area = new Finding(area.Raw, area.Display, Confidence.Read, "matched the card's text to the form's list");
            }
        }
    }

    private static bool IsKnownLabelLine(string arabic) =>
        KnownLabels.Any(label => ArabicText.ContainsLabel(arabic, label));

    /// <summary>The longest option whose Arabic name appears in the text (or the text appears in it), ignoring letter variants and "governorate" prefixes.</summary>
    private static DropdownOption? MatchOption(string text, IReadOnlyList<DropdownOption> options)
    {
        var haystack = ArabicText.Normalise(text);
        if (haystack.Length < 3) return null;

        DropdownOption? best = null;
        var bestLength = 0;
        foreach (var option in options)
        {
            var name = ArabicText.RemoveWords(OptionNumberSuffixRegex().Replace(option.Arabic, ""), "محافظه", "محافطه", "محافظة", "محافطة");
            var core = ArabicText.Normalise(name);
            if (core.Length < 3) continue;

            var matches = haystack == core || haystack.Contains(core, StringComparison.Ordinal) || (haystack.Length >= 4 && core.Contains(haystack, StringComparison.Ordinal));
            if (matches && core.Length > bestLength)
            {
                best = option;
                bestLength = core.Length;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ shared

    private static CardLine? FindCivilIdLine(IReadOnlyList<CardLine> lines)
    {
        var candidates = lines.Where(l => l.Digits.Length == CivilIdLength).ToList();
        return candidates.FirstOrDefault(l => ArabicText.ContainsLabel(l.Arabic, "الرقم المدني")) ?? candidates.FirstOrDefault();
    }

    private static string CivilIdDigits(CardLine line) => line.Digits;

    private static Finding Read(string value, string? note = null) =>
        new(value, value, Confidence.Read, note);

    private static void AddScanProblem(CivilIdData data, CardScan scan)
    {
        if (scan.Problem is not null) data.AddNote(scan.Problem);
        if (scan.Lines.Count == 0) data.AddNote("No text could be read from this image. Try a straight, sharp, well-lit photo of the whole card.");
    }

    [GeneratedRegex(@"[\d\p{P}\p{S}]+")]
    private static partial Regex DigitsAndPunctuationRegex();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigitRegex();

    [GeneratedRegex(@"\s*-\s*\d+\s*$")]
    private static partial Regex OptionNumberSuffixRegex();
}
