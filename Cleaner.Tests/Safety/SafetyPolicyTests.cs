using Cleaner.Core.Safety;
using Xunit;

namespace Cleaner.Tests.Safety;

public sealed class SafetyPolicyTests
{
    [Fact]
    public void ResetForScan_ClearsAllowlistAndWarnings()
    {
        var policy = new SafetyPolicy();
        policy.AddAllowlist("provider", @"C:\Temp\");
        policy.ReportWarning("warning");

        policy.ResetForScan();

        Assert.Empty(policy.AllowlistRootsByProvider);
        Assert.Empty(policy.Warnings);
    }

    [Fact]
    public void ProtectedPathPrefixes_IncludesDefaultAndDynamicRoots()
    {
        var policy = new SafetyPolicy();

        Assert.Contains(@"C:\Windows\System32\", policy.ProtectedPathPrefixes);
        Assert.Contains(@"C:\Users\*\Documents\", policy.ProtectedPathPrefixes);

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDir))
        {
            var dynamicSystem32 = Path.Combine(windowsDir, "System32") + Path.DirectorySeparatorChar;
            Assert.Contains(dynamicSystem32, policy.ProtectedPathPrefixes);
        }
    }
}
