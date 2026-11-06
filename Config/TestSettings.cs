using Microsoft.Extensions.Configuration;

namespace SeleniumAutomation.Config;

/// <summary>
/// Test configuration. Values come from Config/appsettings.json and can be overridden by
/// environment variables prefixed with SELAUTO_ (e.g. SELAUTO_PAGEURL, SELAUTO_STATEVALUE).
/// </summary>
public sealed class TestSettings
{
    public const string EnvPrefix = "SELAUTO_";

    public string PageUrl { get; set; } = "https://app.cloudqa.io/home/AutomationPracticeForm";
    public string FirstNameValue { get; set; } = "Alex";
    public string GenderValue { get; set; } = "Male";
    public string StateValue { get; set; } = "India";
    public int TimeoutSeconds { get; set; } = 20;
    public bool Headless { get; set; } = true;

    /// <summary>Optional extra Chrome arguments, space separated (e.g. "--no-sandbox" inside Docker).</summary>
    public string ChromeExtraArgs { get; set; } = "";

    public static TestSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(Path.Combine("Config", "appsettings.json"), optional: false, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: EnvPrefix)
            .Build();

        var settings = configuration.Get<TestSettings>() ?? new TestSettings();
        settings.Validate();
        return settings;
    }

    private void Validate()
    {
        if (!Uri.TryCreate(PageUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"PageUrl '{PageUrl}' is not an absolute URL.");
        if (string.IsNullOrWhiteSpace(FirstNameValue))
            throw new InvalidOperationException("FirstNameValue must not be empty.");
        if (string.IsNullOrWhiteSpace(GenderValue))
            throw new InvalidOperationException("GenderValue must not be empty.");
        if (string.IsNullOrWhiteSpace(StateValue))
            throw new InvalidOperationException("StateValue must not be empty.");
        if (TimeoutSeconds <= 0)
            throw new InvalidOperationException("TimeoutSeconds must be greater than zero.");
    }

    public override string ToString() =>
        $"PageUrl={PageUrl}, FirstName='{FirstNameValue}', Gender='{GenderValue}', State='{StateValue}', " +
        $"Timeout={TimeoutSeconds}s, Headless={Headless}";
}
