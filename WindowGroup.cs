using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GroupInfo = WhimGroupWin.GroupController.GroupInfo;
using Native = WhimGroupWin.WindowNative;

namespace WhimGroupWin;

internal sealed class WindowGroup
{
    private GroupBar bar;
    private readonly Action<WindowGroup> removed;
    private readonly Action<nint> memberDetached;
    private readonly Dictionary<nint, uint> processes = [];
    private readonly Dictionary<nint, Native.Rect> splitMemberBounds = [];
    private readonly Dictionary<nint, Native.Rect> beforeSplitMembers = [];
    private Native.Rect beforeSplitBounds;
    private readonly Dictionary<nint, Native.Rect> pendingSplitPlacements = [];
    private readonly Dictionary<nint, (uint Dpi, Native.Rect Insets)> frameInsets = [];
    private Native.Rect bounds;
    private nint selected;
    private bool hidden;
    private bool minimized;
    private bool maximized;
    private bool disposed;
    private bool moving;
    private nint splitLeft;
    private nint splitRight;
    private readonly HashSet<nint> splitLeftMembers = [];
    private double splitRatio = 0.5;
    private Native.Rect dragBarBounds;
    private Native.Rect normalBounds;
    private readonly object dragMoveLock = new();
    private (nint Handle, uint Process, Native.Rect Bounds)[]? pendingDragMove;
    private bool dragMoveRunning;

    public List<nint> Members { get; } = [];
    public bool IsSplit => bar.IsSplit;
    private readonly Guid identifier = Guid.NewGuid();

    public GroupInfo GetInfo(string name)
    {
        var titles = new List<string>();
        foreach (var member in Members.Where(IsMemberAlive))
        {
            var title = new System.Text.StringBuilder(256);
            Native.GetWindowTextW(member, title, title.Capacity);
            titles.Add(title.ToString());
        }
        var state = hidden ? "已隐藏" : minimized ? "已最小化" : "已显示";
        return new GroupInfo(identifier, name,
            $"{(bar.IsSplit ? "左右分屏" : bar.IsVertical ? "垂直模式" : "重叠模式")} · {titles.Count} 个窗口 · {state}\n{string.Join(" / ", titles)}",
            hidden, ShowGroup, HideGroup, CloseGroup);
    }

    private void ShowGroup()
    {
        if (disposed)
            return;
        hidden = true;
        ToggleHidden();
    }

    private void HideGroup()
    {
        if (!disposed && !hidden)
            ToggleHidden();
    }

    public bool Contains(nint handle) => Members.Contains(handle);
    public bool OwnsForeground(nint handle) => Contains(handle)
        || new WindowInteropHelper(bar).Handle == handle;

    public nint GetDropTarget(Native.Point point)
    {
        if (!bar.IsSplit)
            return selected;
        return point.X < bounds.Left + (bounds.Right - bounds.Left) * splitRatio ? splitLeft : splitRight;
    }

    public WindowGroup(nint target, Action<WindowGroup> removed, Action<nint> memberDetached)
    {
        this.removed = removed;
        this.memberDetached = memberDetached;
        Native.GetWindowRect(target, out bounds);
        normalBounds = bounds;
        bar = new FrmTopGroupBar();
        ConnectBar();
        selected = target;
        Add(target);
        bar.Show();
        PositionBar();
    }

