using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Domain.Tests;

public class MrzTests
{
    [Fact]
    public void Check_digits_match_the_ICAO_specimen()
    {
        Assert.Equal(6, Mrz.Check("L898902C3"));   // passport number
        Assert.Equal(2, Mrz.Check("740812"));      // date of birth
        Assert.Equal(9, Mrz.Check("120415"));      // date of expiry
    }

    [Fact]
    public void FindPair_reads_both_lines_of_the_ICAO_specimen()
    {
        var pair = Mrz.FindPair(new[] { TestData.IcaoNameLine, TestData.IcaoNumberLine });

        Assert.NotNull(pair);
        Assert.Equal("ERIKSSON", pair.L1!.Surname);
        Assert.Equal("ANNA MARIA", pair.L1.GivenNames);
        Assert.Equal("UTO", pair.L1.Country);
        Assert.Equal("L898902C3", pair.L2!.Number);
        Assert.True(pair.L2.NumberOk && pair.L2.DobOk && pair.L2.ExpiryOk);
        Assert.Equal('F', pair.L2.Sex);
    }

    [Fact]
    public void FindPair_ignores_noise_lines_around_the_real_pair()
    {
        var lines = new[] { "KKKLLLSSSEEEKKKLLLSSSEEEKKKLLLSSSEEEKKK", TestData.BangladeshiNameLine, TestData.BangladeshiNumberLine, "QQMAR00007AOEWMSTEELLIIYELMBEELELELLEDLLIMS" };

        var pair = Mrz.FindPair(lines);

        Assert.NotNull(pair);
        Assert.Equal("A01234567", pair.L2!.Number);
        Assert.Equal("RAHMAN", pair.L1!.Surname);
    }

    [Fact]
    public void Letter_O_read_instead_of_zero_in_number_fields_is_corrected()
    {
        const string noisy = "L898902C36UTO74O8122F12O4159ZE184226B<<<<<1O";

        var pair = Mrz.FindPair(new[] { TestData.IcaoNameLine, noisy });

        Assert.True(pair!.L2!.DobOk);
        Assert.True(pair.L2.ExpiryOk);
        Assert.Equal("740812", pair.L2.Dob);
    }

    [Fact]
    public void A_wrong_digit_is_flagged_not_repaired()
    {
        var wrongBirthDigit = TestData.IcaoNumberLine.Replace("7408122", "7409122");

        var pair = Mrz.FindPair(new[] { TestData.IcaoNameLine, wrongBirthDigit });

        Assert.False(pair!.L2!.DobOk);
        Assert.Equal("740912", pair.L2.Dob);
    }

    [Fact]
    public void Printed_dates_are_found_with_or_without_spaces()
    {
        var dates = Mrz.VisualDates("15MAR1990\nDate of Issue 14 FEB 2021\n12.01.2030");

        Assert.Contains(new DateOnly(1990, 3, 15), dates);
        Assert.Contains(new DateOnly(2021, 2, 14), dates);
        Assert.Contains(new DateOnly(2030, 1, 12), dates);
    }

    [Fact]
    public void A_placeholder_date_is_not_a_date()
    {
        Assert.Empty(Mrz.VisualDates("00 MAR 0000"));
    }

    [Fact]
    public void Issue_date_prefers_the_candidate_that_gives_a_ten_year_validity()
    {
        var birth = new DateOnly(1990, 3, 15);
        var expiry = new DateOnly(2031, 2, 13);
        var candidates = new[] { new DateOnly(2005, 5, 5), new DateOnly(2021, 2, 14) };

        var (date, confident) = Mrz.PickIssue(birth, expiry, candidates);

        Assert.Equal(new DateOnly(2021, 2, 14), date);
        Assert.True(confident);
    }
}
