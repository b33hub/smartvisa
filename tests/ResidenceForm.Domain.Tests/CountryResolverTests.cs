using ResidenceForm.Domain.Forms;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Domain.Tests;

public class CountryResolverTests
{
    private readonly CountryResolver _resolver = new(new FakeDropdownCatalog());

    [Theory]
    [InlineData("BGD")]
    [InlineData("bgd")]
    public void The_country_code_selects_the_matching_option(string code)
    {
        Assert.Equal(" بنجلاديش - 145", _resolver.FromIso3(code)?.Raw);
    }

    [Theory]
    [InlineData("BANGLADESHI")]
    [InlineData("Bangladeshi")]
    [InlineData("BANGLADESH")]
    [InlineData("BANGLADESHl")]          // one OCR slip
    public void A_printed_nationality_selects_the_matching_option(string printed)
    {
        Assert.Equal("Bangladesh - 145", _resolver.FromText(printed)?.English);
    }

    [Fact]
    public void Regular_nationality_words_are_understood()
    {
        Assert.Equal("India - 14", _resolver.FromText("INDIAN")?.English);
        Assert.Equal("Egypt - 7", _resolver.FromText("EGYPTIAN")?.English);
    }

    [Fact]
    public void Arabic_text_matches_the_arabic_option()
    {
        Assert.Equal("Egypt - 7", _resolver.FromText("مصر")?.English);
    }

    [Fact]
    public void An_unknown_country_gives_nothing()
    {
        Assert.Null(_resolver.FromText("ATLANTIAN"));
        Assert.Null(_resolver.FromIso3("ZZZ"));
    }

    [Fact]
    public void The_employer_list_is_resolved_separately_from_the_applicant_list()
    {
        // Kuwait is only offered for the employer's nationality
        Assert.Null(_resolver.FromIso3("KWT"));
        Assert.Equal("Kuwait - 1", _resolver.FromIso3("KWT", FieldNames.EmployerNationality)?.English);
    }
}
