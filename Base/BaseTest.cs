using NUnit.Framework;
using NUnit.Framework.Interfaces;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using SeleniumAutomation.Config;
using SeleniumAutomation.Pages;
using SeleniumAutomation.Utilities;

namespace SeleniumAutomation.Base;

/// <summary>Owns the browser lifecycle.</summary>
public abstract class BaseTest
{
    protected TestSettings Settings { get; private set; } = null!;
    protected IWebDriver Driver { get; private set; } = null!;

    protected static void Log(string message) => TestContext.Progress.WriteLine(message);

    [SetUp]
    public void StartBrowser()
    {
        Settings = TestSettings.Load();
        Log($"Settings: {Settings}");

        var options = new ChromeOptions
        {
            // 'Eager' returns once the DOM is ready; the page's third-party scripts (analytics, CDN fonts) are not awaited.
            PageLoadStrategy = PageLoadStrategy.Eager
        };
        if (Settings.Headless) options.AddArgument("--headless=new");
        options.AddArguments("--window-size=1920,1080", "--disable-dev-shm-usage");
        foreach (var arg in Settings.ChromeExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            options.AddArgument(arg);

        // No driver path: Selenium Manager locates/downloads the matching ChromeDriver automatically.
        Driver = new ChromeDriver(options);
        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Settings.TimeoutSeconds * 3);

    }

    protected void Open(string url)
    {
        Driver.Navigate().GoToUrl(url);
        new WaitHelper(Driver, TimeSpan.FromSeconds(Settings.TimeoutSeconds), Log)
            .UntilTrue(
                d => (string)((IJavaScriptExecutor)d).ExecuteScript("return document.readyState") != "loading",
                "document to finish loading");
    }

    protected AutomationPracticePage CreatePage(FormCandidate candidate) =>
        new(Driver, Settings, Log, candidate);

    protected IReadOnlyList<FormCandidate> DiscoverCompleteCandidates() =>
        new FormCandidateDiscovery(Driver, log: Log).Discover().Candidates
            .Where(candidate => candidate.IsComplete)
            .ToList();

    [TearDown]
    public void StopBrowser()
    {
        try
        {
            if (Driver != null && TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                var path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                    $"{TestContext.CurrentContext.Test.Name}-failure.png");
                ((ITakesScreenshot)Driver).GetScreenshot().SaveAsFile(path);
                Log($"Failure screenshot: {path}");
            }
        }
        catch (Exception ex)
        {
            Log($"Could not capture failure screenshot: {ex.Message}");
        }
        finally
        {
            if (Settings.KeepBrowserOpen)
            {
                Log("KeepBrowserOpen is enabled; leaving the browser open.");
            }
            else
            {
                try { Driver?.Quit(); }
                catch (Exception ex) { Log($"Driver.Quit failed: {ex.Message}"); }
                Driver?.Dispose();
            }
        }
    }
}
