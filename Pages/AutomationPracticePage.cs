using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using SeleniumAutomation.Config;
using SeleniumAutomation.Utilities;

namespace SeleniumAutomation.Pages;

/// <summary>Outcome of one field operation after read-back verification.</summary>
public sealed record FieldResult(string Field, string Expected, string Actual, string Details)
{
    public bool Passed => string.Equals(Expected, Actual, StringComparison.Ordinal);

    public override string ToString() =>
        $"[{(Passed ? "PASS" : "FAIL")}] {Field}: expected '{Expected}', actual '{Actual}'. {Details}";
}

/// <summary>
/// Page object for the practice form. It contains no locators at all: every element comes from
/// <see cref="ElementResolver"/>. Each operation re-resolves elements inside a bounded retry, so a re-rendered
/// (stale) element is simply found again.
/// </summary>
public sealed class AutomationPracticePage
{
    private readonly IWebDriver _driver;
    private readonly TestSettings _settings;
    private readonly WaitHelper _wait;
    private readonly ElementResolver _resolver;
    private readonly Action<string> _log;

    public AutomationPracticePage(IWebDriver driver, TestSettings settings, Action<string> log)
    {
        _driver = driver;
        _settings = settings;
        _log = log;
        _wait = new WaitHelper(driver, TimeSpan.FromSeconds(settings.TimeoutSeconds), log);
        _resolver = new ElementResolver(driver, _wait, log);
    }

    public void Open()
    {
        _log($"Opening {_settings.PageUrl}");
        _driver.Navigate().GoToUrl(_settings.PageUrl);
        _wait.UntilTrue(
            d => (string)((IJavaScriptExecutor)d).ExecuteScript("return document.readyState") != "loading",
            "document to finish loading");
    }

    // ------------------------------------------------------------------ First Name

    public FieldResult EnterFirstName(string value)
    {
        var spec = FieldSpecs.FirstName;

        _wait.Retry(() =>
        {
            var input = _resolver.ResolveTextInput(spec);
            _wait.WaitInteractable(input, "First Name input");
            input.Clear();
            input.SendKeys(value);
            return true;
        }, "enter First Name");

        var actual = _wait.Retry(
            () => _resolver.ResolveTextInput(spec).GetDomProperty("value") ?? "",
            "read back First Name");

        return Report(new FieldResult(spec.Name, value, actual, "value read back from the resolved text input"));
    }

    // ------------------------------------------------------------------ Gender

    public FieldResult SelectGender(string value)
    {
        var spec = FieldSpecs.Gender;

        var matchedBy = _wait.Retry(() =>
        {
            var option = _resolver.ResolveRadioOption(spec, value);
            if (!option.Radio.Selected) ClickRadio(option.Radio);
            return option.MatchedBy;
        }, "select Gender");

        var (selected, othersSelected) = _wait.Retry(() =>
        {
            var option = _resolver.ResolveRadioOption(spec, value);
            var others = option.Group.Count(r => !r.Equals(option.Radio) && r.Selected);
            return (option.Radio.Selected, others);
        }, "verify Gender");

        var actual = selected && othersSelected == 0
            ? value
            : $"<selected={selected}, otherOptionsSelected={othersSelected}>";

        return Report(new FieldResult(spec.Name, value, actual,
            $"option matched via {matchedBy}; verified it is selected and no other option in its group is"));
    }

    private void ClickRadio(IWebElement radio)
    {
        if (radio.Displayed && radio.Enabled)
        {
            radio.Click();
            return;
        }

        // Custom-styled radios hide the <input>; its <label> is then the real click target (still a Selenium click).
        var label = _resolver.GetAssociatedLabels(radio).FirstOrDefault(l => l.Displayed);
        if (label == null)
            throw new ElementNotInteractableException("Radio button is not interactable and has no visible <label> to click.");
        label.Click();
    }

    // ------------------------------------------------------------------ State

    public FieldResult SelectState(string value)
    {
        var spec = FieldSpecs.State;

        var chosen = _wait.Retry(() =>
        {
            var element = _resolver.ResolveSelect(spec);
            _wait.WaitInteractable(element, "State dropdown");
            var select = new SelectElement(element);
            var (text, optionValue, byText) = FindOption(select, value);

            if (byText) select.SelectByText(text);
            else select.SelectByValue(optionValue);
            return (text, optionValue, byText);
        }, "select State");

        var actual = _wait.Retry(() =>
        {
            var selected = new SelectElement(_resolver.ResolveSelect(spec)).SelectedOption;
            return (Text: TextUtil.NormalizeSpaces(selected.Text), Value: selected.GetDomProperty("value") ?? "");
        }, "verify State");

        var isRequested = actual.Text == chosen.text && actual.Value == chosen.optionValue;
        var shown = isRequested ? value : $"{actual.Text} (value='{actual.Value}')";

        return Report(new FieldResult(spec.Name, value, shown,
            $"native <select> driven by SelectElement; option matched by {(chosen.byText ? "visible text" : "value")}; " +
            $"selected option read back (text='{actual.Text}', value='{actual.Value}')"));
    }

    /// <summary>Finds the option by exact visible text (preferred) or exact value. Case-insensitive; must be unique.</summary>
    private static (string Text, string Value, bool ByText) FindOption(SelectElement select, string wanted)
    {
        var w = TextUtil.NormalizeSpaces(wanted);
        var options = select.Options.ToList();

        var byText = options
            .Where(o => string.Equals(TextUtil.NormalizeSpaces(o.Text), w, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var byValue = options
            .Where(o => string.Equals(o.GetDomProperty("value"), w, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matches = byText.Count > 0 ? byText : byValue;
        var how = byText.Count > 0 ? "visible text" : "value";

        if (matches.Count == 0)
            throw new NoSuchElementException(
                $"State dropdown has no option with text or value '{wanted}'. Available: " +
                string.Join(", ", options.Select(o => $"'{TextUtil.NormalizeSpaces(o.Text)}'")));
        if (matches.Count > 1)
            throw new ElementResolutionException($"{matches.Count} options match '{wanted}' by {how}; refusing to guess.");

        var option = matches[0];
        if (!option.Enabled)
            throw new ElementResolutionException($"The option '{wanted}' exists but is disabled.");

        return (TextUtil.NormalizeSpaces(option.Text), option.GetDomProperty("value") ?? "", byText.Count > 0);
    }

    private FieldResult Report(FieldResult result)
    {
        _log(result.ToString());
        return result;
    }
}
