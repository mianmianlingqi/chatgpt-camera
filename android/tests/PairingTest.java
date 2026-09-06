package local.chatgpt.camera;
public class PairingTest {
    static int failures = 0;
    static void check(String name, boolean ok) { System.out.println((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
    static boolean accepts(String host, int port, String token, String pin) { try { new Pairing(host, port, token, pin); return true; } catch (IllegalArgumentException e) { return false; } }
    public static void main(String[] args) {
        String key = "ab".repeat(32);
        check("private LAN endpoint", accepts("192.168.1.20", 47831, key, key));
        check("reject public endpoint", !accepts("8.8.8.8", 443, key, key));
        check("reject invalid IPv4", !accepts("192.168.1.999", 47831, key, key));
        check("reject hostname injection", !accepts("192.168.1.20/path", 47831, key, key));
        check("reject port overflow", !accepts("192.168.1.20", 65536, key, key));
        check("reject short secret", !accepts("192.168.1.20", 47831, "abc", key));
        check("reject invalid pin", !accepts("192.168.1.20", 47831, key, "z".repeat(64)));
        if (failures > 0) throw new AssertionError(failures + " failures");
    }
}
