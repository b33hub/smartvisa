namespace ResidenceForm.Domain.Documents;

/// <summary>How much to trust a value read from a document.</summary>
public enum Confidence
{
    /// <summary>Nothing was found.</summary>
    Missing,
    /// <summary>Found, but verification failed or two sources disagree: the person must check it.</summary>
    Check,
    /// <summary>Read from one source that has no check to verify it against.</summary>
    Read,
    /// <summary>Verified by a check digit, or confirmed by two independent sources.</summary>
    Good
}

/// <param name="Value">What goes into the form (for dropdowns: the exact PDF option string).</param>
/// <param name="Display">What the person sees (for dropdowns: English and Arabic).</param>
public sealed record Finding(string? Value, string? Display, Confidence Confidence, string? Note = null)
{
    public static readonly Finding None = new(null, null, Confidence.Missing);

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}
