namespace DKImageSimpleUpscaler;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "DK Image Simple Upscaler 시작 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
