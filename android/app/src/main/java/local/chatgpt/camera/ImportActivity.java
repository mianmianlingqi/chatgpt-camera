package local.chatgpt.camera;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.view.View;
import android.widget.*;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.UUID;
import java.util.concurrent.*;

public final class ImportActivity extends Activity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private TextView status;
    private Button choose;
    private boolean busy;
    static Uri sharedUri(Intent intent) {
        if (!Intent.ACTION_SEND.equals(intent.getAction()) || intent.getType() == null || !intent.getType().startsWith("image/")) return null;
        try {
            Object extra = intent.getParcelableExtra(Intent.EXTRA_STREAM);
            Uri uri = extra instanceof Uri ? (Uri) extra : null;
            if (uri == null && intent.getClipData() != null && intent.getClipData().getItemCount() == 1) uri = intent.getClipData().getItemAt(0).getUri();
            return uri != null && "content".equals(uri.getScheme()) ? uri : null;
        } catch (RuntimeException invalid) { return null; }
    }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL); root.setPadding(32,48,32,24);
        root.setOnApplyWindowInsetsListener((v,insets) -> { if (android.os.Build.VERSION.SDK_INT >= 30) { android.graphics.Insets bars = insets.getInsets(android.view.WindowInsets.Type.systemBars()); v.setPadding(32,bars.top+24,32,bars.bottom+24); } return insets; });
        TextView title = new TextView(this); title.setText("截图与图片传输"); title.setTextSize(24); root.addView(title);
        status = new TextView(this); status.setTextSize(17); status.setPadding(0,32,0,32); root.addView(status);
        choose = new Button(this); choose.setText("选择图片"); choose.setOnClickListener(v -> pick()); root.addView(choose);
        Button camera = new Button(this); camera.setText("返回相机 / 配对电脑"); camera.setOnClickListener(v -> { startActivity(new Intent(this,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP)); finish(); }); root.addView(camera);
        TextView note = new TextView(this); note.setText("截图保持原比例，长截图会等比缩小。到电脑后可预览、拖入 Codex。不会自动发送消息。"); root.addView(note); setContentView(root);
        if (saved != null) { show("请在电脑查看传输结果；尚未送达的图片可回到相机点击“重试待传”。"); return; }
        if (Intent.ACTION_SEND.equals(getIntent().getAction())) {
            Uri uri = sharedUri(getIntent());
            if (uri == null) show("没有可读取的图片，请从截图分享菜单重新选择本应用。"); else transfer(uri);
        } else if ("local.chatgpt.camera.PICK".equals(getIntent().getAction())) pick();
        else show("选择截图，或在系统截图分享菜单中选择“ChatGPT 相机”。");
    }
    private void pick() {
        if (busy) return;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("image/*").addCategory(Intent.CATEGORY_OPENABLE);
        try { startActivityForResult(intent,101); } catch (android.content.ActivityNotFoundException ex) { show("系统没有可用的图片选择器，请从相册分享图片。"); }
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if (request == 101 && result == RESULT_OK && data != null && data.getData() != null) transfer(data.getData());
        else if (request == 101) show("已取消选择，没有传输图片。");
    }
    private void show(String message) { runOnUiThread(() -> { if (!isDestroyed()) status.setText(message); }); }
    private void transfer(Uri uri) {
        if (busy || !"content".equals(uri.getScheme())) return;
        final String pair = getSharedPreferences("MainActivity",MODE_PRIVATE).getString("pairing","");
        final Pairing endpoint;
        try { JSONObject p = new JSONObject(pair); if (p.optInt("v") != 1) throw new IOException(); endpoint = new Pairing(p.getString("host"),p.getInt("port"),p.getString("token"),p.getString("pin")); }
        catch (Exception ex) { show("请先返回相机配对电脑，再重新分享或选择截图。"); return; }
        busy = true; choose.setEnabled(false); show("正在保存并传输图片，请稍候……");
        worker.execute(() -> {
            String id = UUID.randomUUID().toString().replace("-",""); File folder = new File(getFilesDir(),"photos"); folder.mkdirs();
            File temp = new File(folder,id + ".import.tmp"), photo = new File(folder,id + ".jpg"); boolean saved = false;
            try {
                try (InputStream input = getContentResolver().openInputStream(uri)) {
                    if (input == null) throw new IOException("图片无法打开"); ImageImport.convert(input,temp,getCacheDir());
                }
                Files.move(temp.toPath(),photo.toPath(),StandardCopyOption.REPLACE_EXISTING); saveMeta(folder,id,pair,false); saved = true;
                ReceiverClient client = new ReceiverClient(endpoint);
                // Screenshot creation happened outside this app: never assign a new conversation as its original target.
                client.request("POST","/v1/captures/" + id + "?deferred=1",null);
                JSONObject reply = new JSONObject(client.request("PUT","/v1/captures/" + id + "/image",photo));
                if (!reply.optBoolean("stored")) throw new IOException("电脑未确认保存");
                saveMeta(folder,id,pair,true); show("图片已到电脑。请在“照片与拖拽”页预览，然后拖进 Codex 输入框。");
            } catch (Exception error) { show(saved ? "图片已保存在手机，暂未确认送达。请检查连接，回相机点击“重试待传”。" : "无法导入图片，请重新选择可读取的 PNG 或 JPEG 图片（不超过 20 MB）。"); }
            finally { temp.delete(); runOnUiThread(() -> { if (!isDestroyed()) { busy=false; choose.setEnabled(true); } }); }
        });
    }
    private static void saveMeta(File folder,String id,String pair,boolean stored) throws Exception {
        JSONObject meta = new JSONObject().put("id",id).put("pairing",pair).put("stored",stored);
        File temp = new File(folder,id + ".meta.tmp"); Files.write(temp.toPath(),meta.toString().getBytes(StandardCharsets.UTF_8));
        Files.move(temp.toPath(),new File(folder,id + ".json").toPath(),StandardCopyOption.REPLACE_EXISTING);
    }
    @Override protected void onDestroy() { worker.shutdown(); super.onDestroy(); }
}
