using ResidenceForm.Domain.Documents;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Domain.Passports;

/// <summary>Everything we could read from a passport page, each value with its own confidence.</summary>
public sealed class PassportData : IDocumentReading
{
    public Finding FullName { get; internal set; } = Finding.None;
    public Finding Number { get; internal set; } = Finding.None;
    public Finding DateOfBirth { get; internal set; } = Finding.None;
    public Finding IssueDate { get; internal set; } = Finding.None;
    public Finding ExpiryDate { get; internal set; } = Finding.None;
    public Finding Sex { get; internal set; } = Finding.None;
    public Finding Nationality { get; internal set; } = Finding.None;
    public Finding PlaceOfBirth { get; internal set; } = Finding.None;
    public Finding PassportType { get; internal set; } = Finding.None;

    private readonly List<string> _notes = new();

    /// <summary>Things the person should know (what couldn't be read, what looks suspicious).</summary>
    public IReadOnlyList<string> Notes => _notes;

    internal void AddNote(string note) => _notes.Add(note);

    public IEnumerable<(string Field, string Label, Finding Finding)> FormMapping()
    {
        yield return (FieldNames.FullName, "Full name", FullName);
        yield return (FieldNames.PassportNumber, "Passport number", Number);
        yield return (FieldNames.PassportType, "Passport type", PassportType);
        yield return (FieldNames.DateOfBirth, "Date of birth", DateOfBirth);
        yield return (FieldNames.Sex, "Sex", Sex);
        yield return (FieldNames.Nationality, "Nationality", Nationality);
        yield return (FieldNames.PlaceOfBirth, "Place of birth", PlaceOfBirth);
        yield return (FieldNames.PassportIssueDate, "Issue date", IssueDate);
        yield return (FieldNames.PassportExpiryDate, "Expiry date", ExpiryDate);
    }
}
