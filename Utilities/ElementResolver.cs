using OpenQA.Selenium;

namespace SeleniumAutomation.Utilities;

public sealed record RadioOption(IWebElement Radio, IReadOnlyList<IWebElement> Group, string MatchedBy);

/// <summary>Resolves controls from one selected logical form candidate.</summary>
public sealed class ElementResolver
{
    private readonly IWebDriver _driver;
    private readonly WaitHelper _wait;
    private readonly FormCandidate _candidate;
    private readonly Action<string> _log;

    public ElementResolver(
        IWebDriver driver,
        WaitHelper wait,
        FormCandidate candidate,
        Action<string>? log = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _wait = wait ?? throw new ArgumentNullException(nameof(wait));
        _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        _log = log ?? (_ => { });
    }

    public IWebElement ResolveTextInput(FieldSpec spec) =>
        Resolve(spec, ControlKind.Text).Element;

    public IWebElement ResolveSelect(FieldSpec spec) =>
        Resolve(spec, ControlKind.Select).Element;

    public T WithTextInput<T>(Func<IWebElement, T> action) =>
        WithField(FieldSpecs.FirstName, ControlKind.Text, action);

    public T WithSelect<T>(Func<IWebElement, T> action) =>
        WithField(FieldSpecs.State, ControlKind.Select, action);

    public T WithRadioOption<T>(string optionText, Func<RadioOption, T> action)
    {
        var field = Resolve(FieldSpecs.Gender, ControlKind.Radio);
        return WithContext(field, () =>
        {
            var matches = field.Group
                .Where(IsVisibleAndEnabled)
                .Where(r => RadioOptionText(r).Equals(TextUtil.NormalizeLabel(optionText), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
                throw new ElementResolutionException($"Radio option '{optionText}' was not found in the selected Gender group.");
            if (matches.Count > 1)
                throw new AmbiguousElementException($"Radio option '{optionText}' matched multiple controls; refusing to guess.");
            return action(new RadioOption(matches[0], field.Group, field.Strategy));
        });
    }

    public RadioOption ResolveRadioOption(FieldSpec spec, string optionText)
    {
        var field = Resolve(spec, ControlKind.Radio);
        var matches = field.Group
            .Where(IsVisibleAndEnabled)
            .Where(r => RadioOptionText(r).Equals(TextUtil.NormalizeLabel(optionText), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            throw new ElementResolutionException(
                $"Radio option '{optionText}' was not found in the selected Gender group. " +
                $"Available options: {string.Join(", ", field.Group.Select(RadioOptionText))}");
        if (matches.Count > 1)
            throw new AmbiguousElementException(
                $"Radio option '{optionText}' matched {matches.Count} controls in the selected Gender group; refusing to guess.");

        return new RadioOption(matches[0], field.Group, field.Strategy);
    }

    public List<IWebElement> GetAssociatedLabels(IWebElement control)
    {
        var js = (IJavaScriptExecutor)_driver;
        var labels = js.ExecuteScript(
            "return arguments[0].labels ? Array.from(arguments[0].labels) : [];",
            control);
        return labels is System.Collections.IEnumerable values
            ? values.Cast<object>().OfType<IWebElement>().ToList()
            : new List<IWebElement>();
    }

    private ResolvedField Resolve(FieldSpec spec, ControlKind expected)
    {
        if (spec.Kind != expected)
            throw new ArgumentException($"Field '{spec.Name}' is a {spec.Kind} field, not {expected}.", nameof(spec));

        if (!_candidate.IsComplete)
            throw new ElementResolutionException($"Form candidate '{_candidate.Description}' is incomplete.");
        if (!_candidate.Fields.TryGetValue(spec.Name, out var field))
            throw new ElementResolutionException(
                $"Form candidate '{_candidate.Description}' does not contain '{spec.Name}'. " +
                string.Join("; ", _candidate.Diagnostics));

        _log($"Resolved {spec.Name} in {_candidate.Description} via {field.Strategy}.");
        return field;
    }

    private T WithField<T>(FieldSpec spec, ControlKind expected, Func<IWebElement, T> action)
    {
        var field = Resolve(spec, expected);
        return WithContext(field, () => action(field.Element));
    }

    private T WithContext<T>(ResolvedField field, Func<T> action) =>
        new DomContext.DomContextExplorer(_driver, log: _log).WithContext(field.Context, _ => action());

    private static bool IsVisibleAndEnabled(IWebElement element) => element.Displayed && element.Enabled;

    private static string RadioOptionText(IWebElement radio)
    {
        var js = (IJavaScriptExecutor)((IWrapsDriver)radio).WrappedDriver;
        return TextUtil.NormalizeLabel(js.ExecuteScript("""
            const r = arguments[0];
            const labels = r.labels ? Array.from(r.labels).map(x => x.textContent) : [];
            if (labels.length) return labels.join(' ');
            const sibling = r.nextElementSibling || r.previousElementSibling;
            if (sibling && !sibling.matches('input,select,textarea,button')) return sibling.textContent || '';
            return r.value || '';
            """, radio)?.ToString() ?? radio.GetDomProperty("value") ?? "");
    }
}
