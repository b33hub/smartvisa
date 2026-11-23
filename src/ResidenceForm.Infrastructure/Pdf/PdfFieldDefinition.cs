namespace ResidenceForm.Infrastructure.Pdf;

internal enum PdfFieldKind { Text, Choice, Radio }

internal enum PdfTextAlignment { Left = 0, Center = 1, Right = 2 }

/// <summary>One widget of the PDF form: where it is, what it is, and (for dropdowns) which options it offers.</summary>
internal sealed class PdfFieldDefinition
{
    public required string Name { get; init; }
    public required int Page { get; init; }

    /// <summary>x1, y1, x2, y2 in PDF points; the origin is the bottom-left corner of the page.</summary>
    public required float[] Rect { get; init; }

    public PdfTextAlignment Alignment { get; init; }
    public PdfFieldKind Kind { get; set; } = PdfFieldKind.Text;

    /// <summary>Radio buttons only: the state name this widget turns on (e.g. "1").</summary>
    public string? RadioState { get; set; }

    /// <summary>Dropdowns only: the options exactly as stored in the PDF, placeholder included.</summary>
    public List<string> Options { get; } = new();
}
