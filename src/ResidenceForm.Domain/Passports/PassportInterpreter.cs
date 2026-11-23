using System.Globalization;
using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Dropdowns;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Domain.Passports;

/// <summary>
/// Turns a raw <see cref="PassportScan"/> into form-ready values. The rule that matters: a code-line value whose check
/// digit passed always wins over printed text; printed text is used when the code line is unverified; values both sources
/// agree on are marked verified. Pure logic, no I/O.
/// </summary>
public sealed class PassportInterpreter(CountryResolver countries, IDropdownCatalog dropdowns)
{
    public PassportData Interpret(PassportScan scan)
    {
        var data = new PassportData();
        var line1 = scan.CodeLines?.L1;
        var line2 = scan.CodeLines?.L2;

        if (line2 is null && scan.PrintedWords is null)
        {
            data.AddNote("Couldn't find the two machine-readable lines at the bottom of the photo page. Try a straight, well-lit, sharp photo of the data page.");
            return data;
        }

        if (scan.PrintedWords is null)
        {
            var reason = scan.PrintedReaderProblem is null ? "" : $" ({scan.PrintedReaderProblem})";
            data.AddNote($"The printed-text reader isn't available{reason}, so only the code lines and visible dates were used. Place of birth and printed nationality couldn't be read.");
        }

        if (scan.CodeLineReaderProblem is not null)
        {
            data.AddNote($"The code-line reader isn't available ({scan.CodeLineReaderProblem}), so the printed text was used for everything.");
        }

        var codeLinesClean = line2 is { NumberOk: true, DobOk: true, ExpiryOk: true };
        var codeBirth = line2 is null ? null : Mrz.YymmddToDate(line2.Dob, birth: true, scan.VisualDates);
        var codeExpiry = line2 is null ? null : Mrz.YymmddToDate(line2.Expiry, birth: false, scan.VisualDates);
        var printed = scan.PrintedWords is null
            ? null
            : PrintedZoneParser.Parse(scan.PrintedWords, line2?.Number, codeBirth, codeExpiry, line1?.GivenNames, countries);

        ReadName(data, line1);
        ReadNumberAndDates(data, line2, codeBirth, codeExpiry, printed);
        ReadIssueDate(data, codeBirth, codeExpiry, printed, scan.VisualDates);
        ReadSex(data, line2, printed, codeLinesClean);
        ReadNationality(data, line1, line2, printed, codeLinesClean);
        ReadPlaceOfBirth(data, printed);

        if (!codeLinesClean)
        {
            data.AddNote("Some values failed the passport's built-in check digits, so the photo may have been misread. Compare every value with your passport.");
        }
        return data;
    }

    // ------------------------------------------------------------------ one method per kind of value

    private static void ReadName(PassportData data, Mrz.Line1? line1)
    {
        if (line1 is null || string.IsNullOrWhiteSpace(line1.Surname + line1.GivenNames))
        {
            data.AddNote("The name line was not readable.");
            return;
        }

        var name = string.Join(' ', new[] { line1.GivenNames, line1.Surname }.Where(part => !string.IsNullOrWhiteSpace(part)));
        data.FullName = new Finding(name, name, Confidence.Read, "taken from the code line, so special characters are simplified");
        data.PassportType = line1.TypeCode switch
        {
            "P" => new Finding("عادي", "Ordinary passport  ·  عادي", Confidence.Read),
            "PD" => new Finding("دبلوماسي", "Diplomatic passport  ·  دبلوماسي", Confidence.Read),
            "PO" or "PS" => new Finding("خدمة", "Official / service passport  ·  خدمة", Confidence.Read),
            _ => Finding.None
        };
    }

    private static void ReadNumberAndDates(PassportData data, Mrz.Line2? line2, DateOnly? codeBirth, DateOnly? codeExpiry, PrintedValues? printed)
    {
        data.Number = Choose(NullIfEmpty(line2?.Number), line2?.NumberOk ?? false, printed?.Number);
        data.DateOfBirth = Choose(Format(codeBirth), (line2?.DobOk ?? false) && codeBirth is not null, Format(printed?.Dob));
        data.ExpiryDate = Choose(Format(codeExpiry), (line2?.ExpiryOk ?? false) && codeExpiry is not null, Format(printed?.Expiry));
    }

