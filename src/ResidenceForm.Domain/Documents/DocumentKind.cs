namespace ResidenceForm.Domain.Documents;

/// <summary>The documents a person can upload to pre-fill the form. Each one is optional and independently removable.</summary>
public enum DocumentKind
{
    Passport,
    CivilIdFront,
    CivilIdBack
}
