package local.chatgpt.camera;
import android.content.Context;
import android.view.TextureView;
final class PreviewView extends TextureView {
    private double aspect = 3.0 / 4.0;
    PreviewView(Context context) { super(context); }
    void setAspect(int width, int height) { if (width > 0 && height > 0) { aspect = (double) width / height; requestLayout(); } }
    @Override protected void onMeasure(int widthSpec, int heightSpec) {
        int width = MeasureSpec.getSize(widthSpec), height = MeasureSpec.getSize(heightSpec);
        if (width > height * aspect) width = (int) (height * aspect); else height = (int) (width / aspect);
        setMeasuredDimension(width, height);
    }
}
