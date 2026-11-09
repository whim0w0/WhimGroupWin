using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace WhimGroupWin;

public sealed record AutomaticApplication
{
    public string ExecutablePath { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public override string ToString() => string.IsNullOrWhiteSpace(WindowTitle)
        ? Path.GetFileName(ExecutablePath) : $"{Path.GetFileName(ExecutablePath)} · {WindowTitle}";
}

public sealed record AutomaticGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public bool Split { get; init; }
    public ModifierKeys Modifiers { get; init; } = ModifierKeys.Control | ModifierKeys.Alt;
    public Key Key { get; init; } = Key.F1;
    public List<AutomaticApplication> Applications { get; init; } = [];
    public string Summary => $"{(Split ? "左右分屏" : "重叠模式")} · {Applications.Count} 个应用 · {Modifiers} + {Key}";
}

internal static class AutomaticGroups
{
    internal static string FilePath { get; } = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "automatic-groups.json");
    public static IReadOnlyList<AutomaticGroup> Current { get; private set; } = Load();
    public static event Action? Changed;

    internal static List<AutomaticGroup> Read(string path) =>
        JsonSerializer.Deserialize<List<AutomaticGroup>>(File.ReadAllText(path)) ?? [];

    private static IReadOnlyList<AutomaticGroup> Load()
    {
        try
        {
            var groups = Read(FilePath);
            Validate(groups);
            return groups;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return [];
        }
    }

    internal static void Validate(IReadOnlyList<AutomaticGroup> groups)
    {
        var shortcuts = new HashSet<(ModifierKeys, Key)>();
        var rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applications = new List<AutomaticApplication>();
        var identifiers = new HashSet<Guid>();
        foreach (var group in groups)
        {
            if (group is null || group.Id == Guid.Empty || !identifiers.Add(group.Id)
                || string.IsNullOrWhiteSpace(group.Name) || group.Applications is null || group.Applications.Count < 1)
                throw new ArgumentException("请填写组名，并添加至少一个应用窗口规则。");
            if (group.Modifiers == ModifierKeys.None
                || (group.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0
                || !KeyboardSettings.AvailableKeys.Contains(group.Key)
                || group.Modifiers == ModifierKeys.Alt && group.Key is Key.Tab or Key.F4)
                throw new ArgumentException("请选择有效的显示/隐藏快捷键，不能使用 Alt+Tab 或 Alt+F4。");
            if (!shortcuts.Add((group.Modifiers, group.Key))
                || group.Modifiers == KeyboardSettings.Current.SwitchModifiers && group.Key == KeyboardSettings.Current.SwitchKey)
                throw new ArgumentException("快捷键已被其他组或窗口切换操作使用。");
            foreach (var application in group.Applications)
            {
                if (application is null || string.IsNullOrWhiteSpace(application.ExecutablePath)
                    || !Path.IsPathFullyQualified(application.ExecutablePath) || application.WindowTitle is null)
                    throw new ArgumentException("应用必须使用完整的可执行文件路径。");
                if (!rules.Add(application.ExecutablePath + "\n" + application.WindowTitle))
                    throw new ArgumentException("同一个应用窗口规则不能重复加入多个组。");
                if (applications.Any(existing => string.Equals(existing.ExecutablePath, application.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    && (existing.WindowTitle.Length == 0 || application.WindowTitle.Length == 0)))
                    throw new ArgumentException("同一应用的任意标题规则不能与其他窗口规则重叠。");
                applications.Add(application);
            }
        }
    }

    internal static void Write(string path, IReadOnlyList<AutomaticGroup> groups)
    {
        Validate(groups);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    public static void Save(IReadOnlyList<AutomaticGroup> groups)
    {
        Write(FilePath, groups);
        Current = groups.ToArray();
        Changed?.Invoke();
    }
}