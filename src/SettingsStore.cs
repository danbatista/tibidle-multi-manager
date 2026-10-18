using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TibidleMultiManager;

public sealed class AccountSettings
{
    public string Name { get; set; } = "";
    public bool Muted { get; set; }
    [JsonIgnore] public string ProfileName { get; internal set; } = "";
}

public sealed class AppSettings
{
    public List<AccountSettings> Accounts { get; set; } = [];
    public int SelectedTab { get; set; }
    public bool GridView { get; set; }
    public List<int> TabOrder { get; set; } = [0, 1, 2, 3];
}

public sealed class SettingsStore(string root)
{
    public string Root => root;
    // AppData can be redirected when launched from a packaged host such as Codex.
    // Use one physical location for launches from both Explorer and packaged hosts.
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tibidle-multi-manager");
    private string FilePath => Path.Combine(root, "settings.json");

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (JsonException) { settings = new(); }
        settings.Accounts ??= [];
        settings.Accounts = Enumerable.Range(0, 4).Select(i =>
        {
            var account = i < settings.Accounts.Count ? settings.Accounts[i] ?? new() : new AccountSettings();
            account.ProfileName = $"Account{i + 1}";
            account.Name = string.IsNullOrWhiteSpace(account.Name) ? $"Conta {i + 1}" : account.Name.Trim()[..Math.Min(account.Name.Trim().Length, 24)];
            return account;
        }).ToList();
        settings.SelectedTab = Math.Clamp(settings.SelectedTab, 0, 3);
        settings.TabOrder = (settings.TabOrder ?? []).Where(i => i >= 0 && i < 4)
            .Concat(Enumerable.Range(0, 4)).Distinct().ToList();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(root);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
