using System.Threading;

namespace BitReaper.Model;

public sealed record ScanTarget(string Path, string DisplayName, string? VolumeId);

public sealed record DeletionFailure(string Path, string Reason);

public sealed class DeletionOutcome
{
    public sealed record RecycleRecord(FileNode Node, string ParentPath, RecycledItem Item);

    public required DeleteMode Mode { get; init; }
    public required int Count { get; init; }
    public required long Bytes { get; init; }
    public required int FailureCount { get; init; }
    /// <summary>Items Windows deleted permanently although the Recycle Bin was asked for.</summary>
    public int PermanentCount { get; init; }
    public required List<RecycleRecord> Records { get; init; }

    public int RecycledCount => Mode == DeleteMode.RecycleBin ? Count - PermanentCount : 0;

    public bool CanUndo => Mode == DeleteMode.RecycleBin && Records.Count > 0;
}

public enum ViewMode
{
    Tree,
    Largest,
}

public abstract record DeletionPhase
{
    public sealed record Idle : DeletionPhase;
    public sealed record Countdown(int Seconds) : DeletionPhase;
    public sealed record Deleting(int Done, int Total) : DeletionPhase;
    public static readonly DeletionPhase IdlePhase = new Idle();
}

[Flags]
public enum SessionChange
{
    None = 0,
    Focus = 1 << 0,
    Tree = 1 << 1,
    Selection = 1 << 2,
    Collector = 1 << 3,
    Flat = 1 << 4,
    Deletion = 1 << 5,
    Outcome = 1 << 6,
    Toast = 1 << 7,
    Busy = 1 << 8,
    Reveal = 1 << 9,
    History = 1 << 10,
    ViewMode = 1 << 11,
    SearchFocus = 1 << 12,
    Snapshots = 1 << 13,
}

/// <summary>
/// The results of one scan: the tree, where the user is in it, and the collector.
/// Lives on the UI thread; background work (queries, rescans, deletion) reports back through async/await.
/// </summary>
public sealed class ScanSession
{
    public const int LargestLimit = 500;
    public const int SearchLimit = 2000;

    private readonly AppModel _app;

    public FileNode Root { get; private set; }
    public ScanTarget Target { get; }
    public DateTime ScanDate { get; } = DateTime.Now;
    public TimeSpan ScanDuration { get; }
    public int UnreadableCount { get; }
    public Collector Collector { get; } = new();

    public event Action<SessionChange>? Changed;

    // Navigation
    public FileNode Focus { get; private set; }
    private readonly List<FileNode> _backStack = new();
    private readonly List<FileNode> _forwardStack = new();
    private List<FileNode> _selection = new();
    private Dictionary<FileNode, int> _colorSlots = new(ReferenceEqualityComparer.Instance);
    public FileNode? RevealTarget { get; private set; }

    // Flat lists (search results / largest files)
    private ViewMode _viewMode = ViewMode.Tree;
    private string _searchText = "";
    public List<FileNode>? FlatResults { get; private set; }
    public bool IsQuerying { get; private set; }
    private CancellationTokenSource? _query;
    private readonly List<Task> _queryTasks = new();

    // Rescanning
    public bool IsRescanning { get; private set; }

    // Deletion
    public bool CollectorExpanded { get; private set; }
    public DeletionPhase Deletion { get; private set; } = DeletionPhase.IdlePhase;
    public DeletionOutcome? LastOutcome { get; private set; }
    public List<DeletionFailure>? FailureReport { get; private set; }
    public bool DeleteSheetRequested { get; private set; }
    private CancellationTokenSource? _countdown;

    // Messages
    public string? Toast { get; private set; }
    private CancellationTokenSource? _toast;

    public ScanSession(AppModel app, FileNode root, ScanTarget target, int unreadableCount, TimeSpan duration)
    {
        _app = app;
        Root = root;
        Focus = root;
        Target = target;
        UnreadableCount = unreadableCount;
        ScanDuration = duration;
        RecomputeColors();
        Collector.Changed += () => Raise(SessionChange.Collector);
    }

    private void Raise(SessionChange change) => Changed?.Invoke(change);

    // ---- Names & lookup ----

    public string DisplayName(FileNode node) => ReferenceEquals(node, Root) ? Target.DisplayName : node.Name;

