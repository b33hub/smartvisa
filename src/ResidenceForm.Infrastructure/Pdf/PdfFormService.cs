using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using ResidenceForm.Application.Abstractions;

namespace ResidenceForm.Infrastructure.Pdf;

/// <summary>
/// The Ministry's blank PDF as a template. At start-up it reads field names, positions and dropdown options from the PDF
/// itself (nothing is hardcoded), and keeps a copy with the form widgets removed for the print-ready document.
/// Creates the editable document by filling the PDF's real fields.
/// </summary>
public sealed class PdfFormService : IFormTemplate, IFormDocumentWriter, IDisposable
{
    private readonly byte[] _blankPdf;
    private readonly List<PdfFieldDefinition> _fields = new();
    private readonly FlatPdfRenderer _flatRenderer;

    public PdfFormService(IOptions<AssetOptions> assets)
    {
        var paths = assets.Value;
        _blankPdf = File.ReadAllBytes(paths.BlankFormPath);

        var pagesWithoutWidgets = ReadFieldsAndStripWidgets();
        _flatRenderer = new FlatPdfRenderer(pagesWithoutWidgets, _fields, paths.FontPath);

        DropdownFields = _fields.Where(f => f.Kind == PdfFieldKind.Choice).Select(f => f.Name).Distinct().ToList();
    }

    // ------------------------------------------------------------------ IFormTemplate

    public IReadOnlyList<string> DropdownFields { get; }

    public IReadOnlyList<string> OptionsOf(string field) =>
        _fields.First(f => f.Name == field && f.Kind == PdfFieldKind.Choice)
               .Options.Where(option => !IsPlaceholder(option)).ToList();

    // "--- choose ---" and "- choose -" entries are not real choices
    private static bool IsPlaceholder(string option) => option.Trim().StartsWith('-');

    // ------------------------------------------------------------------ IFormDocumentWriter

    public byte[] CreatePrintReady(IReadOnlyDictionary<string, string> answers)
    {
        ThrowIfInvalid(answers);
        return _flatRenderer.Render(answers);
    }

    /// <summary>
    /// Fills the real AcroForm fields: text becomes a /V string, a dropdown gets /V = the exact option string from the PDF,
    /// a radio group gets its state. Stale empty appearances are dropped and NeedAppearances is set, so the viewer redraws
    /// every value itself and the result stays an editable form.
    /// </summary>
    public byte[] CreateEditable(IReadOnlyDictionary<string, string> answers)
    {
        ThrowIfInvalid(answers);

        using var source = new MemoryStream(_blankPdf);
        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);

        for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            foreach (var widget in PdfWidgets.WidgetsOf(document.Pages[pageIndex]))
            {
                var definition = PdfWidgets.Describe(widget, pageIndex);
                if (definition is null || !answers.TryGetValue(definition.Name, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                FillWidget(widget, definition, value);
            }
        }

        document.Internals.Catalog.Elements.GetDictionary("/AcroForm")?.Elements.SetBoolean("/NeedAppearances", true);

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private void FillWidget(PdfDictionary widget, PdfFieldDefinition definition, string value)
    {
        var field = PdfWidgets.FieldDictionary(widget);

        switch (definition.Kind)
        {
            case PdfFieldKind.Radio:
                var isSelected = value.Trim() == definition.RadioState;
                widget.Elements["/AS"] = new PdfName(isSelected ? "/" + definition.RadioState : "/Off");
                if (isSelected) field.Elements["/V"] = new PdfName("/" + definition.RadioState);
                break;

            case PdfFieldKind.Choice:
                var exactOption = OptionsOf(definition.Name).First(option => option.Trim() == value.Trim());
                field.Elements["/V"] = new PdfString(exactOption, PdfStringEncoding.Unicode);
                field.Elements.Remove("/I");
                widget.Elements.Remove("/AP");
                break;

            default:
                field.Elements["/V"] = new PdfString(value.Trim(), PdfStringEncoding.Unicode);
                widget.Elements.Remove("/AP");
                break;
        }
    }

    // ------------------------------------------------------------------ loading

    /// <summary>Collects every field definition and returns the PDF with all form widgets removed (the printed form is page content and stays).</summary>
    private byte[] ReadFieldsAndStripWidgets()
    {
        using var source = new MemoryStream(_blankPdf);
        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);

        for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            var page = document.Pages[pageIndex];
            _fields.AddRange(PdfWidgets.WidgetsOf(page)
                .Select(widget => PdfWidgets.Describe(widget, pageIndex))
                .OfType<PdfFieldDefinition>());
            page.Elements.Remove("/Annots");
        }
        document.Internals.Catalog.Elements.Remove("/AcroForm");

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // ------------------------------------------------------------------ validation

    /// <summary>Every value must name a real field; dropdown values must be real options; radio values must be real states.</summary>
    private void ThrowIfInvalid(IReadOnlyDictionary<string, string> answers)
    {
        var errors = new List<string>();
        foreach (var (name, value) in answers)
        {
            var widgets = _fields.Where(f => f.Name == name).ToList();
            if (widgets.Count == 0)
            {
                errors.Add($"Unknown field: {name}");
                continue;
            }

            switch (widgets[0].Kind)
            {
                case PdfFieldKind.Choice when !string.IsNullOrWhiteSpace(value) && !OptionsOf(name).Any(o => o.Trim() == value.Trim()):
                    errors.Add($"'{value}' is not an option of {name}");
                    break;
                case PdfFieldKind.Radio when !widgets.Any(w => w.RadioState == value.Trim()):
                    errors.Add($"'{value}' is not a valid state for {name} ({string.Join("/", widgets.Select(w => w.RadioState))})");
                    break;
            }
        }

        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
    }

    public void Dispose() => _flatRenderer.Dispose();
}
