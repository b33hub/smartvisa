using System.Globalization;
using ResidenceForm.Domain.Documents;

namespace ResidenceForm.Domain.Forms;

public enum FillOutcome { Filled, SameAsEntered, KeptEntered }

/// <summary>
/// The aggregate holding everything a person has entered so far, keyed by PDF field name. Owns the rules about what is
/// mandatory and valid. Plain C#: no UI, no I/O, no change notifications (those belong to the UI session).
/// </summary>
public sealed class FormAnswers
{
    private const string DisplayDateFormat = "dd/MM/yyyy";
    private const string PickerDateFormat = "yyyy-MM-dd";

    private readonly Dictionary<string, string> _values = new();
    private readonly Dictionary<string, DocumentKind> _origins = new();      // field -> the document that filled it (until the person edits it)

    public IReadOnlyDictionary<string, string> Values => _values;

    public string? Get(string field) => _values.GetValueOrDefault(field);

    /// <summary>A value typed or chosen by the person. It becomes theirs: a document that filled this field earlier no longer owns it.</summary>
    public void Set(string field, string? value)
    {
        SetValue(field, value);
        _origins.Remove(field);
    }

    private void SetValue(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _values.Remove(field);
        else _values[field] = value;
    }

    public void Clear()
    {
        _values.Clear();
        _origins.Clear();
    }

    // ---- dates: the form wants dd/MM/yyyy, the browser's date picker speaks yyyy-MM-dd

    public string? GetDateForPicker(string field) =>
        DateTime.TryParseExact(Get(field), DisplayDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString(PickerDateFormat, CultureInfo.InvariantCulture)
            : null;

    public void SetDateFromPicker(string field, string? pickerValue) =>
        Set(field, DateTime.TryParseExact(pickerValue, PickerDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString(DisplayDateFormat, CultureInfo.InvariantCulture)
            : null);

    // ---- request kind: each kind owns a different "type" dropdown

    public RequestKind? Kind => FormSchema.Kinds.FirstOrDefault(k => k.Value == Get(FieldNames.RequestKind));

    public void ChooseRequestKind(string kindValue)
    {
        foreach (var kind in FormSchema.Kinds) _values.Remove(kind.TypeField);
        Set(FieldNames.RequestKind, kindValue);
    }

    // ---- filling from another source (e.g. a passport scan)

    /// <summary>
    /// Writes the value only when the box is still empty, so the person's own entries are never overwritten.
    /// With a <paramref name="source"/>, the box is remembered as filled by that document (see <see cref="RemoveFilledBy"/>).
    /// </summary>
    public FillOutcome FillIfEmpty(string field, string value, DocumentKind? source = null)
    {
        var current = Get(field);
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current.Trim() == value.Trim() ? FillOutcome.SameAsEntered : FillOutcome.KeptEntered;
        }

        SetValue(field, value);
        if (source is { } document) _origins[field] = document;
        return FillOutcome.Filled;
    }

    /// <summary>
    /// Clears the boxes a document filled, except those the person has edited since (editing hands ownership to the person).
    /// Returns the fields that were cleared.
    /// </summary>
    public IReadOnlyList<string> RemoveFilledBy(DocumentKind source)
    {
        var fields = _origins.Where(origin => origin.Value == source).Select(origin => origin.Key).ToList();
        foreach (var field in fields)
        {
            _values.Remove(field);
            _origins.Remove(field);
        }
        return fields;
    }

    // ---- validation

    /// <summary>What blocks leaving a step: empty mandatory fields and badly formatted values (labels, for display).</summary>
    public IReadOnlyList<string> Issues(int step)
    {
        var issues = new List<string>();

        if (step == FormSchema.RequestStep)
        {
            if (Kind is null) issues.Add("Request kind");
            else if (Get(Kind.TypeField) is null) issues.Add(Kind.TypeLabel);
        }

        foreach (var spec in FormSchema.SpecsForStep(step))
        {
            var value = Get(spec.Pdf);
            if (spec.Required && string.IsNullOrWhiteSpace(value)) issues.Add(spec.Label);
            else if (FormSchema.Check(spec, value) is not null) issues.Add(spec.Label);
        }
        return issues;
    }

    public IReadOnlyList<string> AllIssues() =>
        Enumerable.Range(0, FormSchema.ReviewStep).SelectMany(Issues).ToList();
}
