using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace WhimGroupWin;

public partial class GroupBar : Window
{
    public static readonly DependencyProperty TabWidthProperty = DependencyProperty.Register(
        nameof(TabWidth), typeof(double), typeof(GroupBar), new PropertyMetadata(Configuration.DefaultTabWidth));

    private readonly List<GroupBarTab> tabs = [];
    private Point? dragOrigin;
    private nint? pressedTab;
    private bool acrylicEnabled;
    private Grid? splitTabsPanel;
    private ItemsControl? rightTabs;

    public virtual bool IsVertical => false;
    public virtual bool IsSplit => false;

    public double TabWidth
    {
        get => (double)GetValue(TabWidthProperty);
        private set => SetValue(TabWidthProperty, value);
    }

    public event Action<nint>? TabSelected;
    public event Action<nint>? TabCloseRequested;
    public event Action<nint>? TabRemoveRequested;
    public event Action? MinimizeRequested;
    public event Action? MaximizeRequested;
    public event Action? CloseGroupRequested;
    public event Action? PlacementChangeRequested;
    public event Action? SplitRequested;
    public event Action? ActivationRequested;
    public event Action? DragStarted;
    public event Action? DragCompleted;
    public event Action<int, int>? DragPositionChanged;

    public GroupBar()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
            var nonClientRendering = 2;
            DwmSetWindowAttribute(handle, 2, ref nonClientRendering, sizeof(int));
            var preference = 2;
            if (DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int)) < 0)
                WindowChrome.GetWindowChrome(this).CornerRadius = Configuration.FallbackWindowCornerRadius;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                var lightMode = 0;
                DwmSetWindowAttribute(handle, 20, ref lightMode, sizeof(int));
                var backdrop = 3;
                if (DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) >= 0)
                {
                    acrylicEnabled = true;
                    WindowChrome.GetWindowChrome(this).GlassFrameThickness = new Thickness(-1);
                    Background = Brushes.Transparent;
                    var source = HwndSource.FromHwnd(handle);
                    if (source?.CompositionTarget is { } target)
                        target.BackgroundColor = Colors.Transparent;
                }
            }
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, uint attribute, ref int value, int size);

    protected void ConfigureVerticalLayout()
    {
        DockPanel.SetDock(ExtensionsButton, Dock.Top);
        ExtensionsButton.HorizontalAlignment = HorizontalAlignment.Center;
        ExtensionsButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        PlacementMenuItem.Header = "重叠模式";
        PlacementMenuItem.Visibility = Visibility.Visible;
        DockPanel.SetDock(ActionsPanel, Dock.Bottom);
        ActionsPanel.HorizontalAlignment = HorizontalAlignment.Center;
        TabsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        TabsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Tabs.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
    }

    protected void ConfigureSplitLayout(bool enabled) => SplitMenuItem.Header = enabled ? "重叠模式" : "左右分屏";

    protected void ConfigureSplitTabs()
    {
        TabsScrollViewer.Content = null;
        TabsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        splitTabsPanel = new Grid();
        splitTabsPanel.ColumnDefinitions.Add(new ColumnDefinition());
        splitTabsPanel.ColumnDefinitions.Add(new ColumnDefinition());
        rightTabs = new ItemsControl { ItemTemplate = Tabs.ItemTemplate, ItemsPanel = Tabs.ItemsPanel };
        var leftScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = Tabs
        };
        var rightScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = rightTabs
        };
        Grid.SetColumn(rightScroll, 1);
        splitTabsPanel.Children.Add(leftScroll);
        splitTabsPanel.Children.Add(rightScroll);
        TabsScrollViewer.Content = splitTabsPanel;
    }

    protected void ArrangeSplitTabs(nint left, nint right, double divider, IReadOnlySet<nint> leftHandles)
    {
        if (splitTabsPanel is null || rightTabs is null)
            return;
        var available = Math.Max(0, ActualWidth - Configuration.ReservedBarWidth);
        var leftWidth = Math.Clamp(divider - Configuration.LeftSpacerWidth, 0, available);
        var rightWidth = available - leftWidth;
        splitTabsPanel.ColumnDefinitions[0].Width = new GridLength(leftWidth);
        var leftMembers = tabs.Where(tab => tab.Handle == left || leftHandles.Contains(tab.Handle))
            .OrderByDescending(tab => tab.Handle == left).ToArray();
        var rightMembers = tabs.Where(tab => tab.Handle != left && !leftHandles.Contains(tab.Handle))
            .OrderByDescending(tab => tab.Handle == right).ToArray();
        if (!Tabs.Items.Cast<GroupBarTab>().SequenceEqual(leftMembers))
            Tabs.ItemsSource = leftMembers;
        if (!rightTabs.Items.Cast<GroupBarTab>().SequenceEqual(rightMembers))
            rightTabs.ItemsSource = rightMembers;
        Tabs.Tag = Math.Max(0, Math.Min(Configuration.MaximumTabWidth,
            leftWidth / Math.Max(1, leftMembers.Length) - 2 * Configuration.TabHorizontalMargin));
        rightTabs.Tag = Math.Max(0, Math.Min(Configuration.MaximumTabWidth,
            rightWidth / Math.Max(1, rightMembers.Length) - 2 * Configuration.TabHorizontalMargin));
    }

    protected virtual void RefreshTabLayout() { }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint handle, uint message, nint wParam, nint lParam);

    private nint OnWindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (acrylicEnabled && message == 0x0086)
        {
            handled = true;
            return DefWindowProcW(handle, (uint)message, 1, lParam);
        }
        if (message == 0x0216 && lParam != 0)
            DragPositionChanged?.Invoke(Marshal.ReadInt32(lParam), Marshal.ReadInt32(lParam, sizeof(int)));
        return 0;
    }

    public void SetTabs(IEnumerable<(nint Handle, string Title)> members, nint selected)
    {
        tabs.Clear();
        tabs.AddRange(members.Select(member => new GroupBarTab(member.Handle, member.Title)));
        Tabs.ItemsSource = tabs.ToArray();
        SelectTab(selected);
        ResizeTabs();
    }

    public void UpdateTabTitle(nint handle, string title)
    {
        var tab = tabs.Find(candidate => candidate.Handle == handle);
        if (tab is not null)
            tab.Title = title;
    }

    public void SelectTab(nint selected)
    {
        foreach (var tab in tabs)
            tab.IsSelected = tab.Handle == selected;
    }

    private void ResizeTabs()
    {
        TabWidth = IsVertical
            ? Math.Max(1, ActualWidth - 2 * Configuration.TabHorizontalMargin)
            : Math.Max(Configuration.MinimumTabWidth,
                Math.Min(Configuration.MaximumTabWidth, (ActualWidth - Configuration.ReservedBarWidth)
                    / Math.Max(1, tabs.Count) - 2 * Configuration.TabHorizontalMargin));
        Tabs.Tag = TabWidth;
        RefreshTabLayout();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ResizeTabs();
    private void OnMinimize(object sender, RoutedEventArgs args) => MinimizeRequested?.Invoke();
    private void OnMaximize(object sender, RoutedEventArgs args) => MaximizeRequested?.Invoke();
    private void OnCloseGroup(object sender, RoutedEventArgs args) => CloseGroupRequested?.Invoke();
    private void OnOpenExtensions(object sender, RoutedEventArgs args)
    {
        var button = (Button)sender;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
    private void OnChangePlacement(object sender, RoutedEventArgs args) => PlacementChangeRequested?.Invoke();
    private void OnSplit(object sender, RoutedEventArgs args) => SplitRequested?.Invoke();
    private void OnSelect(object sender, RoutedEventArgs args) => TabSelected?.Invoke(((GroupBarTab)((FrameworkElement)sender).DataContext).Handle);
    private void OnCloseTab(object sender, RoutedEventArgs args) => TabCloseRequested?.Invoke(((GroupBarTab)((FrameworkElement)sender).DataContext).Handle);
    private void OnRemoveTab(object sender, RoutedEventArgs args) => TabRemoveRequested?.Invoke(((GroupBarTab)((FrameworkElement)sender).DataContext).Handle);

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        dragOrigin = null;
        pressedTab = null;
        for (var element = args.OriginalSource as DependencyObject; element is not null;
            element = VisualTreeHelper.GetParent(element))
        {
            if (element is not Button button)
                continue;
            if (button.Tag as string != "DragTab")
            {
                ActivationRequested?.Invoke();
                return;
            }
            if (button.DataContext is GroupBarTab tab)
                pressedTab = tab.Handle;
            break;
        }
        args.Handled = true;
        if (args.ClickCount == 2)
        {
            pressedTab = null;
            MaximizeRequested?.Invoke();
            return;
        }
        dragOrigin = args.GetPosition(this);
        Mouse.Capture(this, CaptureMode.SubTree);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (dragOrigin is null)
            return;
        dragOrigin = null;
        Mouse.Capture(null);
        args.Handled = true;
        var selectedTab = pressedTab;
        pressedTab = null;
        if (selectedTab is nint handle)
            TabSelected?.Invoke(handle);
        else
            ActivationRequested?.Invoke();
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            dragOrigin = null;
            return;
        }
        if (dragOrigin is not Point origin)
            return;
        var current = args.GetPosition(this);
        if (Math.Abs(current.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        dragOrigin = null;
        pressedTab = null;
        args.Handled = true;
        Mouse.Capture(null);
        DragStarted?.Invoke();
        try { DragMove(); }
        finally { DragCompleted?.Invoke(); }
    }

    private sealed class GroupBarTab(nint handle, string title) : INotifyPropertyChanged
    {
        private bool isSelected;
        public nint Handle { get; } = handle;
        public string Title
        {
            get => title;
            set
            {
                if (title == value)
                    return;
                title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}