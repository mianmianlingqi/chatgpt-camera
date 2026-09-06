package local.chatgpt.camera;

import android.app.*;
import android.content.Intent;
import android.graphics.*;
import android.media.ExifInterface;
import android.net.Uri;
import android.os.Bundle;
import java.io.*;

public final class ScreenshotTestRunner extends android.app.Instrumentation {
    private int passed;
    @Override public void onCreate(Bundle args) { super.onCreate(args); start(); }
    private void check(boolean value,String name) { if (!value) throw new AssertionError(name); passed++; }
    private Bitmap convert(Bitmap source,File output,File cache) throws Exception {
        ByteArrayOutputStream png = new ByteArrayOutputStream(); source.compress(Bitmap.CompressFormat.PNG,100,png);
        ImageImport.convert(new ByteArrayInputStream(png.toByteArray()),output,cache);
        return BitmapFactory.decodeFile(output.getPath());
    }
    @Override public void onStart() {
        Bundle result = new Bundle(); File cache = getTargetContext().getCacheDir();
        File out = new File(cache,"test-screenshot-output.jpg"), rotated = new File(cache,"test-screenshot-rotated.jpg");
        try {
            Bitmap source = Bitmap.createBitmap(100,200,Bitmap.Config.ARGB_8888);
            Bitmap imported = convert(source,out,cache);
            check(imported.getWidth()==100 && imported.getHeight()==200,"Preserve screenshot aspect");
            check(Color.red(imported.getPixel(50,50)) > 240,"Flatten transparency on white");
            try (InputStream stream = new FileInputStream(out)) { check(stream.read()==255 && stream.read()==216,"JPEG output"); }
            source.recycle(); imported.recycle();
            Bitmap longShot = Bitmap.createBitmap(1440,4000,Bitmap.Config.ARGB_8888);
            imported = convert(longShot,out,cache);
            check(imported.getWidth()==720 && imported.getHeight()==2000,"Downsample long screenshot without cropping");
            longShot.recycle(); imported.recycle();
            Bitmap sideways = Bitmap.createBitmap(200,100,Bitmap.Config.ARGB_8888);
            try (OutputStream file = new FileOutputStream(rotated)) { sideways.compress(Bitmap.CompressFormat.JPEG,95,file); }
            sideways.recycle(); ExifInterface exif = new ExifInterface(rotated.getPath()); exif.setAttribute(ExifInterface.TAG_ORIENTATION,"6"); exif.saveAttributes();
            try (InputStream file = new FileInputStream(rotated)) { ImageImport.convert(file,out,cache); }
            imported = BitmapFactory.decodeFile(out.getPath()); check(imported.getWidth()==100 && imported.getHeight()==200,"Honor EXIF orientation"); imported.recycle();
            boolean rejected = false;
            try { ImageImport.convert(new ByteArrayInputStream("invalid".getBytes()),out,cache); } catch (IOException e) { rejected = true; }
            check(rejected && !out.exists(),"Reject corrupt data and remove partial output");
            Intent valid = new Intent(Intent.ACTION_SEND).setType("image/png").putExtra(Intent.EXTRA_STREAM,Uri.parse("content://test/image"));
            check(ImportActivity.sharedUri(valid) != null,"Accept shared content URI");
            valid.putExtra(Intent.EXTRA_STREAM,Uri.parse("https://example.com/image.png"));
            check(ImportActivity.sharedUri(valid)==null,"Reject remote URL");
            result.putString("stream",passed + " screenshot device checks passed\n"); finish(Activity.RESULT_OK,result);
        } catch (Throwable error) { result.putString("stream","FAILED: " + error.toString()); finish(Activity.RESULT_CANCELED,result); }
        finally { out.delete(); rotated.delete(); }
    }
}
