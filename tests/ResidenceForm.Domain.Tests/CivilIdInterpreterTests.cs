using ResidenceForm.Domain.CivilIds;
using ResidenceForm.Domain.Documents;
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

    [Fact]
    public void Two_labels_on_one_line_are_read_separately()
    {
        var scan = new CardScan(new[] { Line("12 5", "القطعة : 5 شارع : 12", 380) });

        var data = CreateInterpreter().InterpretBack(scan);

        Assert.Equal("5", data.Block.Value);
        Assert.Equal("12", data.Street.Value);
    }

    [Fact]
    public void The_street_is_a_number_and_the_plot_value_is_the_building()
    {
        var scan = new CardScan(new[] { Line("12 :", "شارع : 12", 330), Line("345 :", "قسيمة : 345", 430) });

        var data = CreateInterpreter().InterpretBack(scan);

        Assert.Equal("12", data.Street.Value);
        Assert.Equal("345", data.Building.Value);
    }

    [Fact]
    public void When_the_arabic_reading_lost_the_digits_two_labels_are_paired_by_position_and_flagged()
    {
        var scan = new CardScan(new[] { Line("12 5", "القطعة شارع", 380) });

        var data = CreateInterpreter().InterpretBack(scan);

        Assert.Equal("5", data.Block.Value);       // the card is read right to left, the Latin numbers left to right
        Assert.Equal("12", data.Street.Value);
        Assert.Equal(Confidence.Check, data.Block.Confidence);
    }

    [Fact]
    public void A_paci_number_with_the_wrong_length_is_kept_but_flagged()
    {
        var scan = new CardScan(new[] { Line("1234567", "الرقم الآلي : 1234567", 480) });

        var data = CreateInterpreter().InterpretBack(scan);

        Assert.Equal("1234567", data.PaciNumber.Value);
        Assert.Equal(Confidence.Check, data.PaciNumber.Confidence);
    }

    // the back of a real Kuwaiti citizen's card, as the readers returned it (box positions from the raw reading)
    private static CardScan RealBack() => new(new[]
    {
        Line("AB+", "فصيلة الدم: +8يم", 60),
        Line("310041000019", "989 صهش32ظ2", 63),
        Line("8:ah3l1", "القطعة: 8", 143),
        Line("829", "شارع 829", 212),
        Line("3", "3", 293),
        Line("15686869", "الرقم الالى للعنوان: 15686869", 358),
        Line("66638484:", "التليفون:66638484", 426),
        Line("144212310041000019<<<", "", 646),
        Line("1004108M2804109KWT15686869<<<5", "", 732),
    });

    [Fact]
    public void The_real_back_gives_block_street_phone_and_a_confirmed_address_number()
    {
        var data = CreateInterpreter().InterpretBack(RealBack());

        Assert.Equal("8", data.Block.Value);
        Assert.Equal("829", data.Street.Value);
        Assert.Equal("66638484", data.Phone.Value);
        Assert.Equal("15686869", data.PaciNumber.Value);
        Assert.Equal(Confidence.Good, data.PaciNumber.Confidence);       // the label and the code line agree
        Assert.Equal(Confidence.Good, data.CivilId.Confidence);          // the number is repeated in the code lines
    }

    [Fact]
    public void A_lone_number_between_the_street_and_the_address_number_is_the_plot_and_is_flagged()
    {
        var data = CreateInterpreter().InterpretBack(RealBack());

        Assert.Equal("3", data.Building.Value);
        Assert.Equal(Confidence.Check, data.Building.Confidence);
    }

    [Fact]
    public void Profession_and_governorate_missing_from_a_citizens_card_are_not_reported_as_failures()
    {
        var data = CreateInterpreter().InterpretBack(RealBack());

        Assert.False(data.Profession.HasValue);
        Assert.False(data.Governorate.HasValue);
        Assert.DoesNotContain(data.Notes, n => n.Contains("couldn't be read") && n.Contains("profession"));
    }

    [Fact]
    public void The_address_number_is_taken_from_the_code_lines_when_its_label_is_unreadable()
    {
        var scan = new CardScan(new[] { Line("1004108M2804109KWT15686869<<<5", "", 732) });

        var data = CreateInterpreter().InterpretBack(scan);

        Assert.Equal("15686869", data.PaciNumber.Value);
        Assert.Equal(Confidence.Read, data.PaciNumber.Confidence);
    }

    [Fact]
    public void A_code_line_with_failing_check_digits_does_not_supply_an_address_number()
    {
        var scan = new CardScan(new[] { Line("1004109M2804109KWT15686869<<<5", "", 732) });      // birth check digit 8 changed to 9

        var data = CreateInterpreter().InterpretBack(scan);

        Assert.False(data.PaciNumber.HasValue);
    }
}
