using SkiaSharp;

namespace ResidenceForm.Infrastructure.Ocr;

/// <summary>Small image helpers (SkiaSharp): decode with EXIF rotation, rotate, scale, crop, encode.</summary>
internal static class ImageOperations
{
    public static byte[] ToPng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Decodes the image and applies the rotation stored in its EXIF data (phone photos are often stored sideways).</summary>
    public static SKBitmap DecodeUpright(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes)) ?? throw new InvalidDataException("This file is not a readable image.");
        var bitmap = SKBitmap.Decode(codec);

        var degrees = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0
        };
        if (degrees == 0) return bitmap;

        using (bitmap) return Rotate(bitmap, degrees);
    }

    /// <summary>Returns a rotated copy (clockwise; 0, 90, 180 or 270 degrees).</summary>
    public static SKBitmap Rotate(SKBitmap source, int degrees)
    {
        if (degrees == 0) return source.Copy();

        var swapsSides = degrees is 90 or 270;
        var result = new SKBitmap(swapsSides ? source.Height : source.Width, swapsSides ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        switch (degrees)
        {
            case 90: canvas.Translate(source.Height, 0); break;
            case 180: canvas.Translate(source.Width, source.Height); break;
            case 270: canvas.Translate(0, source.Width); break;
        }
        canvas.RotateDegrees(degrees);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    public static SKBitmap ScaleToWidth(SKBitmap source, int targetWidth)
    {
        if (source.Width == targetWidth) return source.Copy();

        var height = (int)Math.Round(source.Height * (targetWidth / (double)source.Width));
        return source.Resize(new SKImageInfo(targetWidth, height), SKFilterQuality.High) ?? source.Copy();
    }

    /// <summary>The bottom part of the image; fraction 1.0 returns a full copy.</summary>
    public static SKBitmap CropBottom(SKBitmap source, double fraction)
    {
        if (fraction >= 1.0) return source.Copy();

        var height = (int)(source.Height * fraction);
        var cropped = new SKBitmap();
        source.ExtractSubset(cropped, new SKRectI(0, source.Height - height, source.Width, source.Height));
        return cropped;
    }

    /// <summary>A copy of the region (pixels, clamped to the image); null when the region is empty.</summary>
    public static SKBitmap? Crop(SKBitmap source, float left, float top, float right, float bottom)
    {
        var area = new SKRectI(
            Math.Max(0, (int)Math.Floor(left)), Math.Max(0, (int)Math.Floor(top)),
            Math.Min(source.Width, (int)Math.Ceiling(right)), Math.Min(source.Height, (int)Math.Ceiling(bottom)));
        if (area.Width < 4 || area.Height < 4) return null;

        var cropped = new SKBitmap();
        return source.ExtractSubset(cropped, area) ? cropped : null;
    }

    /// <summary>Scales so the image is about targetHeight tall (never smaller than the original, at most 4x).</summary>
    public static SKBitmap ScaleTowardsHeight(SKBitmap source, int targetHeight)
    {
        var factor = Math.Clamp(targetHeight / (double)source.Height, 1.0, 4.0);
        if (factor <= 1.0) return source.Copy();

        var info = new SKImageInfo((int)Math.Round(source.Width * factor), (int)Math.Round(source.Height * factor));
        return source.Resize(info, SKFilterQuality.High) ?? source.Copy();
    }
}
