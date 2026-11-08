using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Small helpers shared by all views (fonts, colors, icon buttons, layout shortcuts).</summary>
public static class Ui
{
    public static FontFamily Mono => (FontFamily)Application.Current.FindResource("MonoFont");
    public static FontFamily IconFont => (FontFamily)Application.Current.FindResource("IconFont");

    /// <summary>Accent color brush of the current theme.</summary>
    public static Brush Accent() =>
        Application.Current.TryFindResource("AccentTextFillColorPrimaryBrush") as Brush
        ?? Application.Current.TryFindResource("AccentText") as Brush
        ?? Brushes.RoyalBlue;

    public static Brush Secondary() =>
        Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;

    /// <summary>Applies the theme's accent (primary) button style.</summary>
    public static T Accent<T>(T button) where T : Button
    {
        if (Application.Current.TryFindResource("AccentButtonStyle") is Style s) button.Style = s;
        return button;
    }

    public static Button IconButton(string glyph, string tooltip, Action onClick)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14 },
            ToolTip = tooltip, Padding = new Thickness(8, 5, 8, 5), MinWidth = 0, Margin = new Thickness(4, 0, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static void SetGlyph(TextBlock t, string glyph, Brush color)
    {
        t.Text = glyph;
        t.FontFamily = IconFont;
        t.Foreground = color;
    }

    public static TextBlock Glyph(string glyph, double size = 14, Brush? color = null) =>
        new() { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = color ?? Secondary(), VerticalAlignment = VerticalAlignment.Center };

    public static Brush StateBrush(FileState s) => s switch
    {
        FileState.Modified or FileState.TypeChanged => (Brush)Application.Current.FindResource("StateModified"),
        FileState.Added or FileState.Copied => (Brush)Application.Current.FindResource("StateAdded"),
        FileState.Deleted => (Brush)Application.Current.FindResource("StateDeleted"),
        FileState.Renamed => (Brush)Application.Current.FindResource("StateRenamed"),
        FileState.Conflict => (Brush)Application.Current.FindResource("StateConflict"),
        FileState.Ignored => new SolidColorBrush(Color.FromArgb(0x99, 0x80, 0x80, 0x80)),
        _ => Secondary(),
    };

    public static TextBlock Caption(string text) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource("Caption") };

    public static TextBlock Heading(string text) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource("Heading") };

    /// <summary>Horizontal row of elements (with spacing).</summary>
    public static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items)
        {
            if (i is FrameworkElement fe)
            {
                if (fe.Margin == default) fe.Margin = new Thickness(0, 0, 8, 0);
                fe.VerticalAlignment = VerticalAlignment.Center;
            }
            p.Children.Add(i);
        }
        return p;
    }

    /// <summary>Label + control row for simple forms (label column auto width).</summary>
    public static Grid Form(params (string label, UIElement control)[] rows)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (label, control) = rows[i];
            if (label.Length > 0)
            {
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
                Grid.SetRow(l, i);
                g.Children.Add(l);
            }
            if (control is FrameworkElement f) f.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(control, i);
            Grid.SetColumn(control, label.Length > 0 ? 1 : 0);
            if (label.Length == 0) Grid.SetColumnSpan(control, 2);
            g.Children.Add(control);
        }
        return g;
    }

    /// <summary>Right-aligned button bar: Cancel + primary action (Enter / Esc work).</summary>
    public static StackPanel Buttons(GVView view, string primary, Action onPrimary, string cancel = "キャンセル", params Button[] extra)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var e in extra) { e.Margin = new Thickness(0, 0, 8, 0); p.Children.Add(e); }
        var c = new Button { Content = cancel, MinWidth = 96, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        c.Click += (_, _) => view.Handle.Close();
        var ok = Accent(new Button { Content = primary, MinWidth = 96, IsDefault = true });
        ok.Click += (_, _) => onPrimary();
        p.Children.Add(c);
        p.Children.Add(ok);
        return p;
    }

    public static string FormatDate(DateTimeOffset d) => d.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
}
