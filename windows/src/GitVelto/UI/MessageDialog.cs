using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GitVelto.UI;

public enum MessageKind { Info, Question, Warning, Danger }

/// <summary>Themed replacement for MessageBox (Japanese buttons, selectable details, text input).</summary>
public static class MessageDialog
{
    public static bool Show(Window? owner, string message, string info, MessageKind kind, string ok, string? cancel) =>
        Build(owner, message, info, kind, ok, cancel, null, out _);

    public static string? Prompt(Window? owner, string message, string info, string defaultValue)
    {
        var box = new TextBox { Text = defaultValue, MinWidth = 320 };
        return Build(owner, message, info, MessageKind.Question, "OK", "キャンセル", box, out _) ? box.Text.Trim() : null;
    }

    /// <summary>Builds and shows a modal dialog. <paramref name="extra"/> is placed under the text (input fields).</summary>
    public static bool Build(Window? owner, string message, string info, MessageKind kind, string ok, string? cancel,
        FrameworkElement? extra, out Window window, bool secretFocus = false)
    {
        var glyph = kind switch
        {
            MessageKind.Info => "",
            MessageKind.Question => "",
            MessageKind.Warning => "",
            _ => "",
        };
        var color = kind switch
        {
            MessageKind.Info or MessageKind.Question => Color.FromRgb(0x2F, 0x7B, 0xD9),
            MessageKind.Warning => Color.FromRgb(0xE0, 0x8A, 0x00),
            _ => Color.FromRgb(0xD7, 0x26, 0x3D),
        };

        var root = new Grid { Margin = new Thickness(20) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 30,
            Foreground = new SolidColorBrush(color), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top,
        };
        root.Children.Add(icon);

        var body = new StackPanel { MaxWidth = 520 };
        Grid.SetColumn(body, 1);
        body.Children.Add(new TextBlock { Text = message, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(info))
        {
            var detail = new TextBox
            {
                Text = info.Trim(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Margin = new Thickness(0, 8, 0, 0), MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0),
            };
            body.Children.Add(detail);
        }
        if (extra != null)
        {
            extra.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(extra);
        }
        root.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        var w = new Window
        {
            Title = "GitVelto",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = owner == null,
            Content = root,
        };
        if (owner != null && owner.IsVisible) w.Owner = owner;
        var okBtn = new Button { Content = ok, MinWidth = 96, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        if (kind != MessageKind.Danger) Ui.Accent(okBtn);
        okBtn.Click += (_, _) => { w.DialogResult = true; };
        if (cancel != null)
        {
            var c = new Button { Content = cancel, MinWidth = 96, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(okBtn);
            buttons.Children.Add(c);
            if (kind == MessageKind.Danger) { okBtn.IsDefault = false; c.IsDefault = true; }
        }
        else
        {
            okBtn.IsCancel = true;
            buttons.Children.Add(okBtn);
        }
        root.Children.Add(buttons);
        w.Loaded += (_, _) =>
        {
            if (extra is TextBox tb) { tb.Focus(); tb.SelectAll(); }
            else if (extra is PasswordBox pb) pb.Focus();
            else okBtn.Focus();
            if (kind is MessageKind.Warning or MessageKind.Danger) SystemSounds.Exclamation.Play();
        };
        w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { w.DialogResult = false; } };
        window = w;
        if (w.Owner == null) w.Topmost = true;
        return w.ShowDialog() == true;
    }
}
