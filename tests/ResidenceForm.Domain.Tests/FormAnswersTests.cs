using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Domain.Tests;

public class FormAnswersTests
{
    [Fact]
    public void Every_employer_field_is_mandatory()
    {
        var answers = new FormAnswers();

        Assert.Equal(FormSchema.Employer.Fields.Length, answers.Issues(FormSchema.EmployerStep).Count);
    }

    [Fact]
    public void Applicant_civil_id_is_optional_but_must_be_well_formed_when_given()
    {
        var answers = new FormAnswers();
        Assert.DoesNotContain("Civil ID", answers.Issues(FormSchema.ApplicantStep));

        answers.Set(FieldNames.CivilId, "123");
        Assert.Contains("Civil ID", answers.Issues(FormSchema.ApplicantStep));

        answers.Set(FieldNames.CivilId, "123456789012");
        Assert.DoesNotContain("Civil ID", answers.Issues(FormSchema.ApplicantStep));
    }

    [Fact]
    public void Choosing_another_request_kind_forgets_the_old_type_answer()
    {
        var answers = new FormAnswers();
        answers.ChooseRequestKind("1");
        answers.Set(FieldNames.ResidencyType, "18 عمل أهلي");

        answers.ChooseRequestKind("0");

        Assert.Null(answers.Get(FieldNames.ResidencyType));
        Assert.Equal(FieldNames.EntryVisaType, answers.Kind!.TypeField);
    }

    [Fact]
    public void FillIfEmpty_never_overwrites_what_the_person_entered()
    {
        var answers = new FormAnswers();
        answers.Set(FieldNames.FullName, "MY OWN NAME");

        Assert.Equal(FillOutcome.KeptEntered, answers.FillIfEmpty(FieldNames.FullName, "FROM PASSPORT"));
        Assert.Equal(FillOutcome.Filled, answers.FillIfEmpty(FieldNames.PassportNumber, "A01234567"));
        Assert.Equal(FillOutcome.SameAsEntered, answers.FillIfEmpty(FieldNames.PassportNumber, "A01234567"));
        Assert.Equal("MY OWN NAME", answers.Get(FieldNames.FullName));
    }

    [Fact]
    public void Dates_convert_between_the_picker_format_and_the_form_format()
    {
        var answers = new FormAnswers();

        answers.SetDateFromPicker(FieldNames.DateOfBirth, "1990-03-15");

        Assert.Equal("15/03/1990", answers.Get(FieldNames.DateOfBirth));
        Assert.Equal("1990-03-15", answers.GetDateForPicker(FieldNames.DateOfBirth));
    }

    [Fact]
    public void Removing_a_document_clears_what_it_filled_but_keeps_what_the_person_edited()
    {
        var answers = new FormAnswers();
        answers.FillIfEmpty(FieldNames.EmployerCivilId, "123456789012", DocumentKind.CivilIdFront);
        answers.FillIfEmpty(FieldNames.EmployerName, "من البطاقة", DocumentKind.CivilIdFront);
        answers.Set(FieldNames.EmployerName, "محمد (بعد التعديل)");        // the person edited this one

        var removed = answers.RemoveFilledBy(DocumentKind.CivilIdFront);

        Assert.Equal(new[] { FieldNames.EmployerCivilId }, removed);
        Assert.Null(answers.Get(FieldNames.EmployerCivilId));
        Assert.Equal("محمد (بعد التعديل)", answers.Get(FieldNames.EmployerName));
    }

    [Fact]
    public void Removing_one_document_does_not_touch_what_another_document_filled()
    {
        var answers = new FormAnswers();
        answers.FillIfEmpty(FieldNames.FullName, "JOHN CITIZEN", DocumentKind.Passport);
        answers.FillIfEmpty(FieldNames.EmployerStreet, "الخليج", DocumentKind.CivilIdBack);

        answers.RemoveFilledBy(DocumentKind.CivilIdBack);

        Assert.Equal("JOHN CITIZEN", answers.Get(FieldNames.FullName));
        Assert.Null(answers.Get(FieldNames.EmployerStreet));
    }

    [Fact]
    public void A_box_the_person_filled_first_is_never_owned_by_a_document()
    {
        var answers = new FormAnswers();
        answers.Set(FieldNames.PassportNumber, "MINE");

        answers.FillIfEmpty(FieldNames.PassportNumber, "A01234567", DocumentKind.Passport);
        answers.RemoveFilledBy(DocumentKind.Passport);

        Assert.Equal("MINE", answers.Get(FieldNames.PassportNumber));
    }
}
