namespace ResidenceForm.Application.Abstractions;

/// <summary>Produces the PDF documents from a set of answers (field name to value).</summary>
public interface IFormDocumentWriter
{
    /// <summary>The form with live fields filled in; the person can still edit every box.</summary>
    byte[] CreateEditable(IReadOnlyDictionary<string, string> answers);

    /// <summary>A flat copy with the values drawn into the page; looks identical in every PDF viewer.</summary>
    byte[] CreatePrintReady(IReadOnlyDictionary<string, string> answers);
}
