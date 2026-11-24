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
    private const string PhoneLabel = "التليفون";
    private const string PhoneLabelAlternative = "الهاتف";
    private const string PaciLabel = "الرقم الالي";
    private const string SerialLabel = "المسلسل";
    private const string BloodTypeLabel = "فصيله";

    private const int CivilIdLength = 12;
    private const int PaciLength = 8;

    // words that mark a line as something other than a governorate or area name
    private static readonly string[] KnownLabels =
        { ProfessionLabel, StreetLabel, BlockLabel, PlotLabel, FloorLabel, UnitLabel, "الرقم", SerialLabel, BloodTypeLabel, "الدم", "بلوك", PhoneLabel, PhoneLabelAlternative };

    private static readonly string[] GovernorateWords = { "محافظه", "محافطه", "المحافظه", "المحافطه", "المنطقه" };

    // ------------------------------------------------------------------ front

    public CivilIdData InterpretFront(CardScan scan)
    {
        var data = new CivilIdData(CivilIdSide.Front);
        AddScanProblem(data, scan);
        data.SetRawReading(Describe(scan));

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

    // labels that are followed by a number; every one of them also ends the previous label's value on the same line
    private static readonly string[] NumberLabels = { BlockLabel, StreetLabel, PlotLabel, PaciLabel, FloorLabel, UnitLabel, PhoneLabel, PhoneLabelAlternative };

    /// <param name="Inferred">Matched to its label by position because the Arabic reading lost the digits; the person must check it.</param>
    private sealed record LabelledNumber(string Label, string Value, bool Inferred);

    public CivilIdData InterpretBack(CardScan scan)
    {
        var data = new CivilIdData(CivilIdSide.Back);
        AddScanProblem(data, scan);
        data.SetRawReading(Describe(scan));

        var civilIdLine = FindCivilIdLine(scan.Lines);
        data.CivilId = civilIdLine is null ? Finding.None : ReadBackCivilId(scan.Lines, civilIdLine);

        var numbers = ReadLabelledNumbers(scan.Lines);
        data.Block = FromNumber(numbers, BlockLabel);
        data.Building = FromNumber(numbers, PlotLabel);              // the card's قسيمة value is the form's Building
        if (!data.Building.HasValue) data.Building = FindUnlabelledPlot(scan.Lines);
        data.Street = FromNumber(numbers, StreetLabel);
        if (!data.Street.HasValue) data.Street = ReadAfterLabel(scan.Lines, StreetLabel);     // a street given by name instead of number
        data.PaciNumber = ReadPaciNumber(numbers, scan.Lines);
        data.Phone = ReadPhone(numbers);
        data.Profession = ReadAfterLabel(scan.Lines, ProfessionLabel);
        ReadGovernorateAndArea(data, scan.Lines);

        if (!data.PaciNumber.HasValue) data.AddNote("The address PACI number (8 digits) couldn't be read. Please type it.");
        if (!data.Profession.HasValue && !data.Governorate.HasValue && !data.Area.HasValue)
            data.AddNote("Profession and governorate/area are not printed on every card. Type them yourself if you need them.");
        return data;
    }

    /// <summary>The text after the label (letters only; numbers are read by <see cref="ReadLabelledNumbers"/>).</summary>
    private static Finding ReadAfterLabel(IReadOnlyList<CardLine> lines, string label)
    {
        foreach (var line in lines)
        {
            var value = ArabicText.AfterLabel(line.Arabic, label);
            if (value is null) continue;

            var cleaned = DigitsAndPunctuationRegex().Replace(value, " ").Trim();
            if (cleaned.Length > 0) return Read(cleaned, "taken from the card; check every letter");
        }
        return Finding.None;
    }

    /// <summary>
    /// Each label is paired with the number that follows it, so labels sharing one printed line (القطعة and شارع) are read separately.
    /// A line's first reading of a label wins, but a direct reading beats one inferred from position.
    /// </summary>
    private static Dictionary<string, LabelledNumber> ReadLabelledNumbers(IReadOnlyList<CardLine> lines)
    {
        var result = new Dictionary<string, LabelledNumber>();
        foreach (var number in lines.SelectMany(ReadNumbersInLine))
        {
            if (!result.TryGetValue(number.Label, out var existing) || (existing.Inferred && !number.Inferred))
            {
                result[number.Label] = number;
            }
        }
        return result;
    }

    private static IEnumerable<LabelledNumber> ReadNumbersInLine(CardLine line)
    {
        var labels = ArabicText.FindLabels(line.Arabic, NumberLabels);
        if (labels.Count == 0) yield break;

        var tokens = ArabicText.Words(line.Arabic).Select(ArabicText.Normalise).ToArray();
        var unresolved = new List<string>();

        for (var i = 0; i < labels.Count; i++)
        {
            var start = labels[i].Index + labels[i].WordCount;
            var end = i + 1 < labels.Count ? labels[i + 1].Index : tokens.Length;
            var number = tokens.Skip(start).Take(Math.Max(0, end - start)).FirstOrDefault(IsNumber);

            if (number is not null) yield return new LabelledNumber(labels[i].Label, number, Inferred: false);
            else unresolved.Add(labels[i].Label);
        }

        // the Arabic reading lost some digits: use the Latin reading's numbers when they can only belong to the unresolved labels
        var latin = LatinNumberRegex().Matches(line.Latin).Select(m => m.Value).ToList();
        if (unresolved.Count == 0 || latin.Count != unresolved.Count) yield break;

        if (unresolved.Count == 1)
        {
            yield return new LabelledNumber(unresolved[0], latin[0], Inferred: false);
            yield break;
        }

        // an Arabic line is read right to left, but the Latin reader lists its numbers left to right, so pair them in reverse
        latin.Reverse();
        for (var i = 0; i < unresolved.Count; i++)
        {
            yield return new LabelledNumber(unresolved[i], latin[i], Inferred: true);
        }
    }

    private static bool IsNumber(string token) => token.Length > 0 && token.All(char.IsAsciiDigit);

    private static Finding FromNumber(Dictionary<string, LabelledNumber> numbers, string label) =>
        numbers.TryGetValue(label, out var number)
            ? new Finding(number.Value, number.Value, number.Inferred ? Confidence.Check : Confidence.Read,
                number.Inferred ? "matched to its label by position; please check" : null)
            : Finding.None;

    private Finding ReadBackCivilId(IReadOnlyList<CardLine> lines, CardLine civilIdLine)
    {
        // the code lines repeat the civil ID; finding the same number there confirms the reading
        var number = CivilIdDigits(civilIdLine);
        var inCodeLines = lines.Any(l => l != civilIdLine && l.LatinKey.Contains(number, StringComparison.Ordinal));
        return inCodeLines ? new Finding(number, number, Confidence.Good, "also found in the card's code lines") : Read(number);
    }

    private static Finding ReadPaciNumber(Dictionary<string, LabelledNumber> numbers, IReadOnlyList<CardLine> lines)
    {
        var fromCodeLines = FindPaciInCodeLines(lines);

        if (numbers.TryGetValue(PaciLabel, out var labelled))
        {
            if (fromCodeLines == labelled.Value)
            {
                return new Finding(labelled.Value, labelled.Value, Confidence.Good, "confirmed by the card's code lines");
            }

            var hasExpectedLength = labelled.Value.Length == PaciLength;
            var note = fromCodeLines is not null ? $"the code lines say {fromCodeLines}; please check"
                     : hasExpectedLength ? null
                     : $"expected {PaciLength} digits, found {labelled.Value.Length}; please check";
            return new Finding(labelled.Value, labelled.Value, hasExpectedLength && !labelled.Inferred && note is null ? Confidence.Read : Confidence.Check, note);
        }

        if (fromCodeLines is not null) return new Finding(fromCodeLines, fromCodeLines, Confidence.Read, "taken from the card's code lines");

        // no readable label and no code line: the only 8-digit number on the back is the address number
        var eightDigits = lines.FirstOrDefault(l => l.Digits.Length == PaciLength);
        return eightDigits is null ? Finding.None : Read(eightDigits.Digits);
    }

    /// <summary>
    /// The card's second code line ends with the address number in its optional field:
    /// birth date, check digit, sex, expiry date, check digit, nationality, 11 optional characters, check digit.
    /// </summary>
    private static string? FindPaciInCodeLines(IReadOnlyList<CardLine> lines)
    {
        foreach (var line in lines)
        {
            var text = NonCodeLineCharacterRegex().Replace(line.Latin.ToUpperInvariant(), "");
            var match = SecondCodeLineRegex().Match(text);
            if (!match.Success) continue;

            var birthChecked = Mrz.Check(match.Groups[1].Value) == match.Groups[2].Value[0] - '0';
            var expiryChecked = Mrz.Check(match.Groups[4].Value) == match.Groups[5].Value[0] - '0';
            var optional = match.Groups[7].Value.Trim('<');

            // only trust a code line whose own check digits pass, so a misread line can't supply a wrong address number
            if (birthChecked && expiryChecked && optional.Length == PaciLength && optional.All(char.IsAsciiDigit)) return optional;
        }
        return null;
    }

    private static Finding ReadPhone(Dictionary<string, LabelledNumber> numbers)
    {
        var found = numbers.GetValueOrDefault(PhoneLabel) ?? numbers.GetValueOrDefault(PhoneLabelAlternative);
        if (found is null) return Finding.None;

        const int expectedLength = 8;
        var hasExpectedLength = found.Value.Length == expectedLength;
        return new Finding(found.Value, found.Value, hasExpectedLength ? Confidence.Read : Confidence.Check,
            hasExpectedLength ? null : $"expected {expectedLength} digits, found {found.Value.Length}; please check");
    }

    /// <summary>
    /// Some cards print the plot (قسيمة) value as a lone number with no label box beside it. It sits between the street line and the
    /// address-number line, so it is found by position and always marked for checking.
    /// </summary>
    private static Finding FindUnlabelledPlot(IReadOnlyList<CardLine> lines)
    {
        var street = lines.FirstOrDefault(l => ArabicText.ContainsLabel(l.Arabic, StreetLabel));
        var paci = lines.FirstOrDefault(l => ArabicText.ContainsLabel(l.Arabic, PaciLabel));
        if (street is null || paci is null) return Finding.None;          // without both anchors, position says nothing

        var lone = lines
            .Where(l => l.Box.Cy > street.Box.Cy && l.Box.Cy < paci.Box.Cy)
            .Select(l => LoneSmallNumber(l))
            .FirstOrDefault(number => number is not null);
        return lone is null
            ? Finding.None
            : new Finding(lone, lone, Confidence.Check, "no label was read beside this number; taken as the plot (قسيمة) value by position");
    }

    private static string? LoneSmallNumber(CardLine line)
    {
        foreach (var text in new[] { ArabicText.Normalise(line.Arabic), line.Latin.Trim() })
        {
            if (text.Length is >= 1 and <= 3 && text.All(char.IsAsciiDigit)) return text;
        }
        return null;
    }

    private static IEnumerable<string> Describe(CardScan scan) =>
        scan.Lines.Select((l, i) => $"[{i}] y={l.Box.Y1:0}-{l.Box.Y2:0} x={l.Box.X1:0}-{l.Box.X2:0} | latin: {l.Latin} | arabic: {l.Arabic}");

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

    [GeneratedRegex(@"\d+")]
    private static partial Regex LatinNumberRegex();

    [GeneratedRegex("[^A-Z0-9<]")]
    private static partial Regex NonCodeLineCharacterRegex();

    [GeneratedRegex(@"(\d{6})(\d)([MF<])(\d{6})(\d)([A-Z]{3})([A-Z0-9<]{11})(\d)")]
    private static partial Regex SecondCodeLineRegex();

    [GeneratedRegex(@"\s*-\s*\d+\s*$")]
    private static partial Regex OptionNumberSuffixRegex();
}