using OpenQA.Selenium;

namespace SeleniumAutomation.Utilities;

public enum ControlKind { Text, Radio, Select }

/// <summary>Semantic description of a field: its label text(s), expected control type and weak attribute hints.</summary>
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

/// <summary>The radio button that matched the requested option, plus its whole group.</summary>
public sealed record RadioOption(IWebElement Radio, IReadOnlyList<IWebElement> Group, string MatchedBy);

/// <summary>
/// Resolves First Name / Gender / State by SEMANTICS (visible label text and DOM relationships),
/// never by fixed id, name, class, index or absolute path. See README for the full strategy description.
/// </summary>
public sealed class ElementResolver
{
    private const int MaxClimbLevels = 6;

    private readonly IJavaScriptExecutor _js;
    private readonly WaitHelper _wait;
    private readonly Action<string> _log;

    public ElementResolver(IWebDriver driver, WaitHelper wait, Action<string>? log = null)
    {
        _js = (IJavaScriptExecutor)driver;
        _wait = wait;
        _log = log ?? (_ => { });
    }

    private sealed record Candidate(
        IWebElement Element,
        IWebElement? Label,
        IWebElement Scope,
        string Strategy,
        IReadOnlyList<IWebElement> Group);

    private sealed record RadioInfo(List<string> Labels, List<string> Aria, string Forward, string Backward, string Value);

    // ------------------------------------------------------------------ public API

    public IWebElement ResolveTextInput(FieldSpec spec) => ResolveSingleControl(spec, ControlKind.Text);

    public IWebElement ResolveSelect(FieldSpec spec) => ResolveSingleControl(spec, ControlKind.Select);

    public RadioOption ResolveRadioOption(FieldSpec spec, string optionText)
    {
        EnsureKind(spec, ControlKind.Radio);
        return Resolve<RadioOption>(spec, trace =>
        {
            var candidates = FindCandidates(spec, optionText, trace);
            if (candidates.Count == 0) return null;

            var group = PickUnique(spec, candidates, trace);
            var (matches, how, inventory) = FindOptionMatches(group.Group, optionText);
            trace.Add($"radio group with {group.Group.Count} option(s): {string.Join("; ", inventory)}");

            if (matches.Count == 0)
            {
                trace.Add($"no option in the group matches '{optionText}' by visible text, accessible name or value");
                return null;
            }
            if (matches.Count > 1)
                throw new AmbiguousElementException(trace.Format(
                    $"{matches.Count} options match '{optionText}' via {how}; refusing to guess"));

            trace.Add($"option '{optionText}' matched via {how}");
            return new RadioOption(matches[0], group.Group, how);
        });
    }

    /// <summary>The &lt;label&gt; elements associated with a control (used to click custom-styled radios).</summary>
    public List<IWebElement> GetAssociatedLabels(IWebElement control) =>
        ToElements(_js.ExecuteScript(DomScripts.LabelsOf, control));

    // ------------------------------------------------------------------ resolution driver

    private IWebElement ResolveSingleControl(FieldSpec spec, ControlKind expected)
    {
        EnsureKind(spec, expected);
        return Resolve<IWebElement>(spec, trace =>
        {
            var candidates = FindCandidates(spec, null, trace);
            if (candidates.Count == 0) return null;
            return PickUnique(spec, candidates, trace).Element;
        });
    }

    private static void EnsureKind(FieldSpec spec, ControlKind expected)
    {
        if (spec.Kind != expected)
            throw new ArgumentException($"Field '{spec.Name}' is a {spec.Kind} field, not {expected}.");
    }

    /// <summary>
    /// Polls (WebDriverWait) until the attempt returns an element. "Not found yet" is retried until the timeout;
    /// ambiguity fails immediately with a descriptive exception.
    /// </summary>
    private T Resolve<T>(FieldSpec spec, Func<ResolutionTrace, T?> attempt) where T : class
    {
        var trace = new ResolutionTrace(spec.Name);
        T result;
        try
        {
            result = _wait.Until<T>(_ =>
            {
                trace.Reset();
                return attempt(trace);
            }, $"resolve field '{spec.Name}'");
        }
        catch (WebDriverTimeoutException)
        {
            throw new ElementResolutionException(trace.Format(
                $"no unique, usable {spec.Kind} control was found within {_wait.Timeout.TotalSeconds:0}s"));
        }
        _log(trace.FormatSuccess());
        return result;
    }