    public VolumeInfo? Volume => Target.VolumeId is { } id ? _app.Volumes.Volume(id) : null;

    /// <summary>Space the drive reports as used but the scan couldn't see (system files, restore points, folders without access).</summary>
    public long? HiddenSpace => Volume is { } volume ? Math.Max(0, volume.Used - Root.Size) : null;

    public bool IsAttached(FileNode node) => ReferenceEquals(node.TopmostAncestor, Root);

    public FileNode? NodeForPath(string path)
    {
        string rootPath = Root.Name;
        if (path.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) return Root;
        string prefix = rootPath.EndsWith('\\') ? rootPath : rootPath + "\\";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var node = Root;
        foreach (var component in path[prefix.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            FileNode? next = null;
            foreach (var child in node.Children)
            {
                if (child.Name.Equals(component, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }
            if (next == null) return null;
            node = next;
        }
        return node;
    }

    /// <summary>Path of <paramref name="node"/> relative to the current folder, e.g. "AppData\Local".</summary>
    public string RelativePath(FileNode? node)
    {
        if (node == null || ReferenceEquals(node, Focus)) return "";
        var components = new List<string>();
        for (var current = node; current != null && !ReferenceEquals(current, Focus); current = current.Parent)
            components.Add(DisplayName(current));
        components.Reverse();
        return string.Join("\\", components);
    }

    /// <summary>The colour slot of the top-level item (relative to the current folder) that contains <paramref name="node"/>.</summary>
    public (int Slot, int Depth)? ColorInfo(FileNode node)
    {
        int depth = 0;
        var current = node;
        while (current.Parent != null && !ReferenceEquals(current.Parent, Focus))
        {
            current = current.Parent;
            depth++;
        }
        if (!ReferenceEquals(current.Parent, Focus) || !_colorSlots.TryGetValue(current, out int slot)) return null;
        return (slot, depth);
    }

    private void RecomputeColors()
    {
        var result = new Dictionary<FileNode, int>(ReferenceEqualityComparer.Instance);
        var children = Focus.Children;
        for (int i = 0; i < children.Count; i++) result[children[i]] = i;
        _colorSlots = result;
    }

    // ---- Selection ----

    public IReadOnlyList<FileNode> Selection => _selection;

    public void SetSelection(IEnumerable<FileNode> nodes)
    {
        var list = nodes.ToList();
        if (list.SequenceEqual(_selection, ReferenceEqualityComparer.Instance)) return;
        _selection = list;
        Raise(SessionChange.Selection);
    }

    public bool SelectionIsCollected => _selection.Count > 0 && _selection.All(Collector.Contains);

    // ---- Navigation ----

    public List<FileNode> Breadcrumb
    {
        get
        {
            var nodes = new List<FileNode>();
            for (var node = Focus; node != null; node = node.Parent) nodes.Add(node);
            nodes.Reverse();
            return nodes;
        }
    }

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public bool CanGoUp => Focus.Parent != null;

    public void Navigate(FileNode node)
    {
        if (!node.IsDirectory || ReferenceEquals(node, Focus) || !IsAttached(node)) return;
        _backStack.Add(Focus);
        _forwardStack.Clear();
        SetFocus(node);
    }

    public void GoBack()
    {
        while (_backStack.Count > 0)
        {
            var previous = Pop(_backStack);
            if (!IsAttached(previous)) continue;
            _forwardStack.Add(Focus);
            SetFocus(previous);
            return;
        }
        Raise(SessionChange.History);
    }

    public void GoForward()
    {
        while (_forwardStack.Count > 0)
        {
            var next = Pop(_forwardStack);
            if (!IsAttached(next)) continue;
            _backStack.Add(Focus);
            SetFocus(next);
            return;
        }
        Raise(SessionChange.History);
    }

    private static FileNode Pop(List<FileNode> stack)
    {
        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }

    public void GoUp()
    {
        if (Focus.Parent is not { } parent) return;
        var child = Focus;
        Navigate(parent);
        Reveal(child);
    }

    public void ZoomIntoSelection()
    {
        if (_selection.FirstOrDefault(n => n.IsDirectory) is { } folder)
        {
            if (FlatResults != null) ShowInTree(folder);
            Navigate(folder);
        }
    }

    private void SetFocus(FileNode node)
    {
        Focus = node;
        _selection = new List<FileNode>();
        RecomputeColors();
        Raise(SessionChange.Focus | SessionChange.History | SessionChange.Selection);
        if (FlatResults != null || _viewMode != ViewMode.Tree) ScheduleQuery();
    }

    /// <summary>Selects <paramref name="node"/> in the tree, expanding its ancestors.</summary>
    public void Reveal(FileNode node)
    {
        RevealTarget = node;
        Raise(SessionChange.Reveal);
    }

    /// <summary>From a flat list (search / largest files), go back to the tree with <paramref name="node"/> selected.</summary>
    public void ShowInTree(FileNode node)
    {
        if (!IsAttached(node)) return;
        var change = SessionChange.Flat | SessionChange.ViewMode;
        if (!node.IsDescendantOf(Focus))
        {
            var folder = node.Parent ?? Root;
            if (!ReferenceEquals(folder, Focus))
            {
                _backStack.Add(Focus);
                _forwardStack.Clear();
                Focus = folder;
                RecomputeColors();
                change |= SessionChange.Focus | SessionChange.History;
            }
        }
        CancelQuery();
        _searchText = "";
        _viewMode = ViewMode.Tree;
        FlatResults = null;
        IsQuerying = false;
        Raise(change | SessionChange.Busy);
        Reveal(node);
    }

    // ---- Search & largest files ----

    public ViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value) return;
            _viewMode = value;
            Raise(SessionChange.ViewMode);
            ScheduleQuery();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            ScheduleQuery(debounce: true);
        }
    }

