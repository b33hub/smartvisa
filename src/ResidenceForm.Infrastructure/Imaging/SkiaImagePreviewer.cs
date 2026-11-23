using ResidenceForm.Application.Abstractions;
using ResidenceForm.Infrastructure.Ocr;
using SkiaSharp;

namespace ResidenceForm.Infrastructure.Imaging;

/// <summary>Creates the small preview shown beside the form: upright, at most 900 px on the long side, JPEG.</summary>
public sealed class SkiaImagePreviewer : IImagePreviewer
{
    private const int MaxSide = 900;
    private const int JpegQuality = 80;

    public string CreateDataUrl(byte[] imageBytes)
    {
        using var upright = ImageOperations.DecodeUpright(imageBytes);

        var scale = Math.Min(1.0, MaxSide / (double)Math.Max(upright.Width, upright.Height));
        var info = new SKImageInfo((int)Math.Round(upright.Width * scale), (int)Math.Round(upright.Height * scale));
        using var scaled = scale < 1.0 ? upright.Resize(info, SKFilterQuality.High) ?? upright.Copy() : upright.Copy();

        using var image = SKImage.FromBitmap(scaled);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return "data:image/jpeg;base64," + Convert.ToBase64String(jpeg.ToArray());
    }
}
