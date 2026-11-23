using ResidenceForm.Domain.CivilIds;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Domain.Tests;

public class CivilIdInterpreterTests
{
    private static CivilIdInterpreter CreateInterpreter()
    {
        var catalog = new FakeDropdownCatalog();
        return new CivilIdInterpreter(new CountryResolver(catalog), catalog);
    }

    private static CardLine Line(string latin, string arabic, float top) =>
        new(new OcrWord(latin, 340, top, 1050, top + 30), latin, arabic);

    private static CardScan Front() => new(new[]
    {
        Line("Civil ID No 123456789012", "الرقم المدني", 255),
        Line("", "الاسم محمد أحمد علي", 295),
        Line("Name MOHAMED AHMED ALI", "", 425),
        Line("Nationality KWT", "الجنسية", 535),
    });

    private static CardScan Back(params CardLine[] extra) => new(new[]
    {
        Line("123456789012", "الرقم المدني: 123456789012", 200),
        Line("", "المهنة: مهندس", 250),
        Line("", "شارع الخليج العربي", 330),
        Line("5 :", "القطعة : 5", 380),
        Line("12345678", "الرقم الآلي : 12345678", 480),
    }.Concat(extra).ToList());

    [Fact]
    public void The_front_gives_civil_id_arabic_name_and_the_employer_nationality_option()
    {
        var data = CreateInterpreter().InterpretFront(Front());

        Assert.Equal("123456789012", data.CivilId.Value);
        Assert.Equal("محمد أحمد علي", data.ArabicName.Value);          // the "الاسم" label is not part of the name
        Assert.Equal("الكويت - 1", data.Nationality.Value);
    }

    [Fact]
    public void A_placeholder_nationality_code_is_reported_not_guessed()
    {
        var scan = new CardScan(new[] { Line("Nationality NAT", "الجنسية", 535) });

        var data = CreateInterpreter().InterpretFront(scan);

        Assert.False(data.Nationality.HasValue);
        Assert.Contains(data.Notes, n => n.Contains("nationality"));
    }

    [Fact]
    public void The_back_gives_profession_street_block_and_the_eight_digit_paci_number()
    {
        var data = CreateInterpreter().InterpretBack(Back());

        Assert.Equal("مهندس", data.Profession.Value);
        Assert.Equal("الخليج العربي", data.Street.Value);
        Assert.Equal("5", data.Block.Value);
        Assert.Equal("12345678", data.PaciNumber.Value);
    }

    [Fact]
    public void An_area_name_on_the_back_selects_the_area_option()
    {
        var data = CreateInterpreter().InterpretBack(Back(Line("", "السالمية", 300)));

        Assert.Equal("السالمية", data.Area.Value);
        Assert.False(data.Governorate.HasValue);
    }

    [Fact]
    public void A_governorate_name_on_the_back_selects_the_governorate_option_and_is_not_repeated_as_an_area()
    {
        var data = CreateInterpreter().InterpretBack(Back(Line("", "حولي", 300)));

        Assert.Equal("محافطة حولي", data.Governorate.Value);        // exactly as the PDF stores it, including its spelling
        Assert.False(data.Area.HasValue);
    }

    [Fact]
    public void Arabic_matching_ignores_letter_variants_and_diacritics()
    {
        Assert.Equal("المهنه", ArabicText.Normalise("المهنَة"));
        Assert.Equal("الرقم الالي", ArabicText.Normalise("الرقم الآلي"));
        Assert.Equal("123", ArabicText.Normalise("١٢٣"));
        Assert.Equal("مهندس", ArabicText.AfterLabel("المهنة : مهندس", "المهنه"));
    }

    [Fact]
    public void An_empty_scan_explains_itself()
    {
        var data = CreateInterpreter().InterpretBack(new CardScan(Array.Empty<CardLine>()));

        Assert.NotEmpty(data.Notes);
        Assert.All(data.FormMapping(), mapping => Assert.False(mapping.Finding.HasValue));
    }
}
