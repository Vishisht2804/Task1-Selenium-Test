using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace SeleniumAutomation.Utilities.DomContext;

/// <summary>
/// Generic DOM traversal layer. Discovers, depth-first and in document order:
/// the top document, every OPEN shadow root (recursively, in any document), and every iframe document
/// (nested, inside shadow roots, containing shadow roots). It reports slot assignments but does not interpret them.
///
/// It knows nothing about any particular page: no ids, class names, custom-element names or slot names.
/// Assumption: the driver is in the top-level browsing context when <see cref="Discover"/> is called; it is
/// returned to the top-level context when the call ends (also on failure).
///
/// Element references stored in descriptors belong to the frame they were found in. To use them later call
/// <see cref="WithContext{T}"/>, which re-enters the frame chain, runs the action, and restores the driver.
/// </summary>
public sealed class DomContextExplorer
{
    private readonly IWebDriver _driver;
    private readonly IJavaScriptExecutor _js;
    private readonly DomContextExplorerOptions _options;
    private readonly Action<string> _log;

    public DomContextExplorer(IWebDriver driver, DomContextExplorerOptions? options = null, Action<string>? log = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _js = (IJavaScriptExecutor)driver;
        _options = options ?? new DomContextExplorerOptions();
        _options.Validate();
        _log = log ?? (_ => { });
    }

    // ------------------------------------------------------------------ public API

    public DomContextDiscoveryResult Discover()
    {
        var state = new TraversalState();
        var clock = Stopwatch.StartNew();
        var aborted = false;

        _driver.SwitchTo().DefaultContent();
        try
        {
            var topUrl = ReadDocumentState().Url;
            var top = new DomContextDescriptor(
                state.NextId++, null, DomContextKind.TopDocument, "document", 0, 0,
                Array.Empty<IWebElement>(), Array.Empty<IWebElement>())
            { DocumentUrl = topUrl };
            state.Contexts.Add(top);

            var ancestry = new List<string> { NormalizeUrl(topUrl) ?? topUrl };
            Walk(top, state, ancestry);
        }
        catch (DomContextException ex)
        {
            aborted = true;
            state.Failures.Add(new TraversalFailure("(traversal)", "restore browsing context", nameof(DomContextException), ex.Message));
        }
        finally
        {
            try { _driver.SwitchTo().DefaultContent(); }
            catch (Exception ex)
            {
                state.Failures.Add(new TraversalFailure("(traversal)", "final DefaultContent()", ex.GetType().Name, ex.Message));
            }
        }

        clock.Stop();
        return new DomContextDiscoveryResult(state.Contexts, state.Skipped, state.Failures, state.Truncated, aborted, clock.Elapsed);
    }

    /// <summary>
    /// Enters the frame chain of <paramref name="context"/>, runs <paramref name="action"/> with the context's
    /// <see cref="ISearchContext"/> (shadow root, or the driver for documents), and always restores the driver.
    /// </summary>
    public T WithContext<T>(DomContextDescriptor context, Func<ISearchContext, T> action)
    {
        var entered = 0;
        try
        {
            _driver.SwitchTo().DefaultContent();
            foreach (var frame in context.FrameChain)
            {
                _driver.SwitchTo().Frame(frame);
                entered++;
            }
            return action(context.GetSearchContext(_driver));
        }
        finally
        {
            Unwind(entered);
        }
    }

    public void WithContext(DomContextDescriptor context, Action<ISearchContext> action) =>
        WithContext<bool>(context, sc => { action(sc); return true; });

    // ------------------------------------------------------------------ traversal

    private sealed class TraversalState
    {
        public readonly List<DomContextDescriptor> Contexts = new();
        public readonly List<SkippedContext> Skipped = new();
        public readonly List<TraversalFailure> Failures = new();
        public int NextId;
        public bool Truncated;
    }

    private sealed record ChildInfo(
        string Kind, long Order, IWebElement? Element, string Description, string? Heading,
        string ResolvedSrc, string? SrcAttribute, bool HasSrcdoc);

