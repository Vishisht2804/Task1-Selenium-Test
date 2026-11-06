using NUnit.Framework;
using SeleniumAutomation.Base;
using SeleniumAutomation.Utilities.DomContext;

namespace SeleniumAutomation.Tests;

/// <summary>
/// Diagnostic run against the configured page (default: the live CloudQA page). It prints and saves the discovery
/// report; the assertions are deliberately minimal because the live page is not under our control.
/// Run with: dotnet test --filter "Category=Live"
/// </summary>
[TestFixture]
[Category("Live")]
public sealed class DomContextLiveCloudQaTests : DomDiscoveryTestBase
{
    [Test]
    public void LivePage_ContextDiscovery_Report()
    {
        Open(Settings.PageUrl);
        var result = DiscoverAndReport($"live page {Settings.PageUrl}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Contexts[0].Kind, Is.EqualTo(DomContextKind.TopDocument));
            Assert.That(result.Contexts.Count, Is.GreaterThan(1), "the page is expected to contain shadow roots and iframes");
            Assert.That(result.Aborted, Is.False);
            Assert.That(IsAtTopWindow(), Is.True, "driver must be restored to the top-level browsing context");
        });
    }

    [Test]
    public void LivePage_ContextDiscovery_StrictCycleMode_Report()
    {
        Open(Settings.PageUrl);
        var result = DiscoverAndReport($"live page {Settings.PageUrl} (strict cycle mode)",
            new DomContextExplorerOptions { AllowedUrlRepeatsInAncestry = 0 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Aborted, Is.False);
            Assert.That(IsAtTopWindow(), Is.True);
        });
    }
}
