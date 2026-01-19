using Cleaner.Core.Rules;
using Cleaner.Core.Safety;
using Xunit;

namespace Cleaner.Tests.Safety;

public sealed class TempPathRulesTests
{
    [Fact]
    public void IsSafeUserTempRoot_RequiresTempSegment()
    {
        var policy = new SafetyPolicy();

        Assert.False(TempPathRules.IsSafeUserTempRoot(@"C:\Users\Alice\AppData\Local\Cache", policy));
        Assert.True(TempPathRules.IsSafeUserTempRoot(@"C:\Users\Alice\AppData\Local\Temp", policy));
    }

    [Fact]
    public void IsSafeUserTempRoot_DeniesProtectedPaths()
    {
        var policy = new SafetyPolicy();

        Assert.False(TempPathRules.IsSafeUserTempRoot(@"C:\Users\Alice\Documents\Temp", policy));
    }

    [Fact]
    public void IsSafeSystemTempRoot_AllowsWindowsTempOnly()
    {
        var policy = new SafetyPolicy();
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            Assert.False(TempPathRules.IsSafeSystemTempRoot(@"C:\Windows\Temp", policy));
            return;
        }

        var windowsTemp = Path.Combine(windowsDir, "Temp");
        var root = Path.GetPathRoot(windowsTemp) ?? string.Empty;
        var nonWindowsTemp = Path.Combine(root, "Temp");

        Assert.True(TempPathRules.IsSafeSystemTempRoot(windowsTemp, policy));
        Assert.False(TempPathRules.IsSafeSystemTempRoot(nonWindowsTemp, policy));
    }
}