    private void ConnectBar()
    {
        bar.TabSelected += Select;
        bar.ActivationRequested += ActivateSelected;
        bar.PlacementChangeRequested += ChangeBarPlacement;
        bar.SplitRequested += ToggleSplit;
        bar.TabCloseRequested += CloseMember;
        bar.TabRemoveRequested += handle =>
        {
            memberDetached(handle);
            Remove(handle);
        };
        bar.MinimizeRequested += () => SetState(6);
        bar.MaximizeRequested += ToggleMaximize;
        bar.CloseGroupRequested += CloseGroup;
        bar.DragPositionChanged += (left, top) =>
        {
            if (moving)
                MoveMembersTo(left, top);
        };
        bar.LocationChanged += (_, _) =>
        {
            if (moving)
                MoveMembersWithBar();
        };
        bar.DragStarted += () =>
        {
            Native.GetWindowRect(new WindowInteropHelper(bar).Handle, out dragBarBounds);
            moving = true;
        };
        bar.DragCompleted += () =>
        {
            MoveMembersWithBar();
            moving = false;
            if (bar.IsSplit)
            {
                ShowMembers();
                return;
            }
            if (!hidden && !minimized)
                foreach (var member in Members.Where(member => member != selected && IsMemberAlive(member)))
                    Native.Place(member, bounds);
        };
        bar.Closed += OnBarClosed;
    }

    private void OnBarClosed(object? sender, EventArgs args) => Dissolve();

    private void ChangeBarPlacement()
    {
        ReplaceBar(bar.IsVertical ? new FrmTopGroupBar() : new FrmLeftGroupBar());
    }

    private void ToggleSplit()
    {
        if (disposed || moving || Members.Count(IsMemberAlive) < 2)
            return;
        ReplaceBar(bar.IsSplit ? new FrmTopGroupBar() : new FrmSplitGroupBar());
    }

    private void ReplaceBar(GroupBar replacement)
    {
        if (disposed || moving)
            return;
        var previous = bar;
        if (!previous.IsSplit && replacement.IsSplit)
        {
            beforeSplitBounds = maximized ? normalBounds : bounds;
            beforeSplitMembers.Clear();
            foreach (var member in Members.Where(IsMemberAlive))
                if (Native.GetWindowRect(member, out var memberBounds))
                    beforeSplitMembers[member] = maximized ? normalBounds : memberBounds;
        }
        else if (previous.IsSplit && !replacement.IsSplit)
        {
            bounds = new Native.Rect(bounds.Left, bounds.Top,
                bounds.Left + beforeSplitBounds.Right - beforeSplitBounds.Left,
                bounds.Top + beforeSplitBounds.Bottom - beforeSplitBounds.Top);
            normalBounds = bounds;
            maximized = false;
            pendingOverlapMove = null;
            pendingSplitPlacements.Clear();
            splitMemberBounds.Clear();
            beforeSplitMembers.Clear();
        }
        bar = replacement;
        previous.Closed -= OnBarClosed;
        ConnectBar();
        RebuildTabs();
        bar.Show();
        previous.Close();
        if (maximized)
        {
            UpdateMaximizedBounds();
        }
        ShowMembers();
        PositionBar();
        SyncBarOrder();
    }

    private void MoveMembersWithBar()
    {
        if (!Native.GetWindowRect(new WindowInteropHelper(bar).Handle, out var current))
            return;
        MoveMembersTo(current.Left, current.Top);
    }

    private void MoveMembersTo(int left, int top)
    {
        var deltaX = left - dragBarBounds.Left;
        var deltaY = top - dragBarBounds.Top;
        if (deltaX == 0 && deltaY == 0)
            return;
        dragBarBounds = new Native.Rect(left, top,
            dragBarBounds.Right + deltaX, dragBarBounds.Bottom + deltaY);
        bounds = new Native.Rect(bounds.Left + deltaX, bounds.Top + deltaY,
            bounds.Right + deltaX, bounds.Bottom + deltaY);
        if (!maximized)
            normalBounds = bounds;
        if (bar.IsSplit)
        {
            if (!hidden && !minimized)
            {
                var requests = new List<(nint Handle, uint Process, Native.Rect Bounds)>();
                if (IsMemberAlive(splitLeft))
                    requests.Add((splitLeft, processes[splitLeft], GetSplitBounds(splitLeft)));
                if (IsMemberAlive(splitRight))
                    requests.Add((splitRight, processes[splitRight], GetSplitBounds(splitRight)));
                QueueDragMoves(requests.ToArray());
            }
            return;
        }
        if (!hidden && !minimized)
            if (IsMemberAlive(selected))
                QueueDragMove(selected, processes[selected], bounds);
    }

