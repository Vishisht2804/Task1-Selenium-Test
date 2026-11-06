using NUnit.Framework;
using NUnit.Framework.Interfaces;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using SeleniumAutomation.Config;
using SeleniumAutomation.Pages;

namespace SeleniumAutomation.Base;

/// <summary>Owns the browser lifecycle. The browser is always closed, even when the test fails.</summary>
public abstract class BaseTest
{
    protected TestSettings Settings { get; private set; } = null!;
    protected IWebDriver Driver { get; private set; } = null!;
    protected AutomationPracticePage Page { get; private set; } = null!;

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

        Page = new AutomationPracticePage(Driver, Settings, Log);
        Page.Open();
    }

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
            try { Driver?.Quit(); }
            catch (Exception ex) { Log($"Driver.Quit failed: {ex.Message}"); }
            Driver?.Dispose();
        }
    }
}
