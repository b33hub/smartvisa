# CLAUDE.md: guide for AI agents working on this repository

Read this file first. It tells you what the app does, how the code is organised, the rules to follow, and the traps that have
already cost time. Keep it up to date when you change structure or behaviour.

## 1. What this is

**Residence Request Assistant**: a Blazor Web App (.NET 10, Interactive Server) that helps people with **no Arabic** fill in the Kuwait
Ministry of Interior residence request form (`src/ResidenceForm.Web/Assets/blank.pdf`, an editable AcroForm PDF).

The user walks through a wizard (Request, Applicant, Employer, Transferor, Review), sees English labels and English hints beside the
Arabic dropdown options, optionally uploads a passport photo to auto-fill the Applicant step and the front/back of a Kuwaiti civil ID to auto-fill the Employer step
(previews are shown in a side panel; every upload is optional and removable), then downloads:

* an **editable PDF** (the real form fields are filled; the person can still edit boxes), and
* a **print-ready PDF** (values drawn into the page with correct Arabic shaping; identical in every viewer).

The companions page (page 2 of the form) is intentionally **not collected**; those boxes stay blank and editable.

## 2. Build status: read this before trusting the code

The code was written **without access to a .NET SDK**, so it has **never been compiled or run as a whole**.
Parts were verified separately: the MRZ/OCR logic and the PP-OCR pipeline were prototyped and tested in Python against a specimen
passport; Tesseract's Arabic reader was tried on civil ID specimens. Unit tests exist for the domain (forms, MRZ, passports, civil IDs, countries,
provenance) but have not been executed.

**First actions in any new session:** `dotnet build ResidenceForm.slnx` then `dotnet test`. Fix compile errors before adding features.
If the build is green, update this section.

## 3. Commands

```
dotnet build ResidenceForm.slnx
dotnet test                                   # domain unit tests (xUnit)
dotnet run --project src/ResidenceForm.Web    # http://localhost:5000 (Development profile in Properties/launchSettings.json)
dotnet publish src/ResidenceForm.Web -c Release   # production: always serve the published output, never `dotnet run` in Production
```

Run in **Development** mode when using `dotnet run` (the launch profile does this). In Production mode `dotnet run` cannot find the
static web assets and `blazor.web.js` returns 500.

## 4. Architecture (DDD, clean layering)

```
Web  ──►  Application  ──►  Domain
 │            ▲
 └──►  Infrastructure ─┘      (Infrastructure implements Application's ports)
```

| Project | Responsibility | May reference |
|---|---|---|
| `ResidenceForm.Domain` | Business rules and value objects. **Pure C#**: no packages, no I/O, no DI, no UI. | nothing |
| `ResidenceForm.Application` | Use cases and **ports** (interfaces) the infrastructure implements. | Domain |
| `ResidenceForm.Infrastructure` | Adapters: PDF (PDFsharp), OCR (Tesseract, ONNX Runtime), images/fonts (SkiaSharp), JSON translations. | Application, Domain |
| `ResidenceForm.Web` | Blazor UI and the **composition root** (`Program.cs`). The only project that wires everything. | all |

**Dependency rule:** arrows point inward only. Domain never references Application/Infrastructure/Web. Application never references
Infrastructure. If you need a new capability from the outside world, add an interface in `Application/Abstractions`, implement it in
Infrastructure, and register it in `Infrastructure/DependencyInjection.cs`.

### Project map

