# USB 数据线传输（Windows 0.1.1）

## 使用

Android 0.1.0 APK 可继续使用。手机打开开发者选项和 USB 调试，接上支持数据传输的数据线，在手机上允许此电脑。电脑运行 0.1.1 接收端，点击“连接 USB 数据线”，成功后用手机相机应用扫描新的二维码。新照片通过 USB 传输，不需要同一个 Wi-Fi。只连接一台安卓手机。

电脑需安装 Android SDK platform-tools。接收端自动查找同目录 platform-tools、ANDROID_HOME / ANDROID_SDK_ROOT、默认用户 Android SDK、PATH；没有找到时可手动选择 adb.exe。官方获取入口：https://developer.android.com/tools/releases/platform-tools 。连接此电脑的调试授权由用户在手机上完成。

拔线、手机或 ADB 重启后，重新点击连接；按钮成功信息只证明当次建隧道成功，不表示持续连接监测。保持证书不变通常无需重新扫码；切换 Wi-Fi 与 USB 时重新扫相应二维码。

旧照片保存了旧连接地址，Wi-Fi 拍摄失败的历史照片不会自动改用 USB 地址补传。先验证 USB 下新拍照片；旧照片的迁移仍是 05-RISKS-NEXT.md 中的后续任务。

## 实现与决策

采用 ADB reverse，将手机 127.0.0.1:47831 转发到电脑 47831；复用原有 HTTPS、证书指纹与 token，不改 Android 协议。用户已要求有线方式，默认单人单设备、断线保留照片、不自动发送消息。

备选 USB 网络共享需要手机支持且依赖虚拟网卡/驱动配置；MTP 拷贝无法直接沿用实时预留与上传 API。ADB reverse 对现有原型改动较小，但用户必须开启 USB 调试并授权。

UsbConnection.cs 先用 -d get-serialno 选择唯一 USB 手机，再固定 -s serial 执行后续命令，避免混用模拟器或无线设备。reverse --list 检查现有映射，同目标复用，不同目标拒绝。新映射使用 --no-rebind，随后重新列出验证。不会删除其他映射或重启共享 ADB server。每个 CLI 调用超时 15 秒，只终止自身客户端；整个连接最多多个调用累计时间。

MainForm.cs 仅在成功后加入并选中 127.0.0.1、更新二维码；失败不切换配对。USB 按钮等待期间禁用避免重复操作。正常接收端仍监听全部 IPv4，USB 模式不是关闭 LAN 的独占模式。

## 验证与接手

windows/UsbTests 是独立控制台测试，链接实际 UsbConnection.cs，使用可控命令结果覆盖无设备、未授权、多设备、离线、新建/复用、映射冲突、无关映射保护、列表/创建/验证失败共 12 项。运行：

```powershell
dotnet run --project windows/UsbTests
```

可选传入 adb.exe 绝对路径运行真实连接诊断。没有连接手机时应返回明确失败，不能声称 USB 图片传输已通过。真实 USB 下应关闭手机 Wi-Fi/移动数据后拍摄测试卡，确认电脑 JPEG 与正确 Codex 附件；再拔线重连测试。USB 不解决 Codex UIA 会话/附件识别本身的未验收问题。

ADB 参数依据官方文档：https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/user/adb.1.md 。

## 本次实际结果（2026-09-06）

12 项 USB 流程测试和原有 40 项核心/配对/HTTPS/Java 传输断言全部通过。Windows 0.1.1 自包含发布成功，C# 编译无警告；依赖漏洞信息请求因 NuGet 网络不可达出现 NU1900，未完成在线漏洞数据检查。Android 源码未修改，沿用已构建 APK。

真实 adb devices 列表为空；真实连接诊断正确返回失败提示。尚未执行真机 USB 照片传输、断线重连或 Codex 附件验收。新版接收端已在原开发机启动，旧版已退出。无真实配对凭据进入版本库。
