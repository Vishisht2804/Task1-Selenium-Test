# Offline Selenium Automation Framework

This .NET 8 / Selenium 4 / NUnit project automates the required **First Name**, **Gender**, and **State**
controls without relying on fixed ids, names, classes, field order, or absolute XPath expressions.

## Architecture

- [`DomContextExplorer`](Utilities/DomContext/DomContextExplorer.cs) discovers the top document, open Shadow DOM
  roots, nested roots, and iframe documents. It records frame ancestry and shadow-host paths and restores the
  original browsing context in `finally` blocks.
- [`FormCandidateDiscovery`](Utilities/FormCandidateDiscovery.cs) searches every discovered context and builds
  logical form candidates. It uses semantic labels, control types, form ownership, and accessible/attribute hints.
  Candidates are rejected when required fields are missing.
- Slot-aware inspection uses `assignedElements({ flatten: true })` and associates labels in a shadow tree with
  assigned controls without assuming slot names.
- [`ElementResolver`](Utilities/ElementResolver.cs) is bound to one complete candidate supplied by the test. Discovery
  never guesses when multiple forms exist; tests explicitly exercise each candidate.
- [`AutomationPracticePage`](Pages/AutomationPracticePage.cs) re-enters the recorded context before every Selenium
  interaction, uses native `SendKeys`, radio clicks, and `SelectElement`, then reads back the resulting value.

Only open Shadow DOM is supported. Closed roots are inaccessible through ordinary Selenium traversal and are reported
as skipped contexts. Iframe URLs are inspected only from local fixtures; they are never fetched directly by the
framework.

## Configuration and running

[`Config/appsettings.json`](Config/appsettings.json) defaults to the live CloudQA page. `PageUrl`, field values,
timeout, headless mode, and Chrome arguments can be overridden with `SELAUTO_` environment variables. Local
`file://`, localhost, `127.0.0.1`, and the CloudQA host are accepted.

```powershell
dotnet restore
dotnet test --logger "console;verbosity=detailed"
```

The live form test discovers every complete candidate on the page and exercises each candidate independently.

## Fixtures and verification

Structural field-interaction tests use local HTML fixtures. The ten scenarios cover top-level light DOM,
iframes with and without ids, open Shadow DOM, nested Shadow DOM through three to six levels, and an iframe inside
Shadow DOM. Additional fixtures cover changed markup, ambiguity, slots, recursion, context limits, and discovery
diagnostics.

The supplied CloudQA source was used as a structural reference. Where iframe contents were absent, synthetic local
fixtures were created and are identified as such in their comments and test names. Live tests require network access
to CloudQA and exercise every complete form discovered there.
