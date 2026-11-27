namespace ResidenceForm.Domain.Forms;

/// <summary>An application that was completed and set aside while the person starts another (kept in memory only).</summary>
public sealed record SavedApplication(Guid Id, string Title, IReadOnlyDictionary<string, string> Values);

/// <summary>One application's complete answers, ready for a PDF. <see cref="SavedId"/> is null for the one still being filled in.</summary>
public sealed record ApplicationDocument(Guid? SavedId, string Title, IReadOnlyDictionary<string, string> Answers);

/// <summary>
/// Several applications for the same employer: the employer's answers are entered once and combined with each application's own
/// answers when the PDFs are made. <see cref="Current"/> holds the application being filled in plus the shared answers.
/// </summary>
public sealed class ApplicationBatch
{
    private readonly List<SavedApplication> _saved = new();

    public FormAnswers Current { get; } = new();

    public IReadOnlyList<SavedApplication> Saved => _saved;

    /// <summary>True while the current application has no request or applicant answers (for example right after adding one).</summary>
    public bool CurrentIsBlank => Current.ApplicationValues.Count == 0;

    /// <summary>Moves the current application's own answers into the saved list and clears them; shared answers stay.</summary>
    public SavedApplication SaveCurrentAndStartNew()
    {
        var values = Current.TakeApplicationValues();
        var saved = new SavedApplication(Guid.NewGuid(), TitleOf(values, _saved.Count + 1), values);
        _saved.Add(saved);
        return saved;
    }

    public bool Remove(Guid id) => _saved.RemoveAll(application => application.Id == id) > 0;

    /// <summary>Every application to submit, each with the shared answers merged in. A blank current application is left out.</summary>
    public IReadOnlyList<ApplicationDocument> BuildDocuments()
    {
        var shared = Current.SharedValues;
        var documents = _saved
            .Select(saved => new ApplicationDocument(saved.Id, saved.Title, Merge(shared, saved.Values)))
            .ToList();

        if (!CurrentIsBlank)
        {
            var own = Current.ApplicationValues;
            documents.Add(new ApplicationDocument(null, TitleOf(own, _saved.Count + 1), Merge(shared, own)));
        }
        return documents;
    }

    public void Clear()
    {
        _saved.Clear();
        Current.Clear();
    }

    private static IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string> shared, IReadOnlyDictionary<string, string> own)
    {
        var merged = new Dictionary<string, string>(shared);
        foreach (var (field, value) in own) merged[field] = value;
        return merged;
    }

    private static string TitleOf(IReadOnlyDictionary<string, string> values, int number) =>
        values.TryGetValue(FieldNames.FullName, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : $"Application {number}";
}