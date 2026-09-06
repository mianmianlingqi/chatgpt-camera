# 手机截图传输（Android 0.1.2）

## 使用

截图后点系统“分享”，选择“ChatGPT 相机”，即可发送到已经配对的电脑。也可以在应用中点击“选择图片 / 传截图”，通过系统文件选择器选择已有截图。每次导入一张图片；目前不监控所有新截图，不自动扫描相册。

电脑接收端沿用 Windows 0.1.2。USB 用户连接手机后建立数据线通道；Wi-Fi 用户使用已有局域网配对。截图到电脑队列后可预览、拖入 Codex，消息由用户发送。

## 行为与限制

- 使用 ACTION_SEND image/* 接收 content URI；系统选择器使用 ACTION_OPEN_DOCUMENT + CATEGORY_OPENABLE。无需新增相册读取权限，直接分享不要求相机权限。
- 截图保持原始比例，不使用相机 4:3 尺寸选择。PNG / JPEG 等系统可解码图片转换为 JPEG 95 质量，透明背景填白，尊重 EXIF 方向；不是无损原格式复制。
- 输入限制 20 MiB，按二次幂采样将解码图控制在约 5MP，特别长的截图会等比缩小。不会裁成拍照比例。
- 截图产生于应用外，无法知道截图时的 Codex 会话，因此使用 deferred=1 预留；始终先进入接收队列，由用户拖拽或复制，不能冒充原始会话自动投递。
- 图片与现有相机队列使用同样的 UUID / JPEG / JSON 格式。先保存，再传输；失败可返回相机“重试待传”。stored=true 只代表电脑存储确认。
- 仅接受 content URI，不访问传入的 HTTP URL，也不接受 file URI。导入完成会删除自己的临时输入文件。进程被系统终止时已落盘图片仍可补传；未完成落盘的导入需要重新分享。
- Activity 重建不会自动再次发起同一分享；需要时回相机查看重试待传。队列仍受原来的保留/清空规则约束。

## 源码

- ImportActivity.java：分享入口、系统图片选择器、配对读取、导入落盘、deferred 上传、状态显示。配对读取自既有 MainActivity SharedPreferences，不另存身份。
- ImageImport.java：有界文件导入、读取尺寸、解码采样、EXIF 变换、透明底色与 JPEG 输出。
- ImportLimits.java：纯 Java 字节限制与采样计算。
- MainActivity.java / AndroidManifest.xml：选择按钮和分享过滤器。
- android/tests/ImportLimitsTest.java：6 项 JVM 测试。
- android/app/src/androidTest/.../ScreenshotTestRunner.java：8 项设备图像/分享输入测试，测试 APK 不包含在用户安装包中。

## 构建与验证

本次 6 项导入限制测试通过，覆盖普通截图不缩小、长图采样、非法尺寸、字节边界、超限和大尺寸整数计算。Android assembleDebug、assembleDebugAndroidTest、lintDebug 成功，APK v2 签名通过。已安装 Android 0.1.2（versionCode 3）到用户手机，系统查询能正确解析 image/png 分享到 ImportActivity。

额外测试 APK 安装返回 INSTALL_FAILED_USER_RESTRICTED（用户取消），因此设备测试未执行，不能记作通过；未重复安装测试包。真实分享传输验收结果将在下方补记。此版本的 Windows 代码未修改，沿用已验证接收端。

用户随后使用截图入口测试，并确认电脑端“出现了”。截图传输已通过用户现场验收；该反馈不代表额外 8 项仪器测试通过，也不覆盖全部图片格式和断线场景。原始截图未上传到公共仓库或 Notion。

```powershell
javac -d test-classes android/app/src/main/java/local/chatgpt/camera/ImportLimits.java android/tests/ImportLimitsTest.java
java -cp test-classes local.chatgpt.camera.ImportLimitsTest
```

有用户允许安装测试包的设备可执行：构建 assembleDebugAndroidTest，安装测试 APK，然后运行 adb shell am instrument -w local.chatgpt.camera.test/local.chatgpt.camera.ScreenshotTestRunner。它仅生成临时合成图片，不读取用户照片。

参考：[Android 分享接收](https://developer.android.com/develop/ui/compose/sharing/receive)、[系统文件选择器](https://developer.android.com/training/data-storage/shared/documents-files)。
