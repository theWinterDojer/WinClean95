using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Xunit;

namespace Cleaner.Tests.Safety;

public sealed class SafetyRulesTests
{
    [Fact]
    public void IsAllowedPath_RequiresAllowlist()
    {
        var policy = new SafetyPolicy();
        var allowed = SafetyRules.IsAllowedPath("provider", @"C:\Temp\file.tmp", policy);
        Assert.False(allowed);
    }

    [Fact]
    public void IsAllowedPath_DeniesProtectedPathsEvenWhenAllowlisted()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider", @"C:\Users\Alice\Documents\");

        var allowed = SafetyRules.IsAllowedPath("provider", @"C:\Users\Alice\Documents\file.tmp", policy);
        Assert.False(allowed);
    }

    [Fact]
    public void IsAllowedPath_AllowsNonProtectedPathsUnderAllowlist()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider", @"C:\Temp\");

        var allowed = SafetyRules.IsAllowedPath("provider", @"C:\Temp\file.tmp", policy);
        Assert.True(allowed);
    }

    [Fact]
    public void IsOldEnough_UsesRetentionDays()
    {
        var policy = new SafetyPolicy();
        policy.RetentionDaysByCategory["temp.user"] = 7;
        var utcNow = DateTime.UtcNow;

        Assert.True(SafetyRules.IsOldEnough("temp.user", utcNow.AddDays(-8), policy, utcNow));
        Assert.False(SafetyRules.IsOldEnough("temp.user", utcNow.AddDays(-2), policy, utcNow));
    }
}