    private sealed record Inventory(
        string Url, string Title, List<ChildInfo> Children,
        List<SlotDescriptor> Slots, List<AssignedElementDescriptor> Unassigned);

    /// <summary>Inspects one context (the driver must already be in its frame) and visits its children.</summary>
    private void Walk(DomContextDescriptor context, TraversalState state, List<string> ancestry)
    {
        Inventory inventory;
        try
        {
            inventory = Inspect(context);
        }
        catch (Exception ex) when (ex is not DomContextException)
        {
            state.Failures.Add(new TraversalFailure(context.Path, "inspect context", ex.GetType().Name, ex.Message));
            return;
        }

        context.DocumentUrl = inventory.Url;
        context.DocumentTitle = inventory.Title;
        context.Slots = inventory.Slots;
        context.UnassignedHostChildren = inventory.Unassigned;
        _log($"[dom-context] #{context.Id} {context.Kind} {context.Path}");

        int shadowIndex = 0, frameIndex = 0;
        foreach (var child in inventory.Children)
        {
            switch (child.Kind)
            {
                case "shadow":
                    VisitShadow(context, child, shadowIndex++, state, ancestry);
                    break;
                case "frame":
                    VisitFrame(context, child, frameIndex++, state, ancestry);
                    break;
                default:
                    state.Skipped.Add(new SkippedContext(
                        context.Path, child.Description, DomContextSkipReason.PossibleClosedShadowRoot,
                        "defined custom element without an open shadow root (none, or closed); a closed root cannot be inspected from outside"));
                    break;
            }
        }
    }

    private void VisitShadow(DomContextDescriptor parent, ChildInfo child, int index, TraversalState state, List<string> ancestry)
    {
        var path = $"{parent.Path} > shadow[{index}]{child.Description}";
        var depth = parent.ShadowDepth + 1;

        if (depth > _options.MaxShadowDepth)
        {
            state.Skipped.Add(new SkippedContext(parent.Path, child.Description, DomContextSkipReason.ShadowDepthLimit,
                $"shadow depth {depth} exceeds MaxShadowDepth={_options.MaxShadowDepth}"));
            return;
        }
        if (LimitReached(state, parent, child)) return;

        ISearchContext root;
        try
        {
            root = child.Element!.GetShadowRoot();
        }
        catch (Exception ex) when (ex is not DomContextException)
        {
            state.Failures.Add(new TraversalFailure(path, "GetShadowRoot", ex.GetType().Name, ex.Message));
            return;
        }

        var context = new DomContextDescriptor(
            state.NextId++, parent.Id, DomContextKind.ShadowRoot, path, depth, parent.FrameDepth,
            parent.FrameChain, parent.HostChain.Append(child.Element!).ToList())
        {
            ShadowHost = child.Element,
            ShadowRoot = root,
            ShadowHostDescription = child.Description,
            AssociatedHeading = child.Heading ?? parent.AssociatedHeading,
            DocumentUrl = parent.DocumentUrl,
            DocumentTitle = parent.DocumentTitle
        };
        state.Contexts.Add(context);
        Walk(context, state, ancestry);
    }

