using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Domain.Tests;

public class PassportInterpreterTests
{
    private static PassportInterpreter CreateInterpreter()
    {
        var catalog = new FakeDropdownCatalog();
        return new PassportInterpreter(new CountryResolver(catalog), catalog);
    }

    private static PassportScan CodeLinesOnlyScan() => new(
        Mrz.FindPair(new[] { TestData.BangladeshiNameLine, TestData.BangladeshiNumberLine }),
        PrintedWords: null,
        VisualDates: new HashSet<DateOnly>(),
        PrintedReaderProblem: "test");

    [Fact]
    public void Verified_code_lines_give_verified_values_in_form_format()
    {
        var data = CreateInterpreter().Interpret(CodeLinesOnlyScan());

        Assert.Equal("A01234567", data.Number.Value);
        Assert.Equal(Confidence.Good, data.Number.Confidence);
        Assert.Equal("15/03/1990", data.DateOfBirth.Value);
        Assert.Equal("13/02/2031", data.ExpiryDate.Value);
        Assert.Equal("MD ABDUL KARIM RAHMAN", data.FullName.Value);
    }

    [Fact]
    public void Sex_and_nationality_are_the_exact_dropdown_options()
    {
        var data = CreateInterpreter().Interpret(CodeLinesOnlyScan());

        Assert.Equal("ذكر", data.Sex.Value);                       // M means male
        Assert.Equal(" بنجلاديش - 145", data.Nationality.Value);   // BGD, exactly as the PDF stores it (note the leading space)
    }

    [Fact]
    public void The_passport_type_is_taken_from_the_first_letter()
    {
        var data = CreateInterpreter().Interpret(CodeLinesOnlyScan());

        Assert.Equal("عادي", data.PassportType.Value);
    }

    [Fact]
    public void Without_the_printed_text_reader_the_person_is_told_what_is_missing()
    {
        var data = CreateInterpreter().Interpret(CodeLinesOnlyScan());

        Assert.Contains(data.Notes, n => n.Contains("printed-text reader isn't available"));
        Assert.False(data.PlaceOfBirth.HasValue);
        Assert.False(data.IssueDate.HasValue);
    }

    [Fact]
    public void Nothing_found_gives_a_helpful_note_and_no_values()
    {
        var data = CreateInterpreter().Interpret(new PassportScan(null, null, new HashSet<DateOnly>()));

        Assert.False(data.Number.HasValue);
        Assert.Single(data.Notes);
    }
}
