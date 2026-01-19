using System;
using System.ComponentModel;
using System.IO;
using Cleaner.Core.Models;

namespace Cleaner.Windows.Services;

internal static class DeleteFailureClassifier
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    internal const int ErrorCancelled = 1223;

    public static CleanupOutcome BuildFailureOutcome(Finding finding, int errorCode)
    {
        var message = errorCode switch
        {
            ErrorSharingViolation or ErrorLockViolation => "Locked or in use; skipped.",
            ErrorAccessDenied => "Access denied; skipped.",
            ErrorFileNotFound or ErrorPathNotFound => "Path not found.",
            ErrorCancelled => "Delete canceled; skipped.",
            _ => $"Unable to delete: {new Win32Exception(errorCode).Message}"
        };

        var category = errorCode switch
        {
            ErrorSharingViolation or ErrorLockViolation => CleanupOutcomeCategories.SkippedLocked,
            ErrorAccessDenied => CleanupOutcomeCategories.SkippedAccessDenied,
            _ => CleanupOutcomeCategories.SkippedOther
        };

        return new CleanupOutcome(
            finding.Id,
            false,
            message,
            0,
            category);
    }

    public static int NormalizeErrorCode(string path, int errorCode)
    {
        if (IsKnownErrorCode(errorCode))
        {
            return errorCode;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return ErrorFileNotFound;
        }

        if (File.Exists(path))
        {
            try
            {
                using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return errorCode;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrorAccessDenied;
            }
            catch (IOException)
            {
                return ErrorSharingViolation;
            }
            catch
            {
                return errorCode;
            }
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Any();
            return errorCode;
        }
        catch (UnauthorizedAccessException)
        {
            return ErrorAccessDenied;
        }
        catch (IOException)
        {
            return ErrorSharingViolation;
        }
        catch
        {
            return errorCode;
        }
    }

    public static CleanupOutcome BuildLockedOutcome(Finding finding, Exception ex)
    {
        return new CleanupOutcome(
            finding.Id,
            false,
            $"Locked or in use: {ex.Message}",
            0,
            CleanupOutcomeCategories.SkippedLocked);
    }

    public static CleanupOutcome BuildAccessDeniedOutcome(Finding finding, Exception ex)
    {
        return new CleanupOutcome(
            finding.Id,
            false,
            $"Access denied: {ex.Message}",
            0,
            CleanupOutcomeCategories.SkippedAccessDenied);
    }

    public static CleanupOutcome BuildNotFoundOutcome(Finding finding)
    {
        return new CleanupOutcome(
            finding.Id,
            false,
            "Path not found.",
            0,
            CleanupOutcomeCategories.SkippedOther);
    }

    public static bool IsDirectoryNotEmpty(IOException ex)
    {
        const int DirectoryNotEmpty = 145;
        var code = ex.HResult & 0xFFFF;
        return code == DirectoryNotEmpty;
    }

    private static bool IsKnownErrorCode(int errorCode)
    {
        return errorCode == ErrorAccessDenied
               || errorCode == ErrorSharingViolation
               || errorCode == ErrorLockViolation
               || errorCode == ErrorFileNotFound
               || errorCode == ErrorPathNotFound
               || errorCode == ErrorCancelled;
    }
}
