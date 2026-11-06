using NUnit.Framework;
using SeleniumAutomation.Base;

namespace SeleniumAutomation.Tests;

[TestFixture]
public sealed class AutomationPracticeTests : BaseTest
{
    [Test]
    public void FirstName_TextBox_AcceptsConfiguredValue()
    {
        var result = Page.EnterFirstName(Settings.FirstNameValue);
        Assert.That(result.Passed, Is.True, result.ToString());
    }

    [Test]
    public void Gender_RadioButton_SelectsConfiguredOption()
    {
        var result = Page.SelectGender(Settings.GenderValue);
        Assert.That(result.Passed, Is.True, result.ToString());
    }

    [Test]
    public void State_Dropdown_SelectsConfiguredOption()
    {
        var result = Page.SelectState(Settings.StateValue);
        Assert.That(result.Passed, Is.True, result.ToString());
    }

    [Test]
    public void AllThreeFields_CanBeFilledInOneSession()
    {
        var results = new[]
        {
            Page.EnterFirstName(Settings.FirstNameValue),
            Page.SelectGender(Settings.GenderValue),
            Page.SelectState(Settings.StateValue)
        };

        Assert.Multiple(() =>
        {
            foreach (var r in results)
                Assert.That(r.Passed, Is.True, r.ToString());
        });
    }
}
