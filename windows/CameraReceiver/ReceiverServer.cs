using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CameraReceiver;

public sealed class ReceiverServer {
    public const int MaxBytes = 20 * 1024 * 1024;
    public CaptureStore Store { get; }
    public string Token { get; }
    public string Pin { get; }
    public int Port { get; private set; }
    private readonly X509Certificate2 certificate;
    private readonly Func<Task<TargetIdentity?>> capture;
    private readonly Action<CaptureRecord> received;
    private readonly SemaphoreSlim transfers = new(1);
    private WebApplication? app;
    public ReceiverServer(string data, Func<Task<TargetIdentity?>> capture, Action<CaptureRecord> received) {
        Directory.CreateDirectory(data); this.capture = capture; this.received = received;
        Store = new CaptureStore(Path.Combine(data, "photos"));
        var secretFile = Path.Combine(data, "pairing.json");
        if (File.Exists(secretFile)) {
            var saved = JsonSerializer.Deserialize<Identity>(File.ReadAllText(secretFile))!;
            Token = saved.Token; certificate = new X509Certificate2(Convert.FromBase64String(saved.Certificate), (string?)null, X509KeyStorageFlags.UserKeySet);
        } else {
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=ChatGPTCamera Local Receiver", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            var eku = new OidCollection { new("1.3.6.1.5.5.7.3.1") }; req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
            using var generated = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
            var pfx = generated.Export(X509ContentType.Pfx);
            certificate = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
            File.WriteAllText(secretFile, JsonSerializer.Serialize(new Identity(Token, Convert.ToBase64String(pfx))));
        }
        Pin = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
    }
    private record Identity(string Token, string Certificate);
    public string Pairing(string host) => JsonSerializer.Serialize(new { v = 1, host, port = Port, token = Token, pin = Pin });
    private static object Status(CaptureRecord r) => new { id = r.Id, state = r.State, detail = r.Detail, stored = r.Hash != null };
    public async Task StartAsync(int port = 47831, bool loopback = false) {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); if (loopback) builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Debug);
        builder.WebHost.ConfigureKestrel(k => { k.Limits.MaxRequestBodySize = MaxBytes; k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10); k.Listen(loopback ? IPAddress.Loopback : IPAddress.Any, port, l => l.UseHttps(certificate)); });
        app = builder.Build();
        app.Use(async (ctx, next) => {
            var auth = ctx.Request.Headers.Authorization.ToString(); var expected = "Bearer " + Token;
            if (auth.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth), Encoding.UTF8.GetBytes(expected))) { ctx.Response.StatusCode = 401; return; }
            if (ctx.Request.Headers.ContainsKey("Origin")) { ctx.Response.StatusCode = 403; return; }
            try { await next(ctx); }
            catch (BadHttpRequestException) { if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 413; }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = "请求未完成，请检查接收端或重试。" }); } }
        });
        app.MapGet("/v1/health", () => Results.Json(new { ok = true, version = "0.1.0" }));
        app.MapPost("/v1/captures/{id}", async (string id, HttpRequest request) => {
            if (!CaptureStore.ValidId(id)) return Results.BadRequest();
            await transfers.WaitAsync();
            try { var existing = Store.Get(id); if (existing != null) return Results.Json(Status(existing));
                TargetIdentity? target = null; try { if (request.Query["deferred"] != "1") target = await capture().WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
                return Results.Json(Status(Store.Reserve(id, target)));
            } finally { transfers.Release(); }
        });
        app.MapGet("/v1/captures/{id}", (string id) => !CaptureStore.ValidId(id) ? Results.BadRequest() : Store.Get(id) is { } r ? Results.Json(Status(r)) : Results.NotFound());
        app.MapPut("/v1/captures/{id}/image", async (string id, HttpRequest request) => {
            if (!CaptureStore.ValidId(id)) return Results.BadRequest();
            if (request.ContentType != "image/jpeg") return Results.StatusCode(415);
            if (request.ContentLength is > MaxBytes) return Results.StatusCode(413);
            await transfers.WaitAsync(request.HttpContext.RequestAborted);
            try {
                var record = Store.Get(id); if (record is null) return Results.NotFound();
                using var bytes = new MemoryStream(); var buffer = new byte[65536];
                while (true) { var count = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted); if (count == 0) break; if (bytes.Length + count > MaxBytes) return Results.StatusCode(413); await bytes.WriteAsync(buffer.AsMemory(0, count)); }
                var raw = bytes.ToArray();
                if (raw.Length < 4 || raw[0] != 0xff || raw[1] != 0xd8 || raw[^2] != 0xff || raw[^1] != 0xd9) return Results.BadRequest(new { error = "需要完整 JPEG 图片" });
                bytes.Position = 0;
                try { using var img = System.Drawing.Image.FromStream(bytes, false, true); if ((long)img.Width * img.Height > 40_000_000) return Results.StatusCode(413); }
                catch (ArgumentException) { return Results.BadRequest(new { error = "图片无法解码" }); }
                var hash = Convert.ToHexString(SHA256.HashData(raw));
                if (record.Hash is not null) return record.Hash == hash ? Results.Json(Status(record)) : Results.Conflict(new { error = "同一拍摄编号对应了不同照片" });
                var path = Store.ImagePath(id); await File.WriteAllBytesAsync(path + ".tmp", raw); File.Move(path + ".tmp", path, true);
                record.Hash = hash; record.State = "received"; record.Detail = "照片已到电脑，正在检查接收界面。"; Store.Save(record);
                received(record); return Results.Json(Status(record));
            } finally { transfers.Release(); }
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First(); Port = new Uri(address).Port;
    }
    public async Task StopAsync() { if (app != null) await app.StopAsync(); }
}
