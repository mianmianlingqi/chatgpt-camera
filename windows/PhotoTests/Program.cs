using CameraReceiver;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

internal static class Program {
    [STAThread] static void Main() {
        string directory = Path.Combine(Path.GetTempPath(), "camera-preview-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "photo.jpg");
        using (var original = new Bitmap(800, 400)) { using var graphics = Graphics.FromImage(original); graphics.Clear(Color.CornflowerBlue); original.Save(path, ImageFormat.Jpeg); }
        byte[] before = File.ReadAllBytes(path);
        using var preview = PhotoTransfer.LoadPreview(path);
        Assert("preview preserves aspect and bounds", preview.Width == 640 && preview.Height == 320);
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert("preview does not lock original", stream.Length > 0);
        var drag = PhotoTransfer.CreateDragData(path);
        Assert("drag offers file drop", drag.GetDataPresent(DataFormats.FileDrop));
        var files = (string[])drag.GetData(DataFormats.FileDrop)!;
        Assert("drag uses exact original absolute path", files.Length == 1 && files[0] == Path.GetFullPath(path));
        Assert("original photo unchanged", before.SequenceEqual(File.ReadAllBytes(path)));
        bool rejected = false;
        try { PhotoTransfer.CreateDragData(Path.Combine(directory, "missing.jpg")); } catch (FileNotFoundException) { rejected = true; }
        Assert("missing photo rejected", rejected);
        Console.WriteLine("6 photo transfer checks passed; synthetic fixture: " + directory);
    }
    static void Assert(string name, bool value) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
}
