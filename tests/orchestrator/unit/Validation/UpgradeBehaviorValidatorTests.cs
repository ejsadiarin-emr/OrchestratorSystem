using DeploymentPoC.Orchestrator.Validation;

namespace DeploymentPoC.Orchestrator.Tests.Unit.Validation;

public class UpgradeBehaviorValidatorTests
{
    [TestCase("InPlace")]
    [TestCase("UninstallFirst")]
    [TestCase("SideBySide")]
    public void Validate_AllowedValues_ReturnsSuccess(string value)
    {
        var result = UpgradeBehaviorValidator.Validate(value);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [TestCase("inplace")]
    [TestCase("UNINSTALLFIRST")]
    [TestCase("sidebyside")]
    public void Validate_CaseInsensitive_ReturnsSuccess(string value)
    {
        var result = UpgradeBehaviorValidator.Validate(value);
        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_NullOrEmpty_ReturnsFailure(string? value)
    {
        var result = UpgradeBehaviorValidator.Validate(value);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("required").IgnoreCase);
    }

    [Test]
    public void Validate_InvalidValue_ReturnsFailure()
    {
        var result = UpgradeBehaviorValidator.Validate("Replace");
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("Replace"));
        Assert.That(result.Error, Does.Contain("not valid").IgnoreCase);
    }

    [TestCase("InPlace", true)]
    [TestCase("", false)]
    [TestCase("Invalid", false)]
    public void IsValid_ReturnsExpected(string? value, bool expected)
    {
        Assert.That(UpgradeBehaviorValidator.IsValid(value), Is.EqualTo(expected));
    }

    [TestCase(null, "InPlace")]
    [TestCase("", "InPlace")]
    [TestCase("  ", "InPlace")]
    [TestCase("InPlace", "InPlace")]
    [TestCase("inplace", "InPlace")]
    [TestCase("UninstallFirst", "UninstallFirst")]
    [TestCase("UNINSTALLFIRST", "UninstallFirst")]
    [TestCase("SideBySide", "SideBySide")]
    [TestCase("sidebyside", "SideBySide")]
    [TestCase("Unknown", "Unknown")]
    public void Normalize_ReturnsExpected(string? input, string expected)
    {
        Assert.That(UpgradeBehaviorValidator.Normalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void AllowedValues_ContainsExactlyThreeValues()
    {
        Assert.That(UpgradeBehaviorValidator.AllowedValues.Count, Is.EqualTo(3));
        Assert.That(UpgradeBehaviorValidator.AllowedValues, Does.Contain("InPlace"));
        Assert.That(UpgradeBehaviorValidator.AllowedValues, Does.Contain("UninstallFirst"));
        Assert.That(UpgradeBehaviorValidator.AllowedValues, Does.Contain("SideBySide"));
    }

    [Test]
    public void DefaultValue_IsInPlace()
    {
        Assert.That(UpgradeBehaviorValidator.DefaultValue, Is.EqualTo("InPlace"));
    }
}
