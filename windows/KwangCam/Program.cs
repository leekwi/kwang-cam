namespace KwangCam;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // 두 번 실행되지 않도록 (가상 카메라는 하나만 만들 수 있음)
        using var mutex = new Mutex(true, "KwangCam.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("광쌤캠이 이미 실행 중이에요. 작업 표시줄을 확인해 주세요.", "광쌤캠",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
