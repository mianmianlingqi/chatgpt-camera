# 环境、构建、测试与发布

## 依赖

Windows 10/11 x64，.NET 8 SDK；Android JDK 17+（原开发机使用 Microsoft JDK 21）、Android SDK platform / build-tools 35。Android minSdk 26、target/compileSdk 35，AGP 8.7.2、Gradle wrapper 8.11.1。ZXing 3.5.3、QRCoder 1.6.0，许可证见 THIRD-PARTY-NOTICES.md。
优先使用仓库配置的 Huawei Gradle 镜像、Aliyun Maven 镜像，官方仓库作为回退。不要把本机 SDK 路径写入版本化文件。

## 从干净克隆复现（PowerShell）

下列命令从仓库根目录运行；JDK、dotnet、Python 在 PATH，ANDROID_HOME 指向自己机器的 SDK，Python 安装 Pillow。
```powershell
git status --short
dotnet run --project windows/CoreTests
dotnet publish windows/CameraReceiver -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/windows
Push-Location android
.\gradlew.bat :app:assembleDebug :app:lintDebug
Pop-Location
javac -d test-classes android/app/src/main/java/local/chatgpt/camera/Pairing.java android/app/src/main/java/local/chatgpt/camera/ReceiverClient.java android/tests/PairingTest.java android/tests/TransportTest.java
java -cp test-classes local.chatgpt.camera.PairingTest
python windows/tests/integration.py dist/windows/ChatGPTCameraReceiver.exe test-classes
```

输出：android/app/build/outputs/apk/debug/app-debug.apk；dist/windows/ChatGPTCameraReceiver.exe；Android lint 报告在 app/build/reports。用 Android SDK build-tools 的 apksigner verify --verbose <APK> 检查签名。
预期断言：Core 13、Pairing 7、HTTPS 13、Java transport 7。集成脚本启动自己的 --test-server 临时服务并在结束时终止；不操作真实 Codex。临时目录保留供检查。不要拿正常接收端的真实配对文件跑测试。

## 0.1.0 交付文件

- ChatGPT-Camera-0.1.0.apk：debug 测试安装包，347195 字节。
- ChatGPT-Camera-Android-0.1.0.zip：APK + 中文使用说明，便于 Notion 下载。
- Windows-Receiver-0.1.0.zip：独立 Windows 接收端，76988038 字节，解压后直接启动。
- ChatGPT-Camera-Source-0.1.0.zip：git archive 导出的源码和交接文档，不包含 .git。
- SHA256SUMS.txt、使用说明.md、验证记录.md、Android-lint-report.txt：版本附件与证据。

最终下载入口以 GitHub Releases v0.1.0 为准，GitHub 私有仓库需要获授权登录。Notion 是文档快照；后续源码事实以对应提交为准。

## 发布与恢复步骤

1. 先检查 diff、提交中无凭据/真实照片/本机路径及构建缓存；代码修改运行相应测试。
2. 创建明确提交并记录完整 SHA，保留 Release 不可混淆的版本来源。
3. 使用 git archive 导出跟踪文件。不要递归压缩整个工作区：.git、work 中可能含会话引用、短期上传凭据和测试密钥。
4. 重新计算所有发布资产 SHA-256；源码 ZIP 在增加文档后会变化，不能沿用旧校验和。
5. 发布为 prerelease，注明真机与 Codex 附件未验收。核对远端目标提交、资产大小与下载链接。
6. 更新 Notion 版本索引、验证结果及风险；密钥仅保留本机。

原开发工作区是 C:\Users\35928\Documents\Codex\2026-09-05\new-chat-7；outputs/source 是提交仓库，outputs 是交付件，work 是临时材料。这些绝对路径只用于原机查找，不是新机器构建前提。原机 work/package.py 在初始化 Git 前使用；其旧递归规则没有排除 .git，后续不要直接运行。

