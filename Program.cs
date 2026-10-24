namespace MirWindowsClient;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        var form = new Form1();
        if (args.Contains("--preview-world", StringComparer.OrdinalIgnoreCase)) form.ShowWorldScreenPreview();
        else if (args.Contains("--preview-characters", StringComparer.OrdinalIgnoreCase)) form.ShowCharacterScreenPreview();
        Application.Run(form);
    }
}
