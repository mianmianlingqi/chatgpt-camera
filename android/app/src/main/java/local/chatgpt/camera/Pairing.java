package local.chatgpt.camera;
public final class Pairing {
    public final String host, token, pin;
    public final int port;
    public Pairing(String host, int port, String token, String pin) {
        if (host == null || !host.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}")) throw new IllegalArgumentException("需要电脑的局域网 IPv4 地址");
        String[] parts = host.split("\\."); int[] ip = new int[4];
        for (int i = 0; i < 4; i++) { ip[i] = Integer.parseInt(parts[i]); if (ip[i] > 255 || (parts[i].length() > 1 && parts[i].startsWith("0"))) throw new IllegalArgumentException("IP 地址无效"); }
        if (!(ip[0] == 10 || (ip[0] == 192 && ip[1] == 168) || (ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31) || ip[0] == 127)) throw new IllegalArgumentException("第一版仅支持局域网地址");
        if (port < 1 || port > 65535) throw new IllegalArgumentException("端口无效");
        if (token == null || !token.matches("[a-fA-F0-9]{64}") || pin == null || !pin.matches("[a-fA-F0-9]{64}")) throw new IllegalArgumentException("配对密钥或证书指纹无效，请重新扫描");
        this.host = host; this.port = port; this.token = token.toLowerCase(java.util.Locale.ROOT); this.pin = pin.toLowerCase(java.util.Locale.ROOT);
    }
}
