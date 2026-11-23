# Residence Request Assistant

A Blazor Web App (.NET 10) that helps people without Arabic fill in the Kuwait Ministry of Interior residence request form,
with English hints beside every Arabic dropdown option and optional auto-fill from a passport photo.

```
dotnet build ResidenceForm.slnx
dotnet test
dotnet run --project src/ResidenceForm.Web
```

Layers: `Domain` (rules) → `Application` (use cases, ports) → `Infrastructure` (PDF, OCR, translations) → `Web` (UI, composition root).
Everything an AI agent or new developer needs is in [CLAUDE.md](CLAUDE.md).

Privacy: passport images and answers are processed in memory and never stored or sent to third parties.
