# 预览、拖拽、清空与照片比例修复

交付组合：Windows 0.1.2 + Android 0.1.1。Windows 保留 USB 与 Wi-Fi 两种连接。

## 使用行为

“照片与拖拽”页选中队列图片即可预览，按住预览图拖到 Codex 输入框，看到附件后由用户发送。“连接手机”页保存二维码和 USB 按钮。复制照片功能继续保留。

“清空照片”将电脑上已完成接收/处理的照片移到回收站，并移除对应队列元数据。它保留手机照片、配对设置、未完成捕获和 Codex 中已有附件。正在传输或处理的照片不会被此次清空删除；清空后完成的新照片仍可出现。清空部分失败时保留未处理记录并提示重试。不要把它当作手机清理或 Codex 附件撤销。

## 比例根因

原实机输出 JPEG 为 3120×1440，EXIF Orientation=6，显示方向为 1440×3120。CameraController 的旧选择策略只选 5MP 以下像素最多的尺寸，选中了这台手机的超宽屏输出比例；电脑预览保留原始比例，因此竖图显得很细长。

新增 PhotoSize.java：在 5MP 预算内优先最接近 4:3 的输出，同一比例选像素最多者；没有预算内输出时选最小有效尺寸。CameraController 同步按选定照片比例匹配预览流，保留 EXIF 方向处理。普通竖拍将是 3:4；实际尺寸取决于设备支持。已有照片不拉伸、不改写，需重新拍摄。

## 源码导航

- PhotoTransfer.cs：生成最大 640×360、保持比例的预览；原图释放后不锁定文件。CreateDragData 提供原始文件绝对路径的 FileDrop 数据，只允许 Copy。
- MainForm.cs：两个页签、异步预览、版本计数丢弃过时加载结果；超过系统拖拽阈值才发起拖放。返回拖放效果只提示用户检查附件，不标记自动 attached，也不发送消息。
- Core.cs / ReceiverServer.cs：ClearPhotos 与传输互斥；MainForm 持有附件处理互斥再清空；已删除记录的迟到处理会跳过。删除动作通过回收站实现，测试使用可控回收回调。
- PhotoSize.java / CameraController.java：照片尺寸选择与预览流匹配。
- windows/PhotoTests、windows/CoreTests、android/tests/PhotoSizeTest.java：新覆盖入口。

## 实测与验证

用户已确认从 Windows 0.1.2 预览拖进 Codex 后出现图片附件。接收端预览图片实际可见，拖放完成提示可见。这条手动投递路径已获用户现场确认；自动 UIA 投递仍会因会话标识不可识别进入 held。

本次断言共 68 项：核心 17、配对 7、HTTPS 13、Java 传输 7、USB 12、预览/拖放数据 6、照片尺寸选择 6。Android assembleDebug/lintDebug 成功，v2 签名验证成功，已通过 adb install -r 覆盖安装 Android 0.1.1 并启动；Windows 自包含发布成功。NuGet 在线漏洞数据检查遇到 NU1900 网络错误；安卓保留弃用/lint 提示。

清空逻辑通过合成数据测试，但未代用户点击清空其真实照片。新比例算法与 APK 构建已验证，更新安装后新照片的实际分辨率仍待新一轮拍摄确认。

后续实机反馈：用户在更新后明确确认“我已经观察到了比例正常”。因此新拍照片比例已获得用户现场确认；未额外读取新照片尺寸，不编造确切分辨率。预览拖拽附件与比例修复均已通过用户验收。

用户按 Escape 停止 Windows Computer Use 后已停止界面自动操作。后续开发应重新检查当前用户授权与工具状态，不重用失效 UI 观察。

参考：[Windows FileDrop 官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.dataformats.filedrop)。
