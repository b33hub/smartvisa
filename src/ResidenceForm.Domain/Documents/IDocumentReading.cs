namespace ResidenceForm.Domain.Documents;

/// <summary>What was read from one uploaded document: form-ready values (each with a confidence) and notes for the person.</summary>
public interface IDocumentReading
{
    /// <summary>Which form field each finding belongs to, with a label for display. Only fields this document can supply.</summary>
    IEnumerable<(string Field, string Label, Finding Finding)> FormMapping();

    /// <summary>Things the person should know (what couldn't be read, what looks suspicious).</summary>
    IReadOnlyList<string> Notes { get; }
}
