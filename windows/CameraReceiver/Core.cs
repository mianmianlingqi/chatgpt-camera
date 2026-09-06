using System.Text.Json;
namespace CameraReceiver;

public record TargetIdentity(long Window, int Process, string Session, string Editor);
public static class TargetGuard
{
    public static bool CanAttach(TargetIdentity? original, TargetIdentity? current, DateTimeOffset captured, DateTimeOffset now) =>
        original is not null && current is not null && original == current &&
        original.Window != 0 && original.Process > 0 && !string.IsNullOrEmpty(original.Session) &&
        !string.IsNullOrEmpty(original.Editor) && now >= captured && now - captured <= TimeSpan.FromMinutes(2);
}

public sealed class CaptureRecord
{
    public string Id { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public TargetIdentity? Target { get; set; }
    public string State { get; set; } = "reserved";
    public string Detail { get; set; } = "";
    public string? Hash { get; set; }
}

public sealed class CaptureStore
{
    private readonly string directory;
    private readonly Dictionary<string, CaptureRecord> records = new();
    private readonly object gate = new();
    public CaptureStore(string directory) {
        this.directory = directory; Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json")) {
            try {
                var r = JsonSerializer.Deserialize<CaptureRecord>(File.ReadAllText(file));
                if (r is null || !ValidId(r.Id)) continue;
                r.Target = null;
                if (r.State is "attaching" or "received") { r.State = "held"; r.Detail = "接收端已重启，请检查附件后手动插入。"; }
                records[r.Id] = r;
            } catch (Exception ex) when (ex is IOException or JsonException) { }
        }
    }
    public static bool ValidId(string id) => Guid.TryParseExact(id, "N", out _);
    private static void Validate(string id) { if (!ValidId(id)) throw new ArgumentException("Invalid capture ID"); }
    public CaptureRecord Reserve(string id, TargetIdentity? target) {
        Validate(id);
        lock (gate) {
            if (records.TryGetValue(id, out var existing)) return existing;
            if (records.Count >= 1000) throw new InvalidOperationException("接收队列已满，请清理接收文件后重启。");
            var record = new CaptureRecord { Id = id, Target = target, Created = DateTimeOffset.UtcNow };
            Save(record); return record;
        }
    }
    public CaptureRecord? Get(string id) { Validate(id); lock (gate) return records.GetValueOrDefault(id); }
    public void Save(CaptureRecord record) {
        Validate(record.Id);
        lock (gate) {
            var path = Path.Combine(directory, record.Id + ".json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(record));
            File.Move(path + ".tmp", path, true); records[record.Id] = record;
        }
    }
    public string ImagePath(string id) { Validate(id); return Path.Combine(directory, id + ".jpg"); }
    public int ClearPhotos(Action<string> recycle) {
        lock (gate) {
            int count = 0;
            foreach (var record in records.Values.Where(r => r.Hash != null && r.State is not ("received" or "attaching")).ToArray()) {
                string path = ImagePath(record.Id);
                if (File.Exists(path)) recycle(path);
                File.Delete(Path.Combine(directory, record.Id + ".json"));
                records.Remove(record.Id); count++;
            }
            return count;
        }
    }
    public IReadOnlyList<CaptureRecord> All() { lock (gate) return records.Values.OrderByDescending(r => r.Created).ToArray(); }
}
