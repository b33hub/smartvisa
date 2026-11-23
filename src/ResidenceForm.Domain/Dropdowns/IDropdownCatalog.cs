namespace ResidenceForm.Domain.Dropdowns;

/// <summary>Read-only access to the options of every dropdown of the form.</summary>
public interface IDropdownCatalog
{
    IReadOnlyList<DropdownOption> For(string field);

    DropdownOption? Find(string field, string? raw);
}
