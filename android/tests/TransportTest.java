package local.chatgpt.camera;
import java.nio.file.*;
import java.util.*;
import java.io.*;
public class TransportTest {
    static int count;
    static void check(String name, boolean ok) { if (!ok) throw new AssertionError(name); count++; System.out.println("PASS " + name); }
    public static void main(String[] args) throws Exception {
        Properties p = new Properties(); try (InputStream in = Files.newInputStream(Path.of(args[0]))) { p.load(in); }
        String host = "127.0.0.1", token = p.getProperty("token"), pin = p.getProperty("pin"); int port = Integer.parseInt(p.getProperty("port"));
        ReceiverClient client = new ReceiverClient(new Pairing(host, port, token, pin));
        check("Java client validates pinned receiver", client.request("GET", "/v1/health", null).contains("true"));
        boolean wrongPin = false;
        try { new ReceiverClient(new Pairing(host, port, token, "00".repeat(32))).request("GET", "/v1/health", null); } catch (javax.net.ssl.SSLException e) { wrongPin = true; }
        check("Java client rejects wrong certificate pin", wrongPin);
        boolean wrongToken = false;
        try { new ReceiverClient(new Pairing(host, port, "00".repeat(32), pin)).request("GET", "/v1/health", null); } catch (IOException e) { wrongToken = true; }
        check("Java client rejects wrong pairing token", wrongToken);
        String id = UUID.randomUUID().toString().replace("-", ""), route = "/v1/captures/" + id;
        check("Java deferred reservation", client.request("POST", route + "?deferred=1", null).contains("reserved"));
        check("Java JPEG upload", client.request("PUT", route + "/image", new File(p.getProperty("jpeg"))).contains("\"stored\":true"));
        check("Java duplicate JPEG upload", client.request("PUT", route + "/image", new File(p.getProperty("jpeg"))).contains("\"stored\":true"));
        check("Java capture status", client.request("GET", route, null).contains("\"stored\":true"));
        System.out.println(count + " Java transport checks passed");
    }
}
