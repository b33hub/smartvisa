using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Domain.CivilIds;

public enum CivilIdSide { Front, Back }

/// <summary>What was read from one side of a Kuwaiti civil ID, as values for the employer section of the form.</summary>
public sealed class CivilIdData(CivilIdSide side) : IDocumentReading
{
    public CivilIdSide Side { get; } = side;

    // front
    public Finding CivilId { get; internal set; } = Finding.None;
    public Finding ArabicName { get; internal set; } = Finding.None;
    public Finding Nationality { get; internal set; } = Finding.None;

    // back
    public Finding Profession { get; internal set; } = Finding.None;
    public Finding Governorate { get; internal set; } = Finding.None;
    public Finding Area { get; internal set; } = Finding.None;
    public Finding Block { get; internal set; } = Finding.None;
    public Finding Street { get; internal set; } = Finding.None;
    public Finding Building { get; internal set; } = Finding.None;      // the value printed after قسيمة (plot)
    public Finding PaciNumber { get; internal set; } = Finding.None;
    public Finding Phone { get; internal set; } = Finding.None;

    private readonly List<string> _notes = new();

    public IReadOnlyList<string> Notes => _notes;

    internal void AddNote(string note) => _notes.Add(note);

    private IReadOnlyList<string> _rawReading = Array.Empty<string>();

    public IReadOnlyList<string> RawReading => _rawReading;

    internal void SetRawReading(IEnumerable<string> lines) => _rawReading = lines.ToList();

    public IEnumerable<(string Field, string Label, Finding Finding)> FormMapping()
    {
        yield return (FieldNames.EmployerCivilId, "Civil ID", CivilId);

        if (Side == CivilIdSide.Front)
        {
            yield return (FieldNames.EmployerName, "Name (Arabic)", ArabicName);
            yield return (FieldNames.EmployerNationality, "Nationality", Nationality);
            yield break;
        }

        yield return (FieldNames.EmployerOccupation, "Occupation", Profession);
        yield return (FieldNames.EmployerGovernorate, "Governorate", Governorate);
        yield return (FieldNames.EmployerArea, "Area", Area);
        yield return (FieldNames.EmployerBlock, "Block", Block);
        yield return (FieldNames.EmployerStreet, "Street", Street);
        yield return (FieldNames.EmployerBuilding, "Building", Building);
        yield return (FieldNames.EmployerAddressPaci, "Address PACI number", PaciNumber);
        yield return (FieldNames.EmployerPhone, "Phone", Phone);
    }
}