    // ------------------------------------------------------------------ candidate discovery

    private List<Candidate> FindCandidates(FieldSpec spec, string? option, ResolutionTrace trace)
    {
        var targets = spec.LabelTexts.Select(TextUtil.NormalizeLabel).Distinct().ToList();
        var labels = OnlyDisplayed(ToElements(_js.ExecuteScript(DomScripts.FindLabels, targets)));
        trace.Add($"found {labels.Count} visible label-like element(s) whose text equals one of [{string.Join(", ", spec.LabelTexts)}]");

        var candidates = new List<Candidate>();
        foreach (var label in labels)
            foreach (var c in ResolveFromLabel(label, spec, targets, option, trace))
                if (!candidates.Any(x => SameNode(x.Element, c.Element)))
                    candidates.Add(c);

        if (candidates.Count == 0)
        {
            trace.Add("no control resolved through label strategies; trying attribute signals (aria-label, placeholder, title, name, id)");
            candidates.AddRange(ResolveFromAttributeSignals(spec, option, trace));
        }
        return candidates;
    }

    private List<Candidate> ResolveFromLabel(
        IWebElement label, FieldSpec spec, List<string> targets, string? option, ResolutionTrace trace)
    {
        var who = $"label {Describe(label)}";
        var kind = spec.KindName;
        var isRadio = spec.Kind == ControlKind.Radio;

        // Strategy 1: <label for="id"> (not applicable to a radio GROUP label)
        if (!isRadio)
        {
            var byFor = OnlyDisplayed(ToElements(_js.ExecuteScript(DomScripts.ControlsByFor, label, kind)));
            trace.Add($"{who}: strategy 1 label[for] -> {byFor.Count} control(s)");
            if (byFor.Count > 0) return Wrap(byFor, label, "label[for] association");
        }

        // Strategy 2: aria-labelledby pointing at the label
        var byAria = ToElements(_js.ExecuteScript(DomScripts.ControlsByAriaLabelledBy, label, kind));
        if (!isRadio) byAria = OnlyDisplayed(byAria);
        trace.Add($"{who}: strategy 2 aria-labelledby -> {byAria.Count} control(s)");
        if (byAria.Count > 0)
            return isRadio
                ? WrapRadioGroups(byAria, label, option, "aria-labelledby", trace)
                : Wrap(byAria, label, "aria-labelledby");

        // Strategy 3: control nested inside the label element
        if (!isRadio)
        {
            var inside = OnlyDisplayed(ToElements(_js.ExecuteScript(DomScripts.ControlsInsideLabel, label, kind)));
            trace.Add($"{who}: strategy 3 control nested in label -> {inside.Count} control(s)");
            if (inside.Count > 0) return Wrap(inside, label, "control nested inside label");
        }

        // Strategy 4: nearest container (walk up from the label to the first ancestor holding eligible controls)
        var climb = _js.ExecuteScript(DomScripts.ClimbToControls, label, kind, targets, MaxClimbLevels)
            as IDictionary<string, object>;
        if (climb == null)
        {
            trace.Add($"{who}: strategy 4 nearest container -> no {spec.Kind} control within {MaxClimbLevels} ancestor levels");
            return new List<Candidate>();
        }

        var depth = Convert.ToInt32(climb["depth"]);
        var controls = ToElements(climb["controls"]);
        if (!isRadio) controls = OnlyDisplayed(controls);
        trace.Add($"{who}: strategy 4 nearest container ({depth} level(s) up) -> {controls.Count} control(s)");
        if (controls.Count == 0) return new List<Candidate>();

        var strategy = $"nearest container ({depth} level(s) above the label)";
        return isRadio
            ? WrapRadioGroups(controls, label, option, strategy, trace)
            : Wrap(controls, label, strategy);
    }

