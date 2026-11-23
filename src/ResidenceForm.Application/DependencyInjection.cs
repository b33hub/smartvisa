using Microsoft.Extensions.DependencyInjection;
using ResidenceForm.Application.Documents;
using ResidenceForm.Application.Dropdowns;
using ResidenceForm.Application.Forms;
using ResidenceForm.Domain.CivilIds;
using ResidenceForm.Domain.Dropdowns;
using ResidenceForm.Domain.Passports;

namespace ResidenceForm.Application;

public static class DependencyInjection
{
    /// <summary>Registers use cases and domain services. Ports (IFormTemplate etc.) are registered by the infrastructure.</summary>
    public static IServiceCollection AddFormApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDropdownCatalog, DropdownCatalog>();
        services.AddSingleton<CountryResolver>();
        services.AddSingleton<PassportInterpreter>();
        services.AddSingleton<CivilIdInterpreter>();
        services.AddSingleton<ReadDocumentUseCase>();
        services.AddSingleton<GenerateFormDocumentUseCase>();
        return services;
    }
}
