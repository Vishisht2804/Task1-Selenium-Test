namespace SeleniumAutomation.Utilities;

/// <summary>Thrown when a field cannot be found, or cannot be validated as the expected control type.</summary>
public class ElementResolutionException : Exception
{
    public ElementResolutionException(string message) : base(message) { }
}

/// <summary>Thrown when several plausible elements remain and the resolver refuses to guess.</summary>
public sealed class AmbiguousElementException : ElementResolutionException
{
    public AmbiguousElementException(string message) : base(message) { }
}

/// <summary>
/// Collects what the resolver tried during one resolution attempt. It is reset on every polling
/// iteration, so after a timeout it describes the last attempt.
/// </summary>
public sealed class ResolutionTrace
{
    private readonly List<string> _lines = new();

    public ResolutionTrace(string field) => Field = field;

    public string Field { get; }

    public void Add(string line) => _lines.Add(line);

    public void Reset() => _lines.Clear();

    public string Format(string headline) =>
        $"Could not resolve field '{Field}': {headline}\nStrategies attempted and results:\n  - {string.Join("\n  - ", _lines)}";

    public string FormatSuccess() =>
        $"Resolved field '{Field}':\n  - {string.Join("\n  - ", _lines)}";
}
