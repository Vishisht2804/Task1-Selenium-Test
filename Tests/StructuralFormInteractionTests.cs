using NUnit.Framework;
using SeleniumAutomation.Base;

namespace SeleniumAutomation.Tests;

[TestFixture]
public sealed class StructuralFormInteractionTests : BaseTest
{
    [TestCase("top-level light DOM", "automation-form.html")]
    [TestCase("iframe without id", "form-frame-no-id.html")]
    [TestCase("iframe with id", "form-frame-id.html")]
    [TestCase("single open shadow root", "form-shadow.html")]
    [TestCase("nested shadow roots", "form-nested-shadow.html")]
    [TestCase("three shadow levels", "form-nested-shadow.html")]
    [TestCase("four shadow levels", "form-four-shadow.html")]
    [TestCase("five shadow levels", "form-five-shadow.html")]
    [TestCase("six shadow levels", "form-six-shadow.html")]
    [TestCase("iframe nested in shadow DOM", "form-iframe-in-shadow.html")]
    public void AllThreeFieldsAreFilledAcrossStructuralScenario(string scenario, string fixture)
    {
        Settings.PageUrl = FixtureUrl(fixture);
        Open(Settings.PageUrl);
        var candidates = DiscoverCompleteCandidates();
        Assert.That(candidates, Has.Count.EqualTo(1), $"{scenario} should contain exactly one complete form.");
        var page = CreatePage(candidates[0]);

        var results = new[]
        {
            page.EnterFirstName(Settings.FirstNameValue),
            page.SelectGender(Settings.GenderValue),
            page.SelectState(Settings.StateValue)
        };

        Assert.Multiple(() =>
        {
            foreach (var result in results)
                Assert.That(result.Passed, Is.True, $"{scenario}: {result}");
        });
    }

    private static string FixtureUrl(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException("The structural fixture was not copied to the test output directory.", path);
        return new Uri(path).AbsoluteUri;
    }
}