```
src/ResidenceForm.Domain
  Forms/        FieldNames (PDF field-name constants), FormSchema (labels, hints, mandatory flags, steps),
                FormAnswers (aggregate: values, validation, date conversion, "fill only if empty")
  Dropdowns/    DropdownOption (Raw Arabic option + English hint + search), IDropdownCatalog
  Documents/    DocumentKind (Passport, CivilIdFront, CivilIdBack), IDocumentReading (what any document reading exposes),
                Finding / Confidence (a value + how much to trust it)
  Passports/    Mrz (ICAO 9303 code-line parsing), PrintedZoneParser (printed values by position), OcrWord,
                CountryResolver (BGD / BANGLADESHI -> an option of a nationality dropdown, per field), PassportScan (raw reading),
                PassportInterpreter (merge rules), PassportData (result)
  CivilIds/     CardLine / CardScan (raw reading of a card side: Latin + Arabic text per box), ArabicText (label-aware matching),
                CivilIdInterpreter (front/back -> employer values), CivilIdData (result)
src/ResidenceForm.Application
  Abstractions/ IFormTemplate, IFormDocumentWriter, IOptionTranslator, IPassportScanner, ICardScanner, IPrintedTextReader, IImagePreviewer
  Dropdowns/    DropdownCatalog (builds options once; long lists sorted A-Z by English)
  Documents/    ReadDocumentUseCase (one entry point for passport / civil ID front / civil ID back)
  Forms/        GenerateFormDocumentUseCase
  DependencyInjection.cs   AddFormApplication()
src/ResidenceForm.Infrastructure
  Pdf/          PdfFormService (reads the blank PDF; fills editable fields), PdfWidgets (low-level PDF structure),
                FlatPdfRenderer (print-ready), PdfFieldDefinition
  Ocr/          PassportScanner and CardScanner (orchestrate), TesseractTextReader (English + Arabic), PrintedTextOcr (PP-OCR on ONNX),
                ImageOperations
  Imaging/      SkiaImagePreviewer (small JPEG data-URL previews)
  Translation/  JsonOptionTranslator (Assets/translations.json)
  AssetOptions.cs, DependencyInjection.cs   AddFormInfrastructure()
src/ResidenceForm.Web
  Program.cs    composition root
  Session/      FormSession (scoped per browser tab: FormAnswers + wizard position + upload slots + change events),
                UploadedDocument (state of one upload slot)
  Components/   App, Routes, Layout/MainLayout (LeptonX-style shell), Pages/Home (the wizard + documents panel),
                Shared/ FormField, ChoiceInput, SearchSelect, DocumentSlot
  Assets/       blank.pdf, Amiri font, translations.json, tessdata/ (eng + ara traineddata), ocr/ (det.onnx, rec.onnx, dict.txt)
tests/ResidenceForm.Domain.Tests   xUnit tests for the domain (no mocks needed: the domain is pure)
```

## 5. Domain knowledge you must know

### The PDF form
* Field names, positions and **dropdown options are read from the PDF at start-up** (`PdfFormService`), never hardcoded.
  The field-name constants in `FieldNames` are the keys of every answer. Inspect the PDF if one seems wrong.
* A dropdown value written to the PDF must be the **exact option string stored in the PDF** (`DropdownOption.Raw`), which can contain
  leading spaces. The English text is display-only and must **never** be written to the PDF.
* `Group1` is a radio group (0 entry visa, 1 residency, 2 absence permit); each kind has its own type dropdown
  (`EntrType`, `restype`, `Absance`). `FormAnswers.ChooseRequestKind` clears the other two.
* Dates in the form are `dd/MM/yyyy`. The browser date picker uses `yyyy-MM-dd`; `FormAnswers` converts.
* Mandatory rules live in `FormSchema` (`Required: true`). Employer fields are all mandatory; the applicant's Civil ID is optional;
  the transferor section is optional. `FormAnswers.Issues(step)` blocks leaving a step; `FormSession.GoTo` enforces it.

### Editable vs print-ready PDF
* Editable: fills real AcroForm fields (`/V`), drops stale appearances, sets `NeedAppearances`. Arabic is drawn by the **viewer**, so
  Acrobat is reliable and some browsers/phones are not.
* Print-ready: widgets removed, text drawn with HarfBuzz shaping (Arabic joins correctly) and stamped over the form. Not editable.

### Passport reading pipeline (optional feature on the Applicant step)
1. `PassportScanner` (Infrastructure) decodes the image (EXIF rotation applied), finds the two code lines with **Tesseract**
   (restricted alphabet, several crops, tries rotations), and reads the printed text with **PP-OCR on ONNX Runtime** (`PrintedTextOcr`).
   Result: a raw `PassportScan`.
2. `PassportInterpreter` (Domain) merges it into `PassportData`:
   * a **code-line value whose check digit passed always wins** over printed text;
   * if the check failed, the printed value is used and marked "please check";
   * values both sources agree on are `Confidence.Good`; single-source values are `Read`; disagreements are `Check`.
   * Sex: `M` becomes the "Male" option, `F` "Female". Nationality: country code or printed word matched against the form's own
     Nationality options (`CountryResolver`). Passport type from the first code-line letters (`P` becomes `عادي`). Place of birth is
     filled as printed, **not translated**.
