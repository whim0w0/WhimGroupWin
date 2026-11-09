using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Rectangle rectangle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, uint attribute, out Rectangle rectangle, int size);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int left, int top);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point cursor);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint left, uint top, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint after, int left, int top, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int Left; public int Top; }
    [StructLayout(LayoutKind.Sequential)]
    private record struct Rectangle(int Left, int Top, int Right, int Bottom);

    [STAThread]
    private static int Main(string[] arguments)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (arguments.Length > 0 && arguments[0] != "--left-check" && arguments[0] != "--split-check" && arguments[0] != "--automatic-check" && arguments[0] != "--minimize-check" && arguments[0] != "--drag-check" && arguments[0] != "--overlap-drag-check" && arguments[0] != "--split-restore-check")
        {
            var fixture = new Window { Title = arguments[0], Left = arguments[0] == "fixture-a" ? 140 : 760, Top = 240, Width = 500, Height = 320 };
            fixture.Closed += (_, _) => application.Shutdown();
            application.Run(fixture);
            return 0;
        }
        var exitCode = 0;
        application.Startup += async (_, _) =>
        {
            IDisposable? controller = null;
            var first = new Window { Title = "Whim smoke A", Left = 160, Top = 220, Width = 520, Height = 340 };
            var second = new Window { Title = "Whim smoke B", Left = 740, Top = 220, Width = 560, Height = 380 };
            try
            {
                first.Show();
                second.Show();
                var firstHandle = new WindowInteropHelper(first).Handle;
                var secondHandle = new WindowInteropHelper(second).Handle;
                var type = typeof(WhimGroupWin.FrmSetting).Assembly.GetType("WhimGroupWin.GroupController")!;
                controller = (IDisposable)Activator.CreateInstance(type)!;
                type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [firstHandle, secondHandle]);
                var groups = (System.Collections.IList)type.GetField("groups", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
                Check(groups.Count == 1, "group created");
                var group = groups[0]!;
                void Invoke(string name, params object[] values) => group.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(group, values);
                await Task.Delay(300);
                GetWindowRect(firstHandle, out var firstRect);
                GetWindowRect(secondHandle, out var secondRect);
                Check(firstRect == secondRect, "selected member receives group bounds");
                Check(IsWindowVisible(firstHandle) && !IsWindowVisible(secondHandle), "only selected member visible");
                if (arguments.Contains("--split-restore-check"))
                {
                    bool SameSize(Rectangle actual, Rectangle expected) => actual.Right - actual.Left == expected.Right - expected.Left
                        && actual.Bottom - actual.Top == expected.Bottom - expected.Top;
                    Invoke("ConfigureAutomaticLayout", true);
                    await Task.Delay(250);
                    first.Width += 80;
                    await Task.Delay(250);
                    Invoke("ConfigureAutomaticLayout", false);
                    await Task.Delay(300);
                    GetWindowRect(firstHandle, out var restoredFirst);
                    GetWindowRect(secondHandle, out var restoredSecond);
                    Check(SameSize(restoredFirst, firstRect) && SameSize(restoredSecond, secondRect),
                        "leaving split restores pre-split overlap size after divider resize");
                    Invoke("ConfigureAutomaticLayout", true);
                    await Task.Delay(250);
                    var added = new Window { Title = "Split restore added", Left = 850, Top = 300, Width = 430, Height = 290 };
                    try
                    {
                        added.Show();
                        var addedHandle = new WindowInteropHelper(added).Handle;
                        GetWindowRect(addedHandle, out var originalAdded);
                        Invoke("Add", addedHandle);
                        Invoke("Select", addedHandle);
                        await Task.Delay(250);
                        var detachBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                        System.Windows.Controls.Button? FindRemoveButton(DependencyObject parent)
                        {
                            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                            {
                                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
                                if (child is System.Windows.Controls.Button button && button.Content as string == "\\"
                                    && button.DataContext is { } tab
                                    && tab.GetType().GetProperty("Handle")?.GetValue(tab) is nint handle && handle == addedHandle)
                                    return button;
                                if (FindRemoveButton(child) is { } nested)
                                    return nested;
                            }
                            return null;
                        }
                        detachBar.UpdateLayout();
                        var removeButton = FindRemoveButton(detachBar);
                        Check(removeButton is not null, "tab exposes backslash remove button");
                        removeButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        await Task.Delay(250);
                        GetWindowRect(addedHandle, out var removedAdded);
                        Check(groups.Count == 1 && SameSize(removedAdded, originalAdded),
                            "removing split member restores its own size without dissolving remaining group");
                        Check(added.IsVisible && !(bool)group.GetType().GetMethod("Contains")!.Invoke(group, [addedHandle])!,
                            "remove button detaches window without closing it");
                        Invoke("Add", addedHandle);
                        Invoke("Select", addedHandle);
                        await Task.Delay(250);
                        Invoke("Dissolve");
                        await Task.Delay(300);
                        GetWindowRect(firstHandle, out restoredFirst);
                        GetWindowRect(addedHandle, out var restoredAdded);
                        Check(SameSize(restoredFirst, firstRect), "dissolving split restores original member size");
                        Check(SameSize(restoredAdded, originalAdded), "dissolving split restores later member own size");
                    }
                    finally { added.Close(); }
                    type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [firstHandle, secondHandle]);
                    group = groups[0]!;
                    await Task.Delay(200);
                    GetWindowRect(firstHandle, out var beforeSplitClose);
                    Invoke("ConfigureAutomaticLayout", true);
                    await Task.Delay(250);
                    Invoke("CloseMember", secondHandle);
                    await Task.Delay(400);
                    GetWindowRect(firstHandle, out restoredFirst);
                    Check(groups.Count == 0 && SameSize(restoredFirst, beforeSplitClose),
                        "closing split companion restores surviving member size");
                    return;
                }
                if (arguments.Contains("--drag-check") || arguments.Contains("--overlap-drag-check"))
                {
                    var split = !arguments.Contains("--overlap-drag-check");
                    Invoke("ConfigureAutomaticLayout", split);
                    await Task.Delay(300);
                    var currentBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                    var barHandle = new WindowInteropHelper(currentBar).Handle;
                    currentBar.Activate();
                    await Task.Delay(200);
                    GetWindowRect(barHandle, out var beforeBar);
                    GetWindowRect(firstHandle, out var beforeFirst);
                    GetWindowRect(secondHandle, out var beforeSecond);
                    var dragObserved = false;
                    currentBar.DragStarted += () => dragObserved = true;
                    System.Windows.Controls.Button? FindDragTab(DependencyObject parent)
                    {
                        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                        {
                            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
                            if (child is System.Windows.Controls.Button button && button.Tag as string == "DragTab")
                                return button;
                            if (FindDragTab(child) is { } nested)
                                return nested;
                        }
                        return null;
                    }
                    currentBar.UpdateLayout();
                    var dragTab = FindDragTab(currentBar)!;
                    var clickPoint = dragTab.PointToScreen(new System.Windows.Point(dragTab.ActualWidth / 2, dragTab.ActualHeight / 2));
                    var cursorLeft = (int)clickPoint.X;
                    var cursorTop = (int)clickPoint.Y;
                    SetCursorPos(cursorLeft, cursorTop);
                    mouse_event(0x0002, 0, 0, 0, 0);
                    try
                    {
                        await Task.Delay(100);
                        Check(System.Windows.Input.Mouse.Captured == currentBar, "group bar captures tab press before dragging");
                        await Task.Run(async () =>
                        {
                            for (var step = 1; step <= 12; step++)
                            {
                                await Task.Delay(40);
                                SetCursorPos(cursorLeft + step * 8, cursorTop + step * 3);
                            }
                            mouse_event(0x0004, 0, 0, 0, 0);
                        });
                    }
                    finally
                    {
                        mouse_event(0x0004, 0, 0, 0, 0);
                    }
                    await Task.Delay(400);
                    GetWindowRect(barHandle, out var afterBar);
                    GetWindowRect(firstHandle, out var afterFirst);
                    GetWindowRect(secondHandle, out var afterSecond);
                    Check(dragObserved, "native mouse drag enters group bar move loop");
                    Check(afterBar.Left > beforeBar.Left && afterBar.Top > beforeBar.Top, "native mouse drag moves group bar");
                    Check(afterFirst.Left - beforeFirst.Left == afterBar.Left - beforeBar.Left
                        && afterSecond.Left - beforeSecond.Left == afterBar.Left - beforeBar.Left
                        && afterFirst.Top - beforeFirst.Top == afterBar.Top - beforeBar.Top
                        && afterSecond.Top - beforeSecond.Top == afterBar.Top - beforeBar.Top,
                        "native mouse drag keeps both members aligned with group bar");
                    var clickedTab = false;
                    currentBar.TabSelected += _ => clickedTab = true;
                    clickPoint = dragTab.PointToScreen(new System.Windows.Point(dragTab.ActualWidth / 2, dragTab.ActualHeight / 2));
                    SetCursorPos((int)clickPoint.X, (int)clickPoint.Y);
                    mouse_event(0x0002, 0, 0, 0, 0);
                    await Task.Delay(100);
                    mouse_event(0x0004, 0, 0, 0, 0);
                    await Task.Delay(150);
                    Check(clickedTab && System.Windows.Input.Mouse.Captured is null, "tab click selects member and releases mouse capture");
                    var boundsField = group.GetType().GetField("bounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var targetBounds = boundsField.GetValue(group)!;
                    var boundsType = targetBounds.GetType();
                    int Coordinate(string name) => (int)boundsType.GetProperty(name)!.GetValue(targetBounds)!;
                    boundsField.SetValue(group, Activator.CreateInstance(boundsType, Coordinate("Left") + 40,
                        Coordinate("Top") + 20, Coordinate("Right") + 40, Coordinate("Bottom") + 20));
                    if (!split)
                    {
                        var pendingField = group.GetType().GetField("pendingOverlapMove", BindingFlags.NonPublic | BindingFlags.Instance)!;
                        var pendingHandle = (nint)group.GetType().GetField("selected", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                        pendingField.SetValue(group, Activator.CreateInstance(Nullable.GetUnderlyingType(pendingField.FieldType)!,
                            pendingHandle, boundsField.GetValue(group)!, Environment.TickCount64 + 2000));
                        Invoke("Update");
                        GetWindowRect(barHandle, out var waitingBar);
                        Check(waitingBar.Left == afterBar.Left + 40 && waitingBar.Top == afterBar.Top + 20,
                            "overlap bar retains target position while member move is pending");
                        Check((int)boundsType.GetProperty("Left")!.GetValue(boundsField.GetValue(group))! == Coordinate("Left") + 40,
                            "old member bounds do not overwrite pending overlap layout");
                        return;
                    }
                    var pending = (System.Collections.IDictionary)group.GetType().GetField("pendingSplitPlacements", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                    pending[firstHandle] = Activator.CreateInstance(boundsType, afterFirst.Left, afterFirst.Top, afterFirst.Right, afterFirst.Bottom)!;
                    pending[secondHandle] = Activator.CreateInstance(boundsType, afterSecond.Left, afterSecond.Top, afterSecond.Right, afterSecond.Bottom)!;
                    Invoke("PositionBar");
                    GetWindowRect(barHandle, out var pendingBar);
                    Check(pendingBar.Left == afterBar.Left + 40 && pendingBar.Top == afterBar.Top + 20,
                        "split bar uses target coordinates while member placements are pending");
                    return;
                }
                if (arguments.Contains("--minimize-check"))
                {
                    foreach (var split in new[] { false, true })
                    {
                        Invoke("ConfigureAutomaticLayout", split);
                        Invoke("Select", firstHandle);
                        await Task.Delay(200);
                        var currentBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                        ShowWindowAsync(firstHandle, 6);
                        await Task.Delay(200);
                        Invoke("Update");
                        await Task.Delay(200);
                        Check(!currentBar.IsVisible && IsIconic(firstHandle), "external minimize hides group bar");
                        if (split)
                            Check(IsIconic(secondHandle), "external minimize also minimizes split companion");
                        first.WindowState = WindowState.Normal;
                        first.Activate();
                        await Task.Delay(200);
                        Invoke("Update");
                        await Task.Delay(200);
                        Check(currentBar.IsVisible && !IsIconic(firstHandle), "external restore restores group bar");
                        Invoke("SetState", 6);
                        await Task.Delay(200);
                        Check(!currentBar.IsVisible, "bar minimize command hides group bar");
                        Invoke("Select", firstHandle);
                        await Task.Delay(200);
                        Check(currentBar.IsVisible && !IsIconic(firstHandle), "select restores minimized group bar");
                    }
                    return;
                }
                if (arguments.Contains("--automatic-check"))
                {
                    var settings = new WhimGroupWin.FrmSetting();
                    var navigation = (System.Windows.Controls.TabControl)((System.Windows.Controls.DockPanel)settings.Content).Children[1];
                    Check(navigation.Items.Cast<System.Windows.Controls.TabItem>().Any(item => (string)item.Header == "自动"), "settings includes automatic page");
                    var definition = new WhimGroupWin.AutomaticGroup
                    {
                        Name = "Smoke automatic group",
                        Split = true,
                        Applications = [
                            new() { ExecutablePath = Environment.ProcessPath!, WindowTitle = first.Title },
                            new() { ExecutablePath = Environment.ProcessPath!, WindowTitle = second.Title }]
                    };
                    var store = type.Assembly.GetType("WhimGroupWin.AutomaticGroups")!;
                    var temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"whim-automatic-{Guid.NewGuid():N}.json");
                    try
                    {
                        store.GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [temporary, new[] { definition }]);
                        var loaded = (List<WhimGroupWin.AutomaticGroup>)store.GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [temporary])!;
                        Check(loaded.Count == 1 && loaded[0].Name == definition.Name && loaded[0].Split
                            && loaded[0].Applications[1].WindowTitle == second.Title && loaded[0].Key == definition.Key, "automatic configuration JSON round trip");
                        var rejected = false;
                        try
                        {
                            store.GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                                [new[] { definition, definition with { Id = Guid.NewGuid() } }]);
                        }
                        catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { rejected = true; }
                        Check(rejected, "duplicate automatic shortcuts are rejected");
                        var editor = new WhimGroupWin.FrmAutomaticGroup(definition);
                        Check(((System.Windows.Controls.TextBox)editor.FindName("GroupName")).Text == definition.Name
                            && ((System.Windows.Controls.DataGrid)editor.FindName("ApplicationsList")).Items.Count == 2,
                            "automatic editor restores application list and group name");
                        editor.Close();
                        Invoke("Dissolve");
                        type.GetMethod("AttachAutomaticGroup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller,
                            [definition, new[] { firstHandle, secondHandle }]);
                        group = groups[0]!;
                        await Task.Delay(250);
                        var automaticBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                        Check(automaticBar.IsSplit && IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle), "automatic group applies split arrangement");
                        Invoke("ToggleHidden");
                        await Task.Delay(150);
                        Check(!automaticBar.IsVisible && !IsWindowVisible(firstHandle) && !IsWindowVisible(secondHandle), "automatic hide includes bar and all members");
                        Invoke("ToggleHidden");
                        await Task.Delay(150);
                        Check(automaticBar.IsVisible && IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle), "automatic show restores split members and bar");
                        Invoke("Dissolve");
                        Check(groups.Count == 0 && IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle), "removing automatic group restores windows");
                        Process StartAutomaticFixture(string title)
                        {
                            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                            if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                                start.ArgumentList.Add(typeof(Program).Assembly.Location);
                            start.ArgumentList.Add(title);
                            return Process.Start(start)!;
                        }
                        using var externalFirst = StartAutomaticFixture("automatic-fixture-a");
                        using var externalSecond = StartAutomaticFixture("automatic-fixture-b");
                        ((System.Windows.Threading.DispatcherTimer)type.GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!).Stop();
                        var currentProperty = store.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!;
                        var originalDefinitions = currentProperty.GetValue(null);
                        try
                        {
                            for (var attempt = 0; attempt < 50; attempt++)
                            {
                                externalFirst.Refresh();
                                externalSecond.Refresh();
                                if (externalFirst.MainWindowHandle != 0 && externalSecond.MainWindowHandle != 0)
                                    break;
                                await Task.Delay(100);
                            }
                            Check(externalFirst.MainWindowHandle != 0 && externalSecond.MainWindowHandle != 0, "automatic matching fixtures ready");
                            var externalDefinition = definition with
                            {
                                Applications = [
                                    new() { ExecutablePath = externalFirst.MainModule!.FileName!, WindowTitle = "FIXTURE-A" },
                                    new() { ExecutablePath = externalSecond.MainModule!.FileName!, WindowTitle = "FIXTURE-B" }]
                            };
                            currentProperty.SetValue(null, new[] { externalDefinition });
                            type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                            await Task.Delay(250);
                            Check(groups.Count == 1, "path and case-insensitive title substring matching creates a cross-process group");
                            type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                            Check(groups.Count == 1, "repeated automatic scan does not duplicate groups");
                            type.GetMethod("ToggleAutomaticGroup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [externalDefinition.Id]);
                            await Task.Delay(150);
                            Check(!IsWindowVisible(externalFirst.MainWindowHandle) && !IsWindowVisible(externalSecond.MainWindowHandle), "automatic shortcut dispatch hides matched group");
                            type.GetMethod("ToggleAutomaticGroup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [externalDefinition.Id]);
                            await Task.Delay(150);
                            Check(IsWindowVisible(externalFirst.MainWindowHandle) && IsWindowVisible(externalSecond.MainWindowHandle), "automatic shortcut dispatch shows matched group");
                            type.GetMethod("OnAutomaticGroupsChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                            var singleRule = externalDefinition with
                            {
                                Applications = [new() { ExecutablePath = externalFirst.MainModule!.FileName! }]
                            };
                            store.GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [temporary, new[] { singleRule }]);
                            loaded = (List<WhimGroupWin.AutomaticGroup>)store.GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [temporary])!;
                            Check(loaded.Single().Applications.Count == 1, "single application rule JSON round trip");
                            var singleEditor = new WhimGroupWin.FrmAutomaticGroup(loaded.Single());
                            Check(((System.Windows.Controls.DataGrid)singleEditor.FindName("ApplicationsList")).Items.Count == 1,
                                "single application editor restores rule");
                            singleEditor.Close();
                            foreach (var split in new[] { false, true })
                            {
                                var layoutDefinition = singleRule with { Split = split };
                                currentProperty.SetValue(null, new[] { layoutDefinition });
                                type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller,
                                    [externalSecond.MainWindowHandle, firstHandle]);
                                type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                                Check(groups.Count == 1, "single available match waits and does not steal manual members");
                                group = groups[0]!;
                                Invoke("Dissolve");
                                type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                                await Task.Delay(250);
                                Check(groups.Count == 1, "single application rule groups multiple processes");
                                group = groups[0]!;
                                var members = (List<nint>)group.GetType().GetProperty("Members")!.GetValue(group)!;
                                automaticBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                                Check(members.Count == 2 && automaticBar.IsSplit == split, "single application group applies requested layout");
                                Invoke("ToggleSplit");
                                await Task.Delay(150);
                                automaticBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                                Check(automaticBar.IsSplit == !split, "manual layout switch overrides initial automatic layout");
                                Invoke("ToggleHidden");
                                await Task.Delay(150);
                                using var externalThird = StartAutomaticFixture("automatic-fixture-c");
                                try
                                {
                                    for (var attempt = 0; attempt < 50; attempt++)
                                    {
                                        externalThird.Refresh();
                                        if (externalThird.MainWindowHandle != 0)
                                            break;
                                        await Task.Delay(100);
                                    }
                                    var thirdHandle = externalThird.MainWindowHandle;
                                    Check(thirdHandle != 0, "later same application fixture ready");
                                    type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                                    await Task.Delay(150);
                                    type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                                    Check(groups.Count == 1 && members.Count == 3 && members.Distinct().Count() == 3,
                                        "later same application joins existing group without duplicates");
                                    Check(ReferenceEquals(automaticBar, group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group))
                                        && automaticBar.IsSplit == !split,
                                        "adding automatic member preserves manually selected layout and bar");
                                    Check(!automaticBar.IsVisible && members.All(handle => !IsWindowVisible(handle)),
                                        "hidden automatic group retains all members and hides new window");
                                    type.GetMethod("ToggleAutomaticGroup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [layoutDefinition.Id]);
                                    await Task.Delay(150);
                                    Check(automaticBar.IsVisible && members.Count(IsWindowVisible) == (!split ? 2 : 1),
                                        "single application shortcut restores requested visible slots");
                                    Invoke("Select", thirdHandle);
                                    await Task.Delay(150);
                                    Check(IsWindowVisible(thirdHandle), "later same application member can be selected");
                                    var barTabs = (System.Collections.IEnumerable)typeof(WhimGroupWin.GroupBar)
                                        .GetField("tabs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(automaticBar)!;
                                    var detachedTab = barTabs.Cast<object>().Single(tab =>
                                        (nint)tab.GetType().GetProperty("Handle")!.GetValue(tab)! == thirdHandle);
                                    typeof(WhimGroupWin.GroupBar).GetMethod("OnRemoveTab", BindingFlags.NonPublic | BindingFlags.Instance)!
                                        .Invoke(automaticBar, [new System.Windows.Controls.Button { DataContext = detachedTab }, new RoutedEventArgs()]);
                                    for (var scan = 0; scan < 3; scan++)
                                        type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                                    Check(!members.Contains(thirdHandle) && IsWindowVisible(thirdHandle) && members.Count == 2,
                                        "detached automatic member stays independent after repeated scans");
                                    type.GetMethod("ToggleAutomaticGroup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [layoutDefinition.Id]);
                                    await Task.Delay(150);
                                    Check(!members.Contains(thirdHandle) && IsWindowVisible(thirdHandle),
                                        "automatic shortcut does not reattach or hide detached member");
                                    type.GetMethod("OnAutomaticGroupsChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                                    type.GetMethod("ApplyAutomaticGroups", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                                    group = groups[0]!;
                                    members = (List<nint>)group.GetType().GetProperty("Members")!.GetValue(group)!;
                                    Check(members.Count == 2 && !members.Contains(thirdHandle),
                                        "automatic rule refresh and group recreation preserve detached exclusion");
                                }
                                finally
                                {
                                    Invoke("Dissolve");
                                    externalThird.CloseMainWindow();
                                    await externalThird.WaitForExitAsync();
                                }
                            }
                        }
                        finally
                        {
                            currentProperty.SetValue(null, originalDefinitions);
                            type.GetMethod("OnAutomaticGroupsChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
                            externalFirst.CloseMainWindow();
                            externalSecond.CloseMainWindow();
                        }
                    }
                    finally
                    {
                        settings.Close();
                        System.IO.File.Delete(temporary);
                    }
                    return;
                }
                if (arguments.Contains("--split-check"))
                {
                    WhimGroupWin.GroupBar CurrentBar() => (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                    System.Windows.Controls.MenuItem SplitMenu(WhimGroupWin.GroupBar currentBar) =>
                        (System.Windows.Controls.MenuItem)((System.Windows.Controls.Button)((System.Windows.Controls.DockPanel)currentBar.Content).Children[0]).ContextMenu.Items[1];
                    SplitMenu(CurrentBar()).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                    await Task.Delay(400);
                    var splitBar = CurrentBar();
                    void CheckTabRegions(string stage)
                    {
                        splitBar.UpdateLayout();
                        IEnumerable<System.Windows.Controls.Button> TabButtons(DependencyObject parent)
                        {
                            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                            {
                                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
                                if (child is System.Windows.Controls.Button button && button.Tag as string == "DragTab")
                                    yield return button;
                                foreach (var nested in TabButtons(child))
                                    yield return nested;
                            }
                        }
                        var buttons = TabButtons(splitBar).ToArray();
                        Check(buttons.Length == 2, $"{stage}: both split tabs rendered");
                        foreach (var button in buttons)
                        {
                            var member = (nint)button.DataContext.GetType().GetProperty("Handle")!.GetValue(button.DataContext)!;
                            DwmGetWindowAttribute(member, 9, out var memberFrame, Marshal.SizeOf<Rectangle>());
                            var tab = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(
                                System.Windows.Media.VisualTreeHelper.GetParent(button));
                            var start = tab.PointToScreen(new System.Windows.Point());
                            var end = tab.PointToScreen(new System.Windows.Point(tab.ActualWidth, 0));
                            Check(tab.ActualWidth > 0 && start.X >= memberFrame.Left - 1 && end.X <= memberFrame.Right + 1,
                                $"{stage}: {member} tab stays above its own window");
                        }
                    }
                    Check(splitBar is WhimGroupWin.FrmSplitGroupBar && splitBar.IsSplit, "split uses dedicated group bar");
                    Check(SplitMenu(splitBar).IsChecked, "split menu indicates active mode");
                    GetWindowRect(firstHandle, out firstRect);
                    GetWindowRect(secondHandle, out secondRect);
                    Check(IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle), "both split members remain visible");
                    var leftRect = firstRect.Left < secondRect.Left ? firstRect : secondRect;
                    var rightRect = firstRect.Left < secondRect.Left ? secondRect : firstRect;
                    Check(leftRect.Right >= rightRect.Left && leftRect.Top == rightRect.Top && leftRect.Bottom == rightRect.Bottom,
                        "members occupy adjacent left and right halves");
                    DwmGetWindowAttribute(firstHandle, 9, out var firstFrame, Marshal.SizeOf<Rectangle>());
                    DwmGetWindowAttribute(secondHandle, 9, out var secondFrame, Marshal.SizeOf<Rectangle>());
                    Check(Math.Min(firstFrame.Right, secondFrame.Right) == Math.Max(firstFrame.Left, secondFrame.Left),
                        "visible split edges touch without a gap");
                    GetWindowRect(new WindowInteropHelper(splitBar).Handle, out var splitBarRect);
                    Check(splitBarRect.Left == Math.Min(firstFrame.Left, secondFrame.Left)
                        && splitBarRect.Right == Math.Max(firstFrame.Right, secondFrame.Right), "split bar spans both windows");
                    CheckTabRegions("initial split");
                    var interloper = new Window { Title = "Split z-order check", Left = 180, Top = 240, Width = 400, Height = 300 };
                    try
                    {
                        interloper.Show();
                        var interloperHandle = new WindowInteropHelper(interloper).Handle;
                        var selectedHandle = (nint)group.GetType().GetField("selected", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                        interloper.Activate();
                        await Task.Delay(150);
                        ((Action)typeof(WhimGroupWin.GroupBar).GetField("ActivationRequested", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .GetValue(splitBar)!).Invoke();
                        await Task.Delay(150);
                        Check(GetForegroundWindow() == selectedHandle, "clicking the group bar activates its selected child window");
                        var leftHandle = firstRect.Left < secondRect.Left ? firstHandle : secondHandle;
                        var rightOrderHandle = leftHandle == firstHandle ? secondHandle : firstHandle;
                        foreach (var active in new[] { leftHandle, rightOrderHandle })
                        {
                            interloper.Activate();
                            await Task.Delay(100);
                            Invoke("Select", active);
                            await Task.Delay(200);
                            var follower = active == leftHandle ? rightOrderHandle : leftHandle;
                            Check(GetWindow(active, 2) == follower, "inactive split member is immediately behind the active member");
                            Check(GetWindow(active, 3) == new WindowInteropHelper(splitBar).Handle,
                                "split bar stays immediately above the active member");
                        }
                        var orderBarHandle = new WindowInteropHelper(splitBar).Handle;
                        SetForegroundWindow(orderBarHandle);
                        SetWindowPos(interloperHandle, rightOrderHandle, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                        Invoke("Update");
                        await Task.Delay(150);
                        Check(GetWindow(rightOrderHandle, 2) == leftHandle,
                            "bar foreground repairs an interleaved unrelated window");
                        var movingOrderField = group.GetType().GetField("moving", BindingFlags.NonPublic | BindingFlags.Instance)!;
                        movingOrderField.SetValue(group, true);
                        try
                        {
                            SetWindowPos(interloperHandle, rightOrderHandle, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                            Invoke("Update");
                            await Task.Delay(150);
                            Check(GetWindow(rightOrderHandle, 2) == leftHandle,
                                "bar dragging continues repairing split member z-order");
                        }
                        finally
                        {
                            movingOrderField.SetValue(group, false);
                        }
                        interloper.Activate();
                        await Task.Delay(200);
                        var interloperAboveBar = false;
                        for (var previous = GetWindow(new WindowInteropHelper(splitBar).Handle, 3); previous != 0; previous = GetWindow(previous, 3))
                            if (previous == interloperHandle)
                                interloperAboveBar = true;
                        Check(interloperAboveBar && !splitBar.Topmost, "unrelated foreground window remains above the non-topmost split group");
                        GetWindowRect(firstHandle, out var afterOrderFirst);
                        GetWindowRect(secondHandle, out var afterOrderSecond);
                        Check(afterOrderFirst == firstRect && afterOrderSecond == secondRect, "split z-order changes preserve both window rectangles");
                    }
                    finally
                    {
                        interloper.Close();
                        Invoke("Select", firstHandle);
                    }
                    var addedLeft = new Window { Title = "Split added left", Left = 100, Top = 500, Width = 700, MinWidth = 650, Height = 300 };
                    var addedRight = new Window { Title = "Split added right", Left = 600, Top = 500, Width = 400, Height = 300 };
                    try
                    {
                        addedLeft.Show();
                        addedRight.Show();
                        var addedLeftHandle = new WindowInteropHelper(addedLeft).Handle;
                        var addedRightHandle = new WindowInteropHelper(addedRight).Handle;
                        var originalLeft = firstRect.Left < secondRect.Left ? firstHandle : secondHandle;
                        var originalRight = originalLeft == firstHandle ? secondHandle : firstHandle;
                        GetWindowRect(originalLeft, out var unchangedLeft);
                        GetWindowRect(originalRight, out var unchangedRight);
                        type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [addedLeftHandle, originalLeft]);
                        await Task.Delay(300);
                        GetWindowRect(originalRight, out var afterLeftAddRight);
                        GetWindowRect(originalLeft, out var afterLeftAddLeft);
                        Check(afterLeftAddLeft == unchangedLeft, "adding a minimum-width member preserves the original hidden window bounds");
                        Check(groups.Count == 1 && IsWindowVisible(addedLeftHandle) && !IsWindowVisible(originalLeft)
                            && IsWindowVisible(originalRight) && unchangedRight == afterLeftAddRight,
                            "combining onto left replaces only the left visible member");
                        type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [addedRightHandle, originalRight]);
                        await Task.Delay(300);
                        Check(IsWindowVisible(addedLeftHandle) && IsWindowVisible(addedRightHandle)
                            && !IsWindowVisible(originalLeft) && !IsWindowVisible(originalRight),
                            "combining onto right preserves the added left member");
                        GetWindowRect(addedLeftHandle, out var beforeRightFocus);
                        GetWindowRect(addedRightHandle, out var completedRightPlacement);
                        var placements = (System.Collections.IDictionary)group.GetType().GetField("pendingSplitPlacements", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                        var observedBounds = (System.Collections.IDictionary)group.GetType().GetField("splitMemberBounds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                        var rectType = observedBounds[addedRightHandle]!.GetType();
                        var stalePlacement = Activator.CreateInstance(rectType,
                            [completedRightPlacement.Left + 80, completedRightPlacement.Top + 120,
                             completedRightPlacement.Right + 80, completedRightPlacement.Bottom + 120])!;
                        placements[addedRightHandle] = stalePlacement;
                        observedBounds[addedRightHandle] = stalePlacement;
                        SetForegroundWindow(addedRightHandle);
                        Invoke("Update");
                        await Task.Delay(200);
                        GetWindowRect(addedLeftHandle, out var afterRightFocus);
                        Check(afterRightFocus == beforeRightFocus,
                            "focusing right after asynchronous placement does not move the left member");
                        Check(!placements.Contains(addedRightHandle), "completed asynchronous placement establishes the actual window baseline");
                        var members = (System.Collections.IList)group.GetType().GetProperty("Members")!.GetValue(group)!;
                        Check(members.Count == 4, "both sides retain original members as tabs");
                        var leftTabs = (System.Windows.Controls.ItemsControl)splitBar.FindName("Tabs");
                        var leftHandles = leftTabs.Items.Cast<object>().Select(tab => (nint)tab.GetType().GetProperty("Handle")!.GetValue(tab)!).ToArray();
                        Check(leftHandles.Length == 2 && leftHandles.Contains(originalLeft) && leftHandles.Contains(addedLeftHandle),
                            "left-side tabs retain their assigned members");
                        Invoke("Select", originalLeft);
                        await Task.Delay(200);
                        Check(IsWindowVisible(originalLeft) && !IsWindowVisible(addedLeftHandle) && IsWindowVisible(addedRightHandle),
                            "selecting an old left tab changes only the left column");
                        Invoke("Select", originalRight);
                        await Task.Delay(200);
                        Check(IsWindowVisible(originalLeft) && IsWindowVisible(originalRight) && !IsWindowVisible(addedRightHandle),
                            "selecting an old right tab changes only the right column");
                        Invoke("Remove", addedLeftHandle);
                        Invoke("Remove", addedRightHandle);
                        Invoke("Select", firstHandle);
                        await Task.Delay(200);
                    }
                    finally
                    {
                        addedLeft.Close();
                        addedRight.Close();
                    }
                    Invoke("SelectPrevious");
                    await Task.Delay(200);
                    GetWindowRect(firstHandle, out var afterSelect);
                    Check(afterSelect == firstRect && IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle), "focus switching preserves split slots");
                    CheckTabRegions("focus switch");
                    var dragStarted = (Action)typeof(WhimGroupWin.GroupBar).GetField("DragStarted", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(splitBar)!;
                    var dragCompleted = (Action)typeof(WhimGroupWin.GroupBar).GetField("DragCompleted", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(splitBar)!;
                    dragStarted();
                    splitBar.Left += 24;
                    await Task.Delay(200);
                    GetWindowRect(firstHandle, out var duringDragFirst);
                    GetWindowRect(secondHandle, out var duringDragSecond);
                    Check(duringDragFirst.Left > firstRect.Left
                        && duringDragSecond.Left - secondRect.Left == duringDragFirst.Left - firstRect.Left,
                        "both split columns follow before drag completes");
                    Check(duringDragFirst.Right - duringDragFirst.Left == firstRect.Right - firstRect.Left
                        && duringDragSecond.Right - duringDragSecond.Left == secondRect.Right - secondRect.Left,
                        "split drag preserves window sizes");
                    dragCompleted();
                    await Task.Delay(200);
                    GetWindowRect(firstHandle, out var draggedFirst);
                    GetWindowRect(secondHandle, out var draggedSecond);
                    Check(draggedFirst.Left > firstRect.Left && draggedSecond.Left - secondRect.Left == draggedFirst.Left - firstRect.Left,
                        "split bar drag moves both columns together");
                    Invoke("Select", firstHandle);
                    await Task.Delay(200);
                    GetWindowRect(firstHandle, out var beforeNativeFirst);
                    GetWindowRect(secondHandle, out var beforeNativeSecond);
                    first.Left += 32;
                    first.Top += 16;
                    await Task.Delay(300);
                    GetWindowRect(firstHandle, out var nativeFirst);
                    GetWindowRect(secondHandle, out var nativeSecond);
                    Check(nativeFirst.Left > beforeNativeFirst.Left
                        && nativeSecond.Left - beforeNativeSecond.Left == nativeFirst.Left - beforeNativeFirst.Left
                        && nativeSecond.Top - beforeNativeSecond.Top == nativeFirst.Top - beforeNativeFirst.Top,
                        "moving first native window moves whole split group");
                    Invoke("Select", secondHandle);
                    await Task.Delay(200);
                    GetWindowRect(firstHandle, out beforeNativeFirst);
                    GetWindowRect(secondHandle, out beforeNativeSecond);
                    second.Left += 24;
                    await Task.Delay(300);
                    GetWindowRect(firstHandle, out nativeFirst);
                    GetWindowRect(secondHandle, out nativeSecond);
                    Check(nativeSecond.Left > beforeNativeSecond.Left
                        && nativeFirst.Left - beforeNativeFirst.Left == nativeSecond.Left - beforeNativeSecond.Left,
                        "moving second native window moves whole split group");
                    var rightHandle = nativeFirst.Left > nativeSecond.Left ? firstHandle : secondHandle;
                    Invoke("Select", rightHandle);
                    await Task.Delay(150);
                    GetWindowRect(firstHandle, out var beforeRapidFirst);
                    GetWindowRect(secondHandle, out var beforeRapidSecond);
                    GetWindowRect(rightHandle, out var beforeRapidRight);
                    for (var step = 0; step < 80; step++)
                    {
                        SetWindowPos(rightHandle, 0, beforeRapidRight.Left + (step % 2 == 0 ? 96 : -48),
                            beforeRapidRight.Top + step % 3 * 12, 0, 0, 0x0001 | 0x0004 | 0x0010);
                        Invoke("Update");
                        GetWindowRect(firstHandle, out var rapidFirst);
                        GetWindowRect(secondHandle, out var rapidSecond);
                        if (rapidFirst.Right - rapidFirst.Left != beforeRapidFirst.Right - beforeRapidFirst.Left
                            || rapidFirst.Bottom - rapidFirst.Top != beforeRapidFirst.Bottom - beforeRapidFirst.Top
                            || rapidSecond.Right - rapidSecond.Left != beforeRapidSecond.Right - beforeRapidSecond.Left
                            || rapidSecond.Bottom - rapidSecond.Top != beforeRapidSecond.Bottom - beforeRapidSecond.Top)
                            throw new InvalidOperationException($"Rapid right-window move changed member sizes at step {step}: {beforeRapidFirst} -> {rapidFirst}; {beforeRapidSecond} -> {rapidSecond}");
                    }
                    await Task.Delay(300);
                    Check(true, "rapid right-window movement preserves both member sizes");
                    Invoke("Select", firstHandle);
                    await Task.Delay(150);
                    GetWindowRect(firstHandle, out var beforeResize);
                    first.Width += 32;
                    await Task.Delay(350);
                    GetWindowRect(firstHandle, out var afterResize);
                    DwmGetWindowAttribute(firstHandle, 9, out firstFrame, Marshal.SizeOf<Rectangle>());
                    DwmGetWindowAttribute(secondHandle, 9, out secondFrame, Marshal.SizeOf<Rectangle>());
                    Check(afterResize.Right - afterResize.Left > beforeResize.Right - beforeResize.Left,
                        "native resize persists instead of snapping back");
                    Check(Math.Min(firstFrame.Right, secondFrame.Right) == Math.Max(firstFrame.Left, secondFrame.Left),
                        "resizing keeps visible divider edges touching");
                    CheckTabRegions("first divider resize");
                    Invoke("Select", secondHandle);
                    await Task.Delay(150);
                    second.Left -= 20;
                    second.Width += 20;
                    await Task.Delay(350);
                    DwmGetWindowAttribute(firstHandle, 9, out firstFrame, Marshal.SizeOf<Rectangle>());
                    DwmGetWindowAttribute(secondHandle, 9, out secondFrame, Marshal.SizeOf<Rectangle>());
                    Check(Math.Min(firstFrame.Right, secondFrame.Right) == Math.Max(firstFrame.Left, secondFrame.Left),
                        "opposite member resize keeps divider touching");
                    CheckTabRegions("opposite divider resize");
                    Invoke("SetState", 6);
                    await Task.Delay(200);
                    Check(IsIconic(firstHandle) && IsIconic(secondHandle) && !splitBar.IsVisible, "split minimizes both windows and hides bar");
                    Invoke("Select", firstHandle);
                    await Task.Delay(300);
                    Check(!IsIconic(firstHandle) && !IsIconic(secondHandle) && splitBar.IsVisible, "split selection restores both windows and bar");
                    ShowWindowAsync(firstHandle, 6);
                    await Task.Delay(300);
                    Invoke("Update");
                    await Task.Delay(200);
                    Check(IsIconic(firstHandle) && IsIconic(secondHandle) && !splitBar.IsVisible,
                        "external minimize hides split bar and minimizes companion");
                    ShowWindowAsync(firstHandle, 9);
                    await Task.Delay(200);
                    SetForegroundWindow(firstHandle);
                    await Task.Delay(150);
                    Invoke("Update");
                    await Task.Delay(200);
                    Check(!IsIconic(firstHandle) && !IsIconic(secondHandle) && splitBar.IsVisible,
                        "external restore shows split bar and companion");
                    Invoke("ToggleMaximize");
                    await Task.Delay(300);
                    GetWindowRect(firstHandle, out firstRect);
                    GetWindowRect(secondHandle, out secondRect);
                    DwmGetWindowAttribute(firstHandle, 9, out firstFrame, Marshal.SizeOf<Rectangle>());
                    DwmGetWindowAttribute(secondHandle, 9, out secondFrame, Marshal.SizeOf<Rectangle>());
                    Check(firstRect != secondRect && Math.Min(firstFrame.Right, secondFrame.Right) == Math.Max(firstFrame.Left, secondFrame.Left), "maximized split retains touching columns");
                    CheckTabRegions("maximized split");
                    SplitMenu(splitBar).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                    await Task.Delay(300);
                    Check(CurrentBar() is WhimGroupWin.FrmTopGroupBar && !CurrentBar().IsSplit && groups.Count == 1, "split toggles back to top bar without dissolving");
                    Check(IsWindowVisible(firstHandle) != IsWindowVisible(secondHandle), "leaving split restores selected-only visibility");
                    GetWindowRect(firstHandle, out var beforeReenterSplit);
                    SplitMenu(CurrentBar()).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                    await Task.Delay(300);
                    GetWindowRect(firstHandle, out var beforeMemberClose);
                    Invoke("CloseMember", secondHandle);
                    await Task.Delay(400);
                    Check(groups.Count == 0, "closing one split member dissolves the group");
                    GetWindowRect(firstHandle, out var afterMemberClose);
                    Check(afterMemberClose.Left == beforeMemberClose.Left && afterMemberClose.Top == beforeMemberClose.Top
                        && afterMemberClose.Right - afterMemberClose.Left == beforeReenterSplit.Right - beforeReenterSplit.Left
                        && afterMemberClose.Bottom - afterMemberClose.Top == beforeReenterSplit.Bottom - beforeReenterSplit.Top,
                        "remaining split window keeps position and restores pre-split width after member closes");
                    Check(IsWindowVisible(firstHandle) && !IsIconic(firstHandle), "remaining split window stays visible after member closes");
                    Process StartOrderFixture(string title)
                    {
                        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                        if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                            start.ArgumentList.Add(typeof(Program).Assembly.Location);
                        start.ArgumentList.Add(title);
                        return Process.Start(start)!;
                    }
                    using var externalLeft = StartOrderFixture("fixture-a");
                    using var externalRight = StartOrderFixture("fixture-b");
                    var externalCover = new Window { Title = "External split interference", Width = 400, Height = 300 };
                    try
                    {
                        for (var attempt = 0; attempt < 50; attempt++)
                        {
                            externalLeft.Refresh();
                            externalRight.Refresh();
                            if (externalLeft.MainWindowHandle != 0 && externalRight.MainWindowHandle != 0)
                                break;
                            await Task.Delay(100);
                        }
                        externalLeft.Refresh();
                        externalRight.Refresh();
                        var externalLeftHandle = externalLeft.MainWindowHandle;
                        var externalRightHandle = externalRight.MainWindowHandle;
                        Check(externalLeftHandle != 0 && externalRightHandle != 0, "cross-process split fixtures ready");
                        type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [externalLeftHandle, externalRightHandle]);
                        group = groups[0]!;
                        SplitMenu(CurrentBar()).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                        await Task.Delay(300);
                        externalCover.Show();
                        var externalCoverHandle = new WindowInteropHelper(externalCover).Handle;
                        var orderTestTimer = (System.Windows.Threading.DispatcherTimer)type.GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
                        var tickHandler = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), controller,
                            type.GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!);
                        orderTestTimer.Tick -= tickHandler;
                        try
                        {
                            for (var iteration = 0; iteration < 20; iteration++)
                            {
                                var active = iteration % 2 == 0 ? externalLeftHandle : externalRightHandle;
                                Invoke("Select", active);
                                await Task.Delay(50);
                                active = GetForegroundWindow();
                                if (active != externalLeftHandle && active != externalRightHandle)
                                    throw new InvalidOperationException($"No external split member is foreground: {active}");
                                var follower = active == externalLeftHandle ? externalRightHandle : externalLeftHandle;
                                SetWindowPos(externalCoverHandle, active, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                                await Task.Delay(150);
                                if (GetWindow(active, 2) != follower)
                                    throw new InvalidOperationException($"Cross-process z-order not repaired at iteration {iteration}: active={active}, foreground={GetForegroundWindow()}, next={GetWindow(active, 2)}, follower={follower}, cover={externalCoverHandle}");
                            }
                            Check(true, "cross-process split repairs repeated interleaving without timer polling");
                        }
                        finally
                        {
                            orderTestTimer.Tick += tickHandler;
                        }
                        Invoke("Dissolve");
                    }
                    finally
                    {
                        externalCover.Close();
                        externalLeft.CloseMainWindow();
                        externalRight.CloseMainWindow();
                    }
                    return;
                }
                if (arguments.Contains("--left-check"))
                {
                    var initialBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                    var initialPanel = (System.Windows.Controls.DockPanel)initialBar.Content;
                    var initialMenu = ((System.Windows.Controls.Button)initialPanel.Children[0]).ContextMenu;
                    Check(initialMenu.Items.Count == 2 && ((System.Windows.Controls.MenuItem)initialMenu.Items[0]).Visibility == Visibility.Collapsed, "top menu hides vertical mode entry");
                    Check(((System.Windows.Controls.MenuItem)initialMenu.Items[1]).Visibility == Visibility.Visible, "top menu preserves split entry");
                    ((System.Windows.Controls.MenuItem)initialMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                    await Task.Delay(300);
                    var leftBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                    Check(typeof(WhimGroupWin.FrmTopGroupBar).BaseType == typeof(WhimGroupWin.GroupBar)
                        && typeof(WhimGroupWin.FrmLeftGroupBar).BaseType == typeof(WhimGroupWin.GroupBar), "both layouts directly inherit shared group bar");
                    Check(leftBar is WhimGroupWin.FrmLeftGroupBar && leftBar.IsVertical, "left group bar created");
                    var panel = (System.Windows.Controls.DockPanel)leftBar.Content;
                    var scroll = (System.Windows.Controls.ScrollViewer)panel.Children[2];
                    var items = (System.Windows.Controls.ItemsControl)scroll.Content;
                    var stack = (System.Windows.Controls.StackPanel)items.ItemsPanel.LoadContent();
                    Check(stack.Orientation == System.Windows.Controls.Orientation.Vertical, "tabs use vertical layout");
                    GetWindowRect(new WindowInteropHelper(leftBar).Handle, out var leftRect);
                    DwmGetWindowAttribute(firstHandle, 9, out var visible, Marshal.SizeOf<Rectangle>());
                    Check(leftRect.Top == visible.Top && leftRect.Bottom == visible.Bottom, "left bar follows visible window height");
                    Check(visible.Left - leftRect.Right == 2, "left bar preserves two pixel gap");
                    var menu = ((System.Windows.Controls.Button)panel.Children[0]).ContextMenu;
                    Check(menu.Items.Count == 2 && (string)((System.Windows.Controls.MenuItem)menu.Items[0]).Header == "重叠模式"
                        && ((System.Windows.Controls.MenuItem)menu.Items[0]).Visibility == Visibility.Visible, "left menu offers horizontal mode");
                    Invoke("SelectPrevious");
                    await Task.Delay(150);
                    Check(IsWindowVisible(secondHandle) && !IsWindowVisible(firstHandle), "left mode preserves previous member switching");
                    Invoke("ToggleMaximize");
                    await Task.Delay(300);
                    GetWindowRect(new WindowInteropHelper(leftBar).Handle, out leftRect);
                    DwmGetWindowAttribute(secondHandle, 9, out visible, Marshal.SizeOf<Rectangle>());
                    Check(visible.Left - leftRect.Right == 2 && visible.Top == leftRect.Top, "maximized window reserves left bar space");
                    ((System.Windows.Controls.MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                    await Task.Delay(300);
                    var topBar = (WhimGroupWin.GroupBar)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                    Check(!topBar.IsVertical && groups.Count == 1, "switching back preserves group");
                    var restoredPanel = (System.Windows.Controls.DockPanel)topBar.Content;
                    var restoredMenu = ((System.Windows.Controls.Button)restoredPanel.Children[0]).ContextMenu;
                    Check(((System.Windows.Controls.MenuItem)restoredMenu.Items[0]).Visibility == Visibility.Collapsed, "restored top menu hides vertical mode entry");
                    GetWindowRect(new WindowInteropHelper(topBar).Handle, out var topRect);
                    DwmGetWindowAttribute(secondHandle, 9, out visible, Marshal.SizeOf<Rectangle>());
                    Check(visible.Top - topRect.Bottom == 2, "top bar restored with reserved space");
                    return;
                }
                var switchPrevious = type.GetMethod("TrySwitchPrevious", BindingFlags.NonPublic | BindingFlags.Instance)!;
                Check(!(bool)switchPrevious.Invoke(controller, [(nint)0])!, "shortcut ignores unrelated foreground");
                first.Activate();
                await Task.Delay(100);
                try
                {
                    keybd_event(0x10, 0, 0, 0);
                    await Task.Delay(30);
                    keybd_event(0x09, 0, 0, 0);
                    await Task.Delay(100);
                }
                finally
                {
                    keybd_event(0x09, 0, 2, 0);
                    keybd_event(0x10, 0, 2, 0);
                }
                await Task.Delay(100);
                Check(!IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle),
                    "real Shift+Tab switches active group member and hides previous");
                var shortcutBar = (Window)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                Check((bool)switchPrevious.Invoke(controller, [new WindowInteropHelper(shortcutBar).Handle])!,
                    "shortcut recognizes group bar foreground");
                await Task.Delay(100);
                Check(IsWindowVisible(firstHandle) && !IsWindowVisible(secondHandle),
                    "previous member wraps around in tab order");
                var controllerTimer = (System.Windows.Threading.DispatcherTimer)type.GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
                controllerTimer.Stop();
                try
                {
                    first.Left += 24;
                    await Task.Delay(100);
                    GetWindowRect(firstHandle, out firstRect);
                    GetWindowRect(secondHandle, out secondRect);
                    var eventBar = (Window)group.GetType().GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                    GetWindowRect(new WindowInteropHelper(eventBar).Handle, out var eventBarRect);
                    Check(DwmGetWindowAttribute(firstHandle, 9, out var visibleRect, Marshal.SizeOf<Rectangle>()) == 0,
                        "visible frame bounds available");
                    Check(eventBarRect.Left == visibleRect.Left && eventBarRect.Right == visibleRect.Right,
                        "bar aligns with both visible frame edges");
                    Check(visibleRect.Top - eventBarRect.Bottom == 2, "normal layout preserves two pixel bar gap");
                    Check(firstRect == secondRect && eventBarRect.Left == visibleRect.Left,
                        "location events synchronize members and bar without polling");
                }
                finally
                {
                    controllerTimer.Start();
                }
                var groupType = group.GetType();
                var groupBar = (Window)groupType.GetField("bar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(group)!;
                Check(!groupBar.Topmost, "group bar is not topmost");
                var covering = new Window
                {
                    Title = "Whim smoke covering window",
                    Left = first.Left,
                    Top = first.Top - 50,
                    Width = first.Width,
                    Height = first.Height + 50
                };
                try
                {
                    covering.Show();
                    covering.Activate();
                    await Task.Delay(150);
                    var coveringHandle = new WindowInteropHelper(covering).Handle;
                    var coveringAboveBar = false;
                    for (var previous = GetWindow(new WindowInteropHelper(groupBar).Handle, 3); previous != 0; previous = GetWindow(previous, 3))
                        if (previous == coveringHandle)
                            coveringAboveBar = true;
                    Check(coveringAboveBar, "unrelated window covers group bar without bar resurfacing");
                }
                finally
                {
                    covering.Close();
                    Invoke("Select", firstHandle);
                }
                GetWindowRect(new WindowInteropHelper(groupBar).Handle, out var barRect);
                var dragBoundsField = groupType.GetField("dragBarBounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
                dragBoundsField.SetValue(group, Activator.CreateInstance(dragBoundsField.FieldType,
                    [barRect.Left, barRect.Top, barRect.Right, barRect.Bottom]));
                var movingField = groupType.GetField("moving", BindingFlags.NonPublic | BindingFlags.Instance)!;
                movingField.SetValue(group, true);
                try
                {
                    groupBar.Left += 40;
                    await Task.Delay(30);
                    GetWindowRect(firstHandle, out var draggedFirst);
                    GetWindowRect(secondHandle, out var draggedSecond);
                    Check(draggedFirst.Left > firstRect.Left && draggedFirst != draggedSecond,
                        "visible member follows bar while hidden member is deferred");
                }
                finally
                {
                    ((Action)groupBar.GetType().GetField("DragCompleted", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(groupBar)!).Invoke();
                }
                await Task.Delay(100);
                GetWindowRect(firstHandle, out var completedFirst);
                GetWindowRect(secondHandle, out var completedSecond);
                Check(completedFirst == completedSecond, "hidden member aligned after drag completes");
                first.Width = 680;
                first.Height = 440;
                await Task.Delay(300);
                GetWindowRect(firstHandle, out firstRect);
                GetWindowRect(secondHandle, out secondRect);
                Check(firstRect == secondRect, "resize synchronized before tab switch");
                Invoke("Select", secondHandle);
                await Task.Delay(300);
                GetWindowRect(secondHandle, out secondRect);
                Check(firstRect == secondRect, "resized bounds applied on tab switch");
                Check(!IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle) && !IsIconic(firstHandle), "switch hides old member without minimizing");
                Invoke("Select", firstHandle);
                await Task.Delay(200);
                Invoke("SetState", 6);
                await Task.Delay(200);
                Check(IsIconic(firstHandle) && !IsWindowVisible(secondHandle), "minimize keeps inactive member hidden");
                Invoke("Select", firstHandle);
                await Task.Delay(200);
                Check(!IsIconic(firstHandle) && IsWindowVisible(firstHandle) && !IsWindowVisible(secondHandle), "restore shows selected member only");
                Invoke("ToggleHidden");
                await Task.Delay(200);
                Check(!IsWindowVisible(firstHandle) && !IsWindowVisible(secondHandle), "group hidden");
                Invoke("ToggleHidden");
                await Task.Delay(200);
                Check(IsWindowVisible(firstHandle) && !IsWindowVisible(secondHandle), "show restores selected member only");
                GetWindowRect(firstHandle, out var beforeMaximize);
                Invoke("ToggleMaximize");
                await Task.Delay(300);
                GetWindowRect(firstHandle, out var maximizedRect);
                GetWindowRect(secondHandle, out var maximizedSecond);
                GetWindowRect(new WindowInteropHelper(groupBar).Handle, out var maximizedBar);
                Check(DwmGetWindowAttribute(firstHandle, 9, out var maximizedFrame, Marshal.SizeOf<Rectangle>()) == 0
                    && maximizedFrame.Top - maximizedBar.Bottom == 2, "group maximize preserves two pixel bar gap");
                Check(maximizedRect == maximizedSecond && maximizedRect.Bottom > beforeMaximize.Bottom,
                    "all members fill reserved work area");
                Invoke("Select", secondHandle);
                await Task.Delay(300);
                GetWindowRect(secondHandle, out var switchedMaximized);
                Check(switchedMaximized == maximizedRect && !IsWindowVisible(firstHandle), "maximized tab switch hides old member");
                Invoke("ToggleMaximize");
                await Task.Delay(300);
                Check(!IsZoomed(secondHandle) && !IsZoomed(firstHandle), "all members unmaximized");
                GetWindowRect(firstHandle, out var restoredRect);
                Check(restoredRect == beforeMaximize, "group restore returns original bounds");
                Invoke("Select", firstHandle);
                await Task.Delay(200);
                first.WindowState = WindowState.Maximized;
                await Task.Delay(400);
                GetWindowRect(new WindowInteropHelper(groupBar).Handle, out var nativeMaximizeBar);
                Check(DwmGetWindowAttribute(firstHandle, 9, out var nativeMaximizeFrame, Marshal.SizeOf<Rectangle>()) == 0
                    && nativeMaximizeBar.Bottom <= nativeMaximizeFrame.Top,
                    "native maximize also reserves group bar space");
                Invoke("SetState", 6);
                await Task.Delay(200);
                Invoke("Select", firstHandle);
                await Task.Delay(300);
                GetWindowRect(new WindowInteropHelper(groupBar).Handle, out var resumedBar);
                Check(!IsIconic(firstHandle) && !IsIconic(secondHandle)
                    && DwmGetWindowAttribute(firstHandle, 9, out var resumedFrame, Marshal.SizeOf<Rectangle>()) == 0
                    && resumedBar.Bottom <= resumedFrame.Top,
                    "maximized minimize and restore retains title bar space");
                Invoke("ToggleMaximize");
                await Task.Delay(300);
                GetWindowRect(firstHandle, out restoredRect);
                Check(restoredRect == beforeMaximize, "native maximize restores original bounds");
                Invoke("Select", firstHandle);
                await Task.Delay(200);
                Check(!IsZoomed(firstHandle) && !IsWindowVisible(secondHandle), "restored tab switch keeps inactive member hidden");
                Invoke("Dissolve");
                await Task.Delay(200);
                Check(groups.Count == 0, "group dissolved");
                Check(IsWindowVisible(firstHandle) && IsWindowVisible(secondHandle), "dissolve restores all members");
                type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [firstHandle, secondHandle]);
                await Task.Delay(200);
                group = groups[0]!;
                Invoke("CloseMember", firstHandle);
                await Task.Delay(300);
                Check(groups.Count == 0 && !first.IsVisible && IsWindowVisible(secondHandle), "tab close closes member and restores survivor");
                var third = new Window { Title = "Whim smoke C", Left = 180, Top = 240, Width = 520, Height = 340 };
                try
                {
                    third.Show();
                    var thirdHandle = new WindowInteropHelper(third).Handle;
                    type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [thirdHandle, secondHandle]);
                    await Task.Delay(200);
                    controller.Dispose();
                    await Task.Delay(200);
                    Check(IsWindowVisible(thirdHandle) && IsWindowVisible(secondHandle), "controller exit restores hidden members");
                    controller = (IDisposable)Activator.CreateInstance(type)!;
                    type.GetMethod("Combine", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, [thirdHandle, secondHandle]);
                    groups = (System.Collections.IList)type.GetField("groups", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
                    group = groups[0]!;
                    Invoke("CloseGroup");
                    await Task.Delay(300);
                    Check(!third.IsVisible && !second.IsVisible && groups.Count == 0,
                        "group close closes all member windows");
                }
                finally
                {
                    third.Close();
                }
                if (arguments.Contains("--gesture"))
                {
                    first.Hide();
                    second.Hide();
                    using var source = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "fixture-a") { UseShellExecute = false })!;
                    using var target = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "fixture-b") { UseShellExecute = false })!;
                    GetCursorPos(out var cursor);
                    try
                    {
                        source.WaitForInputIdle(5000);
                        target.WaitForInputIdle(5000);
                        await Task.Delay(400);
                        source.Refresh();
                        target.Refresh();
                        var sourceHandle = source.MainWindowHandle;
                        var targetHandle = target.MainWindowHandle;
                        Check(sourceHandle != 0 && targetHandle != 0, "external fixtures ready");
                        SetForegroundWindow(sourceHandle);
                        GetWindowRect(sourceHandle, out var sourceBounds);
                        GetWindowRect(targetHandle, out var targetBounds);
                        SetCursorPos(sourceBounds.Left + 100, sourceBounds.Top + 16);
                        keybd_event(0x11, 0, 0, 0);
                        keybd_event(0x12, 0, 0, 0);
                        mouse_event(2, 0, 0, 0, 0);
                        await Task.Delay(200);
                        SetCursorPos(targetBounds.Left + 180, targetBounds.Top + 120);
                        await Task.Delay(200);
                        mouse_event(4, 0, 0, 0, 0);
                        await Task.Delay(400);
                        Check(groups.Count == 1, "Ctrl+Alt drag creates group across processes");
                        GetWindowRect(sourceHandle, out sourceBounds);
                        GetWindowRect(targetHandle, out targetBounds);
                        Check(sourceBounds == targetBounds, "external group bounds synchronized");
                    }
                    finally
                    {
                        mouse_event(4, 0, 0, 0, 0);
                        keybd_event(0x12, 0, 2, 0);
                        keybd_event(0x11, 0, 2, 0);
                        SetCursorPos(cursor.Left, cursor.Top);
                        controller.Dispose();
                        if (!source.HasExited) source.Kill();
                        if (!target.HasExited) target.Kill();
                    }
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                exitCode = 1;
            }
            finally
            {
                controller?.Dispose();
                first.Close();
                second.Close();
                application.Shutdown();
            }
        };
        application.Run();
        return exitCode;
    }

    private static void Check(bool success, string name)
    {
        if (!success)
            throw new InvalidOperationException(name);
        Console.WriteLine($"PASS: {name}");
    }
}