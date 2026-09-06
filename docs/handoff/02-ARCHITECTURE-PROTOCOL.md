# 架构、源码地图与协议

## Android 源码

目录 android/app/src/main/java/local/chatgpt/camera/。
- MainActivity.java：相机权限、扫码、配对持久化、快门和网络/存储队列。扫码约每 600ms 从预览取图解码。每张照片使用无连字符 UUID；JPEG 与配对快照 JSON 先以临时文件写入再替换。保存成功后传输，失败可补传。stored=true 表示电脑已存储，不表示附件已出现。状态轮询最多 10 次、间隔 400ms，另加网络耗时。
- CameraController.java：Camera2、优先后摄、JPEG 最大优选约 5MP、连续自动对焦、质量 92、预览比例、JPEG 方向和相机线程生命周期。
- PreviewView.java：按比例显示预览。
- Pairing.java：严格校验 IPv4、私网范围、端口 1..65535、64 位十六进制 token/pin。
- ReceiverClient.java：同一份代码用于 JVM 协议测试。校验证书有效期及 SHA-256 指纹、匹配配对主机、禁重定向。连接超时 7s、读取 15s，响应上限 64KiB，图片上限 20MiB。
- MainActivity 中 activeId/activePairing/reserveSucceeded 是共享状态；涉及异步问题先读这些字段及所有回调。

## Windows 源码

目录 windows/CameraReceiver/。
- Program.cs：STA、WinForms、按数据路径的单实例锁；--test-server DIRECTORY 只启动回环协议服务，不操作桌面。
- ReceiverServer.cs：Kestrel TLS、认证、图片校验、预留、上传、状态查询。正常端口 47831；测试端口随机。图片检查 MIME、JPEG 首尾、可解码性及不超过 40MP；传输受单信号量串行保护。
- Core.cs：TargetIdentity(Window, Process, Session, Editor)、TargetGuard 与 CaptureStore。目标必须完全相等且年龄在 0..2min。最多 1000 条预留（包含尚无照片的预留），启动时清空内存中的旧 Target，received/attaching 转 held。
- DesktopTarget.cs：仅接受前台 ChatGPT/Codex 进程及 OpenAI.Codex_ 或 OpenAI\\Codex\\ 安装路径。要求唯一匹配的 Edit 和已选中、AutomationId 含 GUID 的 UIA SelectionItem；保存运行时身份。聚焦后反复校验目标、焦点和修饰键，再 Ctrl+V。附件移除按钮数量在 12×250ms 内增加才标记 attached；这是启发式判断。
- PhotoBitmap.cs：按 EXIF 旋转/翻转再写剪贴板。
- MainForm.cs：二维码、IP 选择、最新 100 条队列、复制照片、打开目录、托盘；关闭窗口进入托盘，显式退出终止服务；串行处理附件操作。

## 协议

所有 API 都需 HTTPS 与 Authorization: Bearer <paired token>。带 Origin 的浏览器请求被拒绝。配对码包含版本 v1、host、port、token、pin，确切 JSON 字段以 MainForm / MainActivity 为准，避免手工录入真实密钥。

- GET /v1/health：验证认证后的连接。
- POST /v1/captures/{id}：预留目标；id 必须是 32 字符 GUID N 格式。已有 ID 返回原记录。桌面目标捕获等待上限 3s。
- POST /v1/captures/{id}?deferred=1：补传预留，不指定新的当前目标。
- PUT /v1/captures/{id}/image：image/jpeg 原始字节，最多 20MiB。先存储再通知桌面处理。相同内容重试幂等，不同内容 409。认证失败 401、非法图片 400、错误 MIME 415；更多异常映射以 ReceiverServer.cs 为准。
- GET /v1/captures/{id}：返回 id、state、detail、stored。客户端分别理解“收到”和“加为附件”。

## 状态与重启

reserved → received → attaching → attached。
目标不满足条件则 held；粘贴结果无法确认则 unconfirmed。held/unconfirmed 由用户检查并手动复制，不能盲目重新自动粘贴。
CaptureRecord 持久化 Id、Created、Target、State、Detail、Hash；元数据通过临时文件替换。JPEG 与 JSON 不构成同一事务。重启后旧 UI 身份失效，不能复用。

## 数据与隐私

电脑正常目录：%LOCALAPPDATA%\\ChatGPTCamera；pairing.json 含随机 token 与 Base64 PFX 私钥，photos 存每张 JPEG/JSON。证书 RSA 2048、SHA-256、有效期五年，使用 UserKeySet 兼容 Windows Schannel。
手机 filesDir/photos 存 JPEG 与包含配对信息的 JSON；SharedPreferences 存配对，应用备份已禁用。当前没有完善的保留期限、图库或清理界面。剪贴板会被照片替换。
测试目录含测试 token/pin/PFX；即使是测试，也不需要提交。真实照片、配对码截图、pairing.json、local.properties、构建缓存不应上传。

