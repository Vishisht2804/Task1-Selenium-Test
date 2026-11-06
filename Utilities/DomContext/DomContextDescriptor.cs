using OpenQA.Selenium;

namespace SeleniumAutomation.Utilities.DomContext;

public enum DomContextKind
{
    /// <summary>The top-level browsing context's document.</summary>
    TopDocument,

    /// <summary>The document inside an iframe (at any depth).</summary>
    IframeDocument,

    /// <summary>An open shadow root (at any depth, in any document).</summary>
    ShadowRoot
}

/// <summary>One element assigned to a slot (result of <c>slot.assignedElements({ flatten: true })</c>).</summary>
/// <remarks>The <see cref="Element"/> reference is only valid while the driver is inside the owning frame.</remarks>
public sealed record AssignedElementDescriptor(
    IWebElement Element,
    string Description,
    string TagName,
    string SlotAttribute,
    int DescendantCount,
    IReadOnlyList<string> DescendantSample,
    string ControlSummary,
    IReadOnlyList<string> NestedShadowHosts);

/// <summary>
/// A &lt;slot&gt; inside a shadow root and what is currently assigned to it. No semantic association is made here.
/// When <see cref="UsesFallbackContent"/> is true nothing is assigned and <see cref="AssignedElements"/> holds the
/// slot's own fallback content (that is what <c>assignedElements({ flatten: true })</c> returns in that case).
/// </summary>
public sealed record SlotDescriptor(
    IWebElement Element,
    string Description,
    string Name,
    int DirectAssignedCount,
    bool UsesFallbackContent,
    IReadOnlyList<AssignedElementDescriptor> AssignedElements);

/// <summary>
/// Immutable-by-convention description of one DOM context (a document or an open shadow root) discovered by
/// <see cref="DomContextExplorer"/>. Contains no field semantics; it only says where something lives.
/// </summary>
public sealed class DomContextDescriptor
{
    internal DomContextDescriptor(
        int id, int? parentId, DomContextKind kind, string path, int shadowDepth, int frameDepth,
        IReadOnlyList<IWebElement> frameChain, IReadOnlyList<IWebElement> hostChain)
    {
        Id = id;
        ParentId = parentId;
        Kind = kind;
        Path = path;
        ShadowDepth = shadowDepth;
        FrameDepth = frameDepth;
        FrameChain = frameChain;
        HostChain = hostChain;
    }

    public int Id { get; }
    public int? ParentId { get; }
    public DomContextKind Kind { get; }

    /// <summary>Unique, human-readable path, e.g. <c>document &gt; shadow[3]&lt;x-a&gt; &gt; iframe[0]&lt;iframe&gt;</c>.</summary>
    public string Path { get; }

    /// <summary>Number of shadow boundaries between this context and the root of its own document (0 for documents).</summary>
    public int ShadowDepth { get; }

    /// <summary>Number of iframe boundaries between this context and the top-level document (0 for the top document).</summary>
    public int FrameDepth { get; }

    public string DocumentUrl { get; internal set; } = "";
    public string DocumentTitle { get; internal set; } = "";

    /// <summary>Nearest preceding h1-h6 of the host/iframe (inherited from the parent context when none is found).</summary>
    public string? AssociatedHeading { get; internal set; }

    public string? ShadowHostDescription { get; internal set; }
    public string? IframeDescription { get; internal set; }

    /// <summary>The shadow host (ShadowRoot contexts only). Valid only while the driver is inside this context's frame.</summary>
    public IWebElement? ShadowHost { get; internal set; }

    /// <summary>The shadow root as <see cref="ISearchContext"/>, from <c>host.GetShadowRoot()</c> (ShadowRoot contexts only).</summary>
    public ISearchContext? ShadowRoot { get; internal set; }

    /// <summary>The &lt;iframe&gt; element that embeds this document (IframeDocument contexts only), valid in the parent frame.</summary>
    public IWebElement? FrameElement { get; internal set; }

    /// <summary>iframe elements from the top document down to this context's document. Each is valid in its own parent frame.</summary>
    public IReadOnlyList<IWebElement> FrameChain { get; }

    /// <summary>Shadow hosts from the document root down to this context (ShadowRoot contexts only).</summary>
    public IReadOnlyList<IWebElement> HostChain { get; }

    /// <summary>Slots of this shadow root and their assigned elements (ShadowRoot contexts only).</summary>
    public IReadOnlyList<SlotDescriptor> Slots { get; internal set; } = Array.Empty<SlotDescriptor>();

    /// <summary>Light-DOM children of this shadow root's host that are not assigned to any slot.</summary>
    public IReadOnlyList<AssignedElementDescriptor> UnassignedHostChildren { get; internal set; } =
        Array.Empty<AssignedElementDescriptor>();

    /// <summary>Non-fatal observations (for example "iframe switched by index fallback").</summary>
    public List<string> Notes { get; } = new();

    /// <summary>
    /// The context to run searches against: the shadow root for ShadowRoot contexts, otherwise the driver
    /// (which searches the document of the frame the driver is currently in).
    /// </summary>
    public ISearchContext GetSearchContext(IWebDriver driver) => ShadowRoot ?? driver;

    public override string ToString() => $"#{Id} {Kind} {Path}";
}
