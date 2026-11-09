using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WhimGroupWin;

public partial class FrmSetting : Window
{
    private readonly GroupController? groupController;
    private readonly DispatcherTimer groupsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private IReadOnlyList<GroupController.GroupInfo> displayedGroups = [];

    public FrmSetting() : this(null) { }

    internal FrmSetting(GroupController? groupController)
    {
        this.groupController = groupController;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongW(handle, -16);
            SetWindowLongW(handle, -16, style & ~0x00030000);
            SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0037);
        };
        RefreshAutomaticGroups();
        SwitchKey.ItemsSource = KeyboardSettings.AvailableKeys;
        ShowKeyboardSettings(KeyboardSettings.Current);
        groupsTimer.Tick += (_, _) => RefreshWindowGroups();
        Loaded += (_, _) => { RefreshWindowGroups(); groupsTimer.Start(); };
        Closed += (_, _) => groupsTimer.Stop();
    }

    private void RefreshWindowGroups()
    {
        var current = groupController?.GetGroups() ?? [];
        if (!displayedGroups.SequenceEqual(current))
        {
            displayedGroups = current;
            WindowGroupsList.ItemsSource = current;
        }
        WindowGroupsEmpty.Visibility = current.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShowWindowGroup(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is GroupController.GroupInfo group)
            group.Show();
        RefreshWindowGroups();
    }

    private void OnHideWindowGroup(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is GroupController.GroupInfo group)
            group.Hide();
        RefreshWindowGroups();
    }

    private void OnCloseWindowGroup(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is GroupController.GroupInfo group
            && MessageBox.Show(this, $"关闭“{group.Name}”中的所有窗口？", "关闭窗口组",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            group.Close();
        RefreshWindowGroups();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(nint handle, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint handle, nint after, int left, int top, int width, int height, uint flags);

    private void ShowKeyboardSettings(KeyboardSettings settings)
    {
        GroupControl.IsChecked = settings.GroupModifiers.HasFlag(ModifierKeys.Control);
        GroupAlt.IsChecked = settings.GroupModifiers.HasFlag(ModifierKeys.Alt);
        GroupShift.IsChecked = settings.GroupModifiers.HasFlag(ModifierKeys.Shift);
        SwitchControl.IsChecked = settings.SwitchModifiers.HasFlag(ModifierKeys.Control);
        SwitchAlt.IsChecked = settings.SwitchModifiers.HasFlag(ModifierKeys.Alt);
        SwitchShift.IsChecked = settings.SwitchModifiers.HasFlag(ModifierKeys.Shift);
        SwitchKey.SelectedItem = settings.SwitchKey;
    }

    private void OnResetKeyboard(object sender, RoutedEventArgs args)
    {
        ShowKeyboardSettings(new KeyboardSettings());
        KeyboardStatus.Text = "已恢复默认值，尚未保存。";
    }

    private void RefreshAutomaticGroups()
    {
        AutomaticGroupList.ItemsSource = AutomaticGroups.Current;
        AutomaticStatus.Text = AutomaticGroups.Current.Count == 0 ? "暂无自动组" : "";
    }

    private void OnAddAutomaticGroup(object sender, RoutedEventArgs args) => EditAutomaticGroup(null);

    private void OnEditAutomaticGroup(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is AutomaticGroup group)
            EditAutomaticGroup(group);
    }

    private void EditAutomaticGroup(AutomaticGroup? group)
    {
        var editor = new FrmAutomaticGroup(group) { Owner = this };
        if (editor.ShowDialog() == true)
            RefreshAutomaticGroups();
    }

    private void OnDeleteAutomaticGroup(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is not AutomaticGroup group
            || MessageBox.Show(this, $"删除自动组“{group.Name}”？", "删除组", MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        try
        {
            AutomaticGroups.Save(AutomaticGroups.Current.Where(candidate => candidate.Id != group.Id).ToArray());
            RefreshAutomaticGroups();
        }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            AutomaticStatus.Text = $"删除失败：{error.Message}";
        }
    }

    private void OnSaveKeyboard(object sender, RoutedEventArgs args)
    {
        var settings = new KeyboardSettings
        {
            GroupModifiers = ReadModifiers(GroupControl.IsChecked, GroupAlt.IsChecked, GroupShift.IsChecked),
            SwitchModifiers = ReadModifiers(SwitchControl.IsChecked, SwitchAlt.IsChecked, SwitchShift.IsChecked),
            SwitchKey = SwitchKey.SelectedItem is Key key ? key : Key.None
        };
        try
        {
            settings.Save();
            KeyboardStatus.Text = "已保存并生效。";
        }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            KeyboardStatus.Text = $"保存失败：{error.Message}";
        }
    }

    private static ModifierKeys ReadModifiers(bool? control, bool? alt, bool? shift) =>
        (control == true ? ModifierKeys.Control : ModifierKeys.None)
        | (alt == true ? ModifierKeys.Alt : ModifierKeys.None)
        | (shift == true ? ModifierKeys.Shift : ModifierKeys.None);
}