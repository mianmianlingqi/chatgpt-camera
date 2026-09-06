using CameraReceiver;

int failures = 0, total = 0;
void Check(string name, bool actual) { total++; Console.WriteLine($"{(actual ? "PASS" : "FAIL")} {name}"); if (!actual) failures++; }
var now = DateTimeOffset.UtcNow;
var target = new TargetIdentity(100, 10, "thread-A", "editor-A");
Check("same foreground session can attach", TargetGuard.CanAttach(target, target, now, now));
Check("different window held", !TargetGuard.CanAttach(target, target with { Window = 101 }, now, now));
Check("different session held", !TargetGuard.CanAttach(target, target with { Session = "thread-B" }, now, now));
Check("different editor held", !TargetGuard.CanAttach(target, target with { Editor = "editor-B" }, now, now));
Check("different process held", !TargetGuard.CanAttach(target, target with { Process = 11 }, now, now));
Check("unknown target held", !TargetGuard.CanAttach(null, target, now, now));
Check("unknown session held", !TargetGuard.CanAttach(target with { Session = "" }, target with { Session = "" }, now, now));
Check("expired capture held", !TargetGuard.CanAttach(target, target, now.AddMinutes(-3), now));
Check("future timestamp held", !TargetGuard.CanAttach(target, target, now.AddSeconds(5), now));
var dir = Path.Combine(Path.GetTempPath(), "camera-core-" + Guid.NewGuid());
try {
    var store = new CaptureStore(dir); var id = Guid.NewGuid().ToString("N");
    var record = store.Reserve(id, target); record.State = "received"; record.Hash = "abc"; store.Save(record);
    Check("duplicate reserve preserves receipt", store.Reserve(id, target with { Session = "thread-B" }).Hash == "abc");
    var reopened = new CaptureStore(dir);
    Check("receipt survives restart", reopened.Get(id)?.Hash == "abc");
    Check("restart invalidates stale desktop identity", reopened.Get(id)?.Target == null);
    bool rejected = false; try { store.Reserve("../escape", target); } catch (ArgumentException) { rejected = true; }
    Check("path traversal rejected", rejected);
    record.State = "held"; store.Save(record); File.WriteAllText(store.ImagePath(id), "synthetic");
    string extra = Path.Combine(dir, "keep.txt"); File.WriteAllText(extra, "unrelated");
    var recycled = new List<string>();
    int cleared = store.ClearPhotos(path => { recycled.Add(path); File.Delete(path); });
    Check("clear removes received photo and queue record", cleared == 1 && store.Get(id) == null && !File.Exists(store.ImagePath(id)));
    Check("clear delegates only known photo to recycle", recycled.Count == 1 && recycled[0] == store.ImagePath(id) && File.Exists(extra));
    Check("cleared record does not return after restart", new CaptureStore(dir).Get(id) == null);
    var pendingId = Guid.NewGuid().ToString("N"); store.Reserve(pendingId, target);
    Check("clear preserves pending capture", store.ClearPhotos(_ => throw new Exception("unexpected")) == 0 && store.Get(pendingId) != null);
} finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
Console.WriteLine($"{total - failures}/{total} passed"); return failures == 0 ? 0 : 1;