3. `FormSession.ApplyPassport` fills **only boxes that are still empty** (`FormAnswers.FillIfEmpty`); the person's entries are never overwritten.

Confidence meanings: `Good` verified or confirmed by two sources; `Read` one source, nothing to verify against; `Check` unverified or
conflicting; `Missing` not found.

Never "repair" a value by trying alternatives until a check digit passes: with several candidates a wrong value will pass by chance
(~10 % per candidate). Only class-based normalisation is allowed (letters inside digit fields become digits; see `Mrz`).

The printed-zone parser follows the **Bangladeshi data-page layout** (left column: given name, nationality, birth date, sex, issue,
expiry; place of birth right of sex). Other layouts degrade to the code lines. Values are located by pattern and neighbours because the
bilingual labels are often unreadable.

### Civil ID reading (optional, Employer step)
* Two slots, front and back, each optional and independent. Front: civil ID number, Arabic holder name, nationality code (e.g. `KWT`).
  Back: profession, street, block, the 8-digit address (PACI) number, and one governorate/area line.
  Form fields filled: `CID-2`, `Comp Name`, `Nationality 1`, `Occupation2`, `Dropdown2`, `Dropdown3`, `Block`, `Street`, `CID-building`.
  Not on the card, so the person types them: reference no., email, salary, phone. **Building is deliberately left empty** (which card value
  is the building number was never confirmed; change it in `CivilIdInterpreter` and `CivilIdData.FormMapping` when known).
* **Two readers per text box.** The PP-OCR model has **no Arabic letters**, so it reads digits and English only. Every detected box is
  therefore cropped and read again with Tesseract's Arabic model (`CardScanner`). `CardLine.Latin` is used for numbers, `CardLine.Arabic`
  for labels and Arabic values. Arabic labels (المهنة, شارع, القطعة, الرقم الآلي ...) identify lines, so rules do not depend on exact positions.
* The back's governorate/area line is matched against **both** the governorate and the area option lists (a governorate match is not
  repeated as an area). Nationality uses the employer's list (`FieldNames.EmployerNationality`), which, unlike the applicant's, contains Kuwait.
* Everything read from a card is `Confidence.Read`; there is nothing on a card to verify it against, so the person reviews it.
* Arabic values (name, profession, street) keep their original letters; only comparisons use `ArabicText.Normalise`.

### Uploaded documents: UX rules (do not break)
* All uploads are optional; the form must remain fully usable without them.
* While any document is being read, the form is locked (`<fieldset disabled>` plus an overlay with a spinner) and wizard navigation is disabled.
* The side panel (right of the form; below it on narrow screens) shows the current step's slots: passport on the Applicant step,
  civil ID front/back on the Employer step. Loaded documents show a preview, what was read, and a **Remove** button.
* **Provenance:** `FormAnswers.FillIfEmpty(field, value, source)` remembers which document filled which box. `RemoveFilledBy(source)` clears
  those boxes, **except any the person has edited since** (a typed edit hands ownership to the person: `FormAnswers.Set` drops the origin).
  Boxes the person had already filled before the upload are never touched and never owned by a document.

## 6. Conventions (follow these)

* **C#**: file-scoped namespaces, nullable reference types on, `sealed` by default, records for value objects, primary constructors for
  simple DI classes, expression-bodied members when they stay readable, no `#region`.
* **Names**: PascalCase types/members, `_camelCase` private fields, `IInterface`, async methods end with `Async` only when they return `Task`
  and there is a sync twin. Use intention-revealing names; avoid abbreviations (`Mrz` and `Ocr` are domain terms and fine).
* **No magic strings** for PDF field names: use `FieldNames`. No magic numbers for wizard steps: use `FormSchema.*Step`.
* **DI**: constructor injection only; no service locator, no `static` mutable state. Register in `AddFormApplication` /
  `AddFormInfrastructure` / `Program.cs`. Infrastructure classes needing paths take `IOptions<AssetOptions>`.
