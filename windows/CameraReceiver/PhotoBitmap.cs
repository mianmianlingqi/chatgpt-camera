namespace CameraReceiver;
internal static class PhotoBitmap {
    public static System.Drawing.Bitmap Load(string path) {
        var image = new System.Drawing.Bitmap(path);
        if (image.PropertyIdList.Contains(0x0112)) {
            var value = image.GetPropertyItem(0x0112)?.Value;
            if (value is { Length: >= 2 }) {
                var transform = BitConverter.ToUInt16(value, 0) switch {
                    2 => System.Drawing.RotateFlipType.RotateNoneFlipX,
                    3 => System.Drawing.RotateFlipType.Rotate180FlipNone,
                    4 => System.Drawing.RotateFlipType.Rotate180FlipX,
                    5 => System.Drawing.RotateFlipType.Rotate90FlipX,
                    6 => System.Drawing.RotateFlipType.Rotate90FlipNone,
                    7 => System.Drawing.RotateFlipType.Rotate270FlipX,
                    8 => System.Drawing.RotateFlipType.Rotate270FlipNone,
                    _ => System.Drawing.RotateFlipType.RotateNoneFlipNone
                };
                image.RotateFlip(transform);
            }
        }
        return image;
    }
}