    private List<Candidate> ResolveFromAttributeSignals(FieldSpec spec, string? option, ResolutionTrace trace)
    {
        var wanted = spec.LabelTexts.Select(TextUtil.Strip)
            .Concat(spec.PositiveHints.Select(TextUtil.Strip))
            .Where(s => s.Length > 0).Distinct().ToList();
        var controls = ToElements(_js.ExecuteScript(DomScripts.AttributeSignals, spec.KindName, wanted));
        if (spec.Kind != ControlKind.Radio) controls = OnlyDisplayed(controls);
        trace.Add($"strategy 5 attribute signals -> {controls.Count} control(s)");
        if (controls.Count == 0) return new List<Candidate>();

        return spec.Kind == ControlKind.Radio
            ? WrapRadioGroups(controls, null, option, "attribute signals", trace)
            : Wrap(controls, null, "attribute signals");
    }

    private List<Candidate> Wrap(IEnumerable<IWebElement> controls, IWebElement? label, string strategy) =>
        controls.Select(c => new Candidate(c, label, ScopeOf(c), strategy, new[] { c })).ToList();

    private List<Candidate> WrapRadioGroups(
        List<IWebElement> radios, IWebElement? label, string? option, string strategy, ResolutionTrace trace)
    {
        var groups = GroupRadios(radios);
        if (option != null && groups.Count > 1)
        {
            var withOption = groups.Where(g => FindOptionMatches(g, option).Matches.Count > 0).ToList();
            trace.Add($"{groups.Count} radio groups at this level; {withOption.Count} contain an option '{option}'");
            if (withOption.Count > 0) groups = withOption;
        }
        return groups.Select(g => new Candidate(g[0], label, ScopeOf(g[0]), strategy, g)).ToList();
    }

    // ------------------------------------------------------------------ disambiguation

    private Candidate PickUnique(FieldSpec spec, List<Candidate> candidates, ResolutionTrace trace)
    {
        var current = candidates;
        trace.Add($"{current.Count} candidate(s) before disambiguation");

        if (current.Count > 1) current = NarrowByScope(spec, current, trace);
        if (current.Count > 1) current = NarrowByHints(spec, current, trace);

        if (current.Count == 1)
        {
            trace.Add($"selected {Describe(current[0].Element)} via {current[0].Strategy}");
            return current[0];
        }

        var listing = string.Join("\n", current.Select((c, i) => $"    #{i + 1} via {c.Strategy}: {Describe(c.Element)}"));
        throw new AmbiguousElementException(trace.Format(
            $"{current.Count} plausible candidates remain after scope and attribute-hint narrowing; refusing to guess.\n  Candidates:\n{listing}"));
    }

    /// <summary>
    /// Keeps candidates whose enclosing form also contains candidates for the OTHER target fields. This is how
    /// the "primary form" is identified semantically (it is the form that contains First Name + Gender + State
    /// labels), without hardcoding any form id.
    /// </summary>
    private List<Candidate> NarrowByScope(FieldSpec spec, List<Candidate> current, ResolutionTrace trace)
    {
        var otherScopeLists = new List<List<IWebElement>>();
        foreach (var other in FieldSpecs.All.Where(s => s.Name != spec.Name))
        {
            var found = FindCandidates(other, null, new ResolutionTrace(other.Name));
            if (found.Count > 0) otherScopeLists.Add(found.Select(c => c.Scope).ToList());
        }

        if (otherScopeLists.Count == 0)
        {
            trace.Add("scope narrowing skipped: the other target fields were not found");
            return current;
        }

        var kept = current
            .Where(c => otherScopeLists.All(scopes => scopes.Any(s => SameNode(s, c.Scope))))
            .ToList();
        trace.Add($"scope narrowing (candidate must share a form/scope with the other target fields): {kept.Count} of {current.Count} kept");
        return kept.Count > 0 ? kept : current;
    }

    private List<Candidate> NarrowByHints(FieldSpec spec, List<Candidate> current, ResolutionTrace trace)
    {
        var scored = current.Select(c => (Candidate: c, Score: HintScore(spec, c))).ToList();
        var best = scored.Max(x => x.Score);
        var winners = scored.Where(x => x.Score == best).Select(x => x.Candidate).ToList();
        trace.Add($"attribute-hint scoring (id/name/placeholder/aria-label/autocomplete): scores [{string.Join(", ", scored.Select(x => x.Score))}] -> {winners.Count} top candidate(s)");
        return winners;
    }

