using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ResidenceForm.Application.Abstractions;

namespace ResidenceForm.Infrastructure.Translation;

/// <summary>
/// English hints loaded from Assets/translations.json (key: the Arabic option with whitespace collapsed).
/// Edit that file to fix or add a translation; no code change needed.
/// </summary>
public sealed partial class JsonOptionTranslator : IOptionTranslator
{
    private readonly Dictionary<string, string> _translations;

    public JsonOptionTranslator(IOptions<AssetOptions> assets)
    {
        var path = assets.Value.TranslationsPath;
        _translations = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8)) ?? new()
            : new();
    }

    public string? English(string arabicOption) =>
        _translations.GetValueOrDefault(CollapseWhitespace(arabicOption));

    private static string CollapseWhitespace(string text) => WhitespaceRegex().Replace(text.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
