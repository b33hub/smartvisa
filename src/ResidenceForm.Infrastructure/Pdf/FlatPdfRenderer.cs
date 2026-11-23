using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace ResidenceForm.Infrastructure.Pdf;

/// <summary>
/// Draws the answers into the form page (no live fields). Arabic is shaped by HarfBuzz through SkiaSharp into an overlay
/// PDF, which PDFsharp stamps on top of the printed form; the result looks the same in every viewer.
/// </summary>
internal sealed class FlatPdfRenderer : IDisposable
{
    private const float PageWidth = 595.32f, PageHeight = 841.92f;       // A4 in points
    private const float MaxFontSize = 11f, MinFontSize = 5f;

    private readonly byte[] _pagesWithoutWidgets;
    private readonly IReadOnlyList<PdfFieldDefinition> _fields;
    private readonly SKTypeface _typeface;

    public FlatPdfRenderer(byte[] pagesWithoutWidgets, IReadOnlyList<PdfFieldDefinition> fields, string fontPath)
    {
        _pagesWithoutWidgets = pagesWithoutWidgets;
        _fields = fields;
        _typeface = SKTypeface.FromFile(fontPath) ?? throw new FileNotFoundException("The Arabic font could not be loaded.", fontPath);
    }

    public byte[] Render(IReadOnlyDictionary<string, string> answers)
    {
        using var overlay = DrawOverlay(answers);
        return StampOverlay(overlay);
    }

    private MemoryStream DrawOverlay(IReadOnlyDictionary<string, string> answers)
    {
        var overlay = new MemoryStream();
        using var shaper = new SKShaper(_typeface);        // one shaper per render: HarfBuzz objects are not shared across requests

        using (var pdf = SKDocument.CreatePdf(overlay))
        {
            var pageCount = _fields.Max(f => f.Page) + 1;
            for (var page = 0; page < pageCount; page++)
            {
                using var canvas = pdf.BeginPage(PageWidth, PageHeight);
                foreach (var field in _fields.Where(f => f.Page == page))
                {
                    answers.TryGetValue(field.Name, out var value);
                    if (field.Kind == PdfFieldKind.Radio) DrawRadio(canvas, field, value?.Trim() == field.RadioState);
                    else if (!string.IsNullOrWhiteSpace(value)) DrawText(canvas, shaper, field, value.Trim());
                }
                pdf.EndPage();
            }
            pdf.Close();
        }
        return overlay;
    }

    private byte[] StampOverlay(MemoryStream overlay)
    {
        using var templateStream = new MemoryStream(_pagesWithoutWidgets);
        using var document = PdfReader.Open(templateStream, PdfDocumentOpenMode.Modify);

        for (var i = 0; i < document.PageCount; i++)
        {
            overlay.Position = 0;
            using var graphics = XGraphics.FromPdfPage(document.Pages[i]);
            var form = XPdfForm.FromStream(overlay);
            form.PageNumber = i + 1;
            graphics.DrawImage(form, 0, 0, document.Pages[i].Width.Point, document.Pages[i].Height.Point);
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private void DrawText(SKCanvas canvas, SKShaper shaper, PdfFieldDefinition field, string text)
    {
        float left = field.Rect[0], bottom = field.Rect[1], right = field.Rect[2], top = field.Rect[3];
        var availableWidth = right - left - 4;
        var height = top - bottom;

        using var paint = new SKPaint { Typeface = _typeface, IsAntialias = true, Color = SKColors.Black };

        // shrink the font until the text fits the box
        var size = Math.Min(MaxFontSize, height * 0.7f);
        float width;
        do
        {
            paint.TextSize = size;
            width = shaper.Shape(text, paint).Width;
            size -= 0.5f;
        }
        while (width > availableWidth && size > MinFontSize);

        var x = field.Alignment switch
        {
            PdfTextAlignment.Right => right - 2 - width,
            PdfTextAlignment.Center => left + (right - left - width) / 2,
            _ => left + 2
        };
        var baselineFromBottom = bottom + (height - paint.TextSize * 0.55f) / 2f;
        canvas.DrawShapedText(shaper, text, x, PageHeight - baselineFromBottom, paint);
    }

    private static void DrawRadio(SKCanvas canvas, PdfFieldDefinition field, bool selected)
    {
        var rect = field.Rect;
        var centerX = (rect[0] + rect[2]) / 2;
        var centerY = PageHeight - (rect[1] + rect[3]) / 2;
        var radius = (rect[2] - rect[0]) / 2 - 1;

        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, Color = SKColors.Black };
        canvas.DrawCircle(centerX, centerY, radius, ring);              // the widget's own circle was removed, so redraw it

        if (!selected) return;
        using var dot = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        canvas.DrawCircle(centerX, centerY, radius * 0.55f, dot);
    }

    public void Dispose() => _typeface.Dispose();
}
