namespace Pidal.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) =>
            MessageBox.Show(
                e.Exception.ToString(),
                "PIDAL - unexpected error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show(
                e.ExceptionObject?.ToString() ?? "Unknown error",
                "PIDAL - unexpected error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "PIDAL - failed to start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