    private void VisitFrame(DomContextDescriptor parent, ChildInfo child, int index, TraversalState state, List<string> ancestry)
    {
        var path = $"{parent.Path} > iframe[{index}]{child.Description}";
        var depth = parent.FrameDepth + 1;

        if (depth > _options.MaxFrameDepth)
        {
            state.Skipped.Add(new SkippedContext(parent.Path, child.Description, DomContextSkipReason.FrameDepthLimit,
                $"iframe depth {depth} exceeds MaxFrameDepth={_options.MaxFrameDepth}"));
            return;
        }
        if (LimitReached(state, parent, child)) return;

        // Cycle check 1: the URL the iframe is declared to load, against its ANCESTOR documents only.
        var declared = child.HasSrcdoc ? null : NormalizeUrl(child.ResolvedSrc);
        if (declared != null && CycleDetected(declared, ancestry, state, parent, child)) return;

        var entered = false;
        try
        {
            var viaFallback = SwitchInto(child.Element!);
            entered = true;

            var (url, loaded) = WaitForFrameDocument(declared != null);

            // Cycle check 2: the URL actually loaded (covers redirects and srcdoc/about:blank-to-navigation).
            var actual = NormalizeUrl(url);
            if (actual != null && actual != declared && CycleDetected(actual, ancestry, state, parent, child)) return;

            var context = new DomContextDescriptor(
                state.NextId++, parent.Id, DomContextKind.IframeDocument, path, 0, depth,
                parent.FrameChain.Append(child.Element!).ToList(), Array.Empty<IWebElement>())
            {
                FrameElement = child.Element,
                IframeDescription = child.Description,
                AssociatedHeading = child.Heading ?? parent.AssociatedHeading,
                DocumentUrl = url
            };
            if (viaFallback) context.Notes.Add("entered via window.frames index because SwitchTo().Frame(element) failed");
            if (!loaded) context.Notes.Add($"document did not finish loading within {_options.FrameLoadTimeout.TotalSeconds:0}s; inspected as-is");
            state.Contexts.Add(context);

            ancestry.Add(actual ?? url);
            try { Walk(context, state, ancestry); }
            finally { ancestry.RemoveAt(ancestry.Count - 1); }
        }
        catch (Exception ex) when (ex is not DomContextException)
        {
            state.Failures.Add(new TraversalFailure(path, "enter iframe", ex.GetType().Name, ex.Message));
        }
        finally
        {
            if (entered) LeaveFrame(parent);
        }
    }

    private bool LimitReached(TraversalState state, DomContextDescriptor parent, ChildInfo child)
    {
        if (state.Contexts.Count < _options.MaxContexts) return false;
        state.Truncated = true;
        state.Skipped.Add(new SkippedContext(parent.Path, child.Description, DomContextSkipReason.ContextLimit,
            $"MaxContexts={_options.MaxContexts} reached"));
        return true;
    }

    private bool CycleDetected(string url, List<string> ancestry, TraversalState state, DomContextDescriptor parent, ChildInfo child)
    {
        var count = ancestry.Count(a => a == url);
        if (count <= _options.AllowedUrlRepeatsInAncestry) return false;

        state.Skipped.Add(new SkippedContext(parent.Path, child.Description, DomContextSkipReason.IframeUrlCycle,
            $"'{url}' already appears {count} time(s) among ancestor documents (allowed: {_options.AllowedUrlRepeatsInAncestry}); " +
            $"ancestry: [{string.Join(" -> ", ancestry)}]"));
        return true;
    }

    // ------------------------------------------------------------------ frame switching

    /// <summary>Switches into the iframe with SwitchTo().Frame(element); falls back to its window.frames index.</summary>
    private bool SwitchInto(IWebElement iframe)
    {
        try
        {
            _driver.SwitchTo().Frame(iframe);
            return false;
        }
        catch (WebDriverException)
        {
            // Still in the parent frame here, so the iframe's index can be computed structurally.
            var index = Convert.ToInt32(_js.ExecuteScript(DomContextScripts.FrameIndexOf, iframe));
            if (index < 0) throw;
            _driver.SwitchTo().Frame(index);
            return true;
        }
    }

    private void LeaveFrame(DomContextDescriptor parent)
    {
        try
        {
            _driver.SwitchTo().ParentFrame();
            return;
        }
        catch (WebDriverException)
        {
            // fall through to a full replay of the parent's frame chain
        }

        try
        {
            _driver.SwitchTo().DefaultContent();
            foreach (var frame in parent.FrameChain) _driver.SwitchTo().Frame(frame);
        }
        catch (Exception ex)
        {
            throw new DomContextException($"Could not return to the document of '{parent.Path}'.", ex);
        }
    }

    private void Unwind(int enteredFrames)
    {
        try
        {
            for (var i = 0; i < enteredFrames; i++) _driver.SwitchTo().ParentFrame();
        }
        catch (WebDriverException)
        {
            _driver.SwitchTo().DefaultContent();
        }
    }