    private void QueueDragMove(nint handle, uint process, Native.Rect target)
    {
        if (!bar.IsSplit)
        {
            GetFrameInsets(handle);
            pendingOverlapMove = (handle, target, Environment.TickCount64 + 2000);
        }
        QueueDragMoves([(handle, process, target)]);
    }

    private (nint Handle, Native.Rect Bounds, long Deadline)? pendingOverlapMove;

    private void QueueDragMoves((nint Handle, uint Process, Native.Rect Bounds)[] requests)
    {
        foreach (var request in requests)
            if (splitMemberBounds.TryGetValue(request.Handle, out var current))
                splitMemberBounds[request.Handle] = new Native.Rect(request.Bounds.Left, request.Bounds.Top,
                    request.Bounds.Left + current.Right - current.Left, request.Bounds.Top + current.Bottom - current.Top);
        lock (dragMoveLock)
        {
            pendingDragMove = requests;
            if (dragMoveRunning)
                return;
            dragMoveRunning = true;
        }
        _ = Task.Run(() =>
        {
            while (true)
            {
                (nint Handle, uint Process, Native.Rect Bounds)[] batch;
                lock (dragMoveLock)
                {
                    if (pendingDragMove is not { } pending)
                    {
                        dragMoveRunning = false;
                        return;
                    }
                    batch = pending;
                    pendingDragMove = null;
                }
                foreach (var request in batch)
                {
                    Native.GetWindowThreadProcessId(request.Handle, out var processId);
                    if (processId == request.Process && Native.IsWindow(request.Handle)
                        && !Native.IsHungAppWindow(request.Handle))
                        Native.SetWindowPos(request.Handle, 0, request.Bounds.Left, request.Bounds.Top,
                            0, 0, 0x0001 | 0x0004 | 0x0010 | 0x0200);
                }
            }
        });
    }

    private void RefreshTabs()
    {
        foreach (var handle in Members)
        {
            var title = new System.Text.StringBuilder(256);
            Native.GetWindowTextW(handle, title, title.Capacity);
            bar.UpdateTabTitle(handle, title.ToString());
        }
        bar.SelectTab(selected);
    }

    private void ActivateSelected()
    {
        if (!disposed && !hidden && !minimized && IsMemberAlive(selected))
        {
            Native.SetForegroundWindow(selected);
            SyncBarOrder();
        }
    }

    public void Add(nint handle)
    {
        if (Contains(handle))
            return;
        Members.Add(handle);
        Native.GetWindowThreadProcessId(handle, out var process);
        processes[handle] = process;
        if (bar.IsSplit && Native.GetWindowRect(handle, out var originalBounds))
            beforeSplitMembers[handle] = originalBounds;
        if (!bar.IsSplit)
            Native.Place(handle, bounds);
        if (hidden || minimized || maximized)
            Native.ShowWindowAsync(handle, hidden ? 0 : minimized ? 6 : 9);
        RebuildTabs();
    }

    public void AssignSplitSide(nint member, nint target)
    {
        if (!bar.IsSplit)
            return;
        if (target == splitLeft || splitLeftMembers.Contains(target))
            splitLeftMembers.Add(member);
        else
            splitLeftMembers.Remove(member);
    }

    private void RebuildTabs()
    {
        var titles = new List<(nint Handle, string Title)>();
        foreach (var handle in Members)
        {
            var title = new System.Text.StringBuilder(256);
            Native.GetWindowTextW(handle, title, title.Capacity);
            titles.Add((handle, title.ToString()));
        }
        bar.SetTabs(titles, selected);
    }

