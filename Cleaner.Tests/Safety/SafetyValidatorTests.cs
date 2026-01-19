using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Xunit;

namespace Cleaner.Tests.Safety;

public sealed class SafetyValidatorTests
{
    [Fact]
    public void ValidateFindingForCleanup_BlocksProtectedPaths()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider", @"C:\Users\Alice\Documents\");

        var finding = new Finding(
            "id",
            Categories.UserTemp.Id,
            "provider",
            @"C:\",
            @"C:\Users\Alice\Documents\file.tmp",
            128,
            DateTime.UtcNow.AddDays(-10),
            "High",
            "Test",
            false,
            false,
            CleanupActions.RecycleFile);

        var result = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);

        Assert.False(result.Ok);
        Assert.Equal(CleanupOutcomeCategories.SkippedSafetyRecheck, result.ReasonCategory);
    }

    [Fact]
    public void ValidateFindingForCleanup_RecentGuardBlocksTempFiles()
    {
        var policy = new SafetyPolicy
        {
            RecentFileGuardHours = 48
        };
        policy.RetentionDaysByCategory[Categories.UserTemp.Id] = 0;

        var tempRoot = Path.Combine(Path.GetTempPath(), "CleanerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "recent.tmp");
        File.WriteAllText(filePath, "data");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        policy.AddAllowlist("provider", tempRoot);

        try
        {
            var finding = new Finding(
                "id",
                Categories.UserTemp.Id,
                "provider",
                Path.GetPathRoot(filePath) ?? string.Empty,
                filePath,
                new FileInfo(filePath).Length,
                File.GetLastWriteTimeUtc(filePath),
                "High",
                "Test",
                false,
                false,
                CleanupActions.RecycleFile);

            var result = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);

            Assert.False(result.Ok);
            Assert.Equal(CleanupOutcomeCategories.SkippedTooNew, result.ReasonCategory);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void ValidateFindingForCleanup_CompatibilityGuardBlocksInstallerFiles()
    {
        var policy = new SafetyPolicy
        {
            RecentFileGuardHours = 0,
            CompatibilityModeEnabled = true,
            CompatibilityInstallerGuardDays = 14
        };
        policy.RetentionDaysByCategory[Categories.UserTemp.Id] = 0;

        var tempRoot = Path.Combine(Path.GetTempPath(), "CleanerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "setup.msi");
        File.WriteAllText(filePath, "data");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        policy.AddAllowlist("provider", tempRoot);

        try
        {
            var finding = new Finding(
                "id",
                Categories.UserTemp.Id,
                "provider",
                Path.GetPathRoot(filePath) ?? string.Empty,
                filePath,
                new FileInfo(filePath).Length,
                File.GetLastWriteTimeUtc(filePath),
                "High",
                "Test",
                false,
                false,
                CleanupActions.RecycleFile);

            var result = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);

            Assert.False(result.Ok);
            Assert.Equal(CleanupOutcomeCategories.SkippedCompatibility, result.ReasonCategory);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void ValidateFindingForCleanup_AllowsRecycleBinRoot()
    {
        var policy = new SafetyPolicy();
        const string recycleProviderId = "provider.recycle-bin";
        policy.AddAllowlist(recycleProviderId, @"Z:\");

        var finding = new Finding(
            "id",
            Categories.RecycleBin.Id,
            recycleProviderId,
            @"Z:\",
            @"Z:\",
            1024,
            null,
            "High",
            "Recycle Bin contains items.",
            false,
            false,
            CleanupActions.EmptyRecycleBin);

        var result = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);

        Assert.True(result.Ok);
    }

    [Fact]
    public void ValidateFindingForCleanup_BlocksRecycleBinPathMismatch()
    {
        var policy = new SafetyPolicy();
        const string recycleProviderId = "provider.recycle-bin";
        policy.AddAllowlist(recycleProviderId, @"Z:\");

        var finding = new Finding(
            "id",
            Categories.RecycleBin.Id,
            recycleProviderId,
            @"Z:\",
            @"Z:\Other",
            1024,
            null,
            "High",
            "Recycle Bin contains items.",
            false,
            false,
            CleanupActions.EmptyRecycleBin);

        var result = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);

        Assert.False(result.Ok);
        Assert.Equal(CleanupOutcomeCategories.SkippedSafetyRecheck, result.ReasonCategory);
    }

    [Fact]
    public void ValidateFindingForCleanup_BlocksMissingPath()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider", @"C:\Temp");

        var finding = new Finding(
            "id",
            Categories.UserTemp.Id,
            "provider",
            @"C:\",
            @"C:\Temp\missing.tmp",
            10,
            DateTime.UtcNow.AddDays(-10),
            "High",
            "Test",
            false,
            false,
            CleanupActions.RecycleFile);

        var result = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);

        Assert.False(result.Ok);
        Assert.Equal(CleanupOutcomeCategories.SkippedSafetyRecheck, result.ReasonCategory);
    }

    [Fact]
    public void ValidateFindingForCleanup_BlocksReparsePointDirectory()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider", @"C:\Temp");

        var root = Path.Combine(Path.GetTempPath(), "CleanerTests", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");

        Directory.CreateDirectory(target);

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }

            var finding = new Finding(
                "id",
                Categories.UserTemp.Id,
                "provider",
                Path.GetPathRoot(link) ?? string.Empty,
                link,
                0,
                DateTime.UtcNow.AddDays(-10),
                "High",
                "Test",
                false,
                false,
                CleanupActions.RecycleFile);

            var result = SafetyValidator.ValidateFindingForCleanupDetailed(finding, policy);

            Assert.False(result.Ok);
            Assert.Equal(CleanupOutcomeCategories.SkippedSafetyRecheck, result.ReasonCategory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
