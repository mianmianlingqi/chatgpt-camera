package local.chatgpt.camera;

import android.Manifest;
import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.ImageFormat;
import android.graphics.SurfaceTexture;
import android.hardware.camera2.*;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Size;
import android.view.Surface;
import android.view.TextureView;
import java.nio.ByteBuffer;
import java.util.*;

final class CameraController implements AutoCloseable {
    interface Listener { void ready(); void photo(byte[] jpeg); void error(String message); }
    private final Activity activity;
    private final TextureView preview;
    private final Listener listener;
    private final HandlerThread thread = new HandlerThread("camera-preview");
    private final Handler handler;
    private CameraDevice camera;
    private CameraCaptureSession session;
    private ImageReader reader;
    private Surface previewSurface;
    private int sensorOrientation = 90;
    private boolean opening, closed, shooting;
    CameraController(Activity activity, TextureView preview, Listener listener) {
        this.activity = activity; this.preview = preview; this.listener = listener;
        thread.start(); handler = new Handler(thread.getLooper());
        preview.setSurfaceTextureListener(new TextureView.SurfaceTextureListener() {
            public void onSurfaceTextureAvailable(SurfaceTexture texture, int width, int height) { open(); }
            public void onSurfaceTextureSizeChanged(SurfaceTexture texture, int width, int height) { }
            public boolean onSurfaceTextureDestroyed(SurfaceTexture texture) { close(); return true; }
            public void onSurfaceTextureUpdated(SurfaceTexture texture) { }
        });
        if (preview.isAvailable()) open();
    }
    private void open() {
        if (closed || opening || activity.checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) return;
        opening = true;
        try {
            CameraManager manager = (CameraManager) activity.getSystemService(Context.CAMERA_SERVICE);
            String selected = null; CameraCharacteristics info = null;
            for (String id : manager.getCameraIdList()) {
                CameraCharacteristics c = manager.getCameraCharacteristics(id);
                if (selected == null || Objects.equals(c.get(CameraCharacteristics.LENS_FACING), CameraCharacteristics.LENS_FACING_BACK)) { selected = id; info = c; }
                if (Objects.equals(c.get(CameraCharacteristics.LENS_FACING), CameraCharacteristics.LENS_FACING_BACK)) break;
            }
            if (selected == null || info == null) throw new IllegalStateException("没有可用相机");
            Integer orientation = info.get(CameraCharacteristics.SENSOR_ORIENTATION); if (orientation != null) sensorOrientation = orientation;
            StreamConfigurationMap map = info.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
            if (map == null) throw new IllegalStateException("相机不支持预览");
            Size[] photos = map.getOutputSizes(ImageFormat.JPEG);
            if (photos == null || photos.length == 0) throw new IllegalStateException("相机不支持 JPEG");
            int[][] dimensions = new int[photos.length][2];
            for (int i = 0; i < photos.length; i++) { dimensions[i][0] = photos[i].getWidth(); dimensions[i][1] = photos[i].getHeight(); }
            Size size = photos[PhotoSize.choose(dimensions)];
            Size[] previews = map.getOutputSizes(SurfaceTexture.class);
            if (previews == null || previews.length == 0) throw new IllegalStateException("相机不支持预览尺寸");
            Size viewSize = Arrays.stream(previews).filter(s -> s.getWidth() <= 1920 && s.getHeight() <= 1080).min(Comparator.comparingDouble(s -> Math.abs((double) s.getWidth() / s.getHeight() - (double) size.getWidth() / size.getHeight()))).orElse(previews[0]);
            if (preview instanceof PreviewView) ((PreviewView) preview).setAspect(sensorOrientation % 180 == 0 ? viewSize.getWidth() : viewSize.getHeight(), sensorOrientation % 180 == 0 ? viewSize.getHeight() : viewSize.getWidth());
            SurfaceTexture texture = preview.getSurfaceTexture(); if (texture == null) return;
            texture.setDefaultBufferSize(viewSize.getWidth(), viewSize.getHeight()); previewSurface = new Surface(texture);
            reader = ImageReader.newInstance(size.getWidth(), size.getHeight(), ImageFormat.JPEG, 2);
            reader.setOnImageAvailableListener(r -> {
                try (Image image = r.acquireNextImage()) {
                    if (image == null || closed) return;
                    ByteBuffer buffer = image.getPlanes()[0].getBuffer(); byte[] bytes = new byte[buffer.remaining()]; buffer.get(bytes);
                    shooting = false; listener.photo(bytes);
                } catch (Exception ex) { shooting = false; listener.error("读取照片失败：" + ex.getMessage()); }
            }, handler);
            manager.openCamera(selected, new CameraDevice.StateCallback() {
                public void onOpened(CameraDevice device) {
                    if (closed) { device.close(); return; } camera = device;
                    try {
                        camera.createCaptureSession(Arrays.asList(previewSurface, reader.getSurface()), new CameraCaptureSession.StateCallback() {
                            public void onConfigured(CameraCaptureSession configured) {
                                if (closed || camera == null) { configured.close(); return; } session = configured;
                                try { CaptureRequest.Builder request = camera.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW); request.addTarget(previewSurface); request.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_PICTURE); session.setRepeatingRequest(request.build(), null, handler); listener.ready(); }
                                catch (CameraAccessException ex) { listener.error("预览启动失败：" + ex.getMessage()); }
                            }
                            public void onConfigureFailed(CameraCaptureSession configured) { listener.error("相机预览配置失败"); }
                        }, handler);
                    } catch (CameraAccessException ex) { listener.error("相机连接失败：" + ex.getMessage()); }
                }
                public void onDisconnected(CameraDevice device) { device.close(); listener.error("相机连接中断，请重新打开应用"); }
                public void onError(CameraDevice device, int error) { device.close(); listener.error("相机暂不可用（" + error + "）"); }
            }, handler);
        } catch (Exception ex) { opening = false; listener.error("打开相机失败：" + ex.getMessage()); }
    }
    void shoot() {
        handler.post(() -> {
            if (closed || session == null || camera == null || shooting) { listener.error("相机尚未就绪"); return; }
            shooting = true;
            try {
                CaptureRequest.Builder shot = camera.createCaptureRequest(CameraDevice.TEMPLATE_STILL_CAPTURE);
                shot.addTarget(reader.getSurface()); shot.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_PICTURE);
                shot.set(CaptureRequest.JPEG_QUALITY, (byte) 92);
                int rotation = activity.getWindowManager().getDefaultDisplay().getRotation();
                int degrees = rotation == Surface.ROTATION_90 ? 90 : rotation == Surface.ROTATION_180 ? 180 : rotation == Surface.ROTATION_270 ? 270 : 0;
                shot.set(CaptureRequest.JPEG_ORIENTATION, (sensorOrientation - degrees + 360) % 360);
                session.capture(shot.build(), new CameraCaptureSession.CaptureCallback() {
                    @Override public void onCaptureFailed(CameraCaptureSession s, CaptureRequest r, CaptureFailure failure) { shooting = false; listener.error("拍摄失败，请重试"); }
                }, handler);
            } catch (CameraAccessException ex) { shooting = false; listener.error("拍摄失败：" + ex.getMessage()); }
        });
    }
    @Override public void close() {
        if (closed) return;
        if (shooting) listener.error("拍摄因离开相机而中断，请重新拍摄。");
        closed = true;
        handler.post(() -> { if (session != null) session.close(); if (camera != null) camera.close(); if (reader != null) reader.close(); if (previewSurface != null) previewSurface.release(); session = null; camera = null; thread.quitSafely(); });
    }
}