    private void Remove(nint handle)
    {
        var wasLeft = splitLeftMembers.Contains(handle);
        if (IsMemberAlive(handle))
        {
            Native.ShowWindowAsync(handle, Native.IsIconic(handle) ? 9 : 8);
            if (bar.IsSplit)
                RestoreBeforeSplitSize(handle);
        }
        Members.Remove(handle);
        processes.Remove(handle);
        beforeSplitMembers.Remove(handle);
        splitMemberBounds.Remove(handle);
        pendingSplitPlacements.Remove(handle);
        splitLeftMembers.Remove(handle);
        if (selected == handle && Members.Count > 0)
            selected = Members.FirstOrDefault(member => IsMemberAlive(member)
                && splitLeftMembers.Contains(member) == wasLeft, Members[0]);
        RebuildTabs();
        if (Members.Count < 2)
            Dissolve();
        else if (bar.IsSplit && !hidden && !minimized)
        {
            ShowMembers();
            RefreshTabs();
            PositionBar();
            SyncBarOrder();
        }
    }

    private void CloseMember(nint handle)
    {
        if (!Contains(handle) || !IsMemberAlive(handle))
            return;
        Select(handle);
        Native.PostMessageW(handle, 0x0010, 0, 0);
    }

    private void CloseGroup()
    {
        var members = Members.Where(IsMemberAlive).ToArray();
        if (bar.IsSplit)
            Dissolve();
        foreach (var member in members.Where(IsMemberAlive))
        {
            Native.ShowWindowAsync(member, Native.IsIconic(member) ? 9 : 8);
            Native.PostMessageW(member, 0x0010, 0, 0);
        }
    }

    public void Select(nint handle)
    {
        if (!Contains(handle) || !IsMemberAlive(handle))
            return;
        selected = handle;
        hidden = false;
        minimized = false;
        if (!bar.IsVisible)
            bar.Show();
        ShowMembers();
        Native.SetForegroundWindow(handle);
        RefreshTabs();
        SyncBarOrder();
    }

    public void SelectPrevious()
    {
        if (disposed)
            return;
        var alive = Members.Where(IsMemberAlive).ToArray();
        if (alive.Length < 2)
            return;
        var current = Array.IndexOf(alive, selected);
        Select(alive[(current + alive.Length - 1) % alive.Length]);
    }

    private bool IsMemberAlive(nint handle)
    {
        Native.GetWindowThreadProcessId(handle, out var process);
        return Native.IsWindow(handle) && processes.TryGetValue(handle, out var expected) && process == expected;
    }

    private void ShowMembers()
    {
        if (bar.IsSplit)
        {
            PlaceSplitMembers(true);
            return;
        }
        foreach (var member in Members.Where(IsMemberAlive))
        {
            Native.Place(member, bounds);
        }
        if (IsMemberAlive(selected))
            Native.ShowWindowAsync(selected, Native.IsIconic(selected) || Native.IsZoomed(selected) ? 9 : 8);
        foreach (var member in Members.Where(member => member != selected && IsMemberAlive(member)))
            Native.ShowWindowAsync(member, 0);
    }

    private void SetState(int command)
    {
        minimized = command == 6;
        if (minimized)
            bar.Hide();
        if (!hidden)
            foreach (var member in Members.Where(member => (member == selected
                || bar.IsSplit && (member == splitLeft || member == splitRight)) && IsMemberAlive(member)))
                Native.ShowWindowAsync(member, command);
    }

    private void PlaceSplitMembers(bool show)
    {
        var alive = Members.Where(IsMemberAlive).ToArray();
        if (alive.Length < 2)
            return;
        if (!alive.Contains(splitLeft))
        {
            splitLeft = alive.Contains(selected) && selected != splitRight ? selected : alive.First(member => member != splitRight);
            splitLeftMembers.Add(splitLeft);
        }
        if (!alive.Contains(splitRight) || splitRight == splitLeft)
        {
            splitRight = alive.First(member => member != splitLeft);
            splitLeftMembers.Remove(splitRight);
        }
        if (selected != splitLeft && selected != splitRight && alive.Contains(selected))
        {
            if (splitLeftMembers.Contains(selected))
                splitLeft = selected;
            else
                splitRight = selected;
        }
        foreach (var member in alive)
        {
            if (member != splitLeft && member != splitRight)
            {
                if (show)
                    Native.ShowWindowAsync(member, 0);
                continue;
            }
            if (show && (Native.IsIconic(member) || Native.IsZoomed(member)))
                Native.ShowWindowAsync(member, 9);
            var target = GetSplitBounds(member);
            if (Native.GetWindowRect(member, out var current))
            {
                if (current != target)
                {
                    pendingSplitPlacements[member] = current;
                    Native.Place(member, target);
                }
                else
                {
                    pendingSplitPlacements.Remove(member);
                    splitMemberBounds[member] = current;
                }
            }
            if (show && !Native.IsWindowVisible(member))
                Native.ShowWindowAsync(member, 8);
        }
    }

