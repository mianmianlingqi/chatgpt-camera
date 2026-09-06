# Agent 接手入口

记录日期：2026-09-06。产品是个人 Android 相机 + Windows Codex 附件接收端，版本 0.1.0 测试原型。

## 五分钟接手

截图传输入口：09-SCREENSHOT-TRANSFER.md。Android 0.1.2 新增系统分享接收和图片选择；Windows 仍沿用 0.1.2。

最新功能入口：08-PREVIEW-CLEAR-ASPECT.md。Windows 0.1.2 已加入预览、手动拖拽和清空；Android 0.1.1 修复细长照片尺寸选择。用户已确认拖拽后 Codex 出现附件。

更新：Windows 0.1.1 已增加 USB 数据线通道；Android 沿用 0.1.0。先阅读 06-USB.md 的使用与验证边界，0.1.0 仍是历史基线。

最新实机结果见 07-USB-DEVICE-VERIFICATION.md：一张真实照片通过 USB 到达电脑，双端字节校验一致；因 Codex 会话标识不可识别进入 held，自动附件仍未完成。

1. 克隆私有仓库 https://github.com/mianmianlingqi/chatgpt-camera ，运行 git status 与 git log -3 --oneline。源码初始基线为 7663508；后续交接提交仅补充文档。以 Release v0.1.0 的目标提交为归档基线。
2. 阅读本页、05-RISKS-NEXT.md、04-VERIFICATION.md。最大未完成项是“真机拍照后，在当前 Codex 的正确输入框看到照片附件”。
3. 按 03-BUILD-RELEASE.md 复现构建与 40 项核心/协议断言。这些断言不覆盖 Camera2 或 Codex UIA。
4. 在有真实安卓设备和允许检查目标 UI 的环境里执行实机验收。首先只观察当前 Codex 的 UIA 结构，确定会话及编辑框身份是否能识别，再修改适配器。
5. 每次结论写明提交、设备/系统/Codex 版本、步骤、预期、实际、证据路径；更新风险页，避免把“源码实现”当成“实测完成”。

## 用户已确认的边界

接收目标是拍摄时用户正在打开的 Windows Codex 界面输入框附件。只加附件，用户手动发送。用户已授权开发、默认私有 GitHub 仓库发布及 Notion 开发信息归档；不需要重新询问相同位置。

## 状态

已实现 Android 拍摄/扫码/本地保存/补传与 Windows TLS 接收/队列/受保护粘贴适配器。已通过构建、40 项断言、签名校验和接收端 GUI 检查。
未验证真实 Android 拍摄、真实局域网、防火墙、Codex 当前会话识别、最终附件及延迟。严格 UIA 条件可能让某些 Codex 布局始终进入手动队列。

## 阅读地图

- 01-REQUIREMENTS-DECISIONS.md：需求、决策与不应改变的行为。
- 02-ARCHITECTURE-PROTOCOL.md：源码导航、API、数据和状态机。
- 03-BUILD-RELEASE.md：构建、测试、打包、发布与环境。
- 04-VERIFICATION.md：证据边界、历史排错和验收步骤。
- 05-RISKS-NEXT.md：按优先级整理的后续任务与复现方案。
- ../使用说明.md、../验证记录.md、../design.md、../plans/：原始安装文档、验证记录和实现计划。

## 可复制接手任务

继续开发 chatgpt-camera 0.1.0。先读取 docs/handoff/00-START-HERE.md 与风险、验证页，检查 Git 状态。优先在真实 Android + Windows Codex 上验证正确会话附件链路，保留失败时本地队列和用户手动发送的行为。不要以协议测试替代最终附件验收。修改后记录提交、复现、证据和残留限制，并同步 Notion 项目档案。配对密钥、证书私钥、真实照片及本机临时数据不得纳入提交。

