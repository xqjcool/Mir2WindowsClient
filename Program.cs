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
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();
        Form1? form = null;
        Application.ThreadException += (_, eventArgs) =>
        {
            LoginClient.RecordClientException("WinForms UI thread", eventArgs.Exception);
            MessageBox.Show(form,
                $"客户端遇到错误，详细信息已写入 %LOCALAPPDATA%\\MirWindowsClient\\client-exceptions.log。\n\n{eventArgs.Exception.GetType().Name}: {eventArgs.Exception.Message}",
                "Mir 1.76", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        form = new Form1();
        if (args.Contains("--preview-world", StringComparer.OrdinalIgnoreCase)) form.ShowWorldScreenPreview();
        else if (args.Contains("--preview-characters", StringComparer.OrdinalIgnoreCase)) form.ShowCharacterScreenPreview();
        Application.Run(form);
    }
}
