using System.IO.Compression;
using System.Text.RegularExpressions;
using ResidenceForm.Application.Abstractions;
using ResidenceForm.Domain.Forms;

namespace ResidenceForm.Application.Forms;

/// <summary>A file ready to download.</summary>
public sealed record GeneratedFile(byte[] Content, string FileName, string ContentType);

/// <summary>Use case: turn the applications into the PDFs the person downloads (one PDF, or a ZIP with one PDF per application).</summary>
public sealed partial class GenerateFormDocumentUseCase(IFormDocumentWriter writer)
{
    /// <summary>A single-page preview of one application's answers.</summary>
    public byte[] CreatePreview(IReadOnlyDictionary<string, string> answers) => writer.CreatePrintReady(answers);

    /// <exception cref="InvalidOperationException">There is no application to generate.</exception>
    public GeneratedFile Create(ApplicationBatch batch, bool printReady)
    {
        var documents = batch.BuildDocuments();
        if (documents.Count == 0) throw new InvalidOperationException("There is no application to generate.");

        var kind = printReady ? "print" : "editable";
        byte[] Build(IReadOnlyDictionary<string, string> answers) => printReady ? writer.CreatePrintReady(answers) : writer.CreateEditable(answers);

        if (documents.Count == 1)
        {
            return new GeneratedFile(Build(documents[0].Answers), $"residence-request-{kind}.pdf", "application/pdf");
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < documents.Count; i++)
            {
                var entry = zip.CreateEntry($"{i + 1:00} - {SafeFileName(documents[i].Title)} ({kind}).pdf", CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(Build(documents[i].Answers));
            }
        }
        return new GeneratedFile(stream.ToArray(), $"residence-requests-{kind}.zip", "application/zip");
    }

    private static string SafeFileName(string title)
    {
        var cleaned = InvalidFileNameCharacters().Replace(title, " ").Trim();
        return cleaned.Length == 0 ? "application" : cleaned.Length > 60 ? cleaned[..60].TrimEnd() : cleaned;
    }

    [GeneratedRegex(@"[^\p{L}\p{N} ._-]+")]
    private static partial Regex InvalidFileNameCharacters();
}