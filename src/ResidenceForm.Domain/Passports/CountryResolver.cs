using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using ResidenceForm.Domain.Dropdowns;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Domain.Passports;

/// <summary>The country lookups of ONE nationality dropdown (the applicant's and the employer's lists differ slightly).</summary>
internal sealed class CountryIndex
{
    private readonly Dictionary<string, DropdownOption> _byName = new();      // "bangladesh" -> option
    private readonly IReadOnlyDictionary<string, string> _iso3;          // "BGD" -> "bangladesh"
    private readonly Dictionary<string, DropdownOption> _byFlat = new();      // "srilanka" -> option (spaces removed)
    private readonly List<DropdownOption> _all = new();

    // ISO/ICAO codes whose .NET region name differs from the names in the form
    private static readonly Dictionary<string, string> IsoAlias = new()
    {
        ["TUR"] = "turkey", ["KOR"] = "south korea", ["PRK"] = "north korea", ["COD"] = "dr congo", ["COG"] = "republic of the congo",
        ["CIV"] = "ivory coast", ["MMR"] = "myanmar", ["SWZ"] = "swaziland", ["MKD"] = "north macedonia", ["PSE"] = "palestine",
        ["RUS"] = "russia", ["VNM"] = "vietnam", ["SYR"] = "syria", ["IRN"] = "iran", ["GBR"] = "united kingdom", ["USA"] = "united states",
        ["ARE"] = "united arab emirates", ["SAU"] = "saudi arabia", ["LKA"] = "sri lanka", ["PHL"] = "philippines", ["CHN"] = "china",
        ["HKG"] = "hong kong", ["MAC"] = "macau", ["LAO"] = "laos", ["TZA"] = "tanzania", ["MDA"] = "moldova", ["BLR"] = "belarus",
        ["LBY"] = "libya", ["BRN"] = "brunei", ["CPV"] = "cape verde", ["TLS"] = "timor islands", ["FSM"] = "micronesia",
        ["BGD"] = "bangladesh", ["IND"] = "india", ["PAK"] = "pakistan", ["EGY"] = "egypt", ["NPL"] = "nepal", ["JOR"] = "jordan",
        ["LBN"] = "lebanon", ["SDN"] = "sudan", ["YEM"] = "yemen", ["IRQ"] = "iraq", ["ETH"] = "ethiopia", ["KEN"] = "kenya",
        ["IDN"] = "indonesia", ["KWT"] = "kuwait", ["OMN"] = "oman", ["QAT"] = "qatar", ["BHR"] = "bahrain", ["AFG"] = "afghanistan",
    };

    // irregular printed nationalities (letters only, upper case) -> country name
    private static readonly Dictionary<string, string> Demonyms = new()
    {
        ["FILIPINO"] = "philippines", ["SRILANKAN"] = "sri lanka", ["NEPALESE"] = "nepal", ["NEPALI"] = "nepal", ["LEBANESE"] = "lebanon",
        ["SUDANESE"] = "sudan", ["BRITISH"] = "united kingdom", ["AMERICAN"] = "united states", ["EMIRATI"] = "united arab emirates",
        ["SAUDI"] = "saudi arabia", ["TURKISH"] = "turkey", ["THAI"] = "thailand", ["VIETNAMESE"] = "vietnam", ["CHINESE"] = "china",
        ["JAPANESE"] = "japan", ["KOREAN"] = "south korea", ["BURMESE"] = "myanmar", ["PALESTINIAN"] = "palestine", ["SPANISH"] = "spain",
        ["FRENCH"] = "france", ["GERMAN"] = "germany", ["DUTCH"] = "netherlands", ["SWISS"] = "switzerland", ["IRISH"] = "ireland",
        ["SOUTHAFRICAN"] = "south africa", ["SENEGALESE"] = "senegal", ["SOMALI"] = "somalia", ["YEMENI"] = "yemen", ["IRAQI"] = "iraq",
        ["KUWAITI"] = "kuwait", ["OMANI"] = "oman", ["QATARI"] = "qatar", ["BAHRAINI"] = "bahrain", ["PAKISTANI"] = "pakistan",
        ["BANGLADESHI"] = "bangladesh", ["MOROCCAN"] = "morocco", ["LIBYAN"] = "libya", ["AFGHAN"] = "afghanistan", ["GREEK"] = "greece",
    };

    public CountryIndex(IReadOnlyList<DropdownOption> options, IReadOnlyDictionary<string, string> iso3Names)
    {
        _iso3 = iso3Names;
        foreach (var option in options)
        {
            var name = Clean(option.English ?? "");
            if (name.Length > 0)
            {
                _byName[name] = option;
                _byFlat[name.Replace(" ", "")] = option;
            }
            _all.Add(option);
        }
    }

