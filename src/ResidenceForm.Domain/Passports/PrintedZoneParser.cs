using System.Text.RegularExpressions;

using ResidenceForm.Domain.Dropdowns;

namespace ResidenceForm.Domain.Passports;

public sealed record PrintedValues(string? Number, DateOnly? Dob, DateOnly? Issue, DateOnly? Expiry,
                               char? Sex, string? Place, DropdownOption? Nationality, string? NationalityText);

/// <summary>
/// Picks the printed values out of the OCR boxes of a passport data page by position, following the Bangladeshi layout
/// (left column: given name, Nationality, Date of Birth, Sex, Date of Issue, Date of Expiry; Place of Birth right of Sex).
/// The labels themselves are bilingual and often unreadable, so values are located by their pattern and neighbours.
/// </summary>
public static class PrintedZoneParser
{
    public static PrintedValues Parse(IReadOnlyList<OcrWord> words, string? mrzNumber, DateOnly? mrzDob, DateOnly? mrzExpiry,
                                string? givenNames, CountryResolver countries)
    {
        // ---- passport number: letters+digits, the one matching the code line, otherwise the top-most (not "Previous Passport No.")
        string? number = null;
        var numCands = words.Where(w => Regex.IsMatch(w.Key, @"^[A-Z]{1,2}\d{7,8}$")).ToList();
        if (numCands.Count > 0)
            number = (mrzNumber != null
                ? numCands.OrderBy(w => Distance(w.Key, mrzNumber)).First()
                : numCands.OrderBy(w => w.Y1).First()).Key;

        // ---- dates
        var dated = words.Select(w => (w, d: Mrz.SingleDate(w.Text)))
                         .Where(t => t.d != null).Select(t => (t.w, d: t.d!.Value))
                         .OrderBy(t => t.w.Cy).ToList();
        DateOnly? dob = null, issue = null, expiry = null;
        if (mrzDob != null && dated.Any(t => t.d == mrzDob)) dob = mrzDob;
        if (mrzExpiry != null && dated.Any(t => t.d == mrzExpiry)) expiry = mrzExpiry;

        if (dob != null && expiry != null)
            issue = Mrz.PickIssue(dob, expiry, dated.Select(t => t.d)).date;
        else if (dated.Count == 3 && dated[0].d < dated[1].d && dated[1].d < dated[2].d)
        { dob = dated[0].d; issue = dated[1].d; expiry = dated[2].d; }        // top to bottom: birth, issue, expiry
        else if (dob != null)                                                   // anchor on birth only: next date below it, before expiry
            issue = dated.Where(t => t.d > dob && t.d != expiry).Select(t => (DateOnly?)t.d).FirstOrDefault();

        // ---- sex and place of birth share a row
        var dobBox = dated.Count > 0 ? dated[0].w : null;
        var sexBoxes = words.Where(w => w.Key.Length == 1 && w.Key is "M" or "F" or "X" && w.Text.Trim().Length == 1).ToList();
        OcrWord? sexBox = sexBoxes
            .Where(w => dobBox == null || w.Cy > dobBox.Cy)
            .OrderBy(w => dobBox == null ? 0 : Math.Abs(w.Cx - dobBox.Cx) + (w.Cy - dobBox.Cy))
            .FirstOrDefault();

        char? sex = sexBox?.Key[0];
        string? place = null;
        if (sexBox != null)
        {
            var row = words.Where(w => w != sexBox && Math.Abs(w.Cy - sexBox.Cy) < Math.Max(sexBox.H, w.H) * 0.8f
                                       && w.X1 >= sexBox.X2 - 2 && Regex.IsMatch(w.Text.Trim(), @"^[A-Za-z][A-Za-z .,'/-]{2,}$"))
                           .OrderBy(w => w.X1).FirstOrDefault();
            place = row?.Text.Trim().ToUpperInvariant();
        }
        else
        {
            var merged = words.Select(w => Regex.Match(w.Text.Trim(), @"^([MF])\s*([A-Za-z][A-Za-z .,'/-]{2,})$")).FirstOrDefault(m => m.Success);
            if (merged != null) { sex = merged.Groups[1].Value[0]; place = merged.Groups[2].Value.ToUpperInvariant(); }
        }

        // ---- nationality: the first recognisable country word below the given-name box, in the same column
        string? natText = null; DropdownOption? nat = null;
        var given = Regex.Replace((givenNames ?? "").ToUpperInvariant(), "[^A-Z]", "");
        if (given.Length >= 2)
        {
            var givenBox = words.Where(w => w.Key == given).OrderByDescending(w => w.Cy).FirstOrDefault();
            if (givenBox != null)
            {
                var below = words.Where(w => w.Cy > givenBox.Cy && Math.Abs(w.X1 - givenBox.X1) <= 3 * givenBox.H
                                             && Regex.IsMatch(w.Text.Trim(), @"^[A-Z][A-Z .'-]{3,}$"))
                                 .OrderBy(w => w.Cy).Take(4).ToList();
                foreach (var w in below)
                {
                    var o = countries.FromText(w.Text);
                    if (o != null) { nat = o; natText = w.Text.Trim(); break; }
                }
                natText ??= below.FirstOrDefault()?.Text.Trim();
            }
        }

        if (nat == null)                                                       // anchor missing: any country-like word on the page
            foreach (var w in words.Where(w => w.Key.Length is >= 5 and <= 24 && Regex.IsMatch(w.Text.Trim(), @"^[A-Z][A-Z .'-]{4,}$")
                                               && !w.Key.Contains("REPUBLIC") && !w.Key.Contains("PEOPLE")).OrderBy(w => w.Cy))
            {
                var o = countries.FromDemonym(w.Text);
                if (o != null) { nat = o; natText = w.Text.Trim(); break; }
            }

        return new PrintedValues(number, dob, issue, expiry, sex, place, nat, natText);
    }

    private static int Distance(string a, string b)
    {
        int n = Math.Max(a.Length, b.Length), d = 0;
        for (int i = 0; i < n; i++) if (i >= a.Length || i >= b.Length || a[i] != b[i]) d++;
        return d;
    }
}
