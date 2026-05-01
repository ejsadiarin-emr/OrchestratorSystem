using DeploymentPoC.Orchestrator.Validation;

namespace DeploymentPoC.Orchestrator.Tests.Unit.Validation;

public class UpgradeBehaviorValidatorTests
{
    [TestCase("InPlace")]
    [TestCase("UninstallFirst")]
    public void Validate_AllowedValues_ReturnsSuccess(string value)
    {
        var result = UpgradeBehaviorValidator.Validate(value);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [TestCase("inplace")]
    [TestCase("UNINSTALLFIRST")]
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
    [TestCase("Unknown", "Unknown")]
    public void Normalize_ReturnsExpected(string? input, string expected)
    {
        Assert.That(UpgradeBehaviorValidator.Normalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void AllowedValues_ContainsExactlyTwoValues()
    {
        Assert.That(UpgradeBehaviorValidator.AllowedValues.Count, Is.EqualTo(2));
        Assert.That(UpgradeBehaviorValidator.AllowedValues, Does.Contain("InPlace"));
        Assert.That(UpgradeBehaviorValidator.AllowedValues, Does.Contain("UninstallFirst"));
    }

    [Test]
    public void DefaultValue_IsInPlace()
    {
        Assert.That(UpgradeBehaviorValidator.DefaultValue, Is.EqualTo("InPlace"));
    }
}