    /// <summary>"Sri Lanka (Ceylon) - 149" -> "sri lanka"</summary>
    internal static string Clean(string s)
    {
        s = Regex.Replace(s, @"\s*-\s*\d+\s*$", "");
        s = Regex.Replace(s, @"\(.*?\)", "");
        var formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in formD) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Trim().ToLowerInvariant();
    }

    public DropdownOption? FromIso3(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim().ToUpperInvariant();
        if (IsoAlias.TryGetValue(code, out var alias) && _byName.TryGetValue(alias, out var a)) return a;
        return _iso3.TryGetValue(code, out var name) && _byName.TryGetValue(name, out var o) ? o : null;
    }

    public DropdownOption? FromDemonym(string? printed)
    {
        var key = Regex.Replace((printed ?? "").ToUpperInvariant(), "[^A-Z]", "");
        if (key.Length < 4) return null;
        if (Demonyms.TryGetValue(key, out var n) && _byName.TryGetValue(n, out var d)) return d;

        // regular forms: BANGLADESH+I, INDIA+N, EGYPT+IAN, NIGERIA+N ... the longest country name that prefixes the word wins
        DropdownOption? best = null; int bestLen = 0;
        foreach (var (name, item) in _byName)
        {
            var flat = name.Replace(" ", "");
            if (flat.Length >= 4 && key.StartsWith(flat, StringComparison.OrdinalIgnoreCase) && key.Length - flat.Length <= 4 && flat.Length > bestLen)
            { best = item; bestLen = flat.Length; }
        }
        return best;
    }

    /// <summary>
    /// Matches any printed nationality or country text to an option of the form's Nationality list:
    /// Arabic text against the Arabic option text, otherwise exact name, demonym (BANGLADESHI), then a one-typo fuzzy match.
    /// </summary>
    public DropdownOption? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (Regex.IsMatch(text, @"\p{IsArabic}"))
        {
            var t = Regex.Replace(text.Trim(), @"\s+", " ");
            foreach (var it in _all)
            {
                var ar = Regex.Replace(it.Arabic, @"\s*-\s*\d+\s*$", "").Trim();
                if (ar.Length >= 3 && (t.Contains(ar) || ar.Contains(t))) return it;
            }
        }

        var key = Regex.Replace(text.ToUpperInvariant(), "[^A-Z]", "").ToLowerInvariant();
        if (key.Length < 3) return null;
        if (_byFlat.TryGetValue(key, out var exact)) return exact;
        var demonym = FromDemonym(text);
        if (demonym != null) return demonym;

        DropdownOption? best = null; int bestDist = 2, bestLen = 0;
        for (int cut = 0; cut <= 3; cut++)
        {
            if (key.Length - cut < 4) break;
            var stem = key[..^cut];
            foreach (var (flat, item) in _byFlat)
            {
                if (flat.Length < 5 || Math.Abs(flat.Length - stem.Length) > 1) continue;
                int dist = Levenshtein(stem, flat);
                if (dist < bestDist || (dist == bestDist && flat.Length > bestLen)) { best = item; bestDist = dist; bestLen = flat.Length; }
            }
        }
        return bestDist <= 1 ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}

/// <summary>
/// Maps a country code (BGD), a printed nationality (BANGLADESHI) or Arabic country text to an option of a nationality dropdown.
/// The field defaults to the applicant's Nationality dropdown; pass <see cref="FieldNames.EmployerNationality"/> for the employer's.
/// </summary>
public sealed class CountryResolver(IDropdownCatalog catalog)
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> RegionNamesByIso3 = new(BuildRegionNames);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CountryIndex> _indexes = new();

    public DropdownOption? FromIso3(string? code, string field = FieldNames.Nationality) => IndexFor(field).FromIso3(code);

    public DropdownOption? FromDemonym(string? printed, string field = FieldNames.Nationality) => IndexFor(field).FromDemonym(printed);

    public DropdownOption? FromText(string? text, string field = FieldNames.Nationality) => IndexFor(field).FromText(text);

    private CountryIndex IndexFor(string field) =>
        _indexes.GetOrAdd(field, name => new CountryIndex(catalog.For(name), RegionNamesByIso3.Value));

    private static IReadOnlyDictionary<string, string> BuildRegionNames()
    {
        var names = new Dictionary<string, string>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                names.TryAdd(region.ThreeLetterISORegionName.ToUpperInvariant(), CountryIndex.Clean(region.EnglishName));
            }
            catch (ArgumentException)
            {
                // a culture without region data
            }
        }
        return names;
    }
}