    public void RequestSearchFocus() => Raise(SessionChange.SearchFocus);

    private void CancelQuery()
    {
        _query?.Cancel();
        _query = null;
    }

    public void ScheduleQuery(bool debounce = false)
    {
        CancelQuery();
        string query = _searchText.Trim();
        if (query.Length == 0 && _viewMode == ViewMode.Tree)
        {
            bool had = FlatResults != null;
            FlatResults = null;
            IsQuerying = false;
            Raise((had ? SessionChange.Flat : SessionChange.None) | SessionChange.Busy);
            return;
        }
        IsQuerying = true;
        Raise(SessionChange.Busy);
        var source = new CancellationTokenSource();
        _query = source;
        var scope = Focus;
        _queryTasks.RemoveAll(t => t.IsCompleted);
        _queryTasks.Add(RunQuery(query, scope, debounce, source.Token));
    }

    private async Task RunQuery(string query, FileNode scope, bool debounce, CancellationToken token)
    {
        try
        {
            if (debounce) await Task.Delay(220, token);
            var results = await Task.Run(() => query.Length == 0
                ? TreeQuery.LargestFiles(scope, LargestLimit, token)
                : TreeQuery.Search(query, scope, SearchLimit, token), token);
            if (token.IsCancellationRequested) return;
            FlatResults = results;
            IsQuerying = false;
            Raise(SessionChange.Flat | SessionChange.Busy);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer query
        }
    }

    /// <summary>Waits until no background query is reading the tree, so it can be mutated safely.</summary>
    private async Task Quiesce()
    {
        CancelQuery();
        var tasks = _queryTasks.ToArray();
        _queryTasks.Clear();
        try { await Task.WhenAll(tasks); }
        catch (Exception) { /* cancelled queries */ }
        IsQuerying = false;
    }

    // ---- Collecting ----

    public void Collect(IEnumerable<FileNode> nodes)
    {
        var refusals = Collector.Add(nodes, Root);
        if (refusals.Count > 0)
            ShowToast(refusals.Count > 1 ? $"{refusals[0]} (+{refusals.Count - 1} more)" : refusals[0]);
    }

    /// <summary>Adds the nodes, or removes them when they're all already collected.</summary>
    public void ToggleCollect(IReadOnlyList<FileNode> nodes)
    {
        if (nodes.Count == 0 || Deletion is not DeletionPhase.Idle) return;
        if (nodes.All(Collector.Contains))
        {
            Collector.Remove(nodes);
            return;
        }
        if (nodes.All(n => Collector.CoveringAncestor(n) != null) && Collector.CoveringAncestor(nodes[0]) is { } ancestor)
        {
            ShowToast($"Already collected as part of “{DisplayName(ancestor)}”.");
            return;
        }
        Collect(nodes.Where(n => !Collector.Contains(n)).ToList());
    }

