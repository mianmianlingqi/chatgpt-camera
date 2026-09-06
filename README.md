# ChatGPT Camera

Personal Android camera + Windows foreground Codex attachment receiver, version 0.1.0.

The Windows receiver is implemented, builds, and passes protocol tests. Android APK builds and passes lint with documented warnings. Real Android camera operation and attachment appearance inside Codex have **not** been verified. The UI Automation adapter fails closed into a local queue if the current session cannot be identified.

## Layout

Latest: Windows 0.1.2 adds preview, original-file drag-and-drop, and clear-to-Recycle-Bin. Android 0.1.1 prefers 4:3 capture sizes. See [latest behavior and validation](docs/handoff/08-PREVIEW-CLEAR-ASPECT.md). USB photo transport and manual drag into Codex have been confirmed on one physical device; automatic session targeting remains unresolved.

Windows 0.1.1 adds a **连接 USB 数据线** button using ADB reverse. Android 0.1.0 remains compatible. See [USB setup and handoff](docs/handoff/06-USB.md). Enable USB debugging, authorize this computer, connect one USB phone, click the button, then scan the refreshed pairing QR. USB transport and actual Codex attachment still require device acceptance testing.

- `android/`: Java / Android Camera2 app, ZXing QR scanner, pinned HTTPS transport.
- `windows/CameraReceiver/`: .NET 8 Windows Forms + Kestrel TLS server and conservative UI Automation adapter.
- `windows/CoreTests/`: dependency-free console assertions for target safety and persistent state.
- `windows/tests/integration.py`: HTTPS integration checks using synthetic JPEGs; never automates Codex.
- `android/tests/`: pure Java pairing validation and transport checks against the real receiver server.
- `docs/plans/`: confirmed implementation plan.

## Build

Android requires JDK 17+ and Android SDK platform/build-tools 35. Set `ANDROID_HOME` or provide your own `android/local.properties` with `sdk.dir`.

```powershell
cd android
.\gradlew.bat :app:assembleDebug :app:lintDebug
```

Output: `android/app/build/outputs/apk/debug/app-debug.apk`. Gradle distribution uses Huawei mirror, dependencies prefer Aliyun mirrors with official fallbacks. For distribution beyond personal testing, configure a private release signing key; no private signing keys are included here.

Windows requires the .NET 8 SDK on Windows:

```powershell
dotnet run --project windows/CoreTests
dotnet publish windows/CameraReceiver -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/windows
```

## Protocol tests

Python 3 with Pillow is required to generate synthetic JPEGs. Compile the pure Java sources with a JDK:

```powershell
javac -d test-classes android/app/src/main/java/local/chatgpt/camera/Pairing.java android/app/src/main/java/local/chatgpt/camera/ReceiverClient.java android/tests/PairingTest.java android/tests/TransportTest.java
java -cp test-classes local.chatgpt.camera.PairingTest
python windows/tests/integration.py dist/windows/ChatGPTCameraReceiver.exe test-classes
```

The test script starts its own receiver in loopback-only mode with a temporary identity and synthetic images, then terminates it. `--test-server DIRECTORY` is specifically a headless protocol-test mode with no desktop injection. Test directories are retained for inspection.

## API

All requests use pinned HTTPS and `Authorization: Bearer <paired token>`. Browser Origin requests are rejected.

- `GET /v1/health`: validate paired connectivity.
- `POST /v1/captures/{32-character GUID}`: reserve a foreground target. Repeated reservation preserves the first record.
- `POST /v1/captures/{id}?deferred=1`: retry reservation without assigning a newly opened conversation as the original target.
- `PUT /v1/captures/{id}/image`: bounded `image/jpeg` upload. Identical retry returns existing status; different bytes return 409.
- `GET /v1/captures/{id}`: report `stored`, `state`, and user-readable `detail`.

Durable states: reserved, received, attaching, attached, held, unconfirmed. Restart invalidates UI identities. An unknown paste outcome is not automatically retried.

## Desktop adapter boundary

Only a foreground process installed under `OpenAI.Codex_` or `OpenAI\Codex\` with process name ChatGPT/Codex is eligible. The adapter requires one recognizable editable composer and an explicit selected UIA item whose AutomationId contains a GUID. Before pasting, it rechecks the window/process, session, editor runtime identity and focus. It never sends Enter. This is a conservative prototype adapter, not an official stable integration API; some Codex layouts may always require the manual queue fallback. Window/selection checks and keyboard injection are not one atomic OS operation, so physical interaction during insertion remains a race to test.

## References

- Android Camera2: https://developer.android.com/reference/android/hardware/camera2/CameraDevice
- Android TLS guidance: https://developer.android.com/privacy-and-security/security-ssl
- Windows UI Automation: https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/obtaining-ui-automation-elements
- Schannel ephemeral-key limitation encountered and fixed during integration: https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting

Dependencies include ZXing core 3.5.3 (Apache-2.0) and QRCoder 1.6.0 (MIT); see `THIRD-PARTY-NOTICES.md`.
