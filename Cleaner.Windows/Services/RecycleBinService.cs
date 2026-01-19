using System.Runtime.InteropServices;
using Cleaner.Windows.Interop;

namespace Cleaner.Windows.Services;

public sealed class RecycleBinService
{
    public (long SizeBytes, long ItemCount) Query(string? rootPath)
    {
        var info = new RecycleBinInterop.SHQUERYRBINFO
        {
            cbSize = (uint)Marshal.SizeOf<RecycleBinInterop.SHQUERYRBINFO>()
        };

        var result = RecycleBinInterop.SHQueryRecycleBin(rootPath, ref info);
        if (result != 0)
        {
            throw new InvalidOperationException($"SHQueryRecycleBin failed with HRESULT 0x{result:X8}");
        }

        return (info.i64Size, info.i64NumItems);
    }

    public void Empty(string? rootPath)
    {
        var flags = RecycleBinInterop.SHERB_NOCONFIRMATION
                    | RecycleBinInterop.SHERB_NOPROGRESSUI
                    | RecycleBinInterop.SHERB_NOSOUND;

        var result = RecycleBinInterop.SHEmptyRecycleBin(IntPtr.Zero, rootPath, flags);
        if (result != 0)
        {
            throw new InvalidOperationException($"SHEmptyRecycleBin failed with HRESULT 0x{result:X8}");
        }
    }
}
