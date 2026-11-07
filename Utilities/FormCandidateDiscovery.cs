using OpenQA.Selenium;
using SeleniumAutomation.Utilities.DomContext;

namespace SeleniumAutomation.Utilities;

public enum ControlKind { Text, Radio, Select }

public sealed record FieldSpec(
    string Name,
    ControlKind Kind,
    string[] LabelTexts,
    string[] PositiveHints,
    string[] NegativeHints)
{
    public string KindName => Kind.ToString().ToLowerInvariant();
}

public static class FieldSpecs
{
    public static readonly FieldSpec FirstName = new(
        "First Name", ControlKind.Text,
        new[] { "First Name", "Firstname", "Given Name" },
        new[] { "first", "fname", "given", "forename" },
        new[] { "last", "lname", "surname", "family", "middle" });

    public static readonly FieldSpec Gender = new(
        "Gender", ControlKind.Radio,
        new[] { "Gender", "Sex" },
        new[] { "gender", "sex" },
        Array.Empty<string>());

    public static readonly FieldSpec State = new(
        "State", ControlKind.Select,
        new[] { "State", "Province", "State/Province" },
        new[] { "state", "province", "region" },
        new[] { "country", "city", "zip", "postal" });

    public static IReadOnlyList<FieldSpec> All { get; } = new[] { FirstName, Gender, State };
}

public sealed record ResolvedField(
    FieldSpec Spec,
    DomContextDescriptor Context,
    IWebElement Element,
    IReadOnlyList<IWebElement> Group,
    string ScopeKey,
    string Strategy);

public sealed class FormCandidate
{
    internal FormCandidate(DomContextDescriptor context, string scopeKey, string? heading)
    {
        Context = context;
        ScopeKey = scopeKey;
        Heading = heading;
    }

    public DomContextDescriptor Context { get; }
    public string ScopeKey { get; }
    public string? Heading { get; }
    public Dictionary<string, ResolvedField> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AmbiguousFields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Diagnostics { get; } = new();
    public bool IsComplete => AmbiguousFields.Count == 0 && FieldSpecs.All.All(spec => Fields.ContainsKey(spec.Name));
    public string Description => $"{Context.Path}, scope '{ScopeKey}'" +
        (string.IsNullOrWhiteSpace(Heading) ? "" : $", heading '{Heading}'");
}

public sealed record FormDiscoveryResult(IReadOnlyList<FormCandidate> Candidates, IReadOnlyList<string> Diagnostics);

/// <summary>
/// Finds semantic field groups in every document and open shadow root discovered by
/// <see cref="DomContextExplorer"/>. It deliberately does not know page-specific ids, names or classes.
/// </summary>
public sealed class FormCandidateDiscovery
{
    private readonly IWebDriver _driver;
    private readonly DomContextExplorerOptions _options;
    private readonly Action<string> _log;

    public FormCandidateDiscovery(IWebDriver driver, DomContextExplorerOptions? options = null, Action<string>? log = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _options = options ?? new DomContextExplorerOptions();
        _log = log ?? (_ => { });
    }

