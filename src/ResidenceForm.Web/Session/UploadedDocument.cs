using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Web.Session;

/// <summary>The state of one upload slot (passport, civil ID front, civil ID back): reading, loaded, or empty.</summary>
public sealed class UploadedDocument(DocumentKind kind)
{
    private static readonly IReadOnlyDictionary<string, FillOutcome> NoOutcomes = new Dictionary<string, FillOutcome>();

    public DocumentKind Kind { get; } = kind;
    public string? FileName { get; private set; }
    public bool IsReading { get; private set; }

    /// <summary>A small data-URL preview of the uploaded image.</summary>
    public string? PreviewDataUrl { get; private set; }

    public IDocumentReading? Reading { get; private set; }
    public IReadOnlyDictionary<string, FillOutcome> Outcomes { get; private set; } = NoOutcomes;
    public string? Error { get; private set; }
    public string? TechnicalDetail { get; private set; }

    public bool IsLoaded => Reading is not null;
    public int FilledCount => Outcomes.Count(o => o.Value == FillOutcome.Filled);
    public int KeptCount => Outcomes.Count(o => o.Value == FillOutcome.KeptEntered);

    internal void StartReading(string fileName)
    {
        Clear();
        FileName = fileName;
        IsReading = true;
    }

    internal void Complete(string previewDataUrl, IDocumentReading reading, IReadOnlyDictionary<string, FillOutcome> outcomes)
    {
        PreviewDataUrl = previewDataUrl;
        Reading = reading;
        Outcomes = outcomes;
        IsReading = false;
    }

    internal void Fail(string message, string? technicalDetail)
    {
        Clear();
        Error = message;
        TechnicalDetail = technicalDetail;
    }

    internal void Clear()
    {
        FileName = null;
        IsReading = false;
        PreviewDataUrl = null;
        Reading = null;
        Outcomes = NoOutcomes;
        Error = null;
        TechnicalDetail = null;
    }
}
