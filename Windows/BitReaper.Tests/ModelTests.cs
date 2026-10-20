using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BitReaper.Model;
using Xunit;

namespace BitReaper.Tests;

/// <summary>A throwaway folder under %TEMP%; everything the tests create or delete lives inside it.</summary>
public sealed class Sandbox : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "BitReaperTests", Guid.NewGuid().ToString("N"));

    public Sandbox() => Directory.CreateDirectory(Root);

    public string File(string relative, int bytes)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[bytes];
        new Random(bytes).NextBytes(data);
        System.IO.File.WriteAllBytes(path, data);
        return path;
    }

    public string Folder(string relative) => Directory.CreateDirectory(Path.Combine(Root, relative)).FullName;

    public void Dispose()
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            try { System.IO.File.SetAttributes(entry, FileAttributes.Normal); }
            catch (Exception) { /* best effort */ }
        }
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception) { /* best effort */ }
    }
}

public class ScannerTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr security);

    [Fact]
    public void ScansSizesCountsAndSortsLargestFirst()
    {
        using var sandbox = new Sandbox();
        sandbox.File(@"big\a.bin", 300_000);
        sandbox.File(@"big\b.bin", 200_000);
        sandbox.File(@"small\c.bin", 10_000);
        sandbox.File("d.bin", 50_000);

        var root = new Scanner().Scan(sandbox.Root);

        Assert.NotNull(root);
        Assert.Equal(sandbox.Root, root!.Name);
        Assert.Equal(new[] { "big", "d.bin", "small" }, root.Children.Select(c => c.Name));
        var big = root.Children[0];
        Assert.True(big.IsDirectory);
        Assert.Equal(2, big.ItemCount);
        Assert.Equal(6, root.ItemCount);   // 2 folders + 4 files
        Assert.True(big.Size >= 500_000, $"physical size {big.Size} should cover the data");
        Assert.True(big.Size < 500_000 + 2 * 65536, $"physical size {big.Size} should be close to the data");
        Assert.Equal(root.Children.Sum(c => c.Size), root.Size);
        Assert.Equal(Path.Combine(sandbox.Root, "big", "a.bin"), big.Children[0].Path);
    }

    [Fact]
    public void CountsHardLinkedFilesOnce()
    {
        using var sandbox = new Sandbox();
        string original = sandbox.File(@"one\data.bin", 400_000);
        sandbox.Folder("two");
        Assert.True(CreateHardLink(Path.Combine(sandbox.Root, "two", "link.bin"), original, IntPtr.Zero));

        var deduped = new Scanner(dedupeHardLinks: true).Scan(sandbox.Root)!;
        var counted = new Scanner(dedupeHardLinks: false).Scan(sandbox.Root)!;

        Assert.True(counted.Size >= 800_000);
        Assert.True(deduped.Size >= 400_000 && deduped.Size < 800_000, $"deduped size {deduped.Size}");
    }

    [Fact]
    public void ListsJunctionsWithoutFollowingThem()
    {
        using var sandbox = new Sandbox();
        sandbox.File(@"target\payload.bin", 600_000);
        string junction = Path.Combine(sandbox.Root, "shortcut");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{Path.Combine(sandbox.Root, "target")}\"")
        {
            CreateNoWindow = true, UseShellExecute = false,
        })!;
        mklink.WaitForExit();
        Assert.True(Directory.Exists(junction));

        var root = new Scanner().Scan(sandbox.Root)!;
        var link = root.Children.Single(c => c.Name == "shortcut");

        Assert.True(link.IsLink);
        Assert.Equal(0, link.Size);
        Assert.Empty(link.Children);
        Assert.True(root.Size < 1_000_000, "the payload must be counted once");
    }

    [Fact]
    public void MissingPathReturnsNull()
    {
        Assert.Null(new Scanner().Scan(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())));
    }

    [Fact]
    public void NormalizesDriveRoots()
    {
        Assert.Equal(@"C:\", Scanner.NormalizeRoot("C:"));
        Assert.Equal(@"C:\", Scanner.NormalizeRoot(@"C:\"));
        Assert.Equal(@"C:\Windows", Scanner.NormalizeRoot(@"C:\Windows\"));
    }
}

public class TreeTests
{
    private static FileNode Tree()
    {
        var root = new FileNode(@"X:\root", null, NodeFlags.Directory);
        var a = new FileNode("a", root, NodeFlags.Directory);
        var b = new FileNode("b.txt", root, NodeFlags.None) { Size = 30 };
        var a1 = new FileNode("a1.bin", a, NodeFlags.None) { Size = 100 };
        var a2 = new FileNode("photo.JPG", a, NodeFlags.None) { Size = 50 };
        a.SetChildren(new List<FileNode> { a2, a1 });
        root.SetChildren(new List<FileNode> { b, a });
        root.FinalizeTree();
        return root;
    }

    [Fact]
    public void FinalizeRollsUpAndSorts()
    {
        var root = Tree();
        Assert.Equal(180, root.Size);
        Assert.Equal(4, root.ItemCount);
        Assert.Equal("a", root.Children[0].Name);
        Assert.Equal("a1.bin", root.Children[0].Children[0].Name);
        Assert.Equal(@"X:\root\a\photo.JPG", root.Children[0].Children[1].Path);
        Assert.Equal("jpg", root.Children[0].Children[1].Extension);
    }

    [Fact]
    public void DetachAndAttachKeepTotalsRight()
    {
        var root = Tree();
        var a = root.Children[0];
        var a1 = a.Children[0];
        var a2 = a.Children[1];
        a1.DetachFromParent();
        Assert.Equal(80, root.Size);
        Assert.Equal(3, root.ItemCount);
        a2.DetachFromParent();
        Assert.Equal(30, root.Size);
        Assert.Equal("b.txt", root.Children[0].Name);   // re-sorted: the emptied folder drops below
        a.Attach(a1);
        a.Attach(a2);
        Assert.Equal(180, root.Size);
        Assert.Equal(4, root.ItemCount);
        Assert.Equal("a", root.Children[0].Name);
    }

    [Fact]
    public void LargestFilesAndSearch()
    {
        var root = Tree();
        Assert.Equal(new[] { "a1.bin", "photo.JPG" }, TreeQuery.LargestFiles(root, 2).Select(n => n.Name));
        Assert.Equal(new[] { "photo.JPG" }, TreeQuery.Search("photo", root, 10).Select(n => n.Name));
        Assert.Equal(new[] { "a", "a1.bin" }, TreeQuery.Search("A", root, 10).Select(n => n.Name));
    }
}

public class CollectorTests
{
    [Fact]
    public void AbsorbsDescendantsAndRefusesTheRoot()
    {
        var root = new FileNode(@"X:\root", null, NodeFlags.Directory);
        var folder = new FileNode("folder", root, NodeFlags.Directory);
        var inner = new FileNode("inner.bin", folder, NodeFlags.None) { Size = 10 };
        folder.SetChildren(new List<FileNode> { inner });
        root.SetChildren(new List<FileNode> { folder });
        root.FinalizeTree();
        var collector = new Collector();

        collector.Add(new[] { inner }, root);
        Assert.True(collector.Contains(inner));

        collector.Add(new[] { folder }, root);
        Assert.False(collector.Contains(inner));
        Assert.True(collector.Contains(folder));
        Assert.True(collector.IsCovered(inner));
        Assert.Single(collector.Items);

        var refusals = collector.Add(new[] { root }, root);
        Assert.Single(refusals);
        Assert.Contains("folder you scanned", refusals[0]);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\pagefile.sys")]
    [InlineData(@"C:\$Recycle.Bin")]
    [InlineData(@"C:\System Volume Information")]
    public void ProtectsSystemLocations(string path)
    {
        Assert.NotNull(SystemAccess.ProtectionReason(path));
    }

    [Fact]
    public void ProtectsTheProfileButNotWhatsInside()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.NotNull(SystemAccess.ProtectionReason(home));
        Assert.NotNull(SystemAccess.ProtectionReason(Path.Combine(home, "Downloads")));
        Assert.NotNull(SystemAccess.ProtectionReason(Path.Combine(home, "AppData", "Local")));
        Assert.Null(SystemAccess.ProtectionReason(Path.Combine(home, "Downloads", "old-installer.exe")));
        Assert.Null(SystemAccess.ProtectionReason(Path.Combine(home, "AppData", "Local", "SomeCache")));
        Assert.Null(SystemAccess.ProtectionReason(Path.Combine(Path.GetTempPath(), "x")));
    }
}

public class DeleterTests
{
    [Fact]
    public void DeletesPermanentlyIncludingReadOnlyItems()
    {
        using var sandbox = new Sandbox();
        string file = sandbox.File(@"stuff\locked.bin", 1000);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        string folder = Path.Combine(sandbox.Root, "stuff");
        new DirectoryInfo(folder).Attributes |= FileAttributes.ReadOnly;
        string loose = sandbox.File("loose.bin", 10);

        var results = Deleter.Delete(new[] { folder, loose }, DeleteMode.Permanent, IntPtr.Zero, _ => { });

        Assert.All(results, r => Assert.Equal(DeleteResultKind.Removed, r.Kind));
        Assert.False(Directory.Exists(folder));
        Assert.False(File.Exists(loose));
    }

    [Fact]
    public void RemovesJunctionsButNotTheirTarget()
    {
        using var sandbox = new Sandbox();
        string payload = sandbox.File(@"target\keep.bin", 1000);
        string holder = sandbox.Folder("holder");
        string junction = Path.Combine(holder, "link");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{Path.Combine(sandbox.Root, "target")}\"")
        {
            CreateNoWindow = true, UseShellExecute = false,
        })!.WaitForExit();

        var results = Deleter.Delete(new[] { holder }, DeleteMode.Permanent, IntPtr.Zero, _ => { });

        Assert.Equal(DeleteResultKind.Removed, results[0].Kind);
        Assert.False(Directory.Exists(holder));
        Assert.True(File.Exists(payload), "the junction's target must survive");
    }

    [Fact]
    public void RecyclesAndPutsBack()
    {
        using var sandbox = new Sandbox();
        string file = sandbox.File(@"recycle\me.bin", 2048);
        string folder = Path.Combine(sandbox.Root, "recycle");

        var results = Deleter.Delete(new[] { folder }, DeleteMode.RecycleBin, IntPtr.Zero, _ => { });

        Assert.Equal(DeleteResultKind.Recycled, results[0].Kind);
        Assert.False(Directory.Exists(folder));
        var item = results[0].Recycled;
        Assert.NotNull(item);
        Assert.Equal(folder, item!.OriginalPath, ignoreCase: true);

        RecycleBin.PutBack(item);

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(item.InfoPath));
    }

    [Fact]
    public void MissingItemsCountAsRemoved()
    {
        string ghost = Path.Combine(Path.GetTempPath(), "BitReaperTests", "ghost-" + Guid.NewGuid());
        var results = Deleter.Delete(new[] { ghost }, DeleteMode.RecycleBin, IntPtr.Zero, _ => { });
        Assert.Equal(DeleteResultKind.Removed, results[0].Kind);
    }

    [Theory]
    [InlineData(true, null, null, true)]      // its $I record was found
    [InlineData(false, 4L, 5L, true)]         // no record, but the bin gained an item
    [InlineData(false, 4L, 4L, false)]        // the bin didn't change: Windows deleted it outright
    [InlineData(false, null, null, false)]    // no Recycle Bin on the drive (removable, network)
    [InlineData(false, 4L, null, false)]      // unknown afterwards: never claim it's recoverable
    public void OnlyItemsShownToBeInTheBinCountAsRecycled(bool foundRecord, long? before, long? after, bool expected)
    {
        Assert.Equal(expected, RecycleBin.WentToBin(foundRecord, before, after));
    }
}
