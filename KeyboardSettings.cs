using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace WhimGroupWin;

internal sealed record KeyboardSettings
{
    public ModifierKeys GroupModifiers { get; init; } = ModifierKeys.Control | ModifierKeys.Alt;
    public ModifierKeys SwitchModifiers { get; init; } = ModifierKeys.Shift;
    public Key SwitchKey { get; init; } = Key.Tab;

    private static readonly string FilePath = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
        "keyboard.json");

    public static IReadOnlyList<Key> AvailableKeys { get; } = new[] { Key.Tab, Key.Space }
        .Concat(Enumerable.Range((int)Key.A, 26).Select(value => (Key)value))
        .Concat(Enumerable.Range((int)Key.F1, 12).Select(value => (Key)value)).ToArray();

    public static KeyboardSettings Current { get; private set; } = Load();

    public bool IsValid => ValidModifiers(GroupModifiers) && ValidModifiers(SwitchModifiers)
        && AvailableKeys.Contains(SwitchKey)
        && !(SwitchKey == Key.Tab && SwitchModifiers == ModifierKeys.Alt)
        && !(SwitchKey == Key.F4 && SwitchModifiers == ModifierKeys.Alt);

    private static bool ValidModifiers(ModifierKeys modifiers) => modifiers != ModifierKeys.None
        && (modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) == 0;

    private static KeyboardSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<KeyboardSettings>(File.ReadAllText(FilePath));
            return settings is { IsValid: true } ? settings : new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        if (!IsValid)
            throw new ArgumentException("每项至少选择一个修饰键，且不能使用 Alt+Tab 或 Alt+F4。");
        if (AutomaticGroups.Current.Any(group => group.Modifiers == SwitchModifiers && group.Key == SwitchKey))
            throw new ArgumentException("窗口切换快捷键已被自动组使用。");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, FilePath, true);
        Current = this;
    }
}