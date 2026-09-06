package local.chatgpt.camera;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.os.*;
import android.view.*;
import android.widget.*;
import com.google.zxing.*;
import com.google.zxing.common.HybridBinarizer;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.*;
import java.util.concurrent.*;

public final class MainActivity extends Activity {
    private TextureView preview;
    private TextView status, connection;
    private Button shutter, pairButton, retry;
    private ImageView thumbnail;
    private CameraController camera;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final ExecutorService network = Executors.newSingleThreadExecutor();
    private final ExecutorService decoder = Executors.newSingleThreadExecutor();
    private final ExecutorService storage = Executors.newSingleThreadExecutor();
    private boolean scanning, decoding, busy, ready, resumed;
    private String pairingJson = "", activeId;
    private String activePairing;
    private volatile boolean reserveSucceeded;
    private File photos;
    private static final int INK = Color.rgb(14, 24, 26), MINT = Color.rgb(99, 229, 183);

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved); photos = new File(getFilesDir(), "photos"); photos.mkdirs();
        pairingJson = getPreferences(MODE_PRIVATE).getString("pairing", "");
        getWindow().setStatusBarColor(INK); getWindow().setNavigationBarColor(INK);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL); root.setBackgroundColor(INK); root.setPadding(dp(20), dp(12), dp(20), dp(12));
        root.setOnApplyWindowInsetsListener((v, insets) -> { if (Build.VERSION.SDK_INT >= 30) { android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars()); v.setPadding(dp(20), bars.top + dp(8), dp(20), bars.bottom + dp(8)); } return insets; });
        setContentView(root);
        TextView title = text("ChatGPT 相机", 25, Color.WHITE); title.setTypeface(null, android.graphics.Typeface.BOLD); root.addView(title);
        connection = text("", 13, MINT); connection.setPadding(0, dp(8), 0, dp(14)); root.addView(connection);
        FrameLayout viewfinder = new FrameLayout(this); viewfinder.setBackgroundColor(Color.BLACK);
        preview = new PreviewView(this); viewfinder.addView(preview, new FrameLayout.LayoutParams(-1, -1, Gravity.CENTER));
        TextView hint = text("将电脑上的 Codex 对话放到前台", 13, Color.WHITE); hint.setGravity(Gravity.CENTER); hint.setBackgroundColor(0x80000000);
        FrameLayout.LayoutParams hintLayout = new FrameLayout.LayoutParams(-1, dp(38), Gravity.BOTTOM); viewfinder.addView(hint, hintLayout);
        root.addView(viewfinder, new LinearLayout.LayoutParams(-1, 0, 1));
        status = text("首次使用，请扫描电脑接收端的配对二维码。", 14, Color.LTGRAY); status.setMinHeight(dp(70)); status.setGravity(Gravity.CENTER_VERTICAL); root.addView(status);
        LinearLayout controls = new LinearLayout(this); controls.setGravity(Gravity.CENTER_VERTICAL);
        thumbnail = new ImageView(this); thumbnail.setScaleType(ImageView.ScaleType.CENTER_CROP); controls.addView(thumbnail, new LinearLayout.LayoutParams(dp(58), dp(58)));
        shutter = button("拍照并传输", MINT, INK); LinearLayout.LayoutParams shotLayout = new LinearLayout.LayoutParams(0, dp(66), 1); shotLayout.setMargins(dp(16), 0, dp(8), 0); controls.addView(shutter, shotLayout);
        root.addView(controls);
        LinearLayout bottom = new LinearLayout(this); bottom.setPadding(0, dp(12), 0, 0);
        pairButton = button("配对电脑", Color.rgb(33, 49, 51), Color.WHITE); retry = button("重试待传", Color.rgb(33, 49, 51), Color.WHITE);
        bottom.addView(pairButton, new LinearLayout.LayoutParams(0, dp(48), 1)); LinearLayout.LayoutParams retryLayout = new LinearLayout.LayoutParams(0, dp(48), 1); retryLayout.setMargins(dp(10), 0, 0, 0); bottom.addView(retry, retryLayout); root.addView(bottom);
        Button importImage = button("选择图片 / 传截图", Color.rgb(33,49,51), Color.WHITE);
        LinearLayout.LayoutParams importLayout = new LinearLayout.LayoutParams(-1,dp(44)); importLayout.topMargin=dp(10); root.addView(importImage,importLayout);
        importImage.setOnClickListener(v -> { if (!busy) startActivity(new Intent(this,ImportActivity.class).setAction("local.chatgpt.camera.PICK")); });
        TextView footer = text("USB / Wi-Fi 加密传输 · 仅添加附件，由你发送消息", 11, Color.GRAY); footer.setPadding(0, dp(12), 0, 0); root.addView(footer);
        pairButton.setOnClickListener(v -> pairingOptions()); shutter.setOnClickListener(v -> takePhoto()); retry.setOnClickListener(v -> retryPending());
        updateConnection(); updateButtons();
    }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private TextView text(String value, int size, int color) { TextView t = new TextView(this); t.setText(value); t.setTextSize(size); t.setTextColor(color); return t; }
    private Button button(String value, int background, int foreground) { Button b = new Button(this); b.setText(value); b.setTextColor(foreground); b.setTextSize(14); b.setAllCaps(false); GradientDrawable d = new GradientDrawable(); d.setColor(background); d.setCornerRadius(dp(16)); b.setBackground(d); return b; }
    private void showStatus(String value) { ui.post(() -> { if (!isDestroyed()) status.setText(value); }); }
    private static Pairing parse(String json) throws Exception { JSONObject j = new JSONObject(json); if (j.optInt("v") != 1) throw new IllegalArgumentException("不支持这个配对版本"); return new Pairing(j.getString("host"), j.getInt("port"), j.getString("token"), j.getString("pin")); }
    private void updateConnection() { try { Pairing p = parse(pairingJson); connection.setText("已保存电脑 · " + p.host + "（拍摄时连接）"); } catch (Exception ex) { pairingJson = ""; connection.setText("尚未配对电脑"); } }
    private File imageFile(String id) { return new File(photos, id + ".jpg"); }
    private File metaFile(String id) { return new File(photos, id + ".json"); }
    private List<File> pending() {
        File[] files = photos.listFiles((d, n) -> n.endsWith(".json")); List<File> result = new ArrayList<>();
        if (files != null) for (File file : files) try { JSONObject meta = new JSONObject(new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8)); if (!meta.optBoolean("stored") && imageFile(meta.getString("id")).isFile()) result.add(file); } catch (Exception ignored) { }
        result.sort(Comparator.comparingLong(File::lastModified)); return result;
    }
    private void saveMeta(String id, String pair, boolean stored) throws Exception {
        JSONObject value = new JSONObject(); value.put("id", id); value.put("pairing", pair); value.put("stored", stored);
        File temp = new File(photos, id + ".meta.tmp"); Files.write(temp.toPath(), value.toString().getBytes(StandardCharsets.UTF_8)); Files.move(temp.toPath(), metaFile(id).toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING);
    }
    private void updateButtons() { shutter.setEnabled(ready && !busy && !scanning && !pairingJson.isEmpty()); pairButton.setEnabled(!busy); retry.setEnabled(!busy && !pending().isEmpty()); retry.setText("重试待传（" + pending().size() + "）"); }
    @Override protected void onResume() { super.onResume(); resumed = true; if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) openCamera(); else requestPermissions(new String[] { Manifest.permission.CAMERA }, 100); }
    @Override protected void onPause() { resumed = false; scanning = false; ready = false; if (camera != null) { camera.close(); camera = null; } super.onPause(); }
    @Override protected void onDestroy() { ui.removeCallbacksAndMessages(null); storage.shutdown(); network.shutdown(); decoder.shutdown(); super.onDestroy(); }
    @Override public void onRequestPermissionsResult(int request, String[] permissions, int[] results) { super.onRequestPermissionsResult(request, permissions, results); if (request == 100 && results.length > 0 && results[0] == PackageManager.PERMISSION_GRANTED && resumed) openCamera(); else showStatus("需要相机权限才能拍照。可在系统设置中为本应用开启相机权限。"); }
    private void openCamera() {
        if (camera != null) return;
        camera = new CameraController(this, preview, new CameraController.Listener() {
            public void ready() { ui.post(() -> { ready = true; updateButtons(); }); }
            public void error(String message) { ui.post(() -> { busy = false; status.setText(message); updateButtons(); }); }
            public void photo(byte[] bytes) {
                final String id = activeId, pair = activePairing;
                if (id == null || pair == null) return;
                storage.execute(() -> {
                    try {
                        File temp = new File(photos, id + ".photo.tmp"); Files.write(temp.toPath(), bytes); Files.move(temp.toPath(), imageFile(id).toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING); saveMeta(id, pair, false);
                        ui.post(() -> { BitmapFactory.Options options = new BitmapFactory.Options(); options.inSampleSize = 8; thumbnail.setImageBitmap(BitmapFactory.decodeByteArray(bytes, 0, bytes.length, options)); });
                        network.execute(() -> {
                            try {
                                if (!reserveSucceeded) throw new IOException("照片已保存在手机。电脑未连接，请检查 Wi-Fi 后点击重试待传。");
                                upload(id, pair, false);
                            } catch (Exception ex) { showStatus("照片未确认送达：" + safeMessage(ex)); }
                            finally { ui.post(() -> { busy = false; activeId = null; updateButtons(); }); }
                        });
                    } catch (Exception ex) { showStatus("保存或传输未完成：" + safeMessage(ex)); ui.post(() -> { busy = false; activeId = null; updateButtons(); }); }
                });
            }
        });
    }
    private void takePhoto() {
        if (!ready || busy || camera == null) return;
        busy = true; reserveSucceeded = false; activeId = UUID.randomUUID().toString().replace("-", ""); activePairing = pairingJson; updateButtons();
        final String id = activeId, pair = activePairing;
        status.setText("正在拍摄并连接电脑…");
        network.execute(() -> { try { new ReceiverClient(parse(pair)).request("POST", "/v1/captures/" + id, null); reserveSucceeded = true; } catch (Exception ignored) { reserveSucceeded = false; } });
        camera.shoot();
    }
    private void upload(String id, String pair, boolean deferred) throws Exception {
        ReceiverClient client = new ReceiverClient(parse(pair));
        if (deferred) client.request("POST", "/v1/captures/" + id + "?deferred=1", null);
        showStatus("照片已保存，正在传输到电脑…");
        JSONObject result = new JSONObject(client.request("PUT", "/v1/captures/" + id + "/image", imageFile(id)));
        if (!result.optBoolean("stored")) throw new IOException("电脑尚未确认保存照片");
        saveMeta(id, pair, true);
        for (int i = 0; i < 10 && (result.optString("state").equals("received") || result.optString("state").equals("attaching")); i++) {
            Thread.sleep(400);
            try { result = new JSONObject(client.request("GET", "/v1/captures/" + id, null)); } catch (IOException ex) { break; }
        }
        String state = result.optString("state");
        showStatus(state.equals("attached") ? "照片已进入 Codex 附件，等待你发送消息。" : "照片已到电脑。" + result.optString("detail", "请检查接收端和输入框。"));
    }
    private void retryPending() {
        if (busy) return; List<File> files = pending(); if (files.isEmpty()) return; busy = true; updateButtons();
        network.execute(() -> {
            int sent = 0;
            try {
                Pairing selected = parse(pairingJson);
                for (File file : files) {
                    JSONObject meta = new JSONObject(new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8)); String pair = meta.getString("pairing");
                    if (!parse(pair).pin.equals(selected.pin)) throw new IOException("存在属于另一台电脑的待传照片，请先配对原电脑");
                    upload(meta.getString("id"), pair, true); sent++;
                }
                if (sent > 1) showStatus(sent + " 张照片已到电脑，请在接收端查看附件状态。");
            } catch (Exception ex) { showStatus("已补传 " + sent + " 张；其余保留。" + safeMessage(ex)); }
            finally { ui.post(() -> { busy = false; updateButtons(); }); }
        });
    }
    private void pairingOptions() {
        if (scanning) { scanning = false; status.setText("已退出扫码。"); updateButtons(); return; }
        new AlertDialog.Builder(this).setTitle("连接电脑接收端").setItems(new String[] { "扫描电脑上的二维码", "粘贴配对内容" }, (dialog, which) -> {
            if (which == 0) { scanning = true; status.setText("将电脑二维码放入取景框。再次点击配对电脑可退出。"); updateButtons(); scanTick(); }
            else {
                EditText input = new EditText(this); input.setHint("粘贴接收端复制的配对内容"); input.setTextSize(13);
                new AlertDialog.Builder(this).setTitle("手动配对").setView(input).setNegativeButton("取消", null).setPositiveButton("连接", (d, w) -> acceptPairing(input.getText().toString().trim())).show();
            }
        }).show();
    }
    private void scanTick() {
        if (!scanning || !resumed) return;
        if (!decoding && preview.isAvailable()) {
            Bitmap bitmap = preview.getBitmap(600, Math.max(1, (int) (600.0 * preview.getHeight() / Math.max(1, preview.getWidth()))));
            if (bitmap != null) {
                decoding = true;
                decoder.execute(() -> {
                    String result = null;
                    try {
                        int[] pixels = new int[bitmap.getWidth() * bitmap.getHeight()]; bitmap.getPixels(pixels, 0, bitmap.getWidth(), 0, 0, bitmap.getWidth(), bitmap.getHeight());
                        RGBLuminanceSource source = new RGBLuminanceSource(bitmap.getWidth(), bitmap.getHeight(), pixels);
                        Map<DecodeHintType, Object> hints = new EnumMap<>(DecodeHintType.class); hints.put(DecodeHintType.POSSIBLE_FORMATS, Collections.singletonList(BarcodeFormat.QR_CODE)); hints.put(DecodeHintType.TRY_HARDER, true);
                        result = new MultiFormatReader().decode(new BinaryBitmap(new HybridBinarizer(source)), hints).getText();
                    } catch (Exception ignored) { } finally { bitmap.recycle(); }
                    final String found = result;
                    ui.post(() -> { decoding = false; if (found != null && scanning) { scanning = false; acceptPairing(found); } });
                });
            }
        }
        ui.postDelayed(this::scanTick, 600);
    }
    private void acceptPairing(String json) {
        try {
            Pairing pair = parse(json); busy = true; updateButtons(); status.setText("正在验证电脑连接…");
            network.execute(() -> {
                try {
                    new ReceiverClient(pair).request("GET", "/v1/health", null);
                    ui.post(() -> { pairingJson = json; getPreferences(MODE_PRIVATE).edit().putString("pairing", json).apply(); updateConnection(); status.setText("配对成功。回到电脑的 Codex 对话后，按下快门即可传输。"); });
                } catch (Exception ex) { showStatus("配对失败：" + safeMessage(ex) + "。请确认同一 Wi-Fi 和电脑接收端已启动。"); }
                finally { ui.post(() -> { busy = false; updateButtons(); }); }
            });
        } catch (Exception ex) { showStatus("二维码不是有效配对内容：" + safeMessage(ex)); updateButtons(); }
    }
    private static String safeMessage(Exception ex) { if (ex instanceof javax.net.ssl.SSLException) return "加密身份校验失败，请核对电脑并重新扫码"; return ex.getMessage() == null ? "连接失败，请重试" : ex.getMessage(); }
}
