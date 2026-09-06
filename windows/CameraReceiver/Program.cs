namespace CameraReceiver;
internal static class Program {
    [STAThread] static void Main(string[] args) {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var data = args.Contains("--test-server") ? Path.GetFullPath(args[Array.IndexOf(args, "--test-server") + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPTCamera");
        Directory.CreateDirectory(data);
        using var singleton = new Mutex(true, "Local\\ChatGPTCamera-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data)))[..16], out bool first);
        if (!first) { System.Windows.Forms.MessageBox.Show("接收端已在运行，请从系统托盘打开。", "ChatGPT 相机"); return; }
        if (args.Contains("--test-server")) {
            var server = new ReceiverServer(data, () => Task.FromResult<TargetIdentity?>(null), _ => { });
            server.StartAsync(0, true).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(data, "test-ready.json"), System.Text.Json.JsonSerializer.Serialize(new { server.Port, server.Token, server.Pin }));
            Thread.Sleep(Timeout.Infinite); return;
        }
        System.Windows.Forms.Application.Run(new MainForm(data));
    }
}
