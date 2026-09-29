using System.Reflection;
using System.Text.Json;

namespace Plutao;

internal static class UserSettingsPersistence
{
    private sealed class StoredSettings
    {
        public string VideoOutputDirectory { get; set; } = string.Empty;
        public string AudioOutputDirectory { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsDirectory
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Plutao");

    private static string SettingsPath
        => Path.Combine(SettingsDirectory, "settings.json");

    public static void Attach(MainForm form)
    {
        var videoOutput = GetPrivateField<TextBox>(form, "txtVideoOutput");
        var audioOutput = GetPrivateField<TextBox>(form, "txtAudioOutput");
        var browseVideo = GetPrivateField<Button>(form, "btnBrowseVideo");
        var browseAudio = GetPrivateField<Button>(form, "btnBrowseAudio");

        videoOutput.PlaceholderText = "Escolha uma pasta para os vídeos...";
        audioOutput.PlaceholderText = "Escolha uma pasta para os áudios...";

        // Substitui os antigos destinos fixos. Se ainda não houver preferência
        // salva, os campos ficam vazios até o usuário escolher manualmente.
        var saved = Load();
        videoOutput.Text = saved.VideoOutputDirectory;
        audioOutput.Text = saved.AudioOutputDirectory;

        void SaveCurrent()
        {
            Save(new StoredSettings
            {
                VideoOutputDirectory = videoOutput.Text.Trim(),
                AudioOutputDirectory = audioOutput.Text.Trim()
            });
        }

        // O MainForm registra primeiro os eventos dos botões Procurar.
        // Como este manipulador é registrado depois, ele salva o caminho
        // somente depois que o FolderBrowserDialog atualiza o TextBox.
        browseVideo.Click += (_, _) => SaveCurrent();
        browseAudio.Click += (_, _) => SaveCurrent();

        // Também funciona se o caminho for digitado/colado manualmente.
        videoOutput.Leave += (_, _) => SaveCurrent();
        audioOutput.Leave += (_, _) => SaveCurrent();

        // Garante persistência ao fechar o aplicativo.
        form.FormClosing += (_, _) => SaveCurrent();
    }

    private static T GetPrivateField<T>(MainForm form, string fieldName) where T : class
    {
        var field = typeof(MainForm).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field?.GetValue(form) is T value)
            return value;

        throw new InvalidOperationException(
            $"Não foi possível localizar o controle interno '{fieldName}'.");
    }

    private static StoredSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new StoredSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<StoredSettings>(json, JsonOptions)
                   ?? new StoredSettings();
        }
        catch
        {
            // Preferências corrompidas não podem impedir o Plutao de abrir.
            return new StoredSettings();
        }
    }

    private static void Save(StoredSettings settings)
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
            // Falha ao salvar preferência não deve interromper downloads.
        }
    }
}
