using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Kleaner.Executor;

/// <summary>
/// 移动路径的句柄锚定：以不含 FILE_SHARE_DELETE 的句柄钉住待移动路径的全部现有祖先目录（至卷根），
/// 使检查与移动之间任何进程都无法改名、删除这些目录或经由父目录的 DELETE_CHILD 移除其子项。
/// 以 FILE_FLAG_OPEN_REPARSE_POINT 打开并按句柄复验属性：与被钉住对象一一对应，竞争中被替换为
/// junction 时要么在打开前就检出并拒绝，要么因句柄存续而无法替换，不存在把 junction 当目录锚定的情况。
/// </summary>
internal static class QuarantineAnchors
{
    private const uint FileListDirectory = 0x1;
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareReadWrite = 0x3;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeReparsePoint = 0x400;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// <summary>打开并复验单个目录；句柄存续期间其他进程对它的写入族打开（改名、删除、DELETE_CHILD）都会被拒绝。</summary>
    internal static SafeFileHandle OpenAnchored(string directory)
    {
        var handle = CreateFile(directory, FileListDirectory | FileReadAttributes, FileShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"无法锚定目录 {directory}（Win32 错误 {Marshal.GetLastWin32Error()}）");
        if (!GetFileInformationByHandle(handle, out var info))
        {
            handle.Dispose();
            throw new IOException($"无法读取目录属性 {directory}（Win32 错误 {Marshal.GetLastWin32Error()}）");
        }
        if ((info.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            handle.Dispose();
            throw new InvalidDataException($"路径包含 reparse point，已拒绝操作：{directory}");
        }
        return handle;
    }

    /// <summary>锚定路径的全部现有祖先目录（自父目录至卷根）；任一级打不开或含 reparse point 即整体失败。</summary>
    internal static List<SafeFileHandle> AnchorAncestors(string path)
    {
        var handles = new List<SafeFileHandle>();
        try
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(path))!;
            while (true)
            {
                handles.Add(OpenAnchored(current));
                var parent = Path.GetDirectoryName(current);
                if (parent is null) break; // 卷根已锚定。
                current = parent;
            }
            return handles;
        }
        catch
        {
            DisposeAll(handles);
            throw;
        }
    }

    internal static void DisposeAll(List<SafeFileHandle> handles)
    {
        foreach (var handle in handles)
            handle.Dispose();
        handles.Clear();
    }
}