    private int HintScore(FieldSpec spec, Candidate c)
    {
        var text = _js.ExecuteScript(DomScripts.HintText, c.Element) as string ?? "";
        var positive = spec.PositiveHints.Count(h => text.Contains(h, StringComparison.Ordinal));
        var negative = spec.NegativeHints.Count(h => text.Contains(h, StringComparison.Ordinal));
        return positive * 2 - negative * 3;
    }

    // ------------------------------------------------------------------ radio option matching

    private (List<IWebElement> Matches, string How, List<string> Inventory) FindOptionMatches(
        IReadOnlyList<IWebElement> group, string wanted)
    {
        var w = TextUtil.NormalizeLabel(wanted);
        var infos = group.Select(ReadRadioInfo).ToList();

        // Option text may sit AFTER the radio (<input><span>Male</span>) or BEFORE it (<span>Male</span><input>).
        // Pick the orientation in which every radio in the group gets a text.
        var forwardComplete = infos.All(i => i.Forward.Length > 0);
        var backwardComplete = infos.All(i => i.Backward.Length > 0);
        string Adjacent(RadioInfo i) =>
            forwardComplete ? i.Forward
            : backwardComplete ? i.Backward
            : (i.Forward.Length > 0 ? i.Forward : i.Backward);

        var inventory = new List<string>();
        var byText = new List<IWebElement>();
        var byValue = new List<IWebElement>();

        for (var k = 0; k < group.Count; k++)
        {
            var texts = infos[k].Labels.Concat(infos[k].Aria).Append(Adjacent(infos[k]))
                .Where(t => t.Length > 0)
                .Select(TextUtil.NormalizeLabel)
                .Distinct().ToList();
            inventory.Add($"[{string.Join(" | ", texts)}] value='{infos[k].Value}'");

            if (texts.Contains(w)) byText.Add(group[k]);          // exact match only: "Male" never matches "Female"
            else if (TextUtil.NormalizeLabel(infos[k].Value) == w) byValue.Add(group[k]);
        }

        return byText.Count > 0
            ? (byText, "visible text / accessible name", inventory)
            : (byValue, "value attribute", inventory);
    }

    private RadioInfo ReadRadioInfo(IWebElement radio)
    {
        var d = (IDictionary<string, object>)_js.ExecuteScript(DomScripts.RadioInfo, radio);
        return new RadioInfo(
            ToStrings(d["labels"]),
            ToStrings(d["aria"]),
            d["forward"] as string ?? "",
            d["backward"] as string ?? "",
            d["value"] as string ?? "");
    }

    private List<List<IWebElement>> GroupRadios(List<IWebElement> radios)
    {
        var raw = _js.ExecuteScript(DomScripts.GroupRadios, radios) as IEnumerable<object>;
        return raw?.Select(ToElements).Where(g => g.Count > 0).ToList() ?? new List<List<IWebElement>>();
    }

    // ------------------------------------------------------------------ small helpers

    private IWebElement ScopeOf(IWebElement element) =>
        (IWebElement)_js.ExecuteScript(DomScripts.ScopeOf, element);

    private bool SameNode(IWebElement a, IWebElement b) =>
        (bool)_js.ExecuteScript(DomScripts.SameNode, a, b);

    private string Describe(IWebElement element)
    {
        try { return _js.ExecuteScript(DomScripts.Describe, element) as string ?? "<element>"; }
        catch (StaleElementReferenceException) { return "<stale element>"; }
    }

    private static List<IWebElement> OnlyDisplayed(IEnumerable<IWebElement> elements) =>
        elements.Where(IsDisplayed).ToList();

    private static bool IsDisplayed(IWebElement element)
    {
        try { return element.Displayed; }
        catch (StaleElementReferenceException) { return false; }
    }

    private static List<IWebElement> ToElements(object? value) => value switch
    {
        IWebElement single => new List<IWebElement> { single },
        IEnumerable<object> items => items.OfType<IWebElement>().ToList(),
        _ => new List<IWebElement>()
    };

    private static List<string> ToStrings(object? value) =>
        value is IEnumerable<object> items ? items.OfType<string>().ToList() : new List<string>();
}
