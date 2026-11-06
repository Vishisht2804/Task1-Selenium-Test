namespace SeleniumAutomation.Utilities.DomContext;

public enum DomContextSkipReason
{
    ShadowDepthLimit,
    FrameDepthLimit,
    ContextLimit,
    IframeUrlCycle,
    PossibleClosedShadowRoot
}

/// <summary>A context deliberately not entered (a safeguard fired, or it cannot be inspected).</summary>
public sealed record SkippedContext(string ParentPath, string Description, DomContextSkipReason Reason, string Detail);

/// <summary>An operation that threw while traversing. Traversal continues with the next sibling.</summary>
public sealed record TraversalFailure(string Path, string Operation, string ExceptionType, string Message);

public sealed class DomContextDiscoveryResult
{
    public DomContextDiscoveryResult(
        IReadOnlyList<DomContextDescriptor> contexts,
        IReadOnlyList<SkippedContext> skipped,
        IReadOnlyList<TraversalFailure> failures,
        bool truncated, bool aborted, TimeSpan elapsed)
    {
        Contexts = contexts;
        Skipped = skipped;
        Failures = failures;
        Truncated = truncated;
        Aborted = aborted;
        Elapsed = elapsed;
    }

    /// <summary>All contexts in depth-first, document order. The first entry is the top document.</summary>
    public IReadOnlyList<DomContextDescriptor> Contexts { get; }
    public IReadOnlyList<SkippedContext> Skipped { get; }
    public IReadOnlyList<TraversalFailure> Failures { get; }

    /// <summary>True when <see cref="DomContextExplorerOptions.MaxContexts"/> stopped discovery.</summary>
    public bool Truncated { get; }

    /// <summary>True when the browsing context was lost and traversal stopped early (partial result).</summary>
    public bool Aborted { get; }
    public TimeSpan Elapsed { get; }

    public IEnumerable<DomContextDescriptor> OfKind(DomContextKind kind) => Contexts.Where(c => c.Kind == kind);
}

/// <summary>Thrown when the driver can no longer be returned to a known frame.</summary>
public sealed class DomContextException : Exception
{
    public DomContextException(string message, Exception? inner = null) : base(message, inner) { }
}
