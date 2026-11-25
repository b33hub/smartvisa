namespace ResidenceForm.Domain.Forms;

public enum FieldType { Text, Choice, Date, Digits, Number, Email }

/// <param name="Pdf">Name of the field in the PDF (see <see cref="FieldNames"/>).</param>
/// <param name="Label">English label shown to the user.</param>
/// <param name="Len">Exact length for digit fields, 0 when not applicable.</param>
public sealed record FieldSpec(
    string Pdf,
    string Label,
    FieldType Type = FieldType.Text,
    string? Hint = null,
    bool Arabic = false,
    int Len = 0,
    bool Required = false,
    bool Wide = false);

public sealed record SectionSpec(string Title, string Subtitle, FieldSpec[] Fields);

/// <summary>The three kinds of request on the form; each one has its own "type" dropdown.</summary>
public sealed record RequestKind(string Value, string Title, string Arabic, string TypeField, string TypeLabel, string Description);

/// <summary>What the form asks for, step by step: the single place to change labels, hints and mandatory flags.</summary>
public static class FormSchema
{
    public const int RequestStep = 0, ApplicantStep = 1, EmployerStep = 2, TransferorStep = 3, ReviewStep = 4;

    /// <summary>The Procedure option (exactly as stored in the PDF) that makes the Transferor section relevant: "transfer of sponsorship".</summary>
    public const string TransferOfSponsorshipProcedure = "نقل كفالة";

    public static readonly string[] Steps = { "Request", "Applicant", "Employer", "Transferor", "Review" };
    public static readonly string[] StepIcons = { "🧾", "👤", "🏢", "🔁", "✅" };

    public static readonly FieldSpec Office =
        new(FieldNames.Office, "Processing office (governorate)", FieldType.Choice, Required: true);

    public static readonly FieldSpec Procedure =
        new(FieldNames.Procedure, "Procedure", FieldType.Choice, Required: true);

    public static readonly RequestKind[] Kinds =
    {
        new("0", "Entry visa", "سمة دخول", FieldNames.EntryVisaType, "Entry visa type", "A visa for someone coming into Kuwait"),
        new("1", "Residency", "إقامة", FieldNames.ResidencyType, "Residency type", "Issue, renew or change a residence permit"),
        new("2", "Absence permit", "إذن غياب", FieldNames.AbsencePermitType, "Absence permit reason", "Permission to stay outside Kuwait"),
    };

    public static FieldSpec TypeSpec(RequestKind kind) => new(kind.TypeField, kind.TypeLabel, FieldType.Choice, Required: true);

    public static readonly SectionSpec Applicant = new("Applicant", "The person who is arriving or resident in Kuwait", new[]
    {
        new FieldSpec(FieldNames.CivilId, "Civil ID", FieldType.Digits, "12 digits (optional)", Len: 12),
        new FieldSpec(FieldNames.ReferenceNumber, "Reference no.", FieldType.Digits),
        new FieldSpec(FieldNames.FullName, "Full name (English)", Required: true, Wide: true, Hint: "As in the passport"),
        new FieldSpec(FieldNames.ArabicName, "Full name (Arabic)", Arabic: true, Required: true, Wide: true, Hint: "Copy from the civil ID or an Arabic document"),
        new FieldSpec(FieldNames.DateOfBirth, "Date of birth", FieldType.Date, Required: true),
        new FieldSpec(FieldNames.Sex, "Sex", FieldType.Choice, Required: true),
        new FieldSpec(FieldNames.PlaceOfBirth, "Place of birth"),
        new FieldSpec(FieldNames.Nationality, "Nationality", FieldType.Choice, Required: true),
        new FieldSpec(FieldNames.PassportNumber, "Passport number", Required: true),
        new FieldSpec(FieldNames.PassportType, "Passport type", Hint: "e.g. Ordinary"),
        new FieldSpec(FieldNames.PassportIssueDate, "Passport issue date", FieldType.Date),
        new FieldSpec(FieldNames.PassportExpiryDate, "Passport expiry date", FieldType.Date),
        new FieldSpec(FieldNames.Occupation, "Occupation"),
        new FieldSpec(FieldNames.RelationToSponsor, "Relation to sponsor", FieldType.Choice),
    });

    public static readonly SectionSpec Employer = new("Employer / sponsor", "The person, company or authority sponsoring the applicant", new[]
    {
        new FieldSpec(FieldNames.EmployerCivilId, "Civil ID", FieldType.Digits, "12 digits", Len: 12, Required: true),
        new FieldSpec(FieldNames.EmployerReferenceNumber, "Reference / legal entity no.", FieldType.Digits, Required: false),
        new FieldSpec(FieldNames.EmployerName, "Name / authority / company", Arabic: true, Wide: true, Hint: "Copy from the company licence or civil ID", Required: true),
        new FieldSpec(FieldNames.EmployerEmail, "Email", FieldType.Email, Required: true),
        new FieldSpec(FieldNames.EmployerAddressPaci, "Address PACI number", FieldType.Digits, "8 digits", Len: 8, Required: true),
        new FieldSpec(FieldNames.EmployerNationality, "Nationality", FieldType.Choice, Required: true),
        new FieldSpec(FieldNames.EmployerOccupation, "Occupation", Required: true),
        new FieldSpec(FieldNames.EmployerSalary, "Salary", FieldType.Number, Required: true),
        new FieldSpec(FieldNames.EmployerGovernorate, "Governorate", FieldType.Choice, Required: true),
        new FieldSpec(FieldNames.EmployerArea, "Area", FieldType.Choice, Required: true),
        new FieldSpec(FieldNames.EmployerBlock, "Block", Required: true),
        new FieldSpec(FieldNames.EmployerStreet, "Street", Required: true),
        new FieldSpec(FieldNames.EmployerBuilding, "Building", Required: true),
        new FieldSpec(FieldNames.EmployerPhone, "Phone", FieldType.Digits, "8 digits", Len: 8, Required: true),
    });

    public static readonly SectionSpec Transferor = new("Transferor", "Only needed when the sponsorship is being transferred from someone else", new[]
    {
        new FieldSpec(FieldNames.TransferorCivilId, "Civil ID", FieldType.Digits, "12 digits", Len: 12),
        new FieldSpec(FieldNames.TransferorName, "Name / authority / company", Arabic: true, Wide: true),
    });

    /// <summary>The fields a wizard step collects (the request step's type dropdown depends on the chosen kind, see <see cref="FormAnswers"/>).</summary>
    public static IEnumerable<FieldSpec> SpecsForStep(int step) => step switch
    {
        RequestStep => new[] { Office, Procedure },
        ApplicantStep => Applicant.Fields,
        EmployerStep => Employer.Fields,
        TransferorStep => Transferor.Fields,
        _ => Array.Empty<FieldSpec>()
    };

    /// <summary>Returns a message when a non-empty value is badly formatted (digits only, exact length, email shape); otherwise null.</summary>
    public static string? Check(FieldSpec spec, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return spec.Type switch
        {
            FieldType.Digits when !value.All(char.IsAsciiDigit) => "Digits only",
            FieldType.Digits when spec.Len > 0 && value.Length != spec.Len => $"Expected {spec.Len} digits (you entered {value.Length})",
            FieldType.Email when !value.Contains('@') || !value.Contains('.') => "Doesn't look like an email address",
            _ => null
        };
    }
}
