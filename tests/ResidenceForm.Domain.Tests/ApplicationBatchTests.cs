using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Domain.Tests;

public class ApplicationBatchTests
{
    private static ApplicationBatch BatchWithEmployer()
    {
        var batch = new ApplicationBatch();
        batch.Current.Set(FieldNames.EmployerCivilId, "123456789012");
        batch.Current.Set(FieldNames.EmployerName, "محمد أحمد");
        batch.Current.Set(FieldNames.TransferorName, "المتنازل");      // the transferor is shared too
        return batch;
    }

    private static void FillApplication(ApplicationBatch batch, string name)
    {
        batch.Current.Set(FieldNames.FullName, name);
        batch.Current.Set(FieldNames.PassportNumber, "P" + name);
        batch.Current.ChooseRequestKind("1");
        batch.Current.Set(FieldNames.ResidencyType, "18 عمل أهلي");
    }

    [Fact]
    public void Only_request_and_applicant_boxes_belong_to_an_application()
    {
        Assert.True(FormSchema.IsApplicationField(FieldNames.FullName));
        Assert.True(FormSchema.IsApplicationField(FieldNames.RequestKind));
        Assert.True(FormSchema.IsApplicationField(FieldNames.ResidencyType));
        Assert.True(FormSchema.IsApplicationField(FieldNames.Procedure));
        Assert.False(FormSchema.IsApplicationField(FieldNames.EmployerCivilId));
        Assert.False(FormSchema.IsApplicationField(FieldNames.TransferorName));
    }

    [Fact]
    public void Saving_moves_the_application_boxes_and_keeps_the_shared_ones()
    {
        var batch = BatchWithEmployer();
        FillApplication(batch, "JOHN");

        var saved = batch.SaveCurrentAndStartNew();

        Assert.Equal("JOHN", saved.Values[FieldNames.FullName]);
        Assert.Null(batch.Current.Get(FieldNames.FullName));
        Assert.Null(batch.Current.Get(FieldNames.RequestKind));
        Assert.Equal("123456789012", batch.Current.Get(FieldNames.EmployerCivilId));
        Assert.True(batch.CurrentIsBlank);
    }

    [Fact]
    public void Each_document_combines_the_shared_answers_with_its_own_application()
    {
        var batch = BatchWithEmployer();
        FillApplication(batch, "JOHN");
        batch.SaveCurrentAndStartNew();
        FillApplication(batch, "MARY");

        var documents = batch.BuildDocuments();

        Assert.Equal(2, documents.Count);
        Assert.Equal("JOHN", documents[0].Answers[FieldNames.FullName]);
        Assert.Equal("MARY", documents[1].Answers[FieldNames.FullName]);
        Assert.All(documents, d => Assert.Equal("123456789012", d.Answers[FieldNames.EmployerCivilId]));
        Assert.All(documents, d => Assert.Equal("المتنازل", d.Answers[FieldNames.TransferorName]));
        Assert.NotNull(documents[0].SavedId);
        Assert.Null(documents[1].SavedId);                      // the one still being filled in
    }

    [Fact]
    public void A_blank_current_application_is_not_a_document()
    {
        var batch = BatchWithEmployer();
        FillApplication(batch, "JOHN");
        batch.SaveCurrentAndStartNew();

        var documents = batch.BuildDocuments();

        Assert.Single(documents);
        Assert.NotNull(documents[0].SavedId);
    }

    [Fact]
    public void Removing_a_saved_application_leaves_the_others()
    {
        var batch = BatchWithEmployer();
        FillApplication(batch, "JOHN");
        var first = batch.SaveCurrentAndStartNew();
        FillApplication(batch, "MARY");
        batch.SaveCurrentAndStartNew();

        Assert.True(batch.Remove(first.Id));

        Assert.Equal(new[] { "MARY" }, batch.BuildDocuments().Select(d => d.Title));
        Assert.False(batch.Remove(first.Id));
    }

    [Fact]
    public void The_title_is_the_applicants_name_or_a_number()
    {
        var batch = BatchWithEmployer();
        batch.Current.ChooseRequestKind("1");                    // no name yet

        Assert.Equal("Application 1", batch.SaveCurrentAndStartNew().Title);

        FillApplication(batch, "MARY");
        Assert.Equal("MARY", batch.SaveCurrentAndStartNew().Title);
    }
}