using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace SeleniumAutomation.Utilities;

/// <summary>Explicit waits (WebDriverWait) and a bounded retry for transient WebDriver errors.</summary>
public sealed class WaitHelper
{
    private readonly IWebDriver _driver;
    private readonly Action<string> _log;

    public WaitHelper(IWebDriver driver, TimeSpan timeout, Action<string>? log = null)
    {
        _driver = driver;
        Timeout = timeout;
        _log = log ?? (_ => { });
    }

    public TimeSpan Timeout { get; }

    private WebDriverWait NewWait(string description, bool ignoreStale = true)
    {
        var wait = new WebDriverWait(_driver, Timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(250),
            Message = $"Timed out after {Timeout.TotalSeconds:0}s waiting for: {description}"
        };
        if (ignoreStale)
            wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
        else
            wait.IgnoreExceptionTypes(typeof(NoSuchElementException));
        return wait;
    }

    /// <summary>Polls until the condition returns a non-null value. NoSuchElement/Stale errors are retried by polling.</summary>
    public T Until<T>(Func<IWebDriver, T?> condition, string description) where T : class =>
        NewWait(description).Until(d => condition(d))!;

    /// <summary>Polls until the condition returns true.</summary>
    public void UntilTrue(Func<IWebDriver, bool> condition, string description) =>
        NewWait(description).Until(condition);

    /// <summary>
    /// Waits until the element is displayed and enabled. A stale element is NOT swallowed here: it is thrown
    /// so that the surrounding <see cref="Retry{T}"/> can re-resolve a fresh element.
    /// </summary>
    public void WaitInteractable(IWebElement element, string description) =>
        NewWait($"{description} to be displayed and enabled", ignoreStale: false)
            .Until(_ => element.Displayed && element.Enabled);

    /// <summary>
    /// Runs an action that re-resolves its own elements, retrying a bounded number of times on transient
    /// WebDriver errors only. Any other exception propagates immediately (no blind retries).
    /// </summary>
    public T Retry<T>(Func<T> action, string description, int maxAttempts = 3)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is StaleElementReferenceException
                                          or ElementClickInterceptedException
                                          or ElementNotInteractableException)
            {
                last = ex;
                _log($"[retry {attempt}/{maxAttempts}] {description}: {ex.GetType().Name}");
                // Short back-off between bounded attempts; this is not the waiting mechanism (WebDriverWait is).
                Task.Delay(200).Wait();
            }
        }
        throw new InvalidOperationException(
            $"'{description}' failed after {maxAttempts} attempts. Last error: {last?.Message}", last);
    }
}