    public FormDiscoveryResult Discover()
    {
        var explorer = new DomContextExplorer(_driver, _options, _log);
        var contexts = explorer.Discover();
        var candidates = new Dictionary<string, FormCandidate>();
        var diagnostics = new List<string>();
        diagnostics.AddRange(contexts.Failures.Select(f => $"{f.Path}: {f.Operation} failed: {f.Message}"));

        foreach (var context in contexts.Contexts)
        {
            try
            {
                explorer.WithContext(context, _ =>
                {
                    foreach (var raw in ReadCandidates(context))
                    {
                        var key = $"{context.Id}:{raw.ScopeKey}";
                        if (!candidates.TryGetValue(key, out var candidate))
                        {
                            candidate = new FormCandidate(context, raw.ScopeKey, context.AssociatedHeading);
                            candidates.Add(key, candidate);
                        }

                        if (candidate.Fields.ContainsKey(raw.Field.Name))
                        {
                            candidate.AmbiguousFields.Add(raw.Field.Name);
                            candidate.Diagnostics.Add(
                                $"{raw.Field.Name}: another plausible control was found via {raw.Strategy}; ambiguity retained.");
                            continue;
                        }

                        candidate.Fields.Add(raw.Field.Name, new ResolvedField(
                            raw.Field, context, raw.Element, raw.Group, raw.ScopeKey, raw.Strategy));
                        candidate.Diagnostics.Add(
                            $"{raw.Field.Name}: accepted {Describe(raw.Element)} via {raw.Strategy}");
                    }
                    return true;
                });
            }
            catch (Exception ex) when (ex is WebDriverException or InvalidOperationException)
            {
                diagnostics.Add($"{context.Path}: candidate inspection failed: {ex.Message}");
            }
        }

        foreach (var candidate in candidates.Values)
        {
            if (candidate.AmbiguousFields.Count > 0)
                candidate.Diagnostics.Add($"rejected: ambiguous {string.Join(", ", candidate.AmbiguousFields)}");
            else if (!candidate.IsComplete)
                candidate.Diagnostics.Add($"rejected: missing {string.Join(", ", FieldSpecs.All.Where(s => !candidate.Fields.ContainsKey(s.Name)).Select(s => s.Name))}");
        }

        return new FormDiscoveryResult(candidates.Values.ToList(), diagnostics);
    }

    private static string Describe(IWebElement element) =>
        $"<{element.TagName} type='{element.GetDomAttribute("type")}' name='{element.GetDomAttribute("name")}'>";

    private IReadOnlyList<RawCandidate> ReadCandidates(DomContextDescriptor context)
    {
        var js = (IJavaScriptExecutor)_driver;
        var host = context.Kind == DomContextKind.ShadowRoot ? context.ShadowHost : null;
        var result = js.ExecuteScript(Script, host, FieldSpecs.All.Select(ToScriptSpec).ToArray());
        if (result is not System.Collections.IEnumerable rows)
            return Array.Empty<RawCandidate>();

        var output = new List<RawCandidate>();
        foreach (var row in rows.Cast<object?>().OfType<IDictionary<string, object?>>())
        {
            if (row["element"] is not IWebElement element ||
                row["field"] is not string fieldName ||
                !FieldSpecs.All.Any(s => s.Name == fieldName))
                continue;

            var spec = FieldSpecs.All.Single(s => s.Name == fieldName);
            var group = row.TryGetValue("group", out var groupValue) && groupValue is System.Collections.IEnumerable groupItems
                ? groupItems.Cast<object?>().OfType<IWebElement>().ToList()
                : new List<IWebElement> { element };
            output.Add(new RawCandidate(
                spec,
                element,
                group,
                row.TryGetValue("scope", out var scope) ? scope as string ?? "context" : "context",
                row.TryGetValue("strategy", out var strategy) ? strategy as string ?? "semantic relationship" : "semantic relationship"));
        }
        return output;
    }

    private static object ToScriptSpec(FieldSpec spec) => new Dictionary<string, object>
    {
        ["name"] = spec.Name,
        ["kind"] = spec.KindName,
        ["labels"] = spec.LabelTexts.Select(TextUtil.NormalizeLabel).ToArray(),
        ["positive"] = spec.PositiveHints.Select(TextUtil.Strip).ToArray(),
        ["negative"] = spec.NegativeHints.Select(TextUtil.Strip).ToArray()
    };

    private sealed record RawCandidate(FieldSpec Field, IWebElement Element, IReadOnlyList<IWebElement> Group, string ScopeKey, string Strategy);

