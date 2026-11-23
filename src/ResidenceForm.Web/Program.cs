using ResidenceForm.Application;
using ResidenceForm.Infrastructure;
using ResidenceForm.Web.Components;
using ResidenceForm.Web.Session;

// Composition root: the only place that knows every layer. Domain and Application never reference Infrastructure.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddFormApplication();
builder.Services.AddFormInfrastructure(assets => assets.RootPath = Path.Combine(builder.Environment.ContentRootPath, "Assets"));
builder.Services.AddScoped<FormSession>();       // one per browser circuit; nothing is persisted

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
