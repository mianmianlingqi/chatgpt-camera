using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using QRCoder;
using Forms = System.Windows.Forms;

namespace CameraReceiver;

public sealed class MainForm : Forms.Form {
    private readonly ReceiverServer server;
    private readonly DesktopTarget desktop = new();
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.Label status = new() { AutoSize = true, MaximumSize = new(660, 0) };
    private readonly Forms.ComboBox hosts = new() { Width = 280, DropDownStyle = Forms.ComboBoxStyle.DropDownList };
    private readonly Forms.PictureBox qr = new() { Width = 290, Height = 290, SizeMode = Forms.PictureBoxSizeMode.Zoom, BackColor = System.Drawing.Color.White };
    private readonly Forms.ListBox queue = new() { Width = 650, Height = 150, HorizontalScrollbar = true };
    private readonly SemaphoreSlim attachGate = new(1);
    private bool quitting;
    public MainForm(string data) {
        Text = "ChatGPT 相机 · 电脑接收端"; Width = 740; Height = 1020; MinimumSize = new(720, 820);
        Font = new("Microsoft YaHei UI", 10); BackColor = System.Drawing.Color.FromArgb(245, 247, 250);
        var layout = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(24) };
        Controls.Add(layout);
        layout.Controls.Add(new Forms.Label { Text = "拍完，即刻到电脑", Font = new("Microsoft YaHei UI", 21, System.Drawing.FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Forms.Label { Text = "手机扫描配对二维码，然后把 Codex 对话放到前台。", AutoSize = true });
        layout.Controls.Add(hosts); layout.Controls.Add(qr);
        var pairRow = new Forms.FlowLayoutPanel { Width = 660, Height = 42 };
        var copyPair = new Forms.Button { Text = "复制配对内容", AutoSize = true }; pairRow.Controls.Add(copyPair);
        var hide = new Forms.Button { Text = "收起到托盘", AutoSize = true }; pairRow.Controls.Add(hide); hide.Click += (_, _) => Hide();
        layout.Controls.Add(pairRow); layout.Controls.Add(status);
        layout.Controls.Add(new Forms.Label { Text = "最近照片（状态不明确时，请先检查 Codex 附件）", AutoSize = true, Margin = new(0, 12, 0, 4) });
        layout.Controls.Add(queue);
        var actions = new Forms.FlowLayoutPanel { Width = 660, Height = 45 };
        var copy = new Forms.Button { Text = "复制所选照片", AutoSize = true }; actions.Controls.Add(copy);
        var folder = new Forms.Button { Text = "打开接收文件夹", AutoSize = true }; actions.Controls.Add(folder);
        layout.Controls.Add(actions);
        layout.Controls.Add(new Forms.Label { Text = "复制后：回到目标输入框按 Ctrl+V。程序不会发送消息。\n自动附加需要此版本暴露可识别的会话和输入框，否则保留照片。", AutoSize = true, MaximumSize = new(650, 0), ForeColor = System.Drawing.Color.DimGray });
        server = new ReceiverServer(data, () => Task.Run(desktop.Capture), r => _ = ProcessAsync(r));
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "ChatGPT 相机", Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("打开接收端", null, (_, _) => { Show(); Activate(); });
        menu.Items.Add("退出", null, (_, _) => { quitting = true; Close(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => { Show(); Activate(); };
        hosts.SelectedIndexChanged += (_, _) => RefreshPairing();
        copyPair.Click += (_, _) => { if (hosts.SelectedItem is string host) { Forms.Clipboard.SetText(server.Pairing(host)); status.Text = "配对内容已复制，可在手机中手动粘贴。请勿分享二维码或配对内容。"; } };
        copy.Click += (_, _) => {
            if (queue.SelectedItem is QueueItem item && File.Exists(server.Store.ImagePath(item.Record.Id))) {
                try { using var image = PhotoBitmap.Load(server.Store.ImagePath(item.Record.Id)); Forms.Clipboard.SetImage(image); status.Text = "照片已复制。请回到目标 Codex 输入框，按 Ctrl+V。"; }
                catch (Exception ex) { status.Text = "复制失败：" + ex.Message; }
            }
        };
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo(Path.Combine(data, "photos")) { UseShellExecute = true });
        Shown += async (_, _) => {
            try {
                await server.StartAsync();
                var addresses = Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()).Distinct().ToArray();
                hosts.Items.AddRange(addresses); if (addresses.Length > 0) hosts.SelectedIndex = 0;
                status.Text = addresses.Length == 0 ? "未找到局域网地址，请连接 Wi-Fi 后重启接收端。" : $"接收端已启动，端口 {server.Port}。请选择与手机同一网络的电脑地址。";
                RefreshQueue();
            } catch (Exception ex) { status.Text = "启动失败：" + ex.Message; }
        };
        FormClosing += (_, e) => { if (!quitting && e.CloseReason == Forms.CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += async (_, _) => { tray.Visible = false; tray.Dispose(); await server.StopAsync(); };
    }
    private void RefreshPairing() {
        if (hosts.SelectedItem is not string host || server.Port == 0) return;
        using var generator = new QRCodeGenerator(); using var code = generator.CreateQrCode(server.Pairing(host), QRCodeGenerator.ECCLevel.M);
        using var bitmap = new QRCode(code); var old = qr.Image; qr.Image = bitmap.GetGraphic(6); old?.Dispose();
    }
    private void RefreshQueue() {
        var selected = (queue.SelectedItem as QueueItem)?.Record.Id; queue.Items.Clear();
        foreach (var record in server.Store.All().Where(r => r.Hash != null).Take(100)) queue.Items.Add(new QueueItem(record));
        for (int i = 0; i < queue.Items.Count; i++) if (((QueueItem)queue.Items[i]).Record.Id == selected) queue.SelectedIndex = i;
        if (queue.SelectedIndex < 0 && queue.Items.Count > 0) queue.SelectedIndex = 0;
    }
    private Task OnUi(Action action) {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed) { completion.SetException(new ObjectDisposedException(nameof(MainForm))); return completion.Task; }
        BeginInvoke(() => { try { action(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); } }); return completion.Task;
    }
    private async Task ProcessAsync(CaptureRecord record) {
        await attachGate.WaitAsync();
        try {
            record.State = "attaching"; server.Store.Save(record);
            var result = await Task.Run(() => desktop.AttachAsync(record, server.Store.ImagePath(record.Id), OnUi));
            record.State = result.State; record.Detail = result.Detail; server.Store.Save(record);
            await OnUi(() => { RefreshQueue(); status.Text = record.Detail; tray.ShowBalloonTip(3000, "照片已到电脑", record.Detail, Forms.ToolTipIcon.Info); });
        } catch (Exception) {
            record.State = "unconfirmed"; record.Detail = "附加过程未完成，请检查 Codex 附件。照片已保留。"; server.Store.Save(record);
            try { await OnUi(() => { RefreshQueue(); status.Text = record.Detail; }); } catch (Exception) { }
        } finally { attachGate.Release(); }
    }
    private record QueueItem(CaptureRecord Record) {
        public override string ToString() => $"{Record.Created.LocalDateTime:HH:mm:ss}  {Record.State switch { "attached" => "已附加", "held" => "待手动插入", "unconfirmed" => "请检查附件", _ => "已接收" }}  {Record.Id[..8]}";
    }
}
