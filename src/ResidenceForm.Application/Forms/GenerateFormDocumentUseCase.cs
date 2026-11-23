using ResidenceForm.Application.Abstractions;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Application.Forms;

/// <summary>Use case: turn the person's answers into the PDF documents they download.</summary>
public sealed class GenerateFormDocumentUseCase(IFormDocumentWriter writer)
{
    public byte[] CreateEditable(FormAnswers answers) => writer.CreateEditable(answers.Values);

    public byte[] CreatePrintReady(FormAnswers answers) => writer.CreatePrintReady(answers.Values);
}
