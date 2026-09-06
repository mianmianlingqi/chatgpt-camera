using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace CameraReceiver;

public sealed class DesktopTarget {
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT Keyboard; [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int x, y; public uint data, flags, time; public UIntPtr extra; }
    public string LastReason { get; private set; } = "等待拍摄";
    private static string Runtime(AutomationElement e) => string.Join(".", e.GetRuntimeId());
    private static bool IsComposer(AutomationElement e) {
        var p = e.Current;
        return p.ControlType == ControlType.Edit && p.IsEnabled && p.IsKeyboardFocusable && !p.IsOffscreen &&
            Regex.IsMatch(p.ClassName + " " + p.AutomationId + " " + p.Name + " " + p.HelpText,
                "ProseMirror|composer|prompt.textarea|message.*(chatgpt|codex)|follow.up|ask.*(anything|codex)|输入.*(消息|问题)|发送消息", RegexOptions.IgnoreCase);
    }
    private (TargetIdentity Identity, AutomationElement Editor, AutomationElement Root)? Observe() {
        try {
            var hwnd = GetForegroundWindow(); if (hwnd == IntPtr.Zero) return Fail("当前没有活动窗口。");
            GetWindowThreadProcessId(hwnd, out var pid); using var process = Process.GetProcessById((int)pid);
            var path = process.MainModule?.FileName ?? "";
            if (!(path.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) || path.Contains("OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase)) ||
                !new[] { "ChatGPT", "Codex" }.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)) return Fail("请将 Codex 对话切到电脑前台。");
            var root = AutomationElement.FromHandle(hwnd);
            var editors = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>().Where(IsComposer).ToArray();
            if (editors.Length != 1) return Fail("无法唯一识别 Codex 输入框，照片会保留待插入。");
            var editor = editors[0];
            // Require an explicit selected conversation identity, never just a generic window title.
            var tabs = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsSelectionItemPatternAvailableProperty, true));
            var selected = tabs.Cast<AutomationElement>().Where(e => !e.Current.IsOffscreen &&
                e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && ((SelectionItemPattern)p).Current.IsSelected &&
                Regex.IsMatch(e.Current.AutomationId, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase))
                .Select(e => e.Current.AutomationId + ":" + Runtime(e)).OrderBy(s => s).ToArray();
            if (selected.Length == 0) return Fail("当前版本未暴露可靠的会话标识；已启用保留队列，请手动粘贴。");
            LastReason = "已识别前台 Codex 对话和输入框。";
            return (new TargetIdentity(hwnd.ToInt64(), (int)pid, string.Join("|", selected), Runtime(editor)), editor, root);
        } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or COMException) { return Fail("读取当前界面失败，照片会保留。" ); }
    }
    private (TargetIdentity, AutomationElement, AutomationElement)? Fail(string reason) { LastReason = reason; return null; }
    public TargetIdentity? Capture() => Observe()?.Identity;
    private static int AttachmentCount(AutomationElement root) => root.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().Count(e => !e.Current.IsOffscreen &&
            Regex.IsMatch(e.Current.Name, @"^(remove|delete).*(attachment|image|file|\.(png|jpe?g|webp))|^(移除|删除).*(附件|图片|文件|\.(png|jpe?g|webp))", RegexOptions.IgnoreCase));
    public async Task<(string State, string Detail)> AttachAsync(CaptureRecord record, string path, Func<Action, Task> onUi) {
        var before = Observe();
        if (before is null || !TargetGuard.CanAttach(record.Target, before.Value.Identity, record.Created, DateTimeOffset.UtcNow)) return ("held", before is null ? LastReason : "窗口、会话或输入框已改变，照片已保留。");
        int count = AttachmentCount(before.Value.Root);
        before.Value.Editor.SetFocus();
        var current = Observe();
        if (current is null || !TargetGuard.CanAttach(record.Target, current.Value.Identity, record.Created, DateTimeOffset.UtcNow) || Runtime(AutomationElement.FocusedElement) != record.Target!.Editor) return ("held", "输入框焦点发生变化，照片已保留。");
        bool injected = false;
        await onUi(() => {
            // Clipboard is intentionally left with this photo so the user can paste manually if verification fails.
            using var bitmap = PhotoBitmap.Load(path);
            System.Windows.Forms.Clipboard.SetImage(bitmap);
            var final = Observe();
            if (final is null || !TargetGuard.CanAttach(record.Target, final.Value.Identity, record.Created, DateTimeOffset.UtcNow) || Runtime(AutomationElement.FocusedElement) != record.Target!.Editor) return;
            if (GetForegroundWindow().ToInt64() != record.Target!.Window || new[] { 0x11, 0x12, 0x10, 0x5b, 0x5c }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0)) return;
            var events = new[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
            injected = SendInput((uint)events.Length, events, Marshal.SizeOf<INPUT>()) == events.Length;
        });
        if (!injected) return ("held", "未执行粘贴；照片已复制，可回到目标输入框按 Ctrl+V。");
        for (int attempt = 0; attempt < 12; attempt++) {
            await Task.Delay(250);
            var check = Observe();
            if (check is null || check.Value.Identity != record.Target) break;
            if (AttachmentCount(check.Value.Root) > count) return ("attached", "已检测到输入框新增附件，等待你发送消息。");
        }
        return ("unconfirmed", "已执行粘贴，但未确认附件出现。请先检查输入框，避免重复粘贴。");
    }
    private static INPUT Key(ushort code, bool up) => new() { type = 1, U = new InputUnion { Keyboard = new KEYBDINPUT { key = code, flags = up ? 2u : 0u } } };
}
