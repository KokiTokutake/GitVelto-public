using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Row of <see cref="FileList"/>: a status entry with a check box.</summary>
public sealed class FileItem : INotifyPropertyChanged
{
    public StatusEntry Entry { get; }
    public FileItem(StatusEntry e, bool isChecked) { Entry = e; this.isChecked = isChecked; }

    private bool isChecked;
    public bool IsChecked
    {
        get => isChecked;
        set { if (isChecked == value) return; isChecked = value; OnChanged(); CheckedChanged?.Invoke(); }
    }

    internal Action? CheckedChanged;

    public string Id => Entry.Id;
    public string DisplayPath => Entry.DisplayPath;
    public string StateText => Entry.State.Label() + (Entry.Staged && Entry.State.IsTracked() ? " (ステージ済)" : "");
    public string StateHelp => Glossary.Entry(Entry.Staged && Entry.State.IsTracked() ? "stage" : Glossary.Key(Entry.State))?.Summary ?? "";
    public string Ext => Entry.Ext;
    public Brush Color => Ui.StateBrush(Entry.State);
    public ImageSource? Icon => Shell.Icon(Entry.Path.TrimEnd('/'), Entry.Path.EndsWith('/'));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>TortoiseGit-like file list (optional check boxes, double click = open, context menu supplied by the owner).</summary>
public sealed class FileList : DataGrid
{
    public ObservableCollection<FileItem> Items_ { get; } = [];

    /// <summary>Double click / Enter on a row.</summary>
    public event Action<StatusEntry>? Open;
    /// <summary>Raised when check marks change.</summary>
    public event Action? CheckedChanged;
    /// <summary>Builds the context menu for the selected entries (return null for none).</summary>
    public Func<IReadOnlyList<StatusEntry>, ContextMenu?>? MenuBuilder { get; set; }

    public FileList(bool checkable)
    {
        // Subclasses do not receive the implicit (Fluent) style of their base type: apply it explicitly.
        SetResourceReference(StyleProperty, typeof(DataGrid));
        AutoGenerateColumns = false;
        CanUserAddRows = false;
        CanUserDeleteRows = false;
        CanUserReorderColumns = false;
        IsReadOnly = !checkable;
        SelectionMode = DataGridSelectionMode.Extended;
        SelectionUnit = DataGridSelectionUnit.FullRow;
        HeadersVisibility = DataGridHeadersVisibility.Column;
        GridLinesVisibility = DataGridGridLinesVisibility.None;
        RowHeight = 26;
        EnableRowVirtualization = true;
        ItemsSource = Items_;
        VirtualizingPanel.SetIsVirtualizing(this, true);

        if (checkable)
        {
            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(ToggleButton_IsChecked, new Binding(nameof(FileItem.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FocusableProperty, false);
            check.SetValue(MinWidthProperty, 0.0);
            Columns.Add(new DataGridTemplateColumn
            {
                Header = "", Width = 36, CanUserResize = false,
                CellTemplate = new DataTemplate { VisualTree = check },
            });
        }

        var pathCell = new FrameworkElementFactory(typeof(StackPanel));
        pathCell.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var img = new FrameworkElementFactory(typeof(Image));
        img.SetBinding(Image.SourceProperty, new Binding(nameof(FileItem.Icon)));
        img.SetValue(WidthProperty, 16.0);
        img.SetValue(HeightProperty, 16.0);
        img.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        pathCell.AppendChild(img);
        var txt = new FrameworkElementFactory(typeof(TextBlock));
        txt.SetBinding(TextBlock.TextProperty, new Binding(nameof(FileItem.DisplayPath)));
        txt.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(FileItem.Color)));
        txt.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        txt.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        pathCell.AppendChild(txt);
        Columns.Add(new DataGridTemplateColumn
        {
            Header = "パス", Width = new DataGridLength(1, DataGridLengthUnitType.Star), SortMemberPath = nameof(FileItem.DisplayPath),
            CellTemplate = new DataTemplate { VisualTree = pathCell }, IsReadOnly = true,
        });

        var st = new FrameworkElementFactory(typeof(TextBlock));
        st.SetBinding(TextBlock.TextProperty, new Binding(nameof(FileItem.StateText)));
        st.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(FileItem.Color)));
        st.SetBinding(ToolTipProperty, new Binding(nameof(FileItem.StateHelp)));
        st.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        Columns.Add(new DataGridTemplateColumn
        {
            Header = "状態", Width = 130, SortMemberPath = nameof(FileItem.StateText),
            CellTemplate = new DataTemplate { VisualTree = st }, IsReadOnly = true,
        });
        Columns.Add(new DataGridTextColumn { Header = "拡張子", Width = 70, Binding = new Binding(nameof(FileItem.Ext)), IsReadOnly = true });

        MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<DataGridRow>(d) is { Item: FileItem fi }) Open?.Invoke(fi.Entry);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && SelectedItem is FileItem fi) { Open?.Invoke(fi.Entry); e.Handled = true; }
            if (e.Key == Key.Space && checkable && SelectedItems.Count > 0)
            {
                var items = SelectedItems.OfType<FileItem>().ToList();
                bool target = !items.All(i => i.IsChecked);
                foreach (var i in items) i.IsChecked = target;
                e.Handled = true;
            }
        };
        ContextMenuOpening += (_, e) =>
        {
            var sel = SelectedEntries;
            ContextMenu = sel.Count > 0 ? MenuBuilder?.Invoke(sel) : null;
            if (ContextMenu == null) e.Handled = true;
        };
    }

    private static readonly DependencyProperty ToggleButton_IsChecked = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    public IReadOnlyList<StatusEntry> SelectedEntries => SelectedItems.OfType<FileItem>().Select(i => i.Entry).ToList();
    public IReadOnlyList<StatusEntry> CheckedEntries => Items_.Where(i => i.IsChecked).Select(i => i.Entry).ToList();
    public IReadOnlyList<StatusEntry> Entries => Items_.Select(i => i.Entry).ToList();

    /// <summary>Replaces the rows. <paramref name="isChecked"/> decides the initial check mark.</summary>
    public void SetEntries(IEnumerable<StatusEntry> entries, Func<StatusEntry, bool>? isChecked = null)
    {
        var selected = new HashSet<string>(SelectedEntries.Select(e => e.Id));
        Items_.Clear();
        foreach (var e in entries)
        {
            var item = new FileItem(e, isChecked?.Invoke(e) ?? false) { CheckedChanged = () => CheckedChanged?.Invoke() };
            Items_.Add(item);
            if (selected.Contains(e.Id)) SelectedItems.Add(item);
        }
        CheckedChanged?.Invoke();
    }

    public void SetAllChecked(bool value)
    {
        foreach (var i in Items_) i.IsChecked = value;
    }

    public static MenuItem Item(string header, Action action, string? glyph = null)
    {
        var m = new MenuItem { Header = header };
        if (glyph != null) m.Icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 14 };
        m.Click += (_, _) => action();
        return m;
    }
}
