namespace Plutao;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppCache.Initialize();
        Application.Run(new MainForm());
    }
}
