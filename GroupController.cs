using System.Runtime.InteropServices;
using System.Windows.Threading;
using Native = WhimGroupWin.WindowNative;

namespace WhimGroupWin;

internal sealed class GroupController : IDisposable
{
    private const int AutomaticScanIntervalMilliseconds = 1000;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(Configuration.PollIntervalMilliseconds) };
    private readonly List<WindowGroup> groups = [];
    private readonly Native.WinEventCallback locationChanged;
    private readonly nint locationHook;
    private readonly Native.WinEventCallback orderChanged;
    private readonly nint foregroundHook;
    private readonly nint reorderHook;
    private bool orderUpdatePending;
    private readonly Native.KeyboardCallback keyboardCallback;
    private readonly nint keyboardHook;
    private int switchKeyPressed;
    private bool pressed;
    private nint dragged;
    private Native.Point origin;
    private readonly Dictionary<Guid, WindowGroup> automaticWindows = [];
    private readonly Dictionary<nint, uint> detachedWindows = [];
    private long nextAutomaticScan;

    internal sealed record GroupInfo(Guid Id, string Name, string Summary, bool Hidden,
        Action Show, Action Hide, Action Close);

    internal IReadOnlyList<GroupInfo> GetGroups() => groups.Select((group, index) =>
    {
        var definition = AutomaticGroups.Current.FirstOrDefault(candidate =>
            automaticWindows.TryGetValue(candidate.Id, out var automatic) && automatic == group);
        return group.GetInfo(definition?.Name ?? $"窗口组 {index + 1}");
    }).ToArray();

    public GroupController()
    {
        locationChanged = OnLocationChanged;
        locationHook = Native.SetWinEventHook(0x800B, 0x800B, 0, locationChanged, 0, 0, 0);
        keyboardCallback = OnKeyboard;
        keyboardHook = Native.SetWindowsHookExW(13, keyboardCallback, Native.GetModuleHandleW(null), 0);
        if (keyboardHook == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (locationHook != 0)
                Native.UnhookWinEvent(locationHook);
            throw new System.ComponentModel.Win32Exception(error);
        }
        orderChanged = OnOrderChanged;
        foregroundHook = Native.SetWinEventHook(3, 3, 0, orderChanged, 0, 0, 0);
        reorderHook = Native.SetWinEventHook(0x8004, 0x8004, 0, orderChanged, 0, 0, 0);
        timer.Tick += Tick;
        AutomaticGroups.Changed += OnAutomaticGroupsChanged;
        timer.Start();
    }

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.ReadInt32(data);
            if (message == 0x0101 || message == 0x0105)
            {
                if (switchKeyPressed == key)
                {
                    switchKeyPressed = 0;
                    return 1;
                }
            }
            else if (message == 0x0100 || message == 0x0104)
            {
                if (switchKeyPressed == key)
                    return 1;
                var automatic = AutomaticGroups.Current.FirstOrDefault(group =>
                    key == System.Windows.Input.KeyInterop.VirtualKeyFromKey(group.Key) && MatchesModifiers(group.Modifiers));
                if (automatic is not null)
                {
                    switchKeyPressed = key;
                    timer.Dispatcher.InvokeAsync(() => ToggleAutomaticGroup(automatic.Id));
                    return 1;
                }
                if (key == System.Windows.Input.KeyInterop.VirtualKeyFromKey(KeyboardSettings.Current.SwitchKey)
                    && MatchesModifiers(KeyboardSettings.Current.SwitchModifiers)
                    && TrySwitchPrevious(Native.GetForegroundWindow()))
                {
                    switchKeyPressed = key;
                    return 1;
                }
                if (switchKeyPressed == key)
                    return 1;
            }
        }
        return Native.CallNextHookEx(keyboardHook, code, message, data);
    }

    private static bool MatchesModifiers(System.Windows.Input.ModifierKeys expected)
    {
        var actual = System.Windows.Input.ModifierKeys.None;
        if (Native.Down(0x10)) actual |= System.Windows.Input.ModifierKeys.Shift;
        if (Native.Down(0x11)) actual |= System.Windows.Input.ModifierKeys.Control;
        if (Native.Down(0x12)) actual |= System.Windows.Input.ModifierKeys.Alt;
        if (Native.Down(0x5B) || Native.Down(0x5C)) actual |= System.Windows.Input.ModifierKeys.Windows;
        return actual == expected;
    }

    private bool TrySwitchPrevious(nint foreground)
    {
        var group = groups.FirstOrDefault(candidate => candidate.OwnsForeground(foreground));
        if (group is null)
            return false;
        timer.Dispatcher.InvokeAsync(group.SelectPrevious);
        return true;
    }

    private void OnLocationChanged(nint hook, uint eventType, nint handle, int objectId,
        int childId, uint threadId, uint eventTime)
    {
        if (objectId != 0 || childId != 0)
            return;
        foreach (var group in groups.ToArray())
            if (group.Contains(handle))
                group.UpdateLocation();
    }

    private void OnOrderChanged(nint hook, uint eventType, nint handle, int objectId,
        int childId, uint threadId, uint eventTime)
    {
        if (orderUpdatePending || !timer.IsEnabled)
            return;
        orderUpdatePending = true;
        timer.Dispatcher.InvokeAsync(() =>
        {
            orderUpdatePending = false;
            if (timer.IsEnabled)
                foreach (var group in groups.ToArray())
                    group.UpdateOrder();
        }, DispatcherPriority.Normal);
    }

    private void Tick(object? sender, EventArgs args)
    {
        UpdateGroupDrag();
        foreach (var group in groups.ToArray())
            group.Update();
        if (Environment.TickCount64 >= nextAutomaticScan)
        {
            nextAutomaticScan = Environment.TickCount64 + AutomaticScanIntervalMilliseconds;
            ApplyAutomaticGroups();
        }
    }

    private void UpdateGroupDrag()
    {
        var down = Native.Down(1);
        Native.GetCursorPos(out var point);
        if (down && !pressed)
        {
            dragged = 0;
            if (MatchesModifiers(KeyboardSettings.Current.GroupModifiers))
            {
                var candidate = Native.GetAncestor(Native.WindowFromPoint(point), 2);
                if (Native.Valid(candidate))
                {
                    dragged = candidate;
                    origin = point;
                }
            }
        }
        if (!down && pressed && dragged != 0)
        {
            if (Math.Abs(point.X - origin.X) + Math.Abs(point.Y - origin.Y) > Configuration.GroupDragThresholdPixels)
            {
                var candidate = Native.GetAncestor(Native.WindowFromPoint(point), 2);
                var targetGroup = groups.FirstOrDefault(group => !group.Contains(dragged) && group.OwnsForeground(candidate));
                var target = targetGroup is not null ? targetGroup.GetDropTarget(point) : Native.FindTarget(point, dragged);
                if (target != 0)
                    Combine(dragged, target);
            }
            dragged = 0;
        }
        pressed = down;
    }

    private void OnAutomaticGroupsChanged()
    {
        foreach (var group in automaticWindows.Values.ToArray())
            group.Dissolve();
        automaticWindows.Clear();
        nextAutomaticScan = 0;
    }

    private void ApplyAutomaticGroups()
    {
        foreach (var entry in detachedWindows.ToArray())
        {
            Native.GetWindowThreadProcessId(entry.Key, out var process);
            if (!Native.IsWindow(entry.Key) || process != entry.Value)
                detachedWindows.Remove(entry.Key);
        }
        if (AutomaticGroups.Current.Count == 0)
            return;
        List<DesktopWindow> windows;
        try
        {
            windows = WindowService.Enumerate();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }
        var paths = new Dictionary<uint, string?>();
        foreach (var definition in AutomaticGroups.Current)
        {
            automaticWindows.TryGetValue(definition.Id, out var destination);
            var matches = new List<nint>();
            foreach (var rule in definition.Applications)
            {
                var candidates = new List<DesktopWindow>();
                if (destination is not null)
                    foreach (var member in destination.Members)
                    {
                        Native.GetWindowThreadProcessId(member, out var processId);
                        var title = new System.Text.StringBuilder(1024);
                        Native.GetWindowTextW(member, title, title.Capacity);
                        candidates.Add(new DesktopWindow(member, processId, title.ToString(), ""));
                    }
                candidates.AddRange(windows);
                foreach (var window in candidates)
                {
                    if (detachedWindows.ContainsKey(window.Handle) || matches.Contains(window.Handle)
                        || groups.Any(group => group != destination && group.Contains(window.Handle)))
                        continue;
                    if (!paths.TryGetValue(window.ProcessId, out var path))
                    {
                        path = WindowService.ExecutablePath(window.ProcessId);
                        paths[window.ProcessId] = path;
                    }
                    if (!string.Equals(path, rule.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                        || rule.WindowTitle.Length > 0 && !window.Title.Contains(rule.WindowTitle, StringComparison.OrdinalIgnoreCase))
                        continue;
                    matches.Add(window.Handle);
                }
            }
            AttachAutomaticGroup(definition, matches);
        }
    }

    private void AttachAutomaticGroup(AutomaticGroup definition, IReadOnlyList<nint> matches)
    {
        automaticWindows.TryGetValue(definition.Id, out var destination);
        var changed = destination is null;
        var created = destination is null;
        if (destination is null)
        {
            if (matches.Count < 2)
                return;
            destination = new WindowGroup(matches[0], group =>
            {
                groups.Remove(group);
                automaticWindows.Remove(definition.Id);
            }, ExcludeDetachedWindow);
            groups.Add(destination);
            automaticWindows[definition.Id] = destination;
        }
        foreach (var handle in matches)
            if (!destination.Contains(handle))
            {
                destination.Add(handle);
                changed = true;
            }
        if (changed)
            destination.ConfigureAutomaticLayout(created ? definition.Split : destination.IsSplit);
    }

    private void ToggleAutomaticGroup(Guid identifier)
    {
        ApplyAutomaticGroups();
        if (automaticWindows.TryGetValue(identifier, out var group))
            group.ToggleHidden();
    }

    private void Combine(nint source, nint target)
    {
        var destination = groups.FirstOrDefault(group => group.Contains(target));
        var previous = groups.FirstOrDefault(group => group.Contains(source));
        if (destination is not null && destination == previous)
            return;
        var members = previous?.Members.ToArray() ?? [source];
        previous?.Dissolve();
        if (destination is null)
        {
            destination = new WindowGroup(target, group => groups.Remove(group), ExcludeDetachedWindow);
            groups.Add(destination);
        }
        foreach (var member in members)
        {
            destination.Add(member);
            destination.AssignSplitSide(member, target);
        }
        destination.Select(source);
    }

    private void ExcludeDetachedWindow(nint handle)
    {
        Native.GetWindowThreadProcessId(handle, out var process);
        detachedWindows[handle] = process;
    }

    public void Dispose()
    {
        timer.Stop();
        AutomaticGroups.Changed -= OnAutomaticGroupsChanged;
        if (keyboardHook != 0)
            Native.UnhookWindowsHookEx(keyboardHook);
        if (locationHook != 0)
            Native.UnhookWinEvent(locationHook);
        if (foregroundHook != 0)
            Native.UnhookWinEvent(foregroundHook);
        if (reorderHook != 0)
            Native.UnhookWinEvent(reorderHook);
        foreach (var group in groups.ToArray())
            group.Dissolve();
    }
}
