using NUnit.Framework;
using SeleniumAutomation.Base;

namespace SeleniumAutomation.Tests;

[TestFixture]
public sealed class AutomationPracticeTests : BaseTest
{
    [Test]
    [Category("Live")]
    public void EveryCompleteLiveForm_CanBeFilled()
    {
        Open(Settings.PageUrl);
        var candidates = DiscoverCompleteCandidates();
        Assert.That(candidates, Is.Not.Empty, "The live page must expose at least one complete form.");

        var failures = new List<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                var page = CreatePage(candidate);
                var results = new[]
                {
                    page.EnterFirstName(Settings.FirstNameValue),
                    page.SelectGender(Settings.GenderValue),
                    page.SelectState(Settings.StateValue)
                };
                failures.AddRange(results.Where(result => !result.Passed)
                    .Select(result => $"{candidate.Description}: {result}"));
            }
            catch (Exception ex)
            {
                failures.Add($"{candidate.Description}: {ex.Message}");
            }
        }

        Assert.That(failures, Is.Empty,
            "One or more complete forms failed:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}
