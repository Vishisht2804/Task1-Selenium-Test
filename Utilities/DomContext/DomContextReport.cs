using System.Text;

namespace SeleniumAutomation.Utilities.DomContext;

/// <summary>Plain-text rendering of a discovery result, used by the diagnostic tests.</summary>
public static class DomContextReport
{
    public static string Format(DomContextDiscoveryResult result, string title)
    {
        var sb = new StringBuilder();
        var shadow = result.OfKind(DomContextKind.ShadowRoot).ToList();
        var frames = result.OfKind(DomContextKind.IframeDocument).ToList();

        sb.AppendLine($"=== DOM CONTEXT DISCOVERY: {title} ===");
        sb.AppendLine($"contexts={result.Contexts.Count} (top documents=1, iframe documents={frames.Count}, shadow roots={shadow.Count})");
        sb.AppendLine($"max shadow depth seen={(shadow.Count == 0 ? 0 : shadow.Max(c => c.ShadowDepth))}, " +
                      $"max frame depth seen={result.Contexts.Max(c => c.FrameDepth)}");
        sb.AppendLine($"skipped={result.Skipped.Count}, failures={result.Failures.Count}, " +
                      $"truncated={result.Truncated}, aborted={result.Aborted}, elapsed={result.Elapsed.TotalSeconds:0.00}s");
        sb.AppendLine();

        foreach (var c in result.Contexts)
        {
            sb.AppendLine($"[#{c.Id}] {c.Kind}  (parent: {(c.ParentId.HasValue ? "#" + c.ParentId : "-")})");
            sb.AppendLine($"    path:          {c.Path}");
            sb.AppendLine($"    shadowDepth:   {c.ShadowDepth}    frameDepth: {c.FrameDepth}");
            sb.AppendLine($"    documentUrl:   {c.DocumentUrl}");
            sb.AppendLine($"    heading:       {c.AssociatedHeading ?? "(none)"}");
            if (c.ShadowHostDescription != null) sb.AppendLine($"    shadowHost:    {c.ShadowHostDescription}");
            if (c.IframeDescription != null) sb.AppendLine($"    iframe:        {c.IframeDescription}");
            foreach (var note in c.Notes) sb.AppendLine($"    note:          {note}");

            if (c.Kind == DomContextKind.ShadowRoot)
            {
                sb.AppendLine($"    slots:         {c.Slots.Count}");
                foreach (var slot in c.Slots)
                {
                    var label = slot.Name.Length == 0 ? "(default)" : $"\"{slot.Name}\"";
                    sb.AppendLine($"      slot {label} {slot.Description}: directlyAssigned={slot.DirectAssignedCount}" +
                                  (slot.UsesFallbackContent ? " [fallback content shown]" : ""));
                    foreach (var a in slot.AssignedElements) AppendAssigned(sb, a, slot.UsesFallbackContent ? "fallback" : "assigned");
                }
                if (c.UnassignedHostChildren.Count > 0)
                {
                    sb.AppendLine($"    unassigned light children of host: {c.UnassignedHostChildren.Count}");
                    foreach (var a in c.UnassignedHostChildren) AppendAssigned(sb, a, "unassigned");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine($"--- skipped contexts ({result.Skipped.Count}) ---");
        foreach (var s in result.Skipped)
            sb.AppendLine($"  {s.Reason}: {s.Description} under {s.ParentPath}\n      {s.Detail}");

        sb.AppendLine($"--- traversal failures ({result.Failures.Count}) ---");
        foreach (var f in result.Failures)
            sb.AppendLine($"  {f.Operation} @ {f.Path}: {f.ExceptionType}: {f.Message}");

        return sb.ToString();
    }

    private static void AppendAssigned(StringBuilder sb, AssignedElementDescriptor a, string how)
    {
        sb.AppendLine($"        -> {how}: {a.Description} descendants={a.DescendantCount} controls=[{a.ControlSummary}]" +
                      (a.NestedShadowHosts.Count > 0 ? $" nestedShadowHosts=[{string.Join(", ", a.NestedShadowHosts)}]" : ""));
        if (a.DescendantSample.Count > 0)
            sb.AppendLine($"             descendants: {string.Join(" ", a.DescendantSample.Take(8))}{(a.DescendantSample.Count > 8 ? " ..." : "")}");
    }
}
