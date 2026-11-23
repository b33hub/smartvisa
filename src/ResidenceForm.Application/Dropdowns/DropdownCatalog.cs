using ResidenceForm.Application.Abstractions;
using ResidenceForm.Domain.Dropdowns;

namespace ResidenceForm.Application.Dropdowns;

/// <summary>
/// Builds the options of every dropdown once, from the PDF template plus English hints. Long lists (over 30 options)
/// are sorted A-Z by English so people can find their entry; short lists keep the PDF's own order.
/// </summary>
public sealed class DropdownCatalog : IDropdownCatalog
{
    private const int SortedWhenLongerThan = 30;

    private readonly Dictionary<string, IReadOnlyList<DropdownOption>> _optionsByField = new();

    public DropdownCatalog(IFormTemplate template, IOptionTranslator translator)
    {
        foreach (var field in template.DropdownFields)
        {
            var options = template.OptionsOf(field)
                .Select(raw => new DropdownOption(raw, raw.Trim(), translator.English(raw)))
                .ToList();

            if (options.Count > SortedWhenLongerThan)
            {
                options = options.OrderBy(o => o.English ?? "~" + o.Arabic, StringComparer.OrdinalIgnoreCase).ToList();
            }
            _optionsByField[field] = options;
        }
    }

    public IReadOnlyList<DropdownOption> For(string field) =>
        _optionsByField.TryGetValue(field, out var options) ? options : Array.Empty<DropdownOption>();

    public DropdownOption? Find(string field, string? raw) =>
        raw is null ? null : For(field).FirstOrDefault(o => o.Raw == raw);
}
