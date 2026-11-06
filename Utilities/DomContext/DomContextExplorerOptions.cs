namespace SeleniumAutomation.Utilities.DomContext;

/// <summary>Recursion safeguards and timing for <see cref="DomContextExplorer"/>.</summary>
public sealed class DomContextExplorerOptions
{
    /// <summary>Maximum shadow boundaries below one document. Deeper hosts are reported as skipped.</summary>
    public int MaxShadowDepth { get; init; } = 10;

    /// <summary>Maximum iframe nesting below the top document. Deeper iframes are reported as skipped.</summary>
    public int MaxFrameDepth { get; init; } = 5;

    /// <summary>Hard cap on discovered contexts (documents + shadow roots). Reaching it sets Truncated.</summary>
    public int MaxContexts { get; init; } = 300;

    /// <summary>
    /// Ancestry-based cycle detection. An iframe is entered only if its URL occurs at most this many times among
    /// its ANCESTOR documents (top document included). Sibling or unrelated iframes with the same URL are never
    /// affected, because only the ancestor chain is compared. 1 (default) lets a page embed itself once and stops
    /// the second repetition; 0 refuses any iframe whose URL already appears in its ancestry.
    /// </summary>
    public int AllowedUrlRepeatsInAncestry { get; init; } = 1;

    /// <summary>How long to wait for an iframe document to leave the "loading" state after switching into it.</summary>
    public TimeSpan FrameLoadTimeout { get; init; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (MaxShadowDepth < 1) throw new ArgumentOutOfRangeException(nameof(MaxShadowDepth));
        if (MaxFrameDepth < 0) throw new ArgumentOutOfRangeException(nameof(MaxFrameDepth));
        if (MaxContexts < 1) throw new ArgumentOutOfRangeException(nameof(MaxContexts));
        if (AllowedUrlRepeatsInAncestry < 0) throw new ArgumentOutOfRangeException(nameof(AllowedUrlRepeatsInAncestry));
    }
}
