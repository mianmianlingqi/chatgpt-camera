package local.chatgpt.camera;
import java.io.*;
public final class ImportLimitsTest {
    public static void main(String[] args) throws Exception {
        if (ImportLimits.sample(1080,2400) != 1) throw new AssertionError("Do not resize ordinary screenshots");
        if (ImportLimits.sample(1440,20000) != 4) throw new AssertionError("Bound long screenshot decoding");
        boolean invalid = false;
        try { ImportLimits.sample(0,10); } catch (IOException e) { invalid = true; }
        if (!invalid) throw new AssertionError("Reject invalid image bounds");
        ByteArrayOutputStream copied = new ByteArrayOutputStream();
        ImportLimits.copy(new ByteArrayInputStream(new byte[]{1,2,3}),copied,3);
        if (copied.size()!=3) throw new AssertionError("Inclusive byte limit");
        boolean tooLarge = false;
        try { ImportLimits.copy(new ByteArrayInputStream(new byte[4]),new ByteArrayOutputStream(),3); } catch (IOException e) { tooLarge=true; }
        if (!tooLarge) throw new AssertionError("Reject oversized import");
        if (ImportLimits.sample(65535,65535) < 32) throw new AssertionError("Handle large bounds without overflow");
        System.out.println("6 import-limit checks passed");
    }
}
