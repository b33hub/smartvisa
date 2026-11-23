namespace ResidenceForm.Application.Abstractions;

/// <summary>English hints for the Arabic dropdown options. Display only: the value written to the PDF is always the Arabic option.</summary>
public interface IOptionTranslator
{
    string? English(string arabicOption);
}