    private (string Url, bool Loaded) WaitForFrameDocument(bool expectsDocument)
    {
        var wait = new WebDriverWait(_driver, _options.FrameLoadTimeout) { PollingInterval = TimeSpan.FromMilliseconds(100) };
        wait.IgnoreExceptionTypes(typeof(WebDriverException));
        var loaded = true;
        try
        {
            wait.Until(_ =>
            {
                var (readyState, current) = ReadDocumentState();
                if (readyState == "loading") return false;
                if (expectsDocument && string.Equals(current, "about:blank", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            });
        }
        catch (WebDriverTimeoutException)
        {
            loaded = false;
        }

        // Read the URL explicitly instead of relying on a side effect of the wait condition.
        return (ReadDocumentState().Url, loaded);
    }

    // ------------------------------------------------------------------ inspection

    private (string ReadyState, string Url) ReadDocumentState()
    {
        var d = Obj(_js.ExecuteScript(DomContextScripts.DocumentState));
        return (Str(d, "readyState"), Str(d, "url"));
    }

    private Inventory Inspect(DomContextDescriptor context)
    {
        // The host element is passed in; the script reads host.shadowRoot itself. For documents the argument is null.
        var d = Obj(_js.ExecuteScript(DomContextScripts.Inspect, context.ShadowHost));
        if (d.TryGetValue("error", out var error) && error is string message)
            throw new InvalidOperationException(message);

        var children = Arr(Get(d, "children"))
            .Select(o => Obj(o))
            .OrderBy(c => Lng(c, "order"))
            .Select(c => new ChildInfo(
                Str(c, "kind"), Lng(c, "order"), El(c, "el"), Str(c, "desc"), StrOrNull(c, "heading"),
                Str(c, "src"), StrOrNull(c, "srcAttr"), Bool(c, "srcdoc")))
            .ToList();

        var slots = Arr(Get(d, "slots")).Select(o => Obj(o)).Select(s => new SlotDescriptor(
            El(s, "el")!, Str(s, "desc"), Str(s, "name"), (int)Lng(s, "direct"), Bool(s, "fallback"),
            Arr(Get(s, "assigned")).Select(a => ParseAssigned(Obj(a))).ToList())).ToList();

        var unassigned = Arr(Get(d, "unassigned")).Select(a => ParseAssigned(Obj(a))).ToList();

        return new Inventory(Str(d, "url"), Str(d, "title"), children, slots, unassigned);
    }

    private static AssignedElementDescriptor ParseAssigned(IDictionary<string, object?> a) => new(
        El(a, "el")!, Str(a, "desc"), Str(a, "tag"), Str(a, "slotAttr"), (int)Lng(a, "descendants"),
        StrList(Get(a, "sample")), Str(a, "controls"), StrList(Get(a, "nested")));

    // ------------------------------------------------------------------ small helpers

    /// <summary>Strips the fragment; about:* (blank, srcdoc) has no comparable URL, so it takes no part in cycle detection.</summary>
    internal static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        if (u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return null;
        var hash = u.IndexOf('#');
        return hash >= 0 ? u[..hash] : u;
    }

    private static IDictionary<string, object?> Obj(object? value) =>
        value as IDictionary<string, object?> ?? throw new InvalidOperationException(
            $"Expected a JavaScript object but got {value?.GetType().Name ?? "null"}.");

    private static IEnumerable<object?> Arr(object? value) =>
        value is System.Collections.IEnumerable items and not string ? items.Cast<object?>() : Array.Empty<object?>();

    private static object? Get(IDictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) ? v : null;

    private static string Str(IDictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) && v is string s ? s : "";

    private static string? StrOrNull(IDictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) && v is string s ? s : null;

    private static long Lng(IDictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) && v != null && v is not string and not bool ? Convert.ToInt64(v) : 0;

    private static bool Bool(IDictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) && v is true;

    private static IWebElement? El(IDictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) ? v as IWebElement : null;

    private static IReadOnlyList<string> StrList(object? value) => Arr(value).OfType<string>().ToList();
}
