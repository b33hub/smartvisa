using System.Text;
using System.Text.RegularExpressions;

namespace ResidenceForm.Domain.CivilIds;

/// <summary>
/// Helpers for matching Arabic text that OCR has read: letter variants and diacritics are ignored when comparing, but values taken
/// from the text keep their original letters (a name must not be "normalised" into the form).
/// </summary>
public static partial class ArabicText
{
    /// <summary>Comparison form: no diacritics or tatweel, alef/ya/ta-marbuta variants unified, Arabic-Indic digits as ASCII, single spaces.</summary>
    public static string Normalise(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var original in text)
        {
            if (IsMark(original)) continue;

            builder.Append(original switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' or 'ئ' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                >= '٠' and <= '٩' => (char)('0' + (original - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (original - '۰')),
                _ => original
            });
        }
        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    private static bool IsMark(char c) =>
        c is (>= '\u064B' and <= '\u065F') or '\u0670' or '\u0640' or '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E');

    public static bool HasArabicLetters(string text) => ArabicLetterRegex().IsMatch(text);

    /// <summary>The words of a line, splitting on spaces and colons (labels are usually followed by ":").</summary>
    public static string[] Words(string text) =>
        SeparatorRegex().Split(text).Where(word => word.Length > 0).ToArray();

    /// <summary>True when the line contains the label (given in normalised form; may be several words).</summary>
    public static bool ContainsLabel(string line, string label) => IndexOfLabel(Words(line), label) >= 0;

    /// <summary>The text after the label, in its original letters, without the colon; null when the label is not in the line.</summary>
    public static string? AfterLabel(string line, string label)
    {
        var words = Words(line);
        var index = IndexOfLabel(words, label);
        if (index < 0) return null;

        var labelWordCount = Words(label).Length;
        return string.Join(' ', words.Skip(index + labelWordCount));
    }

    /// <summary>The line without the given words (normalised form), keeping the original letters of the rest.</summary>
    public static string RemoveWords(string line, params string[] normalisedWords) =>
        string.Join(' ', Words(line).Where(word => !normalisedWords.Contains(Normalise(word))));

    /// <summary>Every occurrence of any of the labels (normalised form) in the line, in reading order.</summary>
    public static IReadOnlyList<(string Label, int Index, int WordCount)> FindLabels(string line, IEnumerable<string> labels)
    {
        var words = Words(line);
        var found = new List<(string Label, int Index, int WordCount)>();
        foreach (var label in labels)
        {
            var labelWords = Words(label).Select(Normalise).ToArray();
            for (var start = 0; start + labelWords.Length <= words.Length; start++)
            {
                var matches = true;
                for (var offset = 0; offset < labelWords.Length && matches; offset++)
                {
                    matches = Normalise(words[start + offset]) == labelWords[offset];
                }
                if (matches) found.Add((label, start, labelWords.Length));
            }
        }
        return found.OrderBy(f => f.Index).ToList();
    }

    private static int IndexOfLabel(string[] words, string label)
    {
        var labelWords = Words(label).Select(Normalise).ToArray();
        for (var start = 0; start + labelWords.Length <= words.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < labelWords.Length && matches; offset++)
            {
                matches = Normalise(words[start + offset]) == labelWords[offset];
            }
            if (matches) return start;
        }
        return -1;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\u0600-\u06FF]")]
    private static partial Regex ArabicLetterRegex();

    [GeneratedRegex(@"[\s:：]+")]
    private static partial Regex SeparatorRegex();
}
