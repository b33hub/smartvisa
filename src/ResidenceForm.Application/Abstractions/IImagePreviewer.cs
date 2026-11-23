namespace ResidenceForm.Application.Abstractions;

/// <summary>Makes a small, browser-friendly preview of an uploaded image.</summary>
public interface IImagePreviewer
{
    /// <summary>A downscaled JPEG as a data URL (data:image/jpeg;base64,...), with phone rotation applied.</summary>
    string CreateDataUrl(byte[] imageBytes);
}
