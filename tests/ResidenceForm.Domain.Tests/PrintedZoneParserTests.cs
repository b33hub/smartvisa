using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Domain.Tests;

public class PrintedZoneParserTests
{
    private readonly CountryResolver _countries = new(new FakeDropdownCatalog());

    // Boxes laid out like the Bangladeshi data page: given name, nationality, birth date, then sex with place of birth on one row, issue, expiry.
    private static List<OcrWord> BangladeshiPage() => new()
    {
        new("P", 600, 1270, 625, 1300),
        new("BGD", 670, 1270, 740, 1300),
        new("A00000000", 1000, 1270, 1150, 1300),       // Passport Number
        new("AA0000000", 1000, 1500, 1150, 1525),       // Previous Passport No: must not be mistaken for the number
        new("JOHN", 506, 1429, 570, 1460),
        new("BANGLADESHI", 505, 1504, 700, 1535),
        new("15MAR1990", 503, 1578, 650, 1605),
        new("M", 503, 1640, 520, 1665),
        new("COMILLA", 682, 1640, 780, 1665),
        new("14FEB2021", 505, 1740, 650, 1765),
        new("13FEB2031", 505, 1808, 650, 1835),
    };

    [Fact]
    public void Reads_number_sex_place_and_nationality_by_position()
    {
        var printed = PrintedZoneParser.Parse(BangladeshiPage(), null, null, null, "JOHN", _countries);

        Assert.Equal("A00000000", printed.Number);
        Assert.Equal('M', printed.Sex);
        Assert.Equal("COMILLA", printed.Place);
        Assert.Equal("Bangladesh - 145", printed.Nationality?.English);
    }

    [Fact]
    public void Assigns_birth_issue_and_expiry_from_top_to_bottom_when_there_is_no_code_line_to_anchor_on()
    {
        var printed = PrintedZoneParser.Parse(BangladeshiPage(), null, null, null, "JOHN", _countries);

        Assert.Equal(new DateOnly(1990, 3, 15), printed.Dob);
        Assert.Equal(new DateOnly(2021, 2, 14), printed.Issue);
        Assert.Equal(new DateOnly(2031, 2, 13), printed.Expiry);
    }

    [Fact]
    public void The_passport_number_matching_the_code_line_wins_over_other_number_like_boxes()
    {
        var printed = PrintedZoneParser.Parse(BangladeshiPage(), "AA0000000", null, null, "JOHN", _countries);

        Assert.Equal("AA0000000", printed.Number);
    }
}
