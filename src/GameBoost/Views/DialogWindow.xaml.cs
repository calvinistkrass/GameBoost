using System.Windows;
using System.Windows.Input;
using GameBoost.Infrastructure;

namespace GameBoost.Views;

public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    public string InputText => InputBox.Text;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DialogResult = true;
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove during another mouse operation – ignore.
        }
    }

    internal static bool? ShowDialog(string icon, string title, string message, string okText, string? cancelText, string? input, out string inputResult)
    {
        var dlg = new DialogWindow();
        dlg.IconText.Text = icon;
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        dlg.OkButton.Content = okText;

        if (cancelText == null) dlg.CancelButton.Visibility = Visibility.Collapsed;
        else dlg.CancelButton.Content = cancelText;

        if (input != null)
        {
            dlg.InputBox.Visibility = Visibility.Visible;
            dlg.InputBox.Text = input;
            dlg.Loaded += (_, _) =>
            {
                dlg.InputBox.Focus();
                dlg.InputBox.SelectAll();
            };
        }

        var owner = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsVisible);
        if (owner != null) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var result = dlg.ShowDialog();
        inputResult = dlg.InputText;
        return result;
    }
}

/// <summary>Dark standard dialogs (info, error, confirm, input).</summary>
public static class Dialogs
{
    private const string InfoIcon = "";
    private const string WarnIcon = "";
    private const string ErrorIcon = "";
    private const string EditIcon = "";

    public static void Info(string message, string? title = null)
        => DialogWindow.ShowDialog(InfoIcon, title ?? Loc.T("Dlg.Info"), message, Loc.T("Dlg.OK"), null, null, out _);

    public static void Error(string message)
        => DialogWindow.ShowDialog(ErrorIcon, Loc.T("Dlg.Error"), message, Loc.T("Dlg.OK"), null, null, out _);

    public static bool Confirm(string message, string? title = null)
        => DialogWindow.ShowDialog(WarnIcon, title ?? Loc.T("Dlg.Confirm"), message, Loc.T("Dlg.Yes"), Loc.T("Dlg.No"), null, out _) == true;

    public static string? Input(string title, string prompt, string initial = "")
    {
        var ok = DialogWindow.ShowDialog(EditIcon, title, prompt, Loc.T("Dlg.OK"), Loc.T("Dlg.Cancel"), initial, out var text);
        return ok == true && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }
}