    public void CollectPaths(IEnumerable<string> paths)
    {
        var nodes = new List<FileNode>();
        int missing = 0;
        foreach (var path in paths)
        {
            if (NodeForPath(SystemAccess.Normalize(path)) is { } node) nodes.Add(node);
            else missing++;
        }
        if (nodes.Count > 0) Collect(nodes);
        if (missing > 0 && nodes.Count == 0) ShowToast($"Only items inside “{Target.DisplayName}” can be collected.");
    }

    public void SetCollectorExpanded(bool expanded)
    {
        if (CollectorExpanded == expanded) return;
        CollectorExpanded = expanded;
        Raise(SessionChange.Collector);
    }

    // ---- Deleting ----

    public void RequestDelete()
    {
        if (Collector.IsEmpty || Deletion is not DeletionPhase.Idle) return;
        if (_app.Prefs.ConfirmDelete)
        {
            DeleteSheetRequested = true;
            Raise(SessionChange.Deletion);
        }
        else
        {
            BeginCountdown(_app.Prefs.DeleteMode);
        }
    }

    public void DismissDeleteSheet()
    {
        if (!DeleteSheetRequested) return;
        DeleteSheetRequested = false;
        Raise(SessionChange.Deletion);
    }

    public void ConfirmDelete(DeleteMode mode)
    {
        DeleteSheetRequested = false;
        BeginCountdown(mode);
    }

