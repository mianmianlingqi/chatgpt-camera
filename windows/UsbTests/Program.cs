using CameraReceiver;

int passed = 0;
async Task Check(string name, CommandResult[] replies, bool success, int calls, string? contains = null) {
    var seen = new List<string>(); var pending = new Queue<CommandResult>(replies);
    var usb = new UsbConnection(args => { seen.Add(string.Join(" ", args)); return Task.FromResult(pending.Dequeue()); });
    var result = await usb.ConnectAsync(47831);
    if (result.Success != success || seen.Count != calls || (contains != null && !result.Message.Contains(contains))) throw new Exception(name + ": " + result.Message);
    if (seen[0] != "-d get-serialno") throw new Exception("Must select USB only");
    if (seen.Skip(1).Any(s => !s.StartsWith("-s USB123 "))) throw new Exception("Device identity must remain fixed");
    if (seen.Any(s => s.Contains("--remove"))) throw new Exception("Must preserve other mappings");
    passed++; Console.WriteLine("PASS " + name);
}
CommandResult Ok(string output = "") => new(0, output, "");
CommandResult Fail(string error) => new(1, "", error);
await Check("No device", [Fail("no devices/emulators found")], false, 1, "数据线");
await Check("Unauthorized", [Fail("device unauthorized")], false, 1, "允许 USB 调试");
await Check("Multiple USB devices", [Fail("more than one device/emulator")], false, 1, "一台");
await Check("Offline", [Fail("device offline")], false, 1);
await Check("Fresh mapping verified", [Ok("USB123\n"), Ok(), Ok(), Ok("UsbFfs tcp:47831 tcp:47831\n")], true, 4);
await Check("Existing mapping reused", [Ok("USB123"), Ok("UsbFfs tcp:47831 tcp:47831\n")], true, 2);
await Check("Conflicting mapping preserved", [Ok("USB123"), Ok("UsbFfs tcp:47831 tcp:1234\n")], false, 2, "占用");
await Check("Unrelated mapping preserved", [Ok("USB123"), Ok("UsbFfs tcp:9000 tcp:9000\n"), Ok(), Ok("UsbFfs tcp:9000 tcp:9000\nUsbFfs tcp:47831 tcp:47831\n")], true, 4);
await Check("List failure", [Ok("USB123"), Fail("offline")], false, 2);
await Check("Reverse failure", [Ok("USB123"), Ok(), Fail("offline")], false, 3);
await Check("Missing verification", [Ok("USB123"), Ok(), Ok(), Ok()], false, 4);
await Check("Verification failure", [Ok("USB123"), Ok(), Ok(), Fail("offline")], false, 4);
Console.WriteLine($"{passed} USB assertions passed");
if (args.Length == 1) {
    var actual = new UsbConnection(command => UsbConnection.ExecuteAsync(args[0], command));
    var result = await actual.ConnectAsync(47831);
    Console.WriteLine($"REAL ADB: success={result.Success}; {result.Message}");
}
