using ResidenceForm.Application.Abstractions;
using ResidenceForm.Domain.CivilIds;
using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Application.Documents;

/// <summary>Use case: read an uploaded document image and return form-ready values with their confidence.</summary>
public sealed class ReadDocumentUseCase(
    IPassportScanner passportScanner,
    PassportInterpreter passportInterpreter,
    ICardScanner cardScanner,
    CivilIdInterpreter civilIdInterpreter)
{
    public IDocumentReading Execute(DocumentKind kind, byte[] imageBytes) => kind switch
    {
        DocumentKind.Passport => passportInterpreter.Interpret(passportScanner.Scan(imageBytes)),
        DocumentKind.CivilIdFront => civilIdInterpreter.InterpretFront(cardScanner.Scan(imageBytes)),
        DocumentKind.CivilIdBack => civilIdInterpreter.InterpretBack(cardScanner.Scan(imageBytes)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown document kind.")
    };
}
