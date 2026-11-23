namespace ResidenceForm.Domain.Forms;

/// <summary>
/// The names of the fields inside the Ministry's PDF form. They are the keys of every answer, so use these constants
/// instead of string literals. Inspect Assets/blank.pdf to list the real names.
/// </summary>
public static class FieldNames
{
    // request
    public const string Office = "--- اختار اسم المحافظه ---";
    public const string Procedure = "Dropdown1";
    public const string RequestKind = "Group1";            // radio: 0 entry visa, 1 residency, 2 absence permit
    public const string EntryVisaType = "EntrType";
    public const string ResidencyType = "restype";
    public const string AbsencePermitType = "Absance";

    // applicant
    public const string CivilId = "CID";
    public const string ReferenceNumber = "Ref";
    public const string FullName = "Full Name";
    public const string ArabicName = "Arabic name";
    public const string DateOfBirth = "DOB";
    public const string Sex = "Sex1";
    public const string PlaceOfBirth = "Place";
    public const string Nationality = "Nationality";
    public const string PassportNumber = "Pass No";
    public const string PassportType = "Pass Type";
    public const string PassportIssueDate = "Issue date";
    public const string PassportExpiryDate = "Exp date";
    public const string Occupation = "Occupation1";
    public const string RelationToSponsor = "Relation";

    // employer / sponsor
    public const string EmployerCivilId = "CID-2";
    public const string EmployerReferenceNumber = "Ref-2";
    public const string EmployerName = "Comp Name";
    public const string EmployerEmail = "Email";
    public const string EmployerAddressPaci = "CID-building";
    public const string EmployerNationality = "Nationality 1";
    public const string EmployerOccupation = "Occupation2";
    public const string EmployerSalary = "salary";
    public const string EmployerGovernorate = "Dropdown2";
    public const string EmployerArea = "Dropdown3";
    public const string EmployerBlock = "Block";
    public const string EmployerStreet = "Street";
    public const string EmployerBuilding = "Building";
    public const string EmployerPhone = "Tel";

    // transferor
    public const string TransferorCivilId = "cid-old";
    public const string TransferorName = "Text1124456";
}
