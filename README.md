# Resilient Selenium Automation (C# / .NET 8 / Selenium 4 / NUnit)

Fills three fields on https://app.cloudqa.io/home/AutomationPracticeForm and verifies them:

| Field | Control | Default value |
|---|---|---|
| First Name | text box | `Alex` |
| Gender | radio group | `Male` |
| State | `<select>` | `India` |

The code contains **no id, name, class, XPath or index locators**. Fields are found by their visible label
text and DOM relationships (see "How the resolver works").

> **Verification status.** Built and run on .NET SDK 10 (net8.0 target), Chrome 157 and Selenium Manager. All 5 tests pass against the live page and against `Fixtures/modified-form.html`, including the ambiguity test.

## Project structure

```
SeleniumAutomation/
  SeleniumAutomation.csproj
  README.md
  Config/
    appsettings.json            URL + test data + timeout + headless
    TestSettings.cs             loads appsettings.json, then SELAUTO_* environment overrides
  Base/BaseTest.cs              browser start/stop, screenshot on failure, always quits the browser
  Pages/AutomationPracticePage.cs   EnterFirstName / SelectGender / SelectState + read-back verification
  Utilities/
    ElementResolver.cs          semantic element resolution + ambiguity detection
    DomScripts.cs               read-only JavaScript used to inspect label/control relationships
    WaitHelper.cs               WebDriverWait wrappers + bounded retry
    ResolutionDiagnostics.cs    exceptions + trace used in error messages
    TextUtil.cs                 whitespace / case normalisation
  Tests/AutomationPracticeTests.cs
  Tests/AmbiguityTests.cs           verifies ambiguous matches fail rather than guess
  Fixtures/modified-form.html      modified page used to demonstrate resilience
  Fixtures/ambiguous-form.html     two identical forms used by the ambiguity test
  .gitignore                       excludes build output, IDE files and test artifacts
```

## Setup and run

Requirements: .NET 8 SDK, Google Chrome, internet access on first run (Selenium Manager downloads the matching
ChromeDriver; no manual driver download).

```bash
cd SeleniumAutomation
dotnet restore
dotnet test --logger "console;verbosity=detailed"
```

Per-field outcomes are printed as `[PASS]` / `[FAIL]` lines, followed by the resolver trace.

### Configuration

`Config/appsettings.json`: `PageUrl`, `FirstNameValue`, `GenderValue`, `StateValue`, `TimeoutSeconds`,
`Headless` (default `true`), `ChromeExtraArgs` (e.g. `--no-sandbox` in Docker).

Any value can be overridden without touching code, using `SELAUTO_` environment variables:

```bash
# bash / zsh
SELAUTO_PAGEURL="https://example.com/other-version.html" SELAUTO_HEADLESS=false dotnet test

# PowerShell
$env:SELAUTO_PAGEURL = "https://example.com/other-version.html"; $env:SELAUTO_HEADLESS = "false"; dotnet test
```

`State` is matched by exact option text or value. On the real page it contains countries, and `India` is one of them.

## Testing against the original page and the modified fixture

Original page (default `PageUrl`):

```bash
dotnet test --logger "console;verbosity=detailed"
```

Modified fixture, served locally (same code, only the URL changes):

```bash
# terminal 1
cd SeleniumAutomation/Fixtures
python3 -m http.server 8080

# terminal 2
cd SeleniumAutomation
SELAUTO_PAGEURL="http://localhost:8080/modified-form.html" dotnet test --logger "console;verbosity=detailed"
```

(`file:///absolute/path/to/Fixtures/modified-form.html` also works as `PageUrl`.)

Try other values too, e.g. `SELAUTO_GENDERVALUE=Female SELAUTO_STATEVALUE=Japan`.

## How the resolver works

For each field the resolver finds **label-like elements**: any visible element whose own text equals the field
label after normalisation (whitespace collapsed, case-insensitive, trailing `:`/`*` ignored). Labels are not required
to be `<label>` tags. Exact equality is used, so `Last Name` never matches `First Name` and `Male` never matches
`Female`/`Transgender`.

For each label it tries, in priority order, until one yields controls of the **expected type**
(text input / radio / native select), visible:

1. `label[for]` → element with that id. If the id is duplicated, only controls in the label's own form are preferred.
2. `aria-labelledby` pointing at the label.
3. A control nested inside the `<label>`.
4. **Nearest container**: walk up from the label (max 6 levels) to the first ancestor that contains eligible
   controls. Controls that are explicitly labelled with a *different* text are excluded.
5. If nothing was resolved through a label at all: attribute signals (`aria-label`, `placeholder`, `title`, `name`, `id`)
   as a last resort. Attributes are never mandatory.

**Gender** is resolved as a *group*: radios found near the label are grouped by (`name`, form owner), restricted to the
container found. The requested option is then matched, exact and case-insensitive, against (a) `<label>` text,
(b) `aria-label`/`aria-labelledby`, (c) adjacent text (the text node/element right after the radio, or right before
it, whichever orientation gives every radio in the group a text) and only if none matches, (d) the `value` attribute.
The invalid `<span for="...">` markup in the original page is deliberately **not** treated as a label (the original's
"Transgender" span points `for="female"`; trusting it would make two radios match).

