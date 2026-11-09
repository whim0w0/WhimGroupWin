using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WhimGroupWin;

public partial class FrmAutomaticGroup : Window
{
    public sealed class ApplicationRow
    {
        public string ExecutablePath { get; set; } = "";
        public string WindowTitle { get; set; } = "";
    }

    private readonly Guid identifier;
    private readonly ObservableCollection<ApplicationRow> applications = [];

    public FrmAutomaticGroup(AutomaticGroup? group = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongW(handle, -16);
            SetWindowLongW(handle, -16, style & ~0x00030000);
            SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0037);
        };
        identifier = group?.Id ?? Guid.NewGuid();
        GroupName.Text = group?.Name ?? "";
        Arrangement.SelectedIndex = group?.Split == true ? 1 : 0;
        ShortcutKey.ItemsSource = KeyboardSettings.AvailableKeys;
        ShortcutKey.SelectedItem = group?.Key ?? Key.F1;
        var modifiers = group?.Modifiers ?? (ModifierKeys.Control | ModifierKeys.Alt);
        ShortcutControl.IsChecked = modifiers.HasFlag(ModifierKeys.Control);
        ShortcutAlt.IsChecked = modifiers.HasFlag(ModifierKeys.Alt);
        ShortcutShift.IsChecked = modifiers.HasFlag(ModifierKeys.Shift);
        foreach (var application in group?.Applications ?? [])
            applications.Add(new ApplicationRow { ExecutablePath = application.ExecutablePath, WindowTitle = application.WindowTitle });
        ApplicationsList.ItemsSource = applications;
        RefreshWindows();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(nint handle, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint handle, nint after, int left, int top, int width, int height, uint flags);

    private void RefreshWindows()
    {
        try
        {
            AvailableWindows.ItemsSource = WindowService.Enumerate();
            AvailableWindows.SelectedIndex = 0;
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            EditorStatus.Text = error.Message;
        }
    }

    private void OnRefreshWindows(object sender, RoutedEventArgs args) => RefreshWindows();

    private void OnBrowseApplication(object sender, RoutedEventArgs args)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "应用程序 (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            applications.Add(new ApplicationRow { ExecutablePath = dialog.FileName });
    }

    private void OnAddWindow(object sender, RoutedEventArgs args)
    {
        if (AvailableWindows.SelectedItem is not DesktopWindow window)
            return;
        try
        {
            using var process = Process.GetProcessById((int)window.ProcessId);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("无法读取应用路径。");
            applications.Add(new ApplicationRow { ExecutablePath = path, WindowTitle = window.Title });
            EditorStatus.Text = "";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            EditorStatus.Text = $"无法添加窗口：{error.Message}";
        }
    }

    private void OnRemoveApplication(object sender, RoutedEventArgs args)
    {
        if (ApplicationsList.SelectedItem is ApplicationRow row)
            applications.Remove(row);
    }

    private void OnConfirm(object sender, RoutedEventArgs args)
    {
        ApplicationsList.CommitEdit();
        var group = new AutomaticGroup
        {
            Id = identifier,
            Name = GroupName.Text.Trim(),
            Split = Arrangement.SelectedIndex == 1,
            Modifiers = (ShortcutControl.IsChecked == true ? ModifierKeys.Control : ModifierKeys.None)
                | (ShortcutAlt.IsChecked == true ? ModifierKeys.Alt : ModifierKeys.None)
                | (ShortcutShift.IsChecked == true ? ModifierKeys.Shift : ModifierKeys.None),
            Key = ShortcutKey.SelectedItem is Key key ? key : Key.None,
            Applications = applications.Select(row => new AutomaticApplication
            {
                ExecutablePath = row.ExecutablePath.Trim(),
                WindowTitle = row.WindowTitle.Trim()
            }).ToList()
        };
        try
        {
            var groups = AutomaticGroups.Current.Where(candidate => candidate.Id != identifier).Append(group).ToArray();
            AutomaticGroups.Save(groups);
            DialogResult = true;
        }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            EditorStatus.Text = $"保存失败：{error.Message}";
        }
    }
}