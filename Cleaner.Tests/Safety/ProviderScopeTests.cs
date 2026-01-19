using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Xunit;

namespace Cleaner.Tests.Safety;

public sealed class ProviderScopeTests
{
    [Fact]
    public void WindowsUpdateCacheProvider_AllowsUpdateRoots()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.windows-update-cache", @"C:\Windows\SoftwareDistribution\Download");
        policy.AddAllowlist("provider.windows-update-cache", @"C:\Windows\SoftwareDistribution\DeliveryOptimization\Cache");

        Assert.True(SafetyRules.IsAllowedPath(
            "provider.windows-update-cache",
            @"C:\Windows\SoftwareDistribution\Download\file.bin",
            policy));
        Assert.True(SafetyRules.IsAllowedPath(
            "provider.windows-update-cache",
            @"C:\Windows\SoftwareDistribution\DeliveryOptimization\Cache\file.bin",
            policy));
    }

    [Fact]
    public void ThumbnailCacheProvider_AllowsExplorerThumbcache()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.thumbnail-cache", @"C:\Users\Alice\AppData\Local\Microsoft\Windows\Explorer");

        Assert.True(SafetyRules.IsAllowedPath(
            "provider.thumbnail-cache",
            @"C:\Users\Alice\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db",
            policy));
    }

    [Fact]
    public void ThumbnailCacheProvider_AllowsExplorerIconcache()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.thumbnail-cache", @"C:\Users\Alice\AppData\Local\Microsoft\Windows\Explorer");

        Assert.True(SafetyRules.IsAllowedPath(
            "provider.thumbnail-cache",
            @"C:\Users\Alice\AppData\Local\Microsoft\Windows\Explorer\iconcache_256.db",
            policy));
    }

    [Fact]
    public void ThumbnailCacheProvider_DeniesProtectedPathsEvenWhenAllowlisted()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.thumbnail-cache", @"C:\Users\Alice\Documents\");

        Assert.False(SafetyRules.IsAllowedPath(
            "provider.thumbnail-cache",
            @"C:\Users\Alice\Documents\thumbcache_256.db",
            policy));
    }

    [Fact]
    public void UserTempProvider_AllowsTempRoot()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.user-temp", @"C:\Users\Alice\AppData\Local\Temp");

        Assert.True(SafetyRules.IsAllowedPath(
            "provider.user-temp",
            @"C:\Users\Alice\AppData\Local\Temp\file.tmp",
            policy));
    }

    [Fact]
    public void SystemTempProvider_AllowsWindowsTemp()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.system-temp", @"C:\Windows\Temp");

        Assert.True(SafetyRules.IsAllowedPath(
            "provider.system-temp",
            @"C:\Windows\Temp\file.tmp",
            policy));
    }

    [Fact]
    public void WerReportsProvider_AllowsReportArchive()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.wer-reports", @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive");

        Assert.True(SafetyRules.IsAllowedPath(
            "provider.wer-reports",
            @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive\report.wer",
            policy));
    }

    [Fact]
    public void BrowserCacheProvider_AllowsCacheRoot()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider.browser-cache", @"C:\Users\Alice\AppData\Local\Microsoft\Edge\User Data\Default\Cache");

        Assert.True(SafetyRules.IsAllowedPath(
            "provider.browser-cache",
            @"C:\Users\Alice\AppData\Local\Microsoft\Edge\User Data\Default\Cache\data.bin",
            policy));
    }

}
