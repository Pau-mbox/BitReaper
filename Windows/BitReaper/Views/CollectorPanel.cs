using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BitReaper.Controls;
using BitReaper.Model;
using BitReaper.Util;
using Path = System.Windows.Shapes.Path;

namespace BitReaper.Views;

/// <summary>
/// The bottom bar: drop zone, collected total, review list, Clear and Delete, plus the deletion
/// safety countdown, progress and result.
/// </summary>
public sealed class CollectorPanel : Grid
{
    private readonly AppModel _model;
    private readonly ScanSession _session;
    private readonly ResultsView _results;
    private readonly NeonPanel _panel;
    private readonly StackPanel _body = new();
    private readonly ContentControl _bar = new() { Focusable = false };
    private readonly Image _mascot = Ui.Image("ReaperPeek", width: 84);
    private readonly ScaleTransform _mascotScale = new();
    private bool _dropTargeted;

    public CollectorPanel(AppModel model, ScanSession session, ResultsView results)
    {
        _model = model;
        _session = session;
        _results = results;

        _body.Children.Add(_bar);
        _panel = Ui.Panel(_body, new Thickness(0));
        Children.Add(_panel);

        // The reaper peeks up from the bottom edge, and pops up when something is dragged over or reaped.
        _mascot.HorizontalAlignment = HorizontalAlignment.Left;
        _mascot.VerticalAlignment = VerticalAlignment.Bottom;
        _mascot.Margin = new Thickness(12, 0, 0, 0);
        _mascot.IsHitTestVisible = false;
        _mascot.RenderTransformOrigin = new Point(0.5, 1);
        _mascot.RenderTransform = _mascotScale;
        Children.Add(_mascot);

        AllowDrop = true;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetDropTargeted(false);
        Drop += (_, e) =>
        {
            SetDropTargeted(false);
            if (_session.Deletion is DeletionPhase.Idle && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                _session.CollectPaths(paths);
        };
        Rebuild();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool ok = _session.Deletion is DeletionPhase.Idle && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
        SetDropTargeted(ok);
    }

    private void SetDropTargeted(bool targeted)
    {
        if (_dropTargeted == targeted) return;
        _dropTargeted = targeted;
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        bool busy = _session.Deletion is not DeletionPhase.Idle;
        _panel.Glow = busy ? Theme.Danger : _dropTargeted ? Theme.Cyan : _session.Collector.IsEmpty ? Theme.Violet : Theme.Magenta;
        _panel.GlowStrength = busy || _dropTargeted || !_session.Collector.IsEmpty ? 0.35 : 0.22;
        _panel.Fill = Theme.Brush(busy ? Theme.Danger.WithAlpha(0.10) : _dropTargeted ? Theme.Cyan.WithAlpha(0.12) : Theme.Panel.WithAlpha(0.85));
        _panel.Dashed = _session.Collector.IsEmpty && !busy;
        bool raised = _dropTargeted || busy;
        _mascotScale.ScaleX = _mascotScale.ScaleY = raised ? 1.1 : 1;
        _mascot.Margin = new Thickness(12, 0, 0, raised ? 6 : 0);
        _mascot.Effect = null;
        _mascot.WithGlow(busy ? Theme.Danger : Theme.Magenta, 8, 0.6);
    }

    public void Rebuild()
    {
        while (_body.Children.Count > 1) _body.Children.RemoveAt(0);
        var collector = _session.Collector;
        if (_session.CollectorExpanded && !collector.IsEmpty && _session.Deletion is DeletionPhase.Idle)
        {
            var list = CollectorList();
            _body.Children.Insert(0, list);
            _body.Children.Insert(1, new Rectangle { Height = 1, Fill = Ui.Res<Brush>("NeonBorderBrush") });
        }
        _bar.Content = Bar();
        UpdateChrome();
    }

    private UIElement Bar()
    {
        var collector = _session.Collector;
        var dock = new DockPanel { Margin = new Thickness(16, 12, 16, 12), MinHeight = 46, LastChildFill = true };
        var spacer = new Border { Width = 84 };
        DockPanel.SetDock(spacer, Dock.Left);
        dock.Children.Add(spacer);

        void Right(params UIElement[] items)
        {
            var row = Ui.Row(10, items);
            DockPanel.SetDock(row, Dock.Right);
            dock.Children.Add(row);
        }
        void Left(params UIElement[] items) => dock.Children.Add(Ui.Row(14, items));

        switch (_session.Deletion)
        {
            case DeletionPhase.Countdown countdown:
            {
                var cancel = Ui.Button("Cancel", click: _session.CancelCountdown, tip: "Cancel the deletion (Esc)");
                Right(cancel);
                var title = Ui.Display($"Reaping {Ui.Plural(collector.Count, "item")} in {countdown.Seconds}…", 16, Theme.Danger, bold: true)
                              .WithGlow(Theme.Danger, 5);
                Left(CountdownRing(countdown.Seconds, _model.Prefs.CountdownSeconds),
                     Ui.Column(2, title, Ui.Secondary("Changed your mind? Press Cancel or Esc.", 12, wrap: false)));
                break;
            }
            case DeletionPhase.Deleting deleting:
            {
                var gauge = new UsageGauge
                {
                    Fraction = (double)deleting.Done / Math.Max(deleting.Total, 1), Colors = new[] { Theme.Magenta, Theme.Danger }, Height = 6,
                    Width = 280, HorizontalAlignment = HorizontalAlignment.Left,
                };
                Left(Ui.Glyph(Ui.GlyphDelete, 18, Theme.Danger).WithGlow(Theme.Danger, 5),
                     Ui.Column(6, Ui.Display($"Reaping {deleting.Done} of {deleting.Total}…", 15), gauge));
                break;
            }
            default:
                if (collector.IsEmpty)
                {
                    var delete = Ui.Button(Ui.Label(Ui.GlyphDelete, "Delete"), Ui.Kind.Ghost);
                    delete.IsEnabled = false;
                    if (_session.LastOutcome is { } outcome)
                    {
                        Right(OutcomeButtons(outcome).Append(delete).ToArray());
                        Left(OutcomeView(outcome).ToArray());
                    }
                    else
                    {
                        Right(delete);
                        Left(Ui.Column(3, Ui.Display("Collector", 16, bold: true),
                                       Ui.Secondary("Drag items here, press Del, or click ⊕ to add them to the Collector for deletion.", 12, wrap: false)));
                    }
                }
                else
                {
                    var clear = Ui.Button("Clear", Ui.Kind.Ghost, click: collector.Clear, tip: "Empty the Collector (nothing is deleted)");
                    var delete = Ui.Button(Ui.Label(Ui.GlyphDelete, "Delete"), Ui.Kind.Danger, click: _session.RequestDelete,
                                           tip: "Delete all collected items (Ctrl+Shift+Del)");
                    Right(clear, delete);

                    var summary = Ui.Row(8,
                                         Ui.Column(2, Ui.Display($"{ByteFormat.Size(collector.TotalSize)} collected", 16, bold: true),
                                                   Ui.Secondary($"{Ui.Plural(collector.Count, "item")} · nothing is deleted until you click Delete", 12, wrap: false)),
                                         Ui.Glyph(_session.CollectorExpanded ? Ui.GlyphChevronDown : Ui.GlyphChevronUp, 11, Theme.Cyan));
                    var toggle = new Button
                    {
                        Style = Ui.Res<Style>("BareButton"), Content = summary, Padding = new Thickness(4, 2, 8, 2),
                        ToolTip = "Review collected items (Ctrl+K)",
                    };
                    toggle.Click += (_, _) => _session.SetCollectorExpanded(!_session.CollectorExpanded);
                    Left(CollectedBadge(collector.TotalSize), toggle);
                }
                break;
        }
        return dock;
    }

    private IEnumerable<UIElement> OutcomeView(DeletionOutcome outcome)
    {
        bool ok = outcome.FailureCount == 0;
        yield return Ui.Glyph(ok ? Ui.GlyphCompleted : Ui.GlyphWarning, 22, ok ? Theme.Mint : Theme.Orange).WithGlow(ok ? Theme.Mint : Theme.Orange, 5);
        int recycled = outcome.RecycledCount;
        int permanent = outcome.PermanentCount;
        string title = outcome.Mode != DeleteMode.RecycleBin || recycled == 0
            ? $"Reaped {Ui.Plural(outcome.Count, "item")} · freed {ByteFormat.Size(outcome.Bytes)}"
            : permanent == 0
                ? $"Moved {Ui.Plural(outcome.Count, "item")} to the Recycle Bin · {ByteFormat.Size(outcome.Bytes)}"
                : $"Moved {Ui.Plural(recycled, "item")} to the Recycle Bin, deleted {permanent} permanently · {ByteFormat.Size(outcome.Bytes)}";
        string detail = outcome.FailureCount > 0
            ? $"{Ui.Plural(outcome.FailureCount, "item")} couldn't be deleted."
            : permanent > 0
                ? $"{(permanent == 1 ? "One item" : $"{permanent} items")} couldn't go to the Recycle Bin (removable or network drive, or too big), so Windows deleted {(permanent == 1 ? "it" : "them")} for good."
                : outcome.Mode == DeleteMode.RecycleBin ? "Empty the Recycle Bin to reclaim the space." : "The space is available right away.";
        yield return Ui.Column(2, Ui.Display(title, 15, bold: true), Ui.Secondary(detail, 12, wrap: false));
    }

    private IEnumerable<UIElement> OutcomeButtons(DeletionOutcome outcome)
    {
        if (outcome.CanUndo)
            yield return Ui.Button(Ui.Label(Ui.GlyphUndo, "Put Back", 12), compact: true, click: _session.UndoLastDeletion, tip: "Restore from the Recycle Bin (Ctrl+Z)");
        if (outcome.RecycledCount > 0)
            yield return Ui.Button("Open Recycle Bin", Ui.Kind.Ghost, compact: true, click: ShellActions.OpenRecycleBin);
        yield return Ui.GlyphButton(Ui.GlyphClose, _session.DismissOutcome, "Dismiss", 11);
    }

    /// <summary>Glowing round total, e.g. "11.1 GB".</summary>
    private static UIElement CollectedBadge(long bytes)
    {
        var (number, unit) = ByteFormat.Parts(bytes);
        var grid = new Grid { Width = 50, Height = 50 };
        grid.Children.Add(new Ellipse { Fill = Theme.Brush(Theme.Magenta.WithAlpha(0.14)) });
        grid.Children.Add(new Ellipse { Stroke = Ui.Res<Brush>("HotGradientBrush"), StrokeThickness = 2.5 }.WithGlow(Theme.Magenta, 7, 0.7));
        var numberText = Ui.Display(number, number.Length > 4 ? 12 : 15, bold: true);
        var unitText = Ui.Text(unit, 9, Theme.TextSecondary, Ui.DisplayFont);
        numberText.HorizontalAlignment = unitText.HorizontalAlignment = HorizontalAlignment.Center;
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(numberText);
        column.Children.Add(unitText);
        grid.Children.Add(column);
        return grid;
    }

    private static UIElement CountdownRing(int seconds, int total)
    {
        var grid = new Grid { Width = 50, Height = 50 };
        grid.Children.Add(new Ellipse { Stroke = Theme.Brush(Theme.Danger.WithAlpha(0.2)), StrokeThickness = 4, Margin = new Thickness(2) });
        double fraction = (double)seconds / Math.Max(total, 1);
        var arc = new Path
        {
            Stroke = Theme.Brush(Theme.Danger), StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = DonutChart.Arc(new Point(25, 25), 21, -90, -90 + 360 * fraction),
        }.WithGlow(Theme.Danger, 6);
        grid.Children.Add(arc);
        var number = Ui.Display(seconds.ToString(), 20, Theme.Danger, bold: true);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(number);
        return grid;
    }

    /// <summary>The expanded collector: every collected item with its size; preview, locate, or remove it.</summary>
    private UIElement CollectorList()
    {
        var items = _session.Collector.SortedItems;
        var stack = new StackPanel { Margin = new Thickness(94, 8, 10, 8) };
        foreach (var item in items) stack.Children.Add(CollectorRow(item));
        return new ScrollViewer
        {
            Content = stack, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false,
            Height = Math.Min(250, items.Count * 42 + 16),
        };
    }

    private UIElement CollectorRow(Collector.Item item)
    {
        var node = item.Node;
        var icon = Ui.Icon(IconCache.Icon(node), 22);
        icon.Margin = new Thickness(0, 0, 10, 0);
        var name = Ui.Text(node.Name, 13, weight: FontWeights.Medium);
        var where = Ui.Text(Ui.Abbreviate(System.IO.Path.GetDirectoryName(item.Path) ?? item.Path), 11, Theme.TextTertiary);
        var size = Ui.Text(ByteFormat.Size(node.Size), 13, weight: FontWeights.SemiBold);
        size.MinWidth = 80;
        size.TextAlignment = TextAlignment.Right;

        var hoverButtons = Ui.Row(2,
                                  Ui.GlyphButton(Ui.GlyphPreview, () => _results.Preview(new[] { node }), "Preview"),
                                  Ui.GlyphButton(Ui.GlyphList, () => _session.ShowInTree(node), "Show in Tree"),
                                  Ui.GlyphButton(Ui.GlyphFolder, () => ShellActions.Reveal(new[] { item.Path }), "Show in Explorer"));
        hoverButtons.Visibility = Visibility.Hidden;
        var remove = Ui.GlyphButton(Ui.GlyphClose, () => _session.Collector.Remove(new[] { node }), "Remove from Collector", 12);

        var right = Ui.Row(6, hoverButtons, size, remove);
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(right);
        dock.Children.Add(Ui.Column(0, name, where));

        var border = new Border { Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = dock };
        border.MouseEnter += (_, _) => { border.Background = Theme.Brush(Theme.Violet.WithAlpha(0.16)); hoverButtons.Visibility = Visibility.Visible; };
        border.MouseLeave += (_, _) => { border.Background = Brushes.Transparent; hoverButtons.Visibility = Visibility.Hidden; };
        border.ContextMenu = Ui.Menu(
            Ui.MenuItem("Preview", Ui.GlyphPreview, () => _results.Preview(new[] { node })),
            Ui.MenuItem("Show in Tree", Ui.GlyphList, () => _session.ShowInTree(node)),
            Ui.MenuItem("Show in Explorer", Ui.GlyphFolder, () => ShellActions.Reveal(new[] { item.Path })),
            new Separator(),
            Ui.MenuItem("Remove from Collector", Ui.GlyphClose, () => _session.Collector.Remove(new[] { node })));
        return border;
    }
}
