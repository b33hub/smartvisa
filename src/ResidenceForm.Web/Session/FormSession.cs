using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Web.Session;

/// <summary>
/// The UI session of one browser tab (scoped to the Blazor circuit): the person's <see cref="FormAnswers"/> plus the wizard's
/// position, with change notifications so components redraw. Business rules stay in the domain; this class only coordinates.
/// </summary>
public sealed class FormSession
{
    private readonly Dictionary<DocumentKind, UploadedDocument> _documents =
        Enum.GetValues<DocumentKind>().ToDictionary(kind => kind, kind => new UploadedDocument(kind));

    public FormAnswers Answers { get; } = new();

    public int Step { get; private set; }

    /// <summary>True after a blocked "Next": empty mandatory fields are then highlighted.</summary>
    public bool ShowRequired { get; private set; }

    /// <summary>Any answer or the wizard state changed.</summary>
    public event Action? Changed;

    /// <summary>The wizard moved to another step.</summary>
    public event Action? StepChanged;

    // ---- answers (each change notifies the UI)

    public string? Get(string field) => Answers.Get(field);

    public void Set(string field, string? value)
    {
        Answers.Set(field, value);
        Changed?.Invoke();
    }

    public string? GetDateForPicker(string field) => Answers.GetDateForPicker(field);

    public void SetDateFromPicker(string field, string? pickerValue)
    {
        Answers.SetDateFromPicker(field, pickerValue);
        Changed?.Invoke();
    }

    public RequestKind? Kind => Answers.Kind;

    public void ChooseRequestKind(string kindValue)
    {
        Answers.ChooseRequestKind(kindValue);
        Changed?.Invoke();
    }

    // ---- uploaded documents

    public UploadedDocument Document(DocumentKind kind) => _documents[kind];

    /// <summary>True while any document is being read; the form is locked meanwhile.</summary>
    public bool IsBusy => _documents.Values.Any(document => document.IsReading);

    public void BeginReading(DocumentKind kind, string fileName)
    {
        _documents[kind].StartReading(fileName);
        Changed?.Invoke();
    }

    /// <summary>Stores the reading and fills the boxes that are still empty; the person's own entries are never changed.</summary>
    public void CompleteReading(DocumentKind kind, string previewDataUrl, IDocumentReading reading)
    {
        var outcomes = new Dictionary<string, FillOutcome>();
        foreach (var (field, _, finding) in reading.FormMapping())
        {
            if (finding.HasValue) outcomes[field] = Answers.FillIfEmpty(field, finding.Value!, kind);
        }

        _documents[kind].Complete(previewDataUrl, reading, outcomes);
        Changed?.Invoke();
    }

    public void FailReading(DocumentKind kind, string message, string? technicalDetail)
    {
        _documents[kind].Fail(message, technicalDetail);
        Changed?.Invoke();
    }

    /// <summary>Removes the document and clears the boxes it filled (except those the person has edited since).</summary>
    public void RemoveDocument(DocumentKind kind)
    {
        Answers.RemoveFilledBy(kind);
        _documents[kind].Clear();
        Changed?.Invoke();
    }

    public IReadOnlyList<string> Issues(int step) => Answers.Issues(step);

    public IReadOnlyList<string> AllIssues() => Answers.AllIssues();

    // ---- wizard

    /// <summary>Moving backwards is always allowed; moving forwards needs every step in between to be valid.</summary>
    public bool GoTo(int target)
    {
        target = Math.Clamp(target, 0, FormSchema.ReviewStep);

        if (target > Step)
        {
            for (var step = Step; step < target; step++)
            {
                if (Issues(step).Count == 0) continue;

                var moved = step != Step;
                Step = step;
                ShowRequired = true;
                Changed?.Invoke();
                if (moved) StepChanged?.Invoke();
                return false;
            }
        }

        var changedStep = target != Step;
        Step = target;
        ShowRequired = false;
        Changed?.Invoke();
        if (changedStep) StepChanged?.Invoke();
        return true;
    }

    public void Reset()
    {
        foreach (var document in _documents.Values) document.Clear();
        Answers.Clear();
        Step = 0;
        ShowRequired = false;
        Changed?.Invoke();
        StepChanged?.Invoke();
    }
}
