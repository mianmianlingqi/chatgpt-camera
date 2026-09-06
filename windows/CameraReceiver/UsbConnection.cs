using System.Diagnostics;

namespace CameraReceiver;

public record CommandResult(int ExitCode, string Output, string Error);
public record UsbResult(bool Success, string Message);

public sealed class UsbConnection(Func<string[], Task<CommandResult>> run) {
    public async Task<UsbResult> ConnectAsync(int port) {
        if (port < 1 || port > 65535) return new(false, "接收服务尚未启动。");
        var device = await run(["-d", "get-serialno"]);
        if (device.ExitCode != 0) return Failure(device);
        string serial = device.Output.Trim();
        if (string.IsNullOrWhiteSpace(serial) || serial == "unknown" || serial.Any(char.IsWhiteSpace))
            return new(false, "没有识别到手机，请检查数据线与 USB 调试授权。");
        string endpoint = $"tcp:{port}";
        var list = await run(["-s", serial, "reverse", "--list"]);
        if (list.ExitCode != 0) return Failure(list);
        var existing = Destination(list.Output, endpoint);
        if (existing != null && existing != endpoint) return new(false, "手机端口已被其他通道占用；请先处理冲突，本程序不会覆盖已有连接。");
        if (existing == null) {
            var created = await run(["-s", serial, "reverse", "--no-rebind", endpoint, endpoint]);
            if (created.ExitCode != 0) return Failure(created);
            var verify = await run(["-s", serial, "reverse", "--list"]);
            if (verify.ExitCode != 0) return Failure(verify);
            if (Destination(verify.Output, endpoint) != endpoint) return new(false, "未确认 USB 通道，请重新连接。");
        }
        return new(true, "USB 通道已建立。请用手机扫描当前二维码配对，再回到 Codex 拍照。拔线或重启后请重新点击连接。");
    }
    private static string? Destination(string output, string endpoint) {
        foreach (var line in output.Split('\n')) {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 3 && fields[1] == endpoint) return fields[2];
        }
        return null;
    }
    private static UsbResult Failure(CommandResult result) {
        var error = (result.Error + " " + result.Output).ToLowerInvariant();
        if (error.Contains("unauthorized")) return new(false, "请解锁手机，在手机上确认“允许 USB 调试”，然后重试。");
        if (error.Contains("more than one")) return new(false, "请只保留一台通过 USB 连接的安卓手机，然后重试。");
        return new(false, "未能建立 USB 通道。请检查数据线、手机 USB 调试授权和驱动，然后重试。");
    }
    public static string? FindAdb() {
        var paths = new List<string> { Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe") };
        foreach (var key in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" }) {
            var sdk = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(sdk)) paths.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));
        }
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"));
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            try { paths.Add(Path.Combine(folder.Trim('"'), "adb.exe")); } catch (ArgumentException) { }
        }
        return paths.FirstOrDefault(File.Exists);
    }
    public static async Task<CommandResult> ExecuteAsync(string executable, string[] args) {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try {
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await stdout, await stderr);
        } catch (OperationCanceledException) {
            // Stop only this CLI client, never the shared ADB server or other tools.
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            return new(-1, "", "USB command timed out");
        }
    }
}
