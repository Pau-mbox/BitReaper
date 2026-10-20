using System.Security.Principal;
using System.Text;
using BitReaper.Util;

namespace BitReaper.Model;

public enum DeleteResultKind
{
    Removed,
    Recycled,
    /// <summary>Recycling was asked for, but Windows deleted the item permanently (after its own warning).</summary>
    RemovedInsteadOfRecycled,
    Failed,
}

/// <param name="Recycled">Where the item now sits in the Recycle Bin, when it could be found (for Put Back).</param>
public sealed record DeleteResult(DeleteResultKind Kind, RecycledItem? Recycled = null, string? Reason = null)
{
    public static readonly DeleteResult Removed = new(DeleteResultKind.Removed);
    public static DeleteResult Failed(string reason) => new(DeleteResultKind.Failed, Reason: reason);
}

/// <summary>An item in the Recycle Bin: its data ($R…) and the record of where it came from ($I…).</summary>
public sealed record RecycledItem(string DataPath, string InfoPath, string OriginalPath);

public static class Deleter
{
    /// <summary>
    /// Deletes (or recycles) each path in turn. Runs off the UI thread; recycling goes through the shell,
    /// so call it from an STA thread. <paramref name="owner"/> parents the one dialog Windows may show:
    /// the warning for an item too big for the Recycle Bin, which would otherwise be destroyed.
    /// </summary>
    public static List<DeleteResult> Delete(IReadOnlyList<string> paths, DeleteMode mode, IntPtr owner, Action<int> progress)
    {
        var results = new List<DeleteResult>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            try
            {
                results.Add(mode == DeleteMode.RecycleBin ? Recycle(paths[i], owner) : RemovePermanently(paths[i]));
            }
            catch (Exception error)
            {
                results.Add(DeleteResult.Failed(Describe(error)));
            }
            progress(i + 1);
        }
        return results;
    }

    // ---- Recycle Bin ----

    private static DeleteResult Recycle(string path, IntPtr owner)
    {
        if (!Exists(path)) return DeleteResult.Removed;
        if (path.Length >= 260)
            return DeleteResult.Failed("The path is too long for the Recycle Bin. Use Delete Immediately instead.");

        var started = System.DateTime.UtcNow;
        string? root = System.IO.Path.GetPathRoot(path);
        long? itemsBefore = root == null ? null : RecycleBin.ItemCount(root);
        var operation = new Native.SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = Native.FO_DELETE,
            pFrom = path + "\0",   // the list is double-null terminated; marshalling adds the second null
            fFlags = Native.FOF_ALLOWUNDO | Native.FOF_NOCONFIRMATION | Native.FOF_SILENT
                     | Native.FOF_NOERRORUI | Native.FOF_WANTNUKEWARNING,
        };
        int code = Native.SHFileOperation(ref operation);
        if (operation.fAnyOperationsAborted != 0) return DeleteResult.Failed("Cancelled.");
        if (code != 0) return DeleteResult.Failed(DescribeShellError(code));
        if (Exists(path)) return DeleteResult.Failed("Windows couldn't move the item to the Recycle Bin.");

        // Windows destroys an item instead, once the user accepts its warning, when it can't go to the
        // Recycle Bin: removable and network drives, items too big for the bin, bins set to delete at once.
        var record = RecycleBin.Find(path, started);
        long? itemsAfter = root == null ? null : RecycleBin.ItemCount(root);
        return RecycleBin.WentToBin(record != null, itemsBefore, itemsAfter)
            ? new DeleteResult(DeleteResultKind.Recycled, record)
            : new DeleteResult(DeleteResultKind.RemovedInsteadOfRecycled);
    }

    // ---- Permanent ----

    /// <summary>
    /// Deleting fails on read-only files and folders; on failure the subtree is made writable and the
    /// deletion retried. Junctions and symbolic links are removed themselves, never what they point to.
    /// </summary>
    private static DeleteResult RemovePermanently(string path)
    {
        if (!Exists(path)) return DeleteResult.Removed;
        try
        {
            DeleteEntry(path);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            if (!Exists(path)) return DeleteResult.Removed;
            MakeWritable(path);
            DeleteEntry(path);
        }
        return DeleteResult.Removed;
    }

    private static void DeleteEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.Delete(path);
        }
        else if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, recursive: false);   // removes the junction or link only
        }
        else
        {
            Directory.Delete(path, recursive: true);    // doesn't follow junctions or links inside
        }
    }

    private static void MakeWritable(string path)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (Exception) { continue; }
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                try { File.SetAttributes(current, attributes & ~FileAttributes.ReadOnly); }
                catch (Exception) { /* the retry reports the real error */ }
            }
            bool realDirectory = (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
            if (!realDirectory) continue;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current, "*", options)) stack.Push(entry);
            }
            catch (Exception) { /* unreadable folder: the retry reports it */ }
        }
    }

    internal static bool Exists(string path) =>
        Native.GetFileAttributes(Native.LongPath(path)) != Native.INVALID_FILE_ATTRIBUTES;

    internal static string Describe(Exception error) => error switch
    {
        UnauthorizedAccessException =>
            "Access denied. The item may be protected by Windows, owned by another user, or need administrator rights.",
        IOException io when (io.HResult & 0xFFFF) is 32 or 33 => "The item is in use by another program.",
        PathTooLongException => "The path is too long.",
        _ => error.Message,
    };

    private static string DescribeShellError(int code) => code switch
    {
        5 or 0x78 => "Access denied. The item may be protected by Windows, owned by another user, or need administrator rights.",
        32 or 33 => "The item is in use by another program.",
        2 or 3 or 0x7C => "The item couldn't be found.",
        0x4C7 => "Cancelled.",
        _ => $"Windows couldn't move the item to the Recycle Bin (error 0x{code:X}).",
    };
}