    private Native.Rect GetFrameInsets(nint member)
    {
        var dpi = Native.GetDpiForWindow(member);
        if (frameInsets.TryGetValue(member, out var cached) && cached.Dpi == dpi)
            return cached.Insets;
        if (Native.GetWindowRect(member, out var outer)
            && Native.DwmGetWindowAttribute(member, 9, out Native.Rect frame, Marshal.SizeOf<Native.Rect>()) == 0
            && frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            var insets = new Native.Rect(frame.Left - outer.Left, frame.Top - outer.Top,
                outer.Right - frame.Right, outer.Bottom - frame.Bottom);
            if (!Native.IsIconic(member) && !Native.IsZoomed(member))
                frameInsets[member] = (dpi, insets);
            return insets;
        }
        return default;
    }

    private Native.Rect GetSplitBounds(nint member)
    {
        var leftInsets = GetFrameInsets(splitLeft);
        var rightInsets = GetFrameInsets(splitRight);
        var insets = member == splitLeft ? leftInsets : rightInsets;
        var visibleLeft = bounds.Left + leftInsets.Left;
        var visibleRight = bounds.Right - rightInsets.Right;
        var middle = visibleLeft + (int)Math.Round((visibleRight - visibleLeft) * splitRatio);
        return new Native.Rect((member == splitLeft ? visibleLeft : middle) - insets.Left,
            bounds.Top, (member == splitLeft ? middle : visibleRight) + insets.Right, bounds.Bottom);
    }

    public void ConfigureAutomaticLayout(bool split)
    {
        if (bar.IsSplit != split)
            ReplaceBar(split ? new FrmSplitGroupBar() : new FrmTopGroupBar());
        else if (!hidden && !minimized)
            ShowMembers();
    }

    public void ToggleHidden()
    {
        hidden = !hidden;
        if (hidden)
        {
            bar.Hide();
            foreach (var member in Members.Where(IsMemberAlive))
                Native.ShowWindowAsync(member, 0);
        }
        if (!hidden)
        {
            minimized = false;
            bar.Show();
            ShowMembers();
            Native.SetForegroundWindow(selected);
            PositionBar();
            SyncBarOrder();
        }
    }

    private void ToggleMaximize()
    {
        hidden = false;
        minimized = false;
        if (!maximized)
            normalBounds = bounds;
        maximized = !maximized;
        if (maximized)
            UpdateMaximizedBounds();
        else
            bounds = normalBounds;
        ShowMembers();
        Native.SetForegroundWindow(selected);
    }

