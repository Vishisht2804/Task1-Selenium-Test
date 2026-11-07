using NUnit.Framework;
using SeleniumAutomation.Base;
using SeleniumAutomation.Utilities;

namespace SeleniumAutomation.Tests;

/// <summary>
/// Demonstrates that discovery returns both complete forms and callers exercise each candidate explicitly.
/// </summary>
[TestFixture]
public sealed class AmbiguityTests : BaseTest
{
    /// <summary>Runs after BaseTest.StartBrowser and navigates the same driver to the ambiguity fixture.</summary>
    [SetUp]
    public void OpenAmbiguousFixture()
    {
        Settings.PageUrl = AmbiguousFixtureUrl();
        Open(Settings.PageUrl);
    }

    [Test]
    public void TwoIdenticalForms_AreDiscoveredAndCanBeExercisedIndependently()
    {
        var candidates = DiscoverCompleteCandidates();
        Assert.That(candidates, Has.Count.EqualTo(2));

        foreach (var candidate in candidates)
        {
            var result = CreatePage(candidate).EnterFirstName(Settings.FirstNameValue);
            Assert.That(result.Passed, Is.True, $"{candidate.Description}: {result}");
        }
    }

    /// <summary>
    /// file:// URL built from the test output directory (the csproj copies Fixtures/*.html there),
    /// so the test works without a local web server or SELAUTO_PAGEURL.
    /// </summary>
    private static string AmbiguousFixtureUrl()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ambiguous-form.html");
        if (!File.Exists(path))
            throw new FileNotFoundException("The ambiguity fixture was not copied to the test output directory.", path);
        return new Uri(path).AbsoluteUri;
    }
}
