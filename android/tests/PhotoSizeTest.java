package local.chatgpt.camera;
public class PhotoSizeTest {
    public static void main(String[] args) {
        int selected = PhotoSize.choose(new int[][]{{3120,1440},{2560,1920},{1920,1080}});
        if (selected != 1) throw new AssertionError("Prefer 4:3 over tall-screen aspect");
        if (PhotoSize.choose(new int[][]{{640,480},{2560,1920},{1600,1200}}) != 1) throw new AssertionError("Largest 4:3 under cap");
        if (PhotoSize.choose(new int[][]{{4000,3000},{1920,1080}}) != 1) throw new AssertionError("Respect transport pixel budget");
        if (PhotoSize.choose(new int[][]{{1920,1080},{3120,1440}}) != 0) throw new AssertionError("Closest fallback ratio");
        if (PhotoSize.choose(new int[][]{{8000,6000},{4000,3000}}) != 1) throw new AssertionError("Smallest fallback if all too large");
        boolean rejected = false; try { PhotoSize.choose(new int[][]{}); } catch (IllegalArgumentException e) { rejected = true; }
        if (!rejected) throw new AssertionError("Reject empty configurations");
        System.out.println("6 photo-size checks passed");
    }
}
