using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace ResidenceForm.Infrastructure.Pdf;

/// <summary>Low-level reading of the PDF's form widgets (PDFsharp dictionaries). Everything PDF-structure-specific lives here.</summary>
internal static class PdfWidgets
{
    private const int RadioFlag = 0x8000;
    private const int PushButtonFlag = 0x10000;

    public static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

    /// <summary>Field attributes (/FT /T /Ff /Opt /Q) are inherited from parent fields.</summary>
    public static PdfItem? Inherited(PdfDictionary widget, string key)
    {
        for (PdfDictionary? current = widget; current is not null; current = current.Elements.GetDictionary("/Parent"))
        {
            var item = current.Elements[key];
            if (item is not null) return item;
        }
        return null;
    }

    public static string AsText(PdfItem? item) => Resolve(item) switch
    {
        PdfString text => text.Value,
        PdfName name => name.Value,
        _ => ""
    };

    public static int AsInt(PdfItem? item) => Resolve(item) switch
    {
        PdfInteger integer => integer.Value,
        PdfLongInteger longInteger => (int)longInteger.Value,
        _ => 0
    };

    public static IEnumerable<PdfDictionary> WidgetsOf(PdfPage page)
    {
        var annotations = page.Elements.GetArray("/Annots");
        if (annotations is null) yield break;

        foreach (var item in annotations.Elements)
        {
            if (Resolve(item) is PdfDictionary widget && AsText(widget.Elements["/Subtype"]) == "/Widget")
            {
                yield return widget;
            }
        }
    }

    /// <summary>The dictionary that owns /V: the widget itself, or its parent when the parent holds the field name (/T).</summary>
    public static PdfDictionary FieldDictionary(PdfDictionary widget)
    {
        for (PdfDictionary? current = widget; current is not null; current = current.Elements.GetDictionary("/Parent"))
        {
            if (current.Elements.ContainsKey("/T")) return current;
        }
        return widget;
    }

    /// <summary>Describes a widget, or returns null for things that are not fillable fields (photo and "clear" buttons).</summary>
    public static PdfFieldDefinition? Describe(PdfDictionary widget, int pageIndex)
    {
        var name = AsText(Inherited(widget, "/T"));
        var fieldType = AsText(Inherited(widget, "/FT"));
        var flags = AsInt(Inherited(widget, "/Ff"));
        if (name.Length == 0) return null;

        var rect = widget.Elements.GetRectangle("/Rect");
        var definition = new PdfFieldDefinition
        {
            Name = name,
            Page = pageIndex,
            Alignment = (PdfTextAlignment)AsInt(Inherited(widget, "/Q")),
            Rect = new[]
            {
                (float)Math.Min(rect.X1, rect.X2), (float)Math.Min(rect.Y1, rect.Y2),
                (float)Math.Max(rect.X1, rect.X2), (float)Math.Max(rect.Y1, rect.Y2)
            }
        };

        switch (fieldType)
        {
            case "/Btn":
                var isRadio = (flags & RadioFlag) != 0 && (flags & PushButtonFlag) == 0;
                if (!isRadio) return null;
                definition.Kind = PdfFieldKind.Radio;
                var states = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")?.Elements.Keys;
                definition.RadioState = states?.FirstOrDefault(key => key != "/Off")?.TrimStart('/');
                return definition;

            case "/Ch":
                definition.Kind = PdfFieldKind.Choice;
                definition.Options.AddRange(ReadOptions(widget));
                return definition;

            case "/Tx":
                return definition;

            default:
                return null;
        }
    }

    private static IEnumerable<string> ReadOptions(PdfDictionary widget)
    {
        if (Resolve(Inherited(widget, "/Opt")) is not PdfArray options) yield break;

        foreach (var option in options.Elements)
        {
            switch (Resolve(option))
            {
                case PdfString text:
                    yield return text.Value;
                    break;
                case PdfArray pair when pair.Elements.Count > 0:      // [export value, display text]
                    yield return AsText(pair.Elements[pair.Elements.Count > 1 ? 1 : 0]);
                    break;
            }
        }
    }
}
