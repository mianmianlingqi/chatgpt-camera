package local.chatgpt.camera;

import android.graphics.*;
import android.media.ExifInterface;
import java.io.*;

final class ImageImport {
    static void convert(InputStream input, File output, File cache) throws IOException {
        File original = File.createTempFile("screenshot-", ".input", cache);
        Bitmap decoded = null, oriented = null, flattened = null;
        try {
            try (OutputStream copy = new FileOutputStream(original)) { ImportLimits.copy(input, copy, ImportLimits.MAX_BYTES); }
            BitmapFactory.Options bounds = new BitmapFactory.Options(); bounds.inJustDecodeBounds = true;
            BitmapFactory.decodeFile(original.getPath(), bounds);
            BitmapFactory.Options options = new BitmapFactory.Options(); options.inSampleSize = ImportLimits.sample(bounds.outWidth,bounds.outHeight);
            decoded = BitmapFactory.decodeFile(original.getPath(),options);
            if (decoded == null) throw new IOException("无法读取这张图片，请选择 PNG 或 JPEG 截图");
            int orientation = ExifInterface.ORIENTATION_NORMAL;
            try { orientation = new ExifInterface(original.getPath()).getAttributeInt(ExifInterface.TAG_ORIENTATION,ExifInterface.ORIENTATION_NORMAL); } catch (IOException ignored) { }
            Matrix matrix = new Matrix();
            switch (orientation) {
                case 2: matrix.setScale(-1,1); break;
                case 3: matrix.setRotate(180); break;
                case 4: matrix.setScale(1,-1); break;
                case 5: matrix.setRotate(90); matrix.postScale(-1,1); break;
                case 6: matrix.setRotate(90); break;
                case 7: matrix.setRotate(-90); matrix.postScale(-1,1); break;
                case 8: matrix.setRotate(-90); break;
                default: break;
            }
            oriented = Bitmap.createBitmap(decoded,0,0,decoded.getWidth(),decoded.getHeight(),matrix,true);
            flattened = Bitmap.createBitmap(oriented.getWidth(),oriented.getHeight(),Bitmap.Config.ARGB_8888);
            Canvas canvas = new Canvas(flattened); canvas.drawColor(Color.WHITE); canvas.drawBitmap(oriented,0,0,null);
            try (OutputStream jpeg = new FileOutputStream(output)) {
                if (!flattened.compress(Bitmap.CompressFormat.JPEG,95,jpeg)) throw new IOException("图片转换失败");
            }
            if (output.length() > ImportLimits.MAX_BYTES) throw new IOException("转换后的图片超过 20 MB");
        } catch (IOException | RuntimeException error) { output.delete(); throw error; }
        finally {
            if (flattened != null) flattened.recycle();
            if (oriented != null && oriented != decoded) oriented.recycle();
            if (decoded != null) decoded.recycle();
            original.delete();
        }
    }
}