    private static void ReadIssueDate(PassportData data, DateOnly? codeBirth, DateOnly? codeExpiry, PrintedValues? printed, IReadOnlyCollection<DateOnly> visualDates)
    {
        // the issue date exists only as printed text
        var birth = codeBirth ?? printed?.Dob;
        var expiry = codeExpiry ?? printed?.Expiry;

        var issue = printed?.Issue;
        bool validityFits;
        if (issue is not null)
        {
            validityFits = Mrz.PickIssue(birth, expiry, new[] { issue.Value }).confident;
        }
        else
        {
            (issue, validityFits) = Mrz.PickIssue(birth, expiry, visualDates);
        }

        if (issue is null)
        {
            data.AddNote("The issue date couldn't be read. Please type it.");
            return;
        }

        data.IssueDate = new Finding(
            Format(issue), Format(issue),
            validityFits ? Confidence.Good : Confidence.Read,
            validityFits ? "the dates give a normal 5 or 10 year validity" : "printed date, compare with your passport");
    }

    private void ReadSex(PassportData data, Mrz.Line2? line2, PrintedValues? printed, bool codeLinesClean)
    {
        char? fromCode = line2 is not null && (line2.Sex is 'M' or 'F') ? line2.Sex : null;
        char? fromPrint = printed is not null && (printed.Sex is 'M' or 'F') ? printed.Sex : null;
        if (fromCode is null && fromPrint is null) return;

        var sex = fromCode ?? fromPrint!.Value;
        var option = dropdowns.For(FieldNames.Sex).FirstOrDefault(o => o.English == (sex == 'M' ? "Male" : "Female"));
        if (option is null) return;

        var confidence = (fromCode, fromPrint) switch
        {
            (not null, not null) => fromCode == fromPrint ? Confidence.Good : Confidence.Check,
            (not null, null) => codeLinesClean ? Confidence.Good : Confidence.Check,
            _ => Confidence.Read
        };
        var note = fromCode is not null && fromPrint is not null && fromCode != fromPrint ? $"the printed text says {fromPrint}" : null;
        data.Sex = new Finding(option.Raw, option.Display, confidence, note);
    }

    private void ReadNationality(PassportData data, Mrz.Line1? line1, Mrz.Line2? line2, PrintedValues? printed, bool codeLinesClean)
    {
        var fromCode = countries.FromIso3(line1?.Country) ?? countries.FromIso3(line2?.Nationality);
        var fromPrint = printed?.Nationality;

        if (fromCode is null && fromPrint is null)
        {
            if (printed?.NationalityText is not null)
            {
                data.AddNote($"The printed nationality \"{printed.NationalityText}\" didn't match any country in the form's list. Please choose it yourself.");
            }
            return;
        }

        var chosen = fromCode ?? fromPrint!;
        var confidence = (fromCode, fromPrint) switch
        {
            (not null, not null) => fromCode.Raw == fromPrint.Raw ? Confidence.Good : Confidence.Check,
            (not null, null) => codeLinesClean ? Confidence.Good : Confidence.Check,
            _ => Confidence.Read
        };
        var note = fromCode is not null && fromPrint is not null && fromCode.Raw != fromPrint.Raw ? $"the printed text says {fromPrint.Label}" : null;
        data.Nationality = new Finding(chosen.Raw, chosen.Display, confidence, note);
    }

    private static void ReadPlaceOfBirth(PassportData data, PrintedValues? printed)
    {
        if (string.IsNullOrWhiteSpace(printed?.Place)) return;
        data.PlaceOfBirth = new Finding(printed.Place, printed.Place, Confidence.Read, "printed text, compare with your passport");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The code-line value wins when its check digit passed; otherwise printed text; otherwise the unverified code-line value.</summary>
    private static Finding Choose(string? fromCode, bool codeVerified, string? fromPrint)
    {
        if (fromCode is not null && codeVerified)
        {
            return new Finding(fromCode, fromCode, Confidence.Good, fromPrint is not null && fromPrint != fromCode ? $"the printed text says {fromPrint}" : null);
        }
        if (fromPrint is not null)
        {
            var agrees = fromCode == fromPrint;
            return new Finding(fromPrint, fromPrint, agrees ? Confidence.Good : Confidence.Check,
                fromCode is not null && !agrees ? "the code line says something different" : "not confirmed by the code line");
        }
        return fromCode is not null
            ? new Finding(fromCode, fromCode, Confidence.Check, "failed the passport's check digit")
            : Finding.None;
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string? Format(DateOnly? date) => date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