**Ambiguity handling.** If several candidates remain, the resolver narrows them, in this order:

1. **Scope**: a candidate must share a `<form>` (or the document, when there is no form) with candidates for the
   *other two* target fields. This identifies the "primary form" semantically, as the form that contains First Name,
   Gender and State, without a form id.
2. **Attribute hints**: weak scoring of id/name/placeholder/aria-label/autocomplete (`given`, `fname` vs `last`, `surname`).

If more than one candidate still remains, it throws `AmbiguousElementException` instead of guessing. The message lists
the strategies tried, counts and the candidate elements. If nothing is found within `TimeoutSeconds`, it throws
`ElementResolutionException` with the trace of the last attempt.

**Shadow DOM and iframes are never searched.** Only the light DOM of the top document is inspected. On the original
page the Shadow DOM demos keep the original ids in their light DOM (`fname`, `state`), but their labels live inside
shadow roots, so they never become candidates.

**Interaction and robustness.** Elements come from `WebDriverWait`-based polling. Operations run inside a bounded
retry (3 attempts) that re-resolves elements on `StaleElementReferenceException`, `ElementClickIntercepted` and
`ElementNotInteractable`; other errors fail immediately. JavaScript is used only for read-only DOM inspection; typing,
clicking and selecting use Selenium APIs (`SendKeys`, `Click`, `SelectElement`). A custom-styled radio whose `<input>`
is hidden is activated by clicking its `<label>`, still through Selenium. After acting, each field is read back and
compared; the form is never submitted.

## Resilience test matrix

"Fixture" means demonstrated by `Fixtures/modified-form.html` (executed, passing).
"Design only" means the code handles it by design but no fixture exercises it.

| Change | Handled | Evidence |
|---|---|---|
| IDs changed | Yes | Fixture (real fields use `q-1`, `region_q`, `g_a`; shadow-demo copies keep the old ids) |
| `name` attributes changed | Yes | Fixture |
| CSS classes changed | Yes | Fixture |
| Fields reordered | Yes | Fixture (State, Gender, then First Name; Last Name first) |
| Extra wrapper elements / deeper nesting | Yes | Fixture |
| Label without `for` | Yes | Fixture (strategy 4) |
| Label is a `<span>`/`<div>`, not `<label>` | Yes | Fixture (Gender, State) |
| Extra similar text input in the same container | Yes | Fixture (`Nickname` before the real input, resolved by hints) |
| Extra unlabeled `<select>` in the same container | Yes | Fixture (`tz_zone`, resolved by hints) |
| Similar field in another form (First Name, State) | Yes | Fixture (`#billing`, removed by scope narrowing) |
| Hidden duplicate field | Yes | Fixture (`display:none` label ignored) |
| Another radio group offering "Male" | Yes | Fixture (`Salutation`) |
| Radio option text as wrapping label / `label[for]` / adjacent span | Yes | Fixture (one of each) |
| Radio values / select option values changed | Yes | Fixture (`value="IN"`; text still matched) |
| Shadow DOM copies with duplicate ids | Yes | Fixture; original page |
| iframes containing a copy of the form | No | Not searched; resolver only inspects the top-level document |
| Radio text placed before the radio instead of after | Yes | Design only |
| `aria-labelledby` / `fieldset+legend` structures | Yes | Design only |
| Custom-styled radio with hidden `<input>` | Yes | Design only (label click) |
| Two identical complete forms with no distinguishing attributes | Fails loudly | `Fixtures/ambiguous-form.html` + `Tests/AmbiguityTests.cs` assert `AmbiguousElementException`; executed, passing |
| Label text with trailing `:` / `*` / extra spaces / different case | Yes | Design only |

## Known limitations (rules that would need updating)

- **Label text is the anchor.** If the visible text changes ("First Name" → "Given name" is only partly covered by the
  synonyms in `FieldSpecs`), or the page is localised, update the `FieldSpec` entries.
- **Labels or controls inside Shadow DOM or iframes** are not found by design. Supporting them needs an explicit,
  scoped rule (`SearchContext` into a shadow root / `SwitchTo().Frame`) so demo copies are not hit by accident.
- **Native controls only.** A div-based custom dropdown (no `<select>`) or a contenteditable "text box" is not
  supported; `SelectElement` requires a real `<select>`.
- **Flat layouts with no per-field container.** If label A, input A, label B, input B are siblings in one flat
  container *and* no `for`/aria link exists *and* attribute hints do not separate them, the resolver reports ambiguity
  rather than guessing from document order.
- **Hints are English tokens** (`first`, `given`, `last`, `region`...). Other languages need extra tokens.
- **Radio option text far from the radio** (not adjacent, not in a `<label>`, no aria) cannot be associated; only the
  `value` attribute remains.
- Select option matching is exact (case-insensitive), not partial. `India` will not match `Indiana`.
- Chrome only. Selenium Manager needs network access the first time.
