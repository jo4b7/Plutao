namespace Plutao;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var form = new MainForm();

        // Carrega e salva os destinos escolhidos manualmente pelo usuário.
        // Em uma instalação nova, Vídeos e Áudios começam sem pasta padrão:
        // o usuário escolhe uma vez e o Plutao lembra nas próximas aberturas.
        UserSettingsPersistence.Attach(form);

        Application.Run(form);
    }
}
