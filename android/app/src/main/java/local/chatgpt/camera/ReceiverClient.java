package local.chatgpt.camera;

import java.io.*;
import java.net.URL;
import java.security.*;
import java.security.cert.*;
import javax.net.ssl.*;

public final class ReceiverClient {
    private final Pairing pairing;
    private final SSLSocketFactory sockets;
    public ReceiverClient(Pairing pairing) throws GeneralSecurityException {
        this.pairing = pairing;
        TrustManager[] trust = { new X509TrustManager() {
            public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
            public void checkClientTrusted(X509Certificate[] chain, String auth) throws CertificateException { throw new CertificateException("Client certificates not accepted"); }
            public void checkServerTrusted(X509Certificate[] chain, String auth) throws CertificateException {
                if (chain == null || chain.length == 0) throw new CertificateException("Empty certificate");
                chain[0].checkValidity();
                try {
                    byte[] expected = new byte[32]; for (int i = 0; i < 32; i++) expected[i] = (byte) Integer.parseInt(pairing.pin.substring(i * 2, i * 2 + 2), 16);
                    if (!MessageDigest.isEqual(expected, MessageDigest.getInstance("SHA-256").digest(chain[0].getEncoded()))) throw new CertificateException("接收端证书不匹配，请检查电脑并重新配对");
                } catch (NoSuchAlgorithmException e) { throw new CertificateException(e); }
            }
        } };
        SSLContext context = SSLContext.getInstance("TLS"); context.init(null, trust, new SecureRandom()); sockets = context.getSocketFactory();
    }
    // A pinned certificate is the receiver identity; no public CA or IP SAN is needed.
    public String request(String method, String path, File jpeg) throws IOException {
        if (!path.startsWith("/v1/") || path.contains("..")) throw new IllegalArgumentException("Invalid API path");
        HttpsURLConnection connection = (HttpsURLConnection) new URL("https", pairing.host, pairing.port, path).openConnection();
        connection.setSSLSocketFactory(sockets); connection.setHostnameVerifier((host, session) -> host.equals(pairing.host));
        connection.setConnectTimeout(7000); connection.setReadTimeout(15000); connection.setInstanceFollowRedirects(false);
        connection.setRequestMethod(method); connection.setRequestProperty("Authorization", "Bearer " + pairing.token);
        try {
            if (jpeg != null) {
                if (jpeg.length() > 20L * 1024 * 1024) throw new IOException("照片超过 20 MB，请重新拍摄");
                connection.setDoOutput(true); connection.setRequestProperty("Content-Type", "image/jpeg"); connection.setFixedLengthStreamingMode(jpeg.length());
                try (InputStream in = new FileInputStream(jpeg); OutputStream out = connection.getOutputStream()) { byte[] buffer = new byte[65536]; int n; while ((n = in.read(buffer)) != -1) out.write(buffer, 0, n); }
            } else if (method.equals("POST")) { connection.setDoOutput(true); connection.setFixedLengthStreamingMode(0); connection.getOutputStream().close(); }
            int status = connection.getResponseCode();
            if (status < 200 || status >= 300) throw new IOException(status == 401 ? "配对已失效，请重新扫码" : "电脑接收失败（HTTP " + status + "）");
            try (InputStream in = connection.getInputStream(); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[4096]; int n;
                while ((n = in.read(buffer)) != -1) { if (out.size() + n > 65536) throw new IOException("接收端响应过大"); out.write(buffer, 0, n); }
                return out.toString("UTF-8");
            }
        } finally { connection.disconnect(); }
    }
}
