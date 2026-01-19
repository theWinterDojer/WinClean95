namespace Cleaner.Core.Models;

public static class Categories
{
    public static readonly Category UserTemp = new(
        "temp.user",
        "User Temp Files",
        "Safe temp files in the current user profile.",
        true,
        "Low");

    public static readonly Category SystemTemp = new(
        "temp.system",
        "System Temp Files",
        "Safe temp files in Windows Temp.",
        false,
        "Medium");

    public static readonly Category WindowsUpdateCache = new(
        "cache.windows-update",
        "Windows Update Cache",
        "Safe Windows Update cache files.",
        false,
        "Medium");

    public static readonly Category ThumbnailCache = new(
        "cache.thumbnails",
        "Thumbnail Cache",
        "Thumbnail cache database files.",
        true,
        "Low");

    public static readonly Category DirectXShaderCache = new(
        "cache.directx-shader",
        "DirectX Shader Cache",
        "DirectX shader cache files (safe to rebuild).",
        true,
        "Low");

    public static readonly Category BrowserCache = new(
        "cache.browser",
        "Browser Cache",
        "Browser cache files (requires browser closed).",
        false,
        "Medium");

    public static readonly Category WerReports = new(
        "reports.wer",
        "Windows Error Reports",
        "Crash and error report archives and queues.",
        true,
        "Low");

    public static readonly Category RecycleBin = new(
        "recyclebin",
        "Recycle Bin",
        "Empty Recycle Bin (irreversible).",
        false,
        "Medium");

    public static readonly IReadOnlyList<Category> All = new[]
    {
        UserTemp,
        SystemTemp,
        WindowsUpdateCache,
        ThumbnailCache,
        DirectXShaderCache,
        BrowserCache,
        WerReports,
        RecycleBin
    };
}
