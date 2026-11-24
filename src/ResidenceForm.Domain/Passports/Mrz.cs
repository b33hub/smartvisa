using System.Text.RegularExpressions;

namespace ResidenceForm.Domain.Passports;

/// <summary>ICAO 9303 TD3 (passport) machine-readable-zone parsing, tolerant of typical OCR mistakes.</summary>
public static class Mrz
{
    private static readonly int[] W = { 7, 3, 1 };

    private static readonly Dictionary<char, char> ToDigit = new()
    { ['O'] = '0', ['Q'] = '0', ['D'] = '0', ['I'] = '1', ['L'] = '1', ['Z'] = '2', ['S'] = '5', ['B'] = '8', ['G'] = '6' };
    private static readonly Dictionary<char, char> ToAlpha = new()
    { ['0'] = 'O', ['1'] = 'I', ['5'] = 'S', ['8'] = 'B', ['2'] = 'Z', ['6'] = 'G' };

    public static int Value(char c) => c == '<' ? 0 : char.IsAsciiDigit(c) ? c - '0' : c is >= 'A' and <= 'Z' ? c - 'A' + 10 : 0;

    public static int Check(string s)
    {
        int sum = 0;
        for (int i = 0; i < s.Length; i++) sum += Value(s[i]) * W[i % 3];
        return sum % 10;
    }

    private static string FixDigits(string s) => new(s.Select(c => ToDigit.GetValueOrDefault(c, c)).ToArray());
    private static string FixAlpha(string s) => new(s.Select(c => ToAlpha.GetValueOrDefault(c, c)).ToArray());

    private static bool ValidDate(string s) =>
        s.Length == 6 && s.All(char.IsAsciiDigit) && int.Parse(s[2..4]) is >= 1 and <= 12 && int.Parse(s[4..6]) is >= 1 and <= 31;

    // ------------------------------------------------------------------ line 1

    public sealed record Line1(string TypeCode, string Country, string Surname, string GivenNames);

    private static bool Junk(string t) =>
        Regex.IsMatch(t, @"(.)\1\1") || (t.Length >= 4 && !Regex.IsMatch(t, "[AEIOUY]"));    // filler noise read as letters

    public static Line1? ParseLine1(string line)
    {
        line = line.Replace(" ", "").Replace("«", "<<");
        var m = Regex.Match(line, @"^([PIAC])([A-Z<])([A-Z0-9<]{3})(.*)$");
        if (!m.Success) return null;

        var type = m.Groups[1].Value + (m.Groups[2].Value == "<" ? "" : m.Groups[2].Value);
        var parts = m.Groups[4].Value.Split("<<");
        var surname = FixAlpha(parts[0]).Replace('<', ' ').Trim();

        var given = new List<string>();
        if (parts.Length > 1)
            foreach (var raw in parts[1].Split('<'))
            {
                var t = FixAlpha(raw);
                if (t.Length == 0) continue;
                if (Junk(t)) break;                                  // everything after is filler
                given.Add(t);
            }
        return new Line1(type, FixAlpha(m.Groups[3].Value), surname, string.Join(' ', given));
    }

    // ------------------------------------------------------------------ line 2

    public sealed record Line2(string Number, bool NumberOk, string Nationality, string Dob, bool DobOk, char Sex, string Expiry, bool ExpiryOk, int Score);

    private static (string number, bool ok) NormaliseNumber(string field, string checkDigit)
    {
        // letters may only lead; after that confusable letters are digits. Prefix length 1 or 2 (or 0) is decided by the check digit.
        string? firstGuess = null;
        foreach (var k in new[] { 1, 2, 0 })
        {
            if (k > field.Length) continue;
            var cand = field[..k] + new string(field[k..].Select(c => ToDigit.GetValueOrDefault(c, c)).ToArray());
            firstGuess ??= cand;
            if (Check(cand).ToString() == checkDigit) return (cand, true);
        }
        return (firstGuess ?? field, false);
    }

    private static Line2 Parse44(string l)
    {
        var (num, numOk) = NormaliseNumber(l[0..9], FixDigits(l[9..10]));
        var nat = FixAlpha(l[10..13]);
        var dob = FixDigits(l[13..19]);
        var exp = FixDigits(l[21..27]);
        bool dobOk = ValidDate(dob) && Check(dob).ToString() == FixDigits(l[19..20]);
        bool expOk = ValidDate(exp) && Check(exp).ToString() == FixDigits(l[27..28]);
        int score = (numOk ? 3 : 0) + (dobOk ? 3 : 0) + (expOk ? 3 : 0) + (ValidDate(dob) ? 1 : 0) + (ValidDate(exp) ? 1 : 0)
                  + ("MF<".Contains(l[20]) ? 1 : 0) + (Regex.IsMatch(nat, "^[A-Z<]{3}$") ? 1 : 0);
        return new Line2(num.Replace("<", ""), numOk, nat, dob, dobOk, l[20], exp, expOk, score);
    }

    private static IEnumerable<string> Candidates(string l, string? country)
    {
        l = l.Replace(" ", "");
        if (l.Length == 44) yield return l;
        else if (l.Length > 44) for (int i = 0; i + 44 <= l.Length; i++) yield return l.Substring(i, 44);
        else if (l.Length >= 38) for (int p = 26; p <= Math.Min(42, l.Length); p++) yield return l[..p] + new string('<', 44 - l.Length) + l[p..];

        if (country != null)                                          // anchor on the country code taken from line 1
            for (int j = 6; j < 15 && j + 3 <= l.Length; j++)
                if (FixAlpha(l.Substring(j, 3)) == country)
                {
                    var c = j >= 10 ? l[(j - 10)..] : new string('<', 10 - j) + l;
                    yield return (c + new string('<', 44))[..44];
                }
    }

