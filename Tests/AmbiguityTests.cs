using NUnit.Framework;
using SeleniumAutomation.Base;
using SeleniumAutomation.Utilities;

namespace SeleniumAutomation.Tests;

/// <summary>
/// Demonstrates the resolver's refusal to guess. The fixture contains two complete, identical,
/// visible forms, so "First Name" resolves to two equally plausible inputs.
/// This test ignores SELAUTO_PAGEURL: it points the existing driver at its own fixture.
/// </summary>
[TestFixture]
public sealed class AmbiguityTests : BaseTest
{
    /// <summary>Runs after BaseTest.StartBrowser and navigates the same driver to the ambiguity fixture.</summary>
    [SetUp]
    public void OpenAmbiguousFixture()
    {
        Settings.PageUrl = AmbiguousFixtureUrl();
        Page.Open();
    }

    [Test]
    public void EnterFirstName_TwoIdenticalForms_ThrowsAmbiguousElementException()
    {
        var ex = Assert.Throws<AmbiguousElementException>(
            () => Page.EnterFirstName(Settings.FirstNameValue));

        Log(ex!.Message);

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Does.Contain("First Name"), "The message should name the ambiguous field.");
            Assert.That(ex.Message, Does.Contain("candidates"), "The message should list the remaining candidates.");
        });
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