/// <summary>
/// Finds and restores items in the current user's Recycle Bin ($Recycle.Bin\&lt;SID&gt; on each drive).
/// Each deleted item is stored as $R&lt;id&gt;, next to a $I&lt;id&gt; record holding its original path.
/// </summary>
public static class RecycleBin
{
    private static string? UserFolder(string anyPathOnDrive)
    {
        string? root = System.IO.Path.GetPathRoot(anyPathOnDrive);
        using var identity = WindowsIdentity.GetCurrent();
        string? sid = identity.User?.Value;
        return root == null || sid == null ? null : System.IO.Path.Combine(root, "$Recycle.Bin", sid);
    }

    /// <summary>The most recent Recycle Bin entry for <paramref name="originalPath"/> deleted since <paramref name="sinceUtc"/>.</summary>
    public static RecycledItem? Find(string originalPath, System.DateTime sinceUtc)
    {
        string? folder = UserFolder(originalPath);
        if (folder == null || !Directory.Exists(folder)) return null;
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, MatchType = MatchType.Win32 };
        RecycledItem? best = null;
        long bestTime = long.MinValue;
        try
        {
            foreach (var info in new DirectoryInfo(folder).EnumerateFiles("$I*", options))
            {
                if (info.LastWriteTimeUtc < sinceUtc.AddSeconds(-5)) continue;
                if (ReadRecord(info.FullName) is not { } record) continue;
                if (!record.Path.Equals(originalPath, StringComparison.OrdinalIgnoreCase) || record.Deleted < bestTime) continue;
                string data = System.IO.Path.Combine(folder, "$R" + info.Name[2..]);
                if (!Deleter.Exists(data)) continue;
                best = new RecycledItem(data, info.FullName, originalPath);
                bestTime = record.Deleted;
            }
        }
        catch (Exception)
        {
            return best;
        }
        return best;
    }

    /// <summary>Moves a recycled item back to where it was. Throws with a readable message on failure.</summary>
    public static void PutBack(RecycledItem item)
    {
        if (Deleter.Exists(item.OriginalPath))
            throw new IOException($"An item named “{System.IO.Path.GetFileName(item.OriginalPath)}” already exists there.");
        if (!Deleter.Exists(item.DataPath))
            throw new IOException("The item is no longer in the Recycle Bin.");
        string? parent = System.IO.Path.GetDirectoryName(item.OriginalPath);
        if (parent != null) Directory.CreateDirectory(parent);
        if (Directory.Exists(item.DataPath))
            Directory.Move(item.DataPath, item.OriginalPath);
        else
            File.Move(item.DataPath, item.OriginalPath);
        try { File.Delete(item.InfoPath); }
        catch (Exception) { /* a stale record only shows as a broken entry in the Recycle Bin */ }
    }

    /// <summary>Items in the Recycle Bin of the drive holding <paramref name="root"/>, or null when unknown.</summary>
    public static long? ItemCount(string root)
    {
        var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        return Native.SHQueryRecycleBin(root, ref info) == 0 ? info.i64NumItems : null;
    }

    /// <summary>
    /// Whether a successful shell delete really put the item in the Recycle Bin: its record was found, or the
    /// bin gained an item. When neither can be shown, the item is reported as deleted permanently, never as
    /// recoverable when it may not be.
    /// </summary>
    internal static bool WentToBin(bool foundRecord, long? itemsBefore, long? itemsAfter) =>
        foundRecord || (itemsBefore is { } before && itemsAfter is { } after && after > before);

    /// <summary>Bytes in the Recycle Bin of the drive holding <paramref name="root"/>, or null when unknown.</summary>
    public static long? Size(string root)
    {
        var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        return Native.SHQueryRecycleBin(root, ref info) == 0 ? info.i64Size : null;
    }

    private static (string Path, long Deleted)? ReadRecord(string file)
    {
        try
        {
            byte[] data = File.ReadAllBytes(file);
            if (data.Length < 24) return null;
            long version = BitConverter.ToInt64(data, 0);
            long deleted = BitConverter.ToInt64(data, 16);
            string path;
            if (version == 2)
            {
                int length = BitConverter.ToInt32(data, 24);
                if (28 + length * 2 > data.Length) return null;
                path = Encoding.Unicode.GetString(data, 28, length * 2);
            }
            else if (version == 1)
            {
                path = Encoding.Unicode.GetString(data, 24, Math.Min(520, data.Length - 24));
            }
            else
            {
                return null;
            }
            int nul = path.IndexOf('\0');
            return (nul >= 0 ? path[..nul] : path, deleted);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
