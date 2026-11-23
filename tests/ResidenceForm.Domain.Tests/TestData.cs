using ResidenceForm.Domain.Dropdowns;

namespace ResidenceForm.Domain.Tests;

/// <summary>An in-memory <see cref="IDropdownCatalog"/> with just the options the tests need.</summary>
internal sealed class FakeDropdownCatalog : IDropdownCatalog
{
    private readonly Dictionary<string, IReadOnlyList<DropdownOption>> _options = new()
    {
        ["Sex1"] = new[]
        {
            new DropdownOption("ذكر", "ذكر", "Male"),
            new DropdownOption("أنثى", "أنثى", "Female"),
        },
        ["Nationality 1"] = new[]
        {
            new DropdownOption("الكويت - 1", "الكويت - 1", "Kuwait - 1"),
            new DropdownOption("مصر - 7", "مصر - 7", "Egypt - 7"),
        },
        ["Dropdown2"] = new[]
        {
            new DropdownOption("محافطة حولي", "محافطة حولي", "Hawalli Governorate"),
            new DropdownOption("محافطة الفروانية", "محافطة الفروانية", "Farwaniya Governorate"),
        },
        ["Dropdown3"] = new[]
        {
            new DropdownOption("السالمية", "السالمية", "Salmiya"),
            new DropdownOption("حولي", "حولي", "Hawalli"),
        },
        ["Nationality"] = new[]
        {
            new DropdownOption(" بنجلاديش - 145", "بنجلاديش - 145", "Bangladesh - 145"),
            new DropdownOption("الهند - 14", "الهند - 14", "India - 14"),
            new DropdownOption("مصر - 7", "مصر - 7", "Egypt - 7"),
        },
    };

    public IReadOnlyList<DropdownOption> For(string field) =>
        _options.TryGetValue(field, out var options) ? options : Array.Empty<DropdownOption>();

    public DropdownOption? Find(string field, string? raw) => For(field).FirstOrDefault(o => o.Raw == raw);
}

internal static class TestData
{
    /// <summary>The ICAO 9303 specimen passport (Anna Maria Eriksson), the standard test vector for code lines.</summary>
    public const string IcaoNameLine = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
    public const string IcaoNumberLine = "L898902C36UTO7408122F1204159ZE184226B<<<<<10";

    /// <summary>A made-up Bangladeshi passport with correct check digits (number A01234567, born 15/03/1990, expires 13/02/2031).</summary>
    public static readonly string BangladeshiNameLine = "P<BGDRAHMAN<<MD<ABDUL<KARIM".PadRight(44, '<');
    public const string BangladeshiNumberLine = "A012345678BGD9003152M3102134<<<<<<<<<<<<<<04";
}