* **Layering**: keep rules in the Domain (testable without mocks); keep I/O in Infrastructure; keep Razor components thin (they call
  `FormSession`, `IDropdownCatalog`, use cases).
* **Comments** explain *why* (rules, traps), not what the code obviously does. XML docs on public types of the Domain and Application.
* **Errors**: expected failures become friendly UI messages (see `PassportUpload`); unexpected ones are logged with `ILogger` in
  Infrastructure. Never swallow exceptions silently.
* **Privacy**: passport images and answers are processed in memory only. Do **not** log, store or transmit them. Keep it that way.
* **Tests**: every domain rule gets a test. Prefer small, intention-named tests over broad ones.

## 7. Blazor traps (learned the hard way)

1. **A child component is skipped on parent re-render when all its parameters are unchanged primitives/strings.** If a component reads
   shared state directly (e.g. an injected service) instead of through parameters, it will not refresh when that state changes.
   `ChoiceInput` once ignored a passport auto-fill for exactly this reason. Pass the current value in as a **parameter**
   (`Value="@Session.Get(...)"`) and raise changes through `EventCallback`s.
2. `FormSession` raises `Changed`; components that must redraw subscribe in `OnInitialized` and unsubscribe in `Dispose`
   (`Home`, `MainLayout`). Notifications from background work must go through `InvokeAsync(StateHasChanged)`.
3. Long work (OCR, PDF generation) runs in `Task.Run`, and the UI shows a spinner first (`StateHasChanged()` before awaiting).
4. File uploads use `InputFile` with an explicit size limit; reset it with a changing `@key` so the same file can be chosen again.
5. `DocumentSlot` takes only an enum parameter, so it would never redraw on its own: it subscribes to `FormSession.Changed` itself.
6. `<fieldset disabled>` locks native controls only; custom components must render disabled children or sit inside the fieldset (they do).

## 8. Common tasks

* **Change a label, hint or mandatory flag:** edit `FormSchema` (Domain). Update tests if a rule changes.
* **Add a form field:** add the constant to `FieldNames`, the `FieldSpec` to the right section in `FormSchema`. The PDF must already contain it.
* **Fix or add an English hint for a dropdown option:** edit `Assets/translations.json` (key = Arabic option, whitespace collapsed).
  Display only. Nothing else to change.
* **A nationality is not matched:** add an entry to `IsoAlias` or `Demonyms` in `CountryResolver`, plus a test.
* **Support another passport layout:** extend `PrintedZoneParser` (add the layout's rules; keep the Bangladeshi test) and add tests with
  synthetic `OcrWord` boxes. Do not edit `PassportInterpreter` for layout differences.
* **Swap an OCR engine or the PDF library:** implement the port in Infrastructure; nothing else should change.

## 9. Known limitations

* Not compiled as a whole yet (see section 2).
* Printed-zone reading is tuned for Bangladeshi passports; text boxes are axis-aligned, so strongly tilted photos may fail (code lines
  are then still used). PDF uploads are not supported, only JPG/PNG/WebP/BMP up to 8 MB.
* The editable PDF relies on the viewer to draw Arabic. The "print-ready" copy exists because of that.
* Arabic names, employer names and Arabic-only boxes must be typed or pasted by the user when a document can't supply them; there is no transliteration.
* Civil ID reading was designed from **specimen cards only** (labelled diagrams); real cards with glare and security patterns will read worse,
  especially Arabic text. Tesseract's Arabic model read the specimen lines well when cropped; that is the only evidence. Civil ID number
  check digits are not verified (the algorithm was not confirmed), and front/back civil ID numbers are not cross-checked.
* Passport-type Arabic words and the translation dictionary are best-effort and should be reviewed by a native speaker.
* Linux/Docker needs extra native packages (HarfBuzzSharp and Tesseract native libraries); the Windows development path is the tested one.

## 10. Licences of bundled assets

Amiri font (SIL OFL, `Assets/OFL-Amiri.txt`), Tesseract `eng` and `ara` traineddata (Apache-2.0), PP-OCRv4 ONNX models from PaddleOCR via
RapidOCR (Apache-2.0, `Assets/ocr/README.txt`). Libraries: PDFsharp (MIT), SkiaSharp and HarfBuzzSharp (MIT), Tesseract .NET (Apache-2.0),
ONNX Runtime (MIT).
