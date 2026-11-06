using System.Text.RegularExpressions;

namespace SeleniumAutomation.Utilities;

public static class TextUtil
{
    /// <summary>Collapses all whitespace (including non-breaking spaces) and trims. Case is preserved.</summary>
    public static string NormalizeSpaces(string? s) =>
        string.IsNullOrEmpty(s) ? "" : Regex.Replace(s.Replace('\u00a0', ' '), @"\s+", " ").Trim();

    /// <summary>
    /// Normalisation used to compare labels: collapsed whitespace, lower case, and without a trailing
    /// colon / asterisk ("First Name *" and "first  name:" both become "first name").
    /// </summary>
    public static string NormalizeLabel(string? s) =>
        NormalizeSpaces(s).ToLowerInvariant().TrimEnd(':', '*', ' ').Trim();

    /// <summary>Lower-case letters and digits only; used to compare attribute values such as name="First_Name".</summary>
    public static string Strip(string? s) =>
        Regex.Replace(NormalizeSpaces(s).ToLowerInvariant(), "[^a-z0-9]", "");
}
