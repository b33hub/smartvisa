using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Application.Abstractions;

/// <summary>Finds and reads lines of printed text in an image, with their positions.</summary>
public interface IPrintedTextReader
{
    /// <summary>Returns null when the reader isn't available (see <see cref="LastError"/>).</summary>
    IReadOnlyList<OcrWord>? Read(byte[] image);

    /// <summary>Why the last attempt failed, or null.</summary>
    string? LastError { get; }
}
