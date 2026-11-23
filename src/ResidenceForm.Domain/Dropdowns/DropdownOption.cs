namespace ResidenceForm.Domain.Dropdowns;

/// <summary>
/// One choice of a dropdown in the PDF. <see cref="Raw"/> is the exact string stored in the PDF and is the only value ever
/// written back; <see cref="English"/> is a display hint.
/// </summary>
public sealed record DropdownOption(string Raw, string Arabic, string? English)
{
    public string Label => English ?? Arabic;
    public string Display => English is null ? Arabic : $"{English}  ·  {Arabic}";

    /// <summary>Lower-cased, Arabic-letter-normalised text used for searching.</summary>
    public string SearchKey { get; } = Normalise($"{English} {Arabic}");

    public static string Normalise(string text) =>
        text.Trim().ToLowerInvariant().Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي');

    /// <summary>Options containing every word of the query (English or Arabic); prefix matches first, original order otherwise.</summary>
    public static IReadOnlyList<DropdownOption> Search(IReadOnlyList<DropdownOption> options, string query)
    {
        var normalised = Normalise(query);
        if (normalised.Length == 0) return options;

        var words = normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return options.Where(o => words.All(o.SearchKey.Contains))
                      .OrderByDescending(o => o.SearchKey.StartsWith(words[0], StringComparison.Ordinal))
                      .ToList();
    }
}
