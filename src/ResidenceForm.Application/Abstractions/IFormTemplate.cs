namespace ResidenceForm.Application.Abstractions;

/// <summary>The blank form as a template: which dropdown fields exist and which options each one offers.</summary>
public interface IFormTemplate
{
    /// <summary>Names of all dropdown fields in the PDF.</summary>
    IReadOnlyList<string> DropdownFields { get; }

    /// <summary>The selectable options of a dropdown, exactly as stored in the PDF (placeholders removed).</summary>
    IReadOnlyList<string> OptionsOf(string field);
}
