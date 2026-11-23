using Microsoft.Extensions.DependencyInjection;
using ResidenceForm.Application.Abstractions;
using ResidenceForm.Infrastructure.Imaging;
using ResidenceForm.Infrastructure.Ocr;
using ResidenceForm.Infrastructure.Pdf;
using ResidenceForm.Infrastructure.Translation;

namespace ResidenceForm.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the PDF, OCR and translation implementations of the application's ports.</summary>
    public static IServiceCollection AddFormInfrastructure(this IServiceCollection services, Action<AssetOptions> configureAssets)
    {
        services.Configure(configureAssets);

        // one PdfFormService plays two roles, so both ports resolve to the same singleton
        services.AddSingleton<PdfFormService>();
        services.AddSingleton<IFormTemplate>(sp => sp.GetRequiredService<PdfFormService>());
        services.AddSingleton<IFormDocumentWriter>(sp => sp.GetRequiredService<PdfFormService>());

        services.AddSingleton<IOptionTranslator, JsonOptionTranslator>();
        services.AddSingleton<IPrintedTextReader, PrintedTextOcr>();
        services.AddSingleton<IPassportScanner, PassportScanner>();
        services.AddSingleton<ICardScanner, CardScanner>();
        services.AddSingleton<IImagePreviewer, SkiaImagePreviewer>();
        return services;
    }
}
