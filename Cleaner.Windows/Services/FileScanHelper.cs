using Cleaner.Core.Safety;

namespace Cleaner.Windows.Services;

public static class FileScanHelper
{
    public sealed record Entry(string Path, bool IsDirectory);

    public static IEnumerable<Entry> EnumerateEntries(string root, SafetyPolicy policy, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            IEnumerable<string> directories = Array.Empty<string>();
            IEnumerable<string> files = Array.Empty<string>();
            var directoriesOk = false;
            var filesOk = false;

            try
            {
                directories = Directory.EnumerateDirectories(current);
                directoriesOk = true;
            }
            catch (Exception ex)
            {
                policy.ReportWarning($"Skip directory list: {current} ({ex.Message})");
            }

            try
            {
                files = Directory.EnumerateFiles(current);
                filesOk = true;
            }
            catch (Exception ex)
            {
                policy.ReportWarning($"Skip file list: {current} ({ex.Message})");
            }

            var hasChild = false;

            foreach (var file in files)
            {
                hasChild = true;
                yield return new Entry(file, false);
            }

            foreach (var dir in directories)
            {
                hasChild = true;
                if (IsReparsePoint(dir, policy))
                {
                    continue;
                }

                stack.Push(dir);
            }

            var canDetermineEmpty = directoriesOk && filesOk;
            if (!hasChild && canDetermineEmpty && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                yield return new Entry(current, true);
            }
        }
    }

    private static bool IsReparsePoint(string path, SafetyPolicy policy)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex)
        {
            policy.ReportWarning($"Skip reparse check: {path} ({ex.Message})");
            return true;
        }
    }
}