    private const string Script = """
const host = arguments[0] || null;
const root = host ? host.shadowRoot : document;
const specs = arguments[1];
const norm = s => String(s ?? '').replace(/[\u00a0\s]+/g, ' ').trim().toLowerCase();
const labelNorm = s => norm(s).replace(/[\s:*]+$/, '').trim();
const strip = s => norm(s).replace(/[^a-z0-9]/g, '');
const visible = e => !!e.getClientRects().length && getComputedStyle(e).display !== 'none' &&
  getComputedStyle(e).visibility !== 'hidden';
const controls = (kind, node) => {
  const found = [];
  const add = e => {
    if (!e || found.includes(e) || !visible(e)) return;
    const tag = e.tagName.toLowerCase(), type = (e.getAttribute('type') || 'text').toLowerCase();
    if ((kind === 'text' && tag === 'input' && (type === 'text' || type === 'search')) ||
        (kind === 'radio' && tag === 'input' && type === 'radio') ||
        (kind === 'select' && tag === 'select')) found.push(e);
  };
  const scan = n => {
    if (!n) return;
    if (n.tagName?.toLowerCase() === 'slot') {
      for (const assigned of n.assignedElements({ flatten: true })) scan(assigned);
      if (!n.assignedElements().length) for (const child of n.children) scan(child);
    } else {
      add(n);
      for (const e of n.querySelectorAll?.('input,select') || []) add(e);
      for (const slot of n.querySelectorAll?.('slot') || []) scan(slot);
    }
  };
  scan(node);
  return found;
};
const scopeFor = (element, label) => {
  const form = element.form || element.closest?.('form') || label?.closest?.('form') || host?.closest?.('form');
  if (!form) return 'context';
  const parts = [];
  let n = form;
  while (n && n.nodeType === 1) {
    let index = 0, sibling = n;
    while ((sibling = sibling.previousElementSibling)) index++;
    parts.unshift(n.tagName.toLowerCase() + '[' + index + ']');
    n = n.parentElement;
  }
  return 'form:' + parts.join('/');
};
const all = Array.from(root.querySelectorAll?.('*') || []);
const labels = all.filter(e => ['LABEL','LEGEND'].includes(e.tagName) ||
  !['INPUT','SELECT','TEXTAREA','OPTION','SCRIPT','STYLE','BUTTON','IFRAME'].includes(e.tagName))
  .filter(e => visible(e));
const out = [];
for (const spec of specs) {
  const wanted = new Set(spec.labels);
  const matches = labels.filter(l => wanted.has(labelNorm(l.tagName === 'LABEL' || l.tagName === 'LEGEND' ? l.textContent : Array.from(l.childNodes).filter(n => n.nodeType === 3).map(n => n.nodeValue).join(''))));
  const found = [];
  const add = (e, label, strategy) => {
    const group = spec.kind === 'radio'
      ? controls('radio', e.closest?.('fieldset,form,[role="group"]') || label?.parentElement || e.parentElement)
      : [e];
    if (group.length === 0) return;
    const representative = spec.kind === 'radio' ? group[0] : e;
    if (!representative || found.some(x => x.element === representative ||
        (spec.kind === 'radio' && x.group.some(r => group.includes(r))))) return;
    found.push({ element: representative, field: spec.name, group, scope: scopeFor(representative, label), strategy });
  };
  for (const label of matches) {
    const id = label.getAttribute('for');
    if (id) {
      const byId = Array.from(root.querySelectorAll?.('[id]') || []).filter(e => e.id === id);
      for (const e of byId) for (const c of controls(spec.kind, e)) add(c, label, 'explicit label association');
    }
    for (const c of controls(spec.kind, label)) add(c, label, 'wrapping label');
    let node = label;
    for (let depth = 0; node && depth < 8; depth++, node = node.parentElement) {
      const foundControls = controls(spec.kind, node);
      if (foundControls.length) {
        for (const c of foundControls) add(c, label, 'nearest semantic container');
        break;
      }
    }
  }
  if (!found.length) {
    for (const e of all) {
      if (!visible(e)) continue;
      const values = [e.getAttribute('aria-label'), e.getAttribute('placeholder'), e.getAttribute('title'),
        e.getAttribute('name'), e.id].map(strip);
      if (controls(spec.kind, e).includes(e) && values.some(v => spec.labels.concat(spec.positive).map(strip).includes(v)))
        add(e, null, 'accessible or attribute hint');
    }
  }
  for (const item of found) out.push(item);
}
return out;
""";
}
