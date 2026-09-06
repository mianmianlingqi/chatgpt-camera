package local.chatgpt.camera;
import java.io.*;

final class ImportLimits {
    static final int MAX_BYTES = 20 * 1024 * 1024;
    static int sample(int width, int height) throws IOException {
        if (width <= 0 || height <= 0 || width > 65535 || height > 65535) throw new IOException("图片尺寸无效或过大");
        int sample = 1;
        while ((long) ((width + sample - 1) / sample) * ((height + sample - 1) / sample) > 5_000_000) sample *= 2;
        return sample;
    }
    static void copy(InputStream source, OutputStream output, int limit) throws IOException {
        byte[] buffer = new byte[65536]; long total = 0; int count;
        while ((count = source.read(buffer)) != -1) {
            total += count; if (total > limit) throw new IOException("图片超过 20 MB，请缩短长截图后再传输");
            output.write(buffer,0,count);
        }
    }
}
