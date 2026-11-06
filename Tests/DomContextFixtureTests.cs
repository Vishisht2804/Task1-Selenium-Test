using NUnit.Framework;
using OpenQA.Selenium;
using SeleniumAutomation.Base;
using SeleniumAutomation.Utilities.DomContext;

namespace SeleniumAutomation.Tests;

/// <summary>
/// Deterministic, offline diagnostics for <see cref="DomContextExplorer"/>. Each test loads a local fixture,
/// prints the full discovery report to the test log, and asserts the structure the fixture was built to have.
/// Pass 1 scope: discovery only. Nothing here fills or resolves form fields.
/// </summary>
[TestFixture]
public sealed class DomContextFixtureTests : DomDiscoveryTestBase
{
    private DomContextDiscoveryResult Run(string fixture, DomContextExplorerOptions? options = null)
    {
        Open(FixtureUrl(fixture));
        return DiscoverAndReport(fixture, options);
    }

    private void AssertTraversalWasClean(DomContextDiscoveryResult result)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Failures, Is.Empty, "no traversal failures expected");
            Assert.That(result.Aborted, Is.False, "traversal must not abort");
            Assert.That(IsAtTopWindow(), Is.True, "driver must be back in the top-level browsing context");
        });
    }

    private static List<DomContextDescriptor> ShadowRoots(DomContextDiscoveryResult r) =>
        r.OfKind(DomContextKind.ShadowRoot).ToList();

    private static List<DomContextDescriptor> IframeDocs(DomContextDiscoveryResult r) =>
        r.OfKind(DomContextKind.IframeDocument).ToList();

    private int CountIn(DomContextExplorer explorer, DomContextDescriptor context, string css) =>
        explorer.WithContext(context, sc => sc.FindElements(By.CssSelector(css)).Count);

    // ------------------------------------------------------------------ shadow DOM

    [Test]
    public void SingleShadow_IsDiscovered()
    {
        var r = Run("dom-shadow-single.html");

        AssertTraversalWasClean(r);
        var shadow = ShadowRoots(r).Single();
        Assert.Multiple(() =>
        {
            Assert.That(r.Contexts, Has.Count.EqualTo(2));
            Assert.That(r.Contexts[0].Kind, Is.EqualTo(DomContextKind.TopDocument));
            Assert.That(shadow.ShadowDepth, Is.EqualTo(1));
            Assert.That(shadow.FrameDepth, Is.EqualTo(0));
            Assert.That(shadow.ShadowHostDescription, Does.Contain("x-single"));
            Assert.That(shadow.AssociatedHeading, Is.EqualTo("Single Shadow"));
            Assert.That(shadow.ShadowRoot, Is.Not.Null, "GetShadowRoot() result must be kept as ISearchContext");
        });

        // The stored shadow root must be usable as an ISearchContext (CSS only; shadow roots do not support XPath).
        Assert.That(CountIn(NewExplorer(), shadow, "input"), Is.EqualTo(1));
    }

    [Test]
    public void SixNestedShadowRoots_AreAllDiscovered()
    {
        var r = Run("dom-shadow-six-levels.html");

        AssertTraversalWasClean(r);
        var shadow = ShadowRoots(r);
        Assert.Multiple(() =>
        {
            Assert.That(shadow.Select(c => c.ShadowDepth).OrderBy(d => d), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(shadow.All(c => c.AssociatedHeading == "Six Levels"), Is.True, "heading is inherited down the chain");
            Assert.That(shadow.Single(c => c.ShadowDepth == 6).HostChain, Has.Count.EqualTo(6));
        });

        var innermost = shadow.Single(c => c.ShadowDepth == 6);
        Assert.That(CountIn(NewExplorer(), innermost, "input"), Is.EqualTo(1));
    }

    // ------------------------------------------------------------------ slots

    [Test]
    public void SlottedContent_ReportsSlotsAssignmentsFallbackAndUnassigned()
    {
        var r = Run("dom-slotted.html");

        AssertTraversalWasClean(r);
        var shadow = ShadowRoots(r);
        Assert.That(shadow, Has.Count.EqualTo(2), "x-slotted, plus the host nested inside slotted (light DOM) content");

        var host = shadow[0];
        var first = host.Slots.Single(s => s.Name == "first");
        var second = host.Slots.Single(s => s.Name == "second");
        var nested = host.Slots.Single(s => s.Name == "nested");
        var empty = host.Slots.Single(s => s.Name == "empty");

        Assert.Multiple(() =>
        {
            Assert.That(host.Slots, Has.Count.EqualTo(5));
            Assert.That(host.Slots.Count(s => s.Name.Length == 0), Is.EqualTo(1), "the default (unnamed) slot");

            Assert.That(first.AssignedElements, Has.Count.EqualTo(1));
            Assert.That(first.AssignedElements[0].ControlSummary, Does.Contain("input[text]x1"));

            Assert.That(second.AssignedElements[0].ControlSummary, Does.Contain("input[radio]x2"));
            Assert.That(second.AssignedElements[0].DescendantCount, Is.EqualTo(4));

            Assert.That(nested.AssignedElements[0].NestedShadowHosts, Has.Count.EqualTo(1));
            Assert.That(nested.AssignedElements[0].NestedShadowHosts[0], Does.Contain("x-inner-host"));

            Assert.That(empty.UsesFallbackContent, Is.True);
            Assert.That(empty.DirectAssignedCount, Is.EqualTo(0));

            Assert.That(host.UnassignedHostChildren, Has.Count.EqualTo(1));
            Assert.That(host.UnassignedHostChildren[0].SlotAttribute, Is.EqualTo("orphan"));

            // A host sitting inside slotted content lives in the OUTER tree, so it is a depth-1 context of its own.
            Assert.That(shadow[1].ShadowDepth, Is.EqualTo(1));
            Assert.That(shadow[1].ShadowHostDescription, Does.Contain("x-inner-host"));
        });
    }

    // ------------------------------------------------------------------ iframes

    [Test]
    public void IframeWithoutId_IsDiscoveredStructurally()
    {
        var r = Run("dom-iframe-no-id.html");

        AssertTraversalWasClean(r);
        var frame = IframeDocs(r).Single();
        Assert.Multiple(() =>
        {
            Assert.That(frame.FrameDepth, Is.EqualTo(1));
            Assert.That(frame.IframeDescription, Does.Not.Contain("id="), "the fixture iframe has no id");
            Assert.That(frame.DocumentUrl, Does.EndWith("dom-frame-child.html"));
            Assert.That(frame.AssociatedHeading, Is.EqualTo("Frame Without Id"));
            Assert.That(frame.FrameChain, Has.Count.EqualTo(1));
        });

        Assert.That(CountIn(NewExplorer(), frame, "input"), Is.EqualTo(1));
        Assert.That(IsAtTopWindow(), Is.True, "WithContext must restore the driver");
    }

    [Test]
    public void NestedIframes_AreDiscoveredWithIncreasingFrameDepth()
    {
        var r = Run("dom-iframe-nested.html");

        AssertTraversalWasClean(r);
        var frames = IframeDocs(r);
        Assert.Multiple(() =>
        {
            Assert.That(frames.Select(f => f.FrameDepth), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(frames[0].DocumentUrl, Does.EndWith("dom-frame-outer.html"));
            Assert.That(frames[1].DocumentUrl, Does.EndWith("dom-frame-child.html"));
            Assert.That(frames[1].FrameChain, Has.Count.EqualTo(2));
            Assert.That(frames[1].ParentId, Is.EqualTo(frames[0].Id));
        });

        Assert.That(CountIn(NewExplorer(), frames[1], "input"), Is.EqualTo(1));
        Assert.That(IsAtTopWindow(), Is.True);
    }

    [Test]
    public void IframeInsideShadow_IsDiscovered()
    {
        var r = Run("dom-iframe-in-shadow.html");

        AssertTraversalWasClean(r);
        var shadow = ShadowRoots(r).Single();
        var frame = IframeDocs(r).Single();
        Assert.Multiple(() =>
        {
            Assert.That(frame.ParentId, Is.EqualTo(shadow.Id));
            Assert.That(frame.Path, Does.Contain("shadow[0]").And.Contain("iframe[0]"));
            Assert.That(frame.FrameDepth, Is.EqualTo(1));
            Assert.That(frame.ShadowDepth, Is.EqualTo(0), "shadow depth restarts in a new document");
            Assert.That(frame.AssociatedHeading, Is.EqualTo("Iframe In Shadow"));
        });

        Assert.That(CountIn(NewExplorer(), frame, "input"), Is.EqualTo(1));
    }

    [Test]
    public void ShadowInsideIframe_IsDiscovered()
    {
        var r = Run("dom-shadow-in-iframe.html");

        AssertTraversalWasClean(r);
        var shadow = ShadowRoots(r).Single();
        Assert.Multiple(() =>
        {
            Assert.That(shadow.FrameDepth, Is.EqualTo(1));
            Assert.That(shadow.ShadowDepth, Is.EqualTo(1));
            Assert.That(shadow.FrameChain, Has.Count.EqualTo(1));
            Assert.That(shadow.DocumentUrl, Does.EndWith("dom-frame-with-shadow.html"));
        });

        // Re-enters the iframe, then searches the stored shadow root.
        Assert.That(CountIn(NewExplorer(), shadow, "input"), Is.EqualTo(1));
        Assert.That(IsAtTopWindow(), Is.True);
    }

    // ------------------------------------------------------------------ recursion safeguards

    [Test]
    public void RecursiveIframe_StopsAfterOneRepeat_ByDefault()
    {
        var r = Run("dom-iframe-recursive.html");

        AssertTraversalWasClean(r);
        Assert.Multiple(() =>
        {
            Assert.That(r.Contexts, Has.Count.EqualTo(4), "top, shadow, one embedded copy, its shadow");
            Assert.That(IframeDocs(r), Has.Count.EqualTo(1));
            Assert.That(r.Skipped.Count(s => s.Reason == DomContextSkipReason.IframeUrlCycle), Is.EqualTo(1));
            Assert.That(r.Truncated, Is.False);
        });
    }

    [Test]
    public void RecursiveIframe_StrictMode_RefusesTheFirstSelfEmbedding()
    {
        var r = Run("dom-iframe-recursive.html", new DomContextExplorerOptions { AllowedUrlRepeatsInAncestry = 0 });

        AssertTraversalWasClean(r);
        Assert.Multiple(() =>
        {
            Assert.That(r.Contexts, Has.Count.EqualTo(2), "top and its shadow root only");
            Assert.That(r.Skipped.Single().Reason, Is.EqualTo(DomContextSkipReason.IframeUrlCycle));
        });
    }

    [Test]
    public void SiblingIframesWithTheSameUrl_AreNotDeduplicated()
    {
        // Only ANCESTORS are compared, so two siblings loading one URL are both entered - even in strict mode.
        var r = Run("dom-iframe-siblings.html", new DomContextExplorerOptions { AllowedUrlRepeatsInAncestry = 0 });

        AssertTraversalWasClean(r);
        var frames = IframeDocs(r);
        Assert.Multiple(() =>
        {
            Assert.That(frames, Has.Count.EqualTo(2));
            Assert.That(frames.All(f => f.DocumentUrl.EndsWith("dom-frame-child.html")), Is.True);
            Assert.That(frames.Select(f => f.AssociatedHeading), Is.EqualTo(new[] { "First Sibling", "Second Sibling" }));
            Assert.That(r.Skipped, Is.Empty);
        });
    }

    [Test]
    public void ShadowDepthLimit_SkipsDeeperHosts()
    {
        var r = Run("dom-shadow-six-levels.html", new DomContextExplorerOptions { MaxShadowDepth = 3 });

        AssertTraversalWasClean(r);
        Assert.Multiple(() =>
        {
            Assert.That(ShadowRoots(r), Has.Count.EqualTo(3));
            Assert.That(r.Skipped.Single().Reason, Is.EqualTo(DomContextSkipReason.ShadowDepthLimit));
        });
    }

    [Test]
    public void FrameDepthLimit_SkipsDeeperFrames()
    {
        var r = Run("dom-iframe-nested.html", new DomContextExplorerOptions { MaxFrameDepth = 1 });

        AssertTraversalWasClean(r);
        Assert.Multiple(() =>
        {
            Assert.That(IframeDocs(r), Has.Count.EqualTo(1));
            Assert.That(r.Skipped.Single().Reason, Is.EqualTo(DomContextSkipReason.FrameDepthLimit));
        });
    }

    [Test]
    public void ContextLimit_TruncatesDiscovery()
    {
        var r = Run("dom-shadow-six-levels.html", new DomContextExplorerOptions { MaxContexts = 3 });

        AssertTraversalWasClean(r);
        Assert.Multiple(() =>
        {
            Assert.That(r.Contexts, Has.Count.EqualTo(3));
            Assert.That(r.Truncated, Is.True);
            Assert.That(r.Skipped.Any(s => s.Reason == DomContextSkipReason.ContextLimit), Is.True);
        });
    }

    // ------------------------------------------------------------------ CloudQA structure replica

    /// <summary>
    /// The replica reproduces the shadow/slot/self-iframe structure read from the real page SOURCE. The expected
    /// numbers below were derived from that source and from running the shipped inspection script in jsdom.
    /// They describe the replica, not the live page; the live page is covered by the Live-category test.
    /// </summary>
    [Test]
    public void CloudQaReplica_MatchesSourceDerivedStructure()
    {
        var r = Run("dom-cloudqa-replica.html");

        AssertTraversalWasClean(r);
        var topLevelShadow = ShadowRoots(r).Where(c => c.FrameDepth == 0).ToList();
        var embeddedShadow = ShadowRoots(r).Where(c => c.FrameDepth == 1).ToList();
        var deepest = topLevelShadow.Single(c => c.ShadowDepth == 6);

        Assert.Multiple(() =>
        {
            Assert.That(r.Contexts, Has.Count.EqualTo(60), "1 top + 29 shadow + 1 embedded copy + 29 shadow inside it");
            Assert.That(topLevelShadow, Has.Count.EqualTo(29));
            Assert.That(embeddedShadow, Has.Count.EqualTo(29));
            Assert.That(IframeDocs(r), Has.Count.EqualTo(1));
            Assert.That(r.Skipped.Count(s => s.Reason == DomContextSkipReason.IframeUrlCycle), Is.EqualTo(1));
            Assert.That(deepest.AssociatedHeading, Is.EqualTo("Nested Shadow DOM with 6 Levels"));

            // Labels are in the shadow tree; the controls are light-DOM sections assigned through named slots.
            Assert.That(deepest.Slots.Select(s => s.Name),
                Is.SupersetOf(new[] { "fname", "lname", "gender", "statedropdown" }));
            Assert.That(deepest.Slots.Single(s => s.Name == "gender").AssignedElements.Single().ControlSummary,
                Does.Contain("input[radio]x3"));
            Assert.That(deepest.UnassignedHostChildren, Has.Count.EqualTo(1), "the section whose slot name matches no slot");
        });
    }
}