    private void UpdateMaximizedBounds()
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfoW(Native.MonitorFromWindow(selected, 2), ref info))
            return;
        var thickness = (int)Math.Ceiling((bar.IsVertical ? Configuration.LeftBarWidth : Configuration.BarHeight)
            * Native.GetDpiForWindow(new WindowInteropHelper(bar).Handle) / 96.0) + Configuration.BarGapPixels;
        var leftInset = 0;
        var topInset = 0;
        var rightInset = 0;
        var bottomInset = 0;
        if (Native.GetWindowRect(selected, out var outer)
            && Native.DwmGetWindowAttribute(selected, 9, out Native.Rect frame, Marshal.SizeOf<Native.Rect>()) == 0
            && frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            leftInset = frame.Left - outer.Left;
            topInset = frame.Top - outer.Top;
            rightInset = outer.Right - frame.Right;
            bottomInset = outer.Bottom - frame.Bottom;
        }
        bounds = new Native.Rect(info.Work.Left + (bar.IsVertical ? thickness : 0) - leftInset,
            info.Work.Top + (bar.IsVertical ? 0 : thickness) - topInset,
            info.Work.Right + rightInset, info.Work.Bottom + bottomInset);
    }

    public void UpdateLocation()
    {
        if (!disposed && !moving && !hidden && !minimized && !Native.IsIconic(selected))
            Update();
    }

    public void Update()
    {
        if (disposed)
            return;
        if (moving)
        {
            SyncBarOrder();
            return;
        }
        var changed = false;
        foreach (var member in Members.ToArray())
        {
            Native.GetWindowThreadProcessId(member, out var process);
            if (!Native.IsWindow(member) || process != processes[member])
            {
                Members.Remove(member);
                processes.Remove(member);
                beforeSplitMembers.Remove(member);
                splitMemberBounds.Remove(member);
                pendingSplitPlacements.Remove(member);
                splitLeftMembers.Remove(member);
                changed = true;
            }
        }
        if (Members.Count < 2)
        {
            Dissolve();
            return;
        }
        if (changed)
            RebuildTabs();
        if (!Contains(selected))
        {
            selected = Members[0];
            if (!hidden && !minimized)
                ShowMembers();
        }
        if (changed && bar.IsSplit && !hidden && !minimized)
            ShowMembers();
        var foreground = Native.GetForegroundWindow();
        if (Contains(foreground))
        {
            selected = foreground;
            if (minimized && !Native.IsIconic(selected))
            {
                minimized = false;
                bar.Show();
                ShowMembers();
                Native.SetForegroundWindow(selected);
            }
        }
        RefreshTabs();
        SyncBarOrder();
        if (hidden || minimized)
            return;
        if (!Native.IsWindowVisible(selected))
            return;
        if (Native.IsIconic(selected))
        {
            SetState(6);
            return;
        }
        if (Native.IsZoomed(selected))
        {
            maximized = true;
            foreach (var member in Members.Where(IsMemberAlive))
                if (Native.IsZoomed(member))
                    Native.ShowWindowAsync(member, 9);
            return;
        }
        if (bar.IsSplit)
        {
            if (maximized)
            {
                UpdateMaximizedBounds();
                PlaceSplitMembers(false);
            }
            else if (Native.GetWindowRect(selected, out var memberRect))
            {
                if (pendingSplitPlacements.TryGetValue(selected, out var previousRect))
                {
                    if (memberRect != previousRect)
                    {
                        pendingSplitPlacements.Remove(selected);
                        splitMemberBounds[selected] = memberRect;
                    }
                    PositionBar();
                    return;
                }
                var expected = splitMemberBounds.TryGetValue(selected, out var placedBounds)
                    ? placedBounds : GetSplitBounds(selected);
                if (memberRect.Right - memberRect.Left == expected.Right - expected.Left
                    && memberRect.Bottom - memberRect.Top == expected.Bottom - expected.Top)
                {
                    var deltaX = memberRect.Left - expected.Left;
                    var deltaY = memberRect.Top - expected.Top;
                    if (deltaX != 0 || deltaY != 0)
                    {
                        splitMemberBounds[selected] = memberRect;
                        bounds = new Native.Rect(bounds.Left + deltaX, bounds.Top + deltaY,
                            bounds.Right + deltaX, bounds.Bottom + deltaY);
                        normalBounds = bounds;
                        var other = selected == splitLeft ? splitRight : splitLeft;
                        if (IsMemberAlive(other))
                        {
                            QueueDragMove(other, processes[other], GetSplitBounds(other));
                        }
                    }
                }
                else
                {
                    var leftInsets = GetFrameInsets(splitLeft);
                    var rightInsets = GetFrameInsets(splitRight);
                    var insets = selected == splitLeft ? leftInsets : rightInsets;
                    var visibleLeft = bounds.Left + leftInsets.Left;
                    var visibleRight = bounds.Right - rightInsets.Right;
                    var divider = selected == splitLeft ? memberRect.Right - insets.Right : memberRect.Left + insets.Left;
                    if (selected == splitLeft && memberRect.Left != expected.Left)
                        bounds = bounds with { Left = memberRect.Left };
                    if (selected == splitRight && memberRect.Right != expected.Right)
                        bounds = bounds with { Right = memberRect.Right };
                    bounds = bounds with { Top = memberRect.Top, Bottom = memberRect.Bottom };
                    visibleLeft = bounds.Left + leftInsets.Left;
                    visibleRight = bounds.Right - rightInsets.Right;
                    splitRatio = Math.Clamp((double)(divider - visibleLeft) / Math.Max(1, visibleRight - visibleLeft), 0.1, 0.9);
                    normalBounds = bounds;
                    PlaceSplitMembers(false);
                }
            }
            PositionBar();
            return;
        }
        if (maximized)
        {
            UpdateMaximizedBounds();
            foreach (var member in Members.Where(IsMemberAlive))
                if (Native.GetWindowRect(member, out var memberBounds) && memberBounds != bounds)
                    Native.Place(member, bounds);
            PositionBar();
            return;
        }
        if (Native.GetWindowRect(selected, out var current))
        {
            if (pendingOverlapMove is { } pending && pending.Handle == selected
                && current != pending.Bounds && Environment.TickCount64 < pending.Deadline)
            {
                PositionBar();
                return;
            }
            pendingOverlapMove = null;
            if (current != bounds)
            {
                bounds = current;
                foreach (var member in Members.Where(member => member != selected && IsMemberAlive(member)))
                {
                    if (Native.IsZoomed(member) != Native.IsZoomed(selected))
                        Native.ShowWindowAsync(member, Native.IsZoomed(selected) ? 3 : 9);
                    Native.Place(member, bounds);
                }
            }
        }
        if (!maximized)
            normalBounds = bounds;
        PositionBar();
    }

    private void PositionBar()
    {
        if (moving)
            return;
        var handle = new WindowInteropHelper(bar).Handle;
        if (handle == 0)
            return;
        var thickness = (int)Math.Ceiling((bar.IsVertical ? Configuration.LeftBarWidth : Configuration.BarHeight)
            * Native.GetDpiForWindow(handle) / 96.0);
        var visibleBounds = bounds;
        if (Native.DwmGetWindowAttribute(selected, 9, out Native.Rect frame, Marshal.SizeOf<Native.Rect>()) == 0
            && frame.Right > frame.Left && frame.Bottom > frame.Top)
            visibleBounds = frame;
        if (!bar.IsSplit && pendingOverlapMove is { } pending && pending.Handle == selected
            && Environment.TickCount64 < pending.Deadline)
        {
            var insets = GetFrameInsets(selected);
            visibleBounds = new Native.Rect(pending.Bounds.Left + insets.Left, pending.Bounds.Top + insets.Top,
                pending.Bounds.Right - insets.Right, pending.Bounds.Bottom - insets.Bottom);
        }
        if (bar.IsSplit
            && Native.DwmGetWindowAttribute(splitLeft, 9, out Native.Rect leftFrame, Marshal.SizeOf<Native.Rect>()) == 0
            && Native.DwmGetWindowAttribute(splitRight, 9, out Native.Rect rightFrame, Marshal.SizeOf<Native.Rect>()) == 0)
        {
            if (pendingSplitPlacements.ContainsKey(splitLeft))
            {
                var target = GetSplitBounds(splitLeft);
                var insets = GetFrameInsets(splitLeft);
                leftFrame = new Native.Rect(target.Left + insets.Left, target.Top + insets.Top,
                    target.Right - insets.Right, target.Bottom - insets.Bottom);
            }
            if (pendingSplitPlacements.ContainsKey(splitRight))
            {
                var target = GetSplitBounds(splitRight);
                var insets = GetFrameInsets(splitRight);
                rightFrame = new Native.Rect(target.Left + insets.Left, target.Top + insets.Top,
                    target.Right - insets.Right, target.Bottom - insets.Bottom);
            }
            visibleBounds = new Native.Rect(Math.Min(leftFrame.Left, rightFrame.Left), Math.Min(leftFrame.Top, rightFrame.Top),
                Math.Max(leftFrame.Right, rightFrame.Right), Math.Max(leftFrame.Bottom, rightFrame.Bottom));
            if (bar is FrmSplitGroupBar splitBar)
                splitBar.SetRegions(splitLeft, splitRight,
                    (rightFrame.Left - visibleBounds.Left) * 96.0 / Native.GetDpiForWindow(handle), splitLeftMembers);
        }
        var monitor = Native.MonitorFromWindow(selected, 2);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        var left = bar.IsVertical ? visibleBounds.Left - thickness - Configuration.BarGapPixels : visibleBounds.Left;
        var top = bar.IsVertical ? visibleBounds.Top : visibleBounds.Top - thickness - Configuration.BarGapPixels;
        if (Native.GetMonitorInfoW(monitor, ref info))
        {
            if (bar.IsVertical)
                left = Math.Max(left, info.Work.Left);
            else
                top = Math.Max(top, info.Work.Top);
        }
        Native.SetWindowPos(handle, 0, left, top,
            bar.IsVertical ? thickness : Math.Max(1, visibleBounds.Right - visibleBounds.Left),
            bar.IsVertical ? Math.Max(1, visibleBounds.Bottom - visibleBounds.Top) : thickness,
            0x0010 | 0x0004 | 0x4000);
    }

    public void UpdateOrder()
    {
        if (!disposed)
            SyncBarOrder();
    }

    private void SyncBarOrder()
    {
        var handle = new WindowInteropHelper(bar).Handle;
        if (handle == 0)
            return;
        var foreground = Native.GetForegroundWindow();
        var active = foreground == handle ? selected : foreground;
        if (bar.IsSplit && !hidden && !minimized
            && (active == splitLeft || active == splitRight))
        {
            var other = active == splitLeft ? splitRight : splitLeft;
            var previousMember = active;
            foreach (var member in Members.Where(member => member != active)
                .OrderBy(member => member == other ? 0 : 1))
            {
                if (!IsMemberAlive(member) || Native.IsIconic(member))
                    continue;
                if (Native.GetWindow(previousMember, 2) != member)
                    Native.SetWindowPos(member, previousMember, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x4000);
                previousMember = member;
            }
        }
        nint highest = 0;
        Native.EnumWindows((candidate, _) =>
        {
            if (!Contains(candidate) || !Native.IsWindowVisible(candidate) || Native.IsIconic(candidate))
                return true;
            highest = candidate;
            return false;
        }, 0);
        if (highest == 0)
            return;
        var previous = Native.GetWindow(highest, 3);
        if (previous == handle)
            return;
        if (previous != 0 && (Native.GetWindowLongW(previous, -20) & 0x0008) != 0)
            previous = 0;
        Native.SetWindowPos(handle, previous, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public void Dissolve()
    {
        if (disposed)
            return;
        disposed = true;
        lock (dragMoveLock)
            pendingDragMove = null;
        foreach (var handle in Members)
            if (IsMemberAlive(handle))
            {
                Native.ShowWindowAsync(handle, 9);
                if (!bar.IsSplit)
                    Native.Place(handle, maximized ? normalBounds : bounds);
                else
                    RestoreBeforeSplitSize(handle);
            }
        removed(this);
        bar.Close();
    }

    private void RestoreBeforeSplitSize(nint handle)
    {
        if (beforeSplitMembers.TryGetValue(handle, out var original)
            && Native.GetWindowRect(handle, out var current))
            Native.Place(handle, new Native.Rect(current.Left, current.Top,
                current.Left + original.Right - original.Left,
                current.Top + original.Bottom - original.Top));
    }
}
