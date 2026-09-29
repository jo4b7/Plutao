using System.Text.Json;

namespace Plutao;

internal sealed class AppSettings
{
    public string VideoOutputDirectory { get; set; } = string.Empty;
    public string AudioOutputDirectory { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Plutao");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Uma preferência inválida nunca deve impedir o Plutao de abrir.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = SettingsPath + ".tmp";

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, true);
        }
        catch
        {
            // Falha ao salvar preferências não deve interromper o programa.
        }
    }
}