    public static Line2? BestLine2(IEnumerable<string> lines, string? country)
    {
        Line2? best = null;
        foreach (var l in lines)
            foreach (var c in Candidates(l, country))
            {
                var p = Parse44(c);
                if (best == null || p.Score > best.Score) best = p;
            }
        return best;
    }

    public sealed record MrzPair(Line1? L1, Line2? L2, int Score);

    /// <summary>Tries every consecutive pair of OCR lines as (name line, number line) and keeps the best-scoring one, so noise lines can't hide the real pair.</summary>
    public static MrzPair? FindPair(IReadOnlyList<string> lines)
    {
        MrzPair? best = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var l1 = i > 0 ? ParseLine1(lines[i - 1]) : null;
            var l2 = BestLine2(new[] { lines[i] }, l1?.Country);
            if (l2 == null) continue;
            int total = l2.Score + (l1 != null && l1.Surname.Length > 0 ? 2 : 0) + (l1 != null && l2.Nationality == l1.Country ? 2 : 0);
            if (best == null || total > best.Score) best = new MrzPair(l1, l2, total);
        }
        return best;
    }

    // ------------------------------------------------------------------ OCR text -> candidate lines

    public static List<string> LongLines(string ocrText) =>
        ocrText.Split('\n')
               .Select(s => Regex.Replace(s.ToUpperInvariant().Replace("«", "<<"), "[^A-Z0-9<]", ""))
               .Where(s => s.Length >= 36)
               .ToList();

    // ------------------------------------------------------------------ dates

    public static DateOnly? YymmddToDate(string yymmdd, bool birth, IReadOnlyCollection<DateOnly> visual)
    {
        if (!ValidDate(yymmdd)) return null;
        int yy = int.Parse(yymmdd[..2]), mm = int.Parse(yymmdd[2..4]), dd = int.Parse(yymmdd[4..6]);
        var hit = visual.FirstOrDefault(v => v.Month == mm && v.Day == dd && v.Year % 100 == yy);     // visual zone settles the century
        if (hit != default) return hit;
        int cur = DateTime.Today.Year % 100;
        int year = birth ? (yy > cur ? 1900 + yy : 2000 + yy) : 2000 + yy;
        try { return new DateOnly(year, mm, dd); } catch { return null; }
    }

    private static readonly string[] Months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    private static int MonthIndex(string m)
    {
        m = m.ToUpperInvariant();
        int exact = Array.IndexOf(Months, m);
        if (exact >= 0) return exact + 1;
        var near = Months.Select((x, i) => (x, i)).Where(t => t.x.Zip(m, (a, b) => a != b).Count(d => d) <= 1).ToList();
        return near.Count == 1 ? near[0].i + 1 : -1;                     // one OCR slip allowed, must be unambiguous
    }

    private static readonly Regex TextDate = new(@"(?<!\d)(\d{1,2})\s*([A-Za-z]{3})[A-Za-z]*(?:\s*/\s*[A-Za-z]{3,})?\s*(\d{4})(?!\d)");
    private static readonly Regex NumDate = new(@"(?<!\d)(\d{2})[./-](\d{2})[./-](\d{4})(?!\d)");

    public static HashSet<DateOnly> VisualDates(string text)
    {
        var set = new HashSet<DateOnly>();
        void Add(int y, int m, int d)
        {
            if (y < 1900 || y > 2100 || m < 1 || m > 12 || d < 1 || d > 31) return;
            try { set.Add(new DateOnly(y, m, d)); } catch { }
        }
        foreach (Match m in TextDate.Matches(text))
        {
            int mo = MonthIndex(m.Groups[2].Value);
            if (mo > 0) Add(int.Parse(m.Groups[3].Value), mo, int.Parse(m.Groups[1].Value));
        }
        foreach (Match m in NumDate.Matches(text)) Add(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        return set;
    }

    /// <summary>The issue date is a printed date strictly between birth and expiry; prefer one that gives a 5 or 10 year validity.</summary>
    public static (DateOnly? date, bool confident) PickIssue(DateOnly? dob, DateOnly? expiry, IEnumerable<DateOnly> visual)
    {
        if (dob == null || expiry == null) return (null, false);
        var cands = visual.Where(d => d > dob && d < expiry).ToList();
        if (cands.Count == 0) return (null, false);

        DateOnly? best = null; double bestGap = double.MaxValue;
        foreach (var d in cands)
        {
            double days = expiry.Value.DayNumber - d.DayNumber;
            double gap = new[] { 5.0, 10.0, 3.0 }.Min(y => Math.Abs(days - y * 365.25));
            if (gap < bestGap) { bestGap = gap; best = d; }
        }
        if (bestGap <= 45) return (best, true);                          // validity matches 5/10 (or 3) years within ~6 weeks
        return cands.Count == 1 ? (cands[0], false) : (null, false);
    }

    /// <summary>Exactly one printed date in the text (e.g. "12 MAR 2020" or "12MAR2020"), otherwise null.</summary>
    public static DateOnly? SingleDate(string text)
    {
        var dates = VisualDates(text);
        return dates.Count == 1 ? dates.First() : null;
    }
}
