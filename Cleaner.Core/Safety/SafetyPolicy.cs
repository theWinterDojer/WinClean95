using Cleaner.Core.Models;

namespace Cleaner.Core.Safety;

public sealed class SafetyPolicy
{
    public TimeSpan MinAgeDefault { get; } = TimeSpan.FromHours(24);
    public int RecentFileGuardHours { get; set; } = 48;
    public bool CompatibilityModeEnabled { get; set; } = true;
    public int CompatibilityInstallerGuardDays { get; set; } = 14;
    public Dictionary<string, int> RetentionDaysByCategory { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temp.user"] = 7,
        ["temp.system"] = 7,
        ["cache.windows-update"] = 7,
        ["cache.thumbnails"] = 7,
        ["cache.directx-shader"] = 7,
        ["cache.browser"] = 7,
        ["reports.wer"] = 7,
        ["logs.system"] = 7,
        ["recyclebin"] = 7
    };

    public HashSet<string> ProtectedPathPrefixes { get; } = BuildProtectedPathPrefixes();

    public Dictionary<string, List<string>> AllowlistRootsByProvider { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; } = new();

    public void AddAllowlist(string providerId, params string[] roots)
    {
        if (!AllowlistRootsByProvider.TryGetValue(providerId, out var list))
        {
            list = new List<string>();
            AllowlistRootsByProvider[providerId] = list;
        }

        foreach (var root in roots)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                list.Add(root);
            }
        }
    }

    public void ReportWarning(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Warnings.Add(message);
        }
    }

    public void ClearWarnings()
    {
        Warnings.Clear();
    }

    public void ResetForScan()
    {
        AllowlistRootsByProvider.Clear();
        Warnings.Clear();
    }

    private static HashSet<string> BuildProtectedPathPrefixes()
    {
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDefaultProtectedPrefixes(prefixes);
        AddDynamicProtectedPrefixes(prefixes);

        return prefixes;
    }

    private static void AddDefaultProtectedPrefixes(ISet<string> prefixes)
    {
        prefixes.Add(@"C:\Windows\System32\");
        prefixes.Add(@"C:\Windows\WinSxS\");
        prefixes.Add(@"C:\Program Files\");
        prefixes.Add(@"C:\Program Files (x86)\");
        prefixes.Add(@"C:\Users\*\Documents\");
        prefixes.Add(@"C:\Users\*\Desktop\");
    }

    private static void AddDynamicProtectedPrefixes(ISet<string> prefixes)
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDir))
        {
            prefixes.Add(Path.Combine(windowsDir, "System32") + Path.DirectorySeparatorChar);
            prefixes.Add(Path.Combine(windowsDir, "WinSxS") + Path.DirectorySeparatorChar);
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            prefixes.Add(EnsureTrailingSeparator(programFiles));
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            prefixes.Add(EnsureTrailingSeparator(programFilesX86));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var profilesRoot = Directory.GetParent(userProfile)?.FullName;
            if (!string.IsNullOrWhiteSpace(profilesRoot))
            {
                prefixes.Add(EnsureTrailingSeparator(Path.Combine(profilesRoot, "*", "Documents")));
                prefixes.Add(EnsureTrailingSeparator(Path.Combine(profilesRoot, "*", "Desktop")));
            }

            prefixes.Add(EnsureTrailingSeparator(Path.Combine(userProfile, "Documents")));
            prefixes.Add(EnsureTrailingSeparator(Path.Combine(userProfile, "Desktop")));
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
