using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Application.Abstractions;

/// <summary>Reads a passport photo or scan and returns the raw findings (see <see cref="PassportScan"/>).</summary>
public interface IPassportScanner
{
    /// <exception cref="InvalidDataException">The bytes are not a readable image.</exception>
    PassportScan Scan(byte[] imageBytes);
}
