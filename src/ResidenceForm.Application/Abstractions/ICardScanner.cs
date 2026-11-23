using ResidenceForm.Domain.CivilIds;

namespace ResidenceForm.Application.Abstractions;

/// <summary>Reads the text lines of a photographed ID card (digits and English plus Arabic) with their positions.</summary>
public interface ICardScanner
{
    /// <exception cref="InvalidDataException">The bytes are not a readable image.</exception>
    CardScan Scan(byte[] imageBytes);
}
