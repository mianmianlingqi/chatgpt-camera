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
    private readonly Forms.PictureBox preview = new() { Width = 650, Height = 360, SizeMode = Forms.PictureBoxSizeMode.Zoom, BackColor = System.Drawing.Color.FromArgb(225, 231, 238), Cursor = Forms.Cursors.Hand, AccessibleName = "照片预览，按住可拖动上传" };
    private readonly Forms.Label previewInfo = new() { AutoSize = true, MaximumSize = new(650, 0), Text = "收到照片后，在列表中选择即可预览。" };
    private string? previewPath;
    private int previewVersion;
    private System.Drawing.Point? dragStart;
    private bool quitting;
    public MainForm(string data) {
        server = new ReceiverServer(data, () => Task.Run(desktop.Capture), r => _ = ProcessAsync(r));
        Text = "ChatGPT 相机 · 电脑接收端"; Width = 740; Height = 1020; MinimumSize = new(720, 820);
        Font = new("Microsoft YaHei UI", 10); BackColor = System.Drawing.Color.FromArgb(245, 247, 250);
        var tabs = new Forms.TabControl { Dock = Forms.DockStyle.Fill };
        var photoTab = new Forms.TabPage("照片与拖拽"); var connectTab = new Forms.TabPage("连接手机");
        tabs.TabPages.Add(photoTab); tabs.TabPages.Add(connectTab); Controls.Add(tabs);
        status.AutoSize = false; status.MaximumSize = System.Drawing.Size.Empty; status.Dock = Forms.DockStyle.Bottom; status.Height = 90; status.Padding = new(16);
        Controls.Add(status);
        var layout = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(24) };
        connectTab.Controls.Add(layout);
        var photos = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(24) };
        photoTab.Controls.Add(photos);
        layout.Controls.Add(new Forms.Label { Text = "拍完，即刻到电脑", Font = new("Microsoft YaHei UI", 21, System.Drawing.FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Forms.Label { Text = "手机扫描配对二维码，然后把 Codex 对话放到前台。", AutoSize = true });
        layout.Controls.Add(hosts); layout.Controls.Add(qr);
        var pairRow = new Forms.FlowLayoutPanel { Width = 660, Height = 42 };
        var copyPair = new Forms.Button { Text = "复制配对内容", AutoSize = true }; pairRow.Controls.Add(copyPair);
        var hide = new Forms.Button { Text = "收起到托盘", AutoSize = true }; pairRow.Controls.Add(hide); hide.Click += (_, _) => Hide();
        layout.Controls.Add(pairRow);
        var usb = new Forms.Button { Text = "连接 USB 数据线", AutoSize = true };
        layout.Controls.Add(usb);
        layout.Controls.Add(new Forms.Label { Text = "USB：手机开启 USB 调试并允许此电脑，接好数据线后点击连接。\n无需同一个 Wi-Fi；首次连接后仍需扫码。只连接一台安卓手机。", AutoSize = true, MaximumSize = new(650, 0) });
        usb.Click += async (_, _) => {
            if (server.Port == 0) { status.Text = "请先等待接收服务启动成功。"; return; }
            string? adb = UsbConnection.FindAdb();
            if (adb == null) {
                using var picker = new Forms.OpenFileDialog { Title = "选择 Android platform-tools 中的 adb.exe", Filter = "Android Debug Bridge|adb.exe", CheckFileExists = true };
                if (picker.ShowDialog(this) != Forms.DialogResult.OK) { status.Text = "需要 Android platform-tools。安装后重新点击连接并选择 adb.exe。"; return; }
                adb = picker.FileName;
            }
            usb.Enabled = false; status.Text = "正在建立 USB 通道，请查看手机上的授权提示……";
            try {
                var connection = new UsbConnection(args => UsbConnection.ExecuteAsync(adb, args));
                var result = await connection.ConnectAsync(server.Port);
                if (IsDisposed) return;
                if (result.Success) {
                    if (!hosts.Items.Contains("127.0.0.1")) hosts.Items.Add("127.0.0.1");
                    hosts.SelectedItem = "127.0.0.1";
                    RefreshPairing();
                }
                status.Text = result.Message;
            } catch (Exception) { if (!IsDisposed) status.Text = "USB 连接失败，请检查 adb.exe 是否可运行，再重试。"; }
            finally { if (!IsDisposed) usb.Enabled = true; }
        };
        photos.Controls.Add(new Forms.Label { Text = "选一张，拖进对话", Font = new("Microsoft YaHei UI", 21, System.Drawing.FontStyle.Bold), AutoSize = true });
        photos.Controls.Add(new Forms.Label { Text = "按住下方预览图，拖到 Codex 输入框后松开。\n看到附件后再发送；也可以使用复制按钮。", AutoSize = true });
        photos.Controls.Add(preview); photos.Controls.Add(previewInfo);
        photos.Controls.Add(new Forms.Label { Text = "最近照片（请在 Codex 中确认附件是否出现）", AutoSize = true, Margin = new(0, 12, 0, 4) });
        photos.Controls.Add(queue);
        queue.SelectedIndexChanged += async (_, _) => await RefreshPreviewAsync();
        preview.MouseDown += (_, e) => { dragStart = e.Button == Forms.MouseButtons.Left && previewPath != null ? e.Location : null; };
        preview.MouseUp += (_, _) => dragStart = null;
        preview.MouseMove += (_, e) => {
            if (e.Button != Forms.MouseButtons.Left || dragStart is not { } start || previewPath is not string path) return;
            var size = Forms.SystemInformation.DragSize;
            if (new System.Drawing.Rectangle(start.X - size.Width / 2, start.Y - size.Height / 2, size.Width, size.Height).Contains(e.Location)) return;
            dragStart = null;
            try {
                var effect = preview.DoDragDrop(PhotoTransfer.CreateDragData(path), Forms.DragDropEffects.Copy);
                status.Text = effect == Forms.DragDropEffects.None ? "未完成拖放。请把预览图拖到目标输入框，或使用复制按钮。" : "拖放已结束，请在 Codex 中确认附件。程序不会自动发送消息。";
            } catch (Exception) { status.Text = "拖放失败。请重新选择照片，或使用复制按钮。"; }
        };
        var actions = new Forms.FlowLayoutPanel { Width = 660, Height = 45 };
        var copy = new Forms.Button { Text = "复制所选照片", AutoSize = true }; actions.Controls.Add(copy);
        var folder = new Forms.Button { Text = "打开接收文件夹", AutoSize = true }; actions.Controls.Add(folder);
        var clear = new Forms.Button { Text = "清空照片", AutoSize = true }; actions.Controls.Add(clear);
        clear.Click += async (_, _) => {
            clear.Enabled = false;
            await attachGate.WaitAsync();
            try {
                previewVersion++; previewPath = null; dragStart = null;
                var oldImage = preview.Image; preview.Image = null; oldImage?.Dispose();
                int count = await server.ClearPhotosAsync(path => Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
                status.Text = $"已清空 {count} 张电脑照片，原文件已移入回收站。手机照片、配对设置和 Codex 附件不受影响。";
            } catch (Exception) { status.Text = "部分照片未能清除，请稍后重试；未清除的照片仍保留在列表中。"; }
            finally { attachGate.Release(); if (!IsDisposed) { clear.Enabled = true; RefreshQueue(); } }
        };
        photos.Controls.Add(actions);
        photos.Controls.Add(new Forms.Label { Text = "复制后：回到目标输入框按 Ctrl+V。程序不会发送消息。\n自动附加需要此版本暴露可识别的会话和输入框，否则保留照片。", AutoSize = true, MaximumSize = new(650, 0), ForeColor = System.Drawing.Color.DimGray });
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
                if (queue.Items.Count == 0) tabs.SelectedTab = connectTab;
            } catch (Exception ex) { status.Text = "启动失败：" + ex.Message; }
        };
        FormClosing += (_, e) => { if (!quitting && e.CloseReason == Forms.CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += async (_, _) => { previewVersion++; preview.Image?.Dispose(); preview.Image = null; tray.Visible = false; tray.Dispose(); await server.StopAsync(); };
    }
    private async Task RefreshPreviewAsync() {
        int version = ++previewVersion; previewPath = null; dragStart = null;
        var previous = preview.Image; preview.Image = null; previous?.Dispose();
        if (queue.SelectedItem is not QueueItem item) { previewInfo.Text = "收到照片后，在列表中选择即可预览。"; return; }
        string path = server.Store.ImagePath(item.Record.Id);
        previewInfo.Text = "正在载入预览……";
        try {
            var image = await Task.Run(() => PhotoTransfer.LoadPreview(path));
            if (IsDisposed || Disposing || version != previewVersion) { image.Dispose(); return; }
            preview.Image = image; previewPath = path;
            previewInfo.Text = $"{item.Record.Created.LocalDateTime:HH:mm:ss} · {new FileInfo(path).Length / 1024:N0} KB · 按住图片拖动上传原图";
        } catch (Exception) { if (!IsDisposed && version == previewVersion) previewInfo.Text = "无法预览这张照片，请重新选择或打开接收文件夹。"; }
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
            if (!ReferenceEquals(server.Store.Get(record.Id), record)) return;
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
