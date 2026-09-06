using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CameraReceiver;

internal static class PhotoTransfer {
    public static Bitmap LoadPreview(string path) {
        using var original = PhotoBitmap.Load(path);
        double scale = Math.Min(1, Math.Min(640.0 / original.Width, 360.0 / original.Height));
        var preview = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
        using var canvas = Graphics.FromImage(preview);
        canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
        canvas.DrawImage(original, 0, 0, preview.Width, preview.Height);
        return preview;
    }
    public static DataObject CreateDragData(string path) {
        string absolute = Path.GetFullPath(path);
        if (!File.Exists(absolute)) throw new FileNotFoundException("照片文件已不存在，请重新选择。", absolute);
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { absolute });
        return data;
    }
}
