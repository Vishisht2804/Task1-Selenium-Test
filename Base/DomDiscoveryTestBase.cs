using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumAutomation.Config;
using SeleniumAutomation.Utilities.DomContext;

namespace SeleniumAutomation.Base;

/// <summary>
/// Browser lifecycle for DOM-context diagnostics. Unlike <see cref="BaseTest"/> it does NOT open the configured
/// page in SetUp, so fixture tests run offline. BaseTest itself is unchanged.
/// </summary>
public abstract class DomDiscoveryTestBase
{
    protected TestSettings Settings { get; private set; } = null!;
    protected IWebDriver Driver { get; private set; } = null!;

    protected static void Log(string message) => TestContext.Progress.WriteLine(message);

    [SetUp]
    public void StartBrowser()
    {
        Settings = TestSettings.Load();
        var options = new ChromeOptions { PageLoadStrategy = PageLoadStrategy.Eager };
        if (Settings.Headless) options.AddArgument("--headless=new");
        options.AddArguments("--window-size=1920,1080", "--disable-dev-shm-usage");
        foreach (var arg in Settings.ChromeExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            options.AddArgument(arg);

        Driver = new ChromeDriver(options);
        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Settings.TimeoutSeconds * 3);
    }

    [TearDown]
    public void StopBrowser()
    {
        try { Driver?.Quit(); }
        catch (Exception ex) { Log($"Driver.Quit failed: {ex.Message}"); }
        Driver?.Dispose();
    }

    /// <summary>file:// URL of a fixture copied next to the test assembly (csproj copies Fixtures/*.html).</summary>
    protected static string FixtureUrl(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        if (!File.Exists(path)) throw new FileNotFoundException("Fixture was not copied to the output directory.", path);
        return new Uri(path).AbsoluteUri;
    }

    protected void Open(string url)
    {
        Driver.Navigate().GoToUrl(url);
        new WebDriverWait(Driver, TimeSpan.FromSeconds(Settings.TimeoutSeconds))
            .Until(d => (string?)((IJavaScriptExecutor)d).ExecuteScript("return document.readyState") != "loading");
    }

    protected DomContextExplorer NewExplorer(DomContextExplorerOptions? options = null) =>
        new(Driver, options, Log);

    /// <summary>Discovers, prints the report to the test log, and saves it next to the test results.</summary>
    protected DomContextDiscoveryResult DiscoverAndReport(string title, DomContextExplorerOptions? options = null)
    {
        var result = NewExplorer(options).Discover();
        var report = DomContextReport.Format(result, title);
        Log(report);

        var file = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"dom-context-{TestContext.CurrentContext.Test.Name}.txt");
        File.WriteAllText(file, report);
        TestContext.AddTestAttachment(file, title);
        return result;
    }

    protected bool IsAtTopWindow() =>
        (bool)((IJavaScriptExecutor)Driver).ExecuteScript("return window.top === window;")!;
}