    private async void BeginCountdown(DeleteMode mode)
    {
        LastOutcome = null;
        CollectorExpanded = false;
        _countdown?.Cancel();
        var source = new CancellationTokenSource();
        _countdown = source;
        Raise(SessionChange.Outcome | SessionChange.Collector);
        try
        {
            for (int remaining = _app.Prefs.CountdownSeconds; remaining > 0; remaining--)
            {
                SetDeletion(new DeletionPhase.Countdown(remaining));
                await Task.Delay(1000, source.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (source.IsCancellationRequested) return;
        _countdown = null;
        await PerformDeletion(mode);
    }

    public void CancelCountdown()
    {
        if (Deletion is not DeletionPhase.Countdown) return;
        _countdown?.Cancel();
        _countdown = null;
        SetDeletion(DeletionPhase.IdlePhase);
    }

    private void SetDeletion(DeletionPhase phase)
    {
        Deletion = phase;
        Raise(SessionChange.Deletion);
    }

    private async Task PerformDeletion(DeleteMode mode)
    {
        var items = Collector.Items.ToList();
        if (items.Count == 0)
        {
            SetDeletion(DeletionPhase.IdlePhase);
            return;
        }
        var paths = items.Select(i => i.Path).ToList();
        int total = paths.Count;
        SetDeletion(new DeletionPhase.Deleting(0, total));
        var context = SynchronizationContext.Current;
        void Progress(int done) => context?.Post(_ =>
        {
            if (Deletion is DeletionPhase.Deleting) SetDeletion(new DeletionPhase.Deleting(done, total));
        }, null);

        var results = await RunOnStaThread(() => Deleter.Delete(paths, mode, _app.OwnerWindow, Progress));
        await Quiesce();

        var failures = new List<DeletionFailure>();
        var records = new List<DeletionOutcome.RecycleRecord>();
        var removed = new List<FileNode>();
        int permanent = 0;
        long bytes = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var (item, result) = (items[i], results[i]);
            if (result.Kind == DeleteResultKind.Failed)
            {
                failures.Add(new DeletionFailure(item.Path, result.Reason ?? "Unknown error."));
                continue;
            }
            if (result.Kind == DeleteResultKind.RemovedInsteadOfRecycled) permanent++;
            string parentPath = item.Node.Parent?.Path ?? "";
            bytes += item.Node.Size;
            item.Node.DetachFromParent();
            removed.Add(item.Node);
            if (result.Recycled is { } recycled) records.Add(new(item.Node, parentPath, recycled));
        }
        Collector.Remove(removed);
        Deletion = DeletionPhase.IdlePhase;
        LastOutcome = new DeletionOutcome
        {
            Mode = mode, Count = removed.Count, Bytes = bytes, FailureCount = failures.Count, PermanentCount = permanent,
            Records = records,
        };
        if (failures.Count > 0) FailureReport = failures;
        TreeDidChange(SessionChange.Deletion | SessionChange.Outcome);
        _app.Volumes.Refresh();
    }

    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "BitReaper delete" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public void DismissFailureReport()
    {
        FailureReport = null;
        Raise(SessionChange.Outcome);
    }

    /// <summary>Puts the items of the last "Move to Recycle Bin" back where they were.</summary>
    public async void UndoLastDeletion()
    {
        if (LastOutcome is not { CanUndo: true } outcome || Deletion is not DeletionPhase.Idle) return;
        await Quiesce();
        int restored = 0;
        var failures = new List<DeletionFailure>();
        for (int i = outcome.Records.Count - 1; i >= 0; i--)
        {
            var record = outcome.Records[i];
            try
            {
                RecycleBin.PutBack(record.Item);
                if (NodeForPath(record.ParentPath) is { } parent) parent.Attach(record.Node);
                restored++;
            }
            catch (Exception error)
            {
                failures.Add(new DeletionFailure(record.Item.OriginalPath, Deleter.Describe(error)));
            }
        }
        LastOutcome = null;
        if (failures.Count > 0) FailureReport = failures;
        ShowToast($"Put back {restored} item{(restored == 1 ? "" : "s")}.");
        TreeDidChange(SessionChange.Outcome);
        _app.Volumes.Refresh();
    }

    public void DismissOutcome()
    {
        LastOutcome = null;
        Raise(SessionChange.Outcome);
    }

    // ---- Rescanning ----

    public async void Rescan(FileNode? node = null)
    {
        var target = node ?? Focus;
        if (!target.IsDirectory || IsRescanning || Deletion is not DeletionPhase.Idle || !IsAttached(target)) return;
        IsRescanning = true;
        Raise(SessionChange.Busy);
        string path = target.Path;
        bool isRoot = target.Parent == null;
        string name = isRoot ? Root.Name : target.Name;
        bool dedupe = _app.Prefs.DedupeHardLinks;
        var fresh = await Task.Run(() => new Scanner(dedupe).Scan(path, name));
        await Quiesce();
        IsRescanning = false;
        if (fresh == null || !IsAttached(target))
        {
            Raise(SessionChange.Busy);
            ShowToast($"Couldn't rescan “{DisplayName(target)}”.");
            return;
        }
        Replace(target, fresh);
        ShowToast($"Rescanned “{DisplayName(fresh)}”.");
    }

    private void Replace(FileNode old, FileNode replacement)
    {
        string focusPath = Focus.Path;
        var selectedPaths = _selection.Select(n => n.Path).ToList();
        if (old.Parent is { } parent) parent.ReplaceChild(old, replacement);
        else Root = replacement;
        Focus = NodeForPath(focusPath) ?? replacement;
        _selection = selectedPaths.Select(NodeForPath).OfType<FileNode>().ToList();
        Collector.Remap(NodeForPath);
        LastOutcome = null;
        TreeDidChange(SessionChange.Focus | SessionChange.Outcome | SessionChange.Busy);
    }

    /// <summary>Fixes up navigation state after nodes were added or removed, then asks views to reload.</summary>
    private void TreeDidChange(SessionChange extra)
    {
        _backStack.RemoveAll(n => !IsAttached(n));
        _forwardStack.RemoveAll(n => !IsAttached(n));
        if (!IsAttached(Focus))
        {
            var candidate = Focus.Parent;
            while (candidate != null && !IsAttached(candidate)) candidate = candidate.Parent;
            Focus = candidate ?? Root;
            extra |= SessionChange.Focus;
        }
        _selection.RemoveAll(n => !IsAttached(n));
        RecomputeColors();
        Raise(extra | SessionChange.Tree | SessionChange.History | SessionChange.Selection);
        if (FlatResults != null || _viewMode != ViewMode.Tree) ScheduleQuery();
    }

    // ---- Toasts ----

    public async void ShowToast(string message)
    {
        _toast?.Cancel();
        var source = new CancellationTokenSource();
        _toast = source;
        Toast = message;
        Raise(SessionChange.Toast);
        try { await Task.Delay(4000, source.Token); }
        catch (OperationCanceledException) { return; }
        Toast = null;
        Raise(SessionChange.Toast);
    }
}
