# Codex Bridge

在 ChatGPT 的讨论与 Codex 的执行之间，完成清晰、可确认的上下文交接。

[![Beta](https://img.shields.io/badge/status-Beta-315f86)](https://github.com/lijingpeng/codexbridge/releases)
![macOS 14+](https://img.shields.io/badge/macOS-14%2B-1f2937)
![Swift 6](https://img.shields.io/badge/Swift-6-f05138)
[![Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-2f855a)](LICENSE)

[下载最新 Beta](https://github.com/lijingpeng/codexbridge/releases) · [更新记录](CHANGELOG.md) · [报告问题](https://github.com/lijingpeng/codexbridge/issues)

![Codex Bridge 主界面](assets/readme/01-codex-bridge-main.png)

Codex Bridge 是一款原生 macOS App，用来查看和整理 ChatGPT、Codex 对话，并把选中的完整对话轮次填入新的 Codex 任务或 ChatGPT 会话。内容始终先进入草稿，由你检查后发送。

## 主要功能

- 在一个工作区中查看 ChatGPT 对话、Codex 任务及任务产生的文件
- 按项目浏览 Codex 会话，并支持搜索、筛选、置顶和本地归档
- 按完整轮次选择需要交接的上下文，避免遗漏问题或回复
- 从 ChatGPT 新建 Codex 任务并填入草稿，可选择工作目录、模型与思考强度
- 从 Codex 新建 ChatGPT 会话并填入草稿，可附带选中的文本文件
- 支持 ChatGPT App，以及通过 Chrome 或 Microsoft Edge 扩展保存的 ChatGPT 网页对话
- 自动同步 Codex 会话变化，并可在 App 内检查、下载和安装 GitHub Releases 中的新版本
- 所有交接均可预览；Codex Bridge 不会自动点击发送

## ChatGPT → Codex

选择 ChatGPT 中已经讨论清楚的轮次，补充执行说明和工作目录，再将内容填入一个新的 Codex 任务。

![把 ChatGPT 讨论交给 Codex](assets/readme/02-chatgpt-to-codex.png)

## Codex → ChatGPT

选择 Codex 的任务轮次和相关文本文件，将执行结果填入一个新的 ChatGPT 会话，继续整理、解释或讨论。

![把 Codex 结果带回 ChatGPT](assets/readme/03-codex-to-chatgpt.png)

## 系统要求

- macOS 14 Sonoma 或更高版本
- Apple Silicon 或 Intel Mac
- 使用 Codex 交接功能时，需要安装并能够正常使用包含 Codex 的 ChatGPT macOS App
- 从 ChatGPT App 保存对话时，需要授予 Codex Bridge 辅助功能权限
- 从 ChatGPT 网页保存对话时，需要 Chrome 或 Microsoft Edge，并手动加载随 App 提供的扩展

## 安装

1. 打开 [GitHub Releases](https://github.com/lijingpeng/codexbridge/releases)，下载最新的 `Codex-Bridge-*.dmg`。
2. 打开 DMG，将 **Codex Bridge** 拖入“应用程序”。
3. 从“应用程序”打开 Codex Bridge。
4. 如果 macOS 阻止首次运行，请先尝试打开一次，然后前往“系统设置 → 隐私与安全性”，找到 Codex Bridge 并点击“仍要打开”。

当前 Beta 使用临时签名，尚未经过 Apple notarization。请只从本仓库的 Releases 页面下载安装包；不需要关闭 Gatekeeper 或修改系统的全局安全策略。

### 校验下载文件

每个 Release 同时提供 `.sha256` 文件。将 DMG 与校验文件放在同一目录后执行：

```bash
shasum -a 256 -c Codex-Bridge-1.1.1-beta.2-universal.dmg.sha256
```

输出包含 `OK` 表示文件与发布时生成的校验值一致。

## 开始使用

### 连接 ChatGPT App

1. 打开 Codex Bridge 设置中的“来源与权限”。
2. 在“ChatGPT App”中点击授权辅助功能。
3. 按系统提示允许 Codex Bridge，然后回到 App 重新检查。
4. 在 ChatGPT App 中打开一段对话，使用 Codex Bridge 保存当前打开的内容。

Codex Bridge 只读取辅助功能接口中当前可见的 ChatGPT 窗口内容，不读取 ChatGPT 的私有数据库、Cookie 或 Token。

### 连接 ChatGPT 网页版

1. 打开 Codex Bridge 设置中的“来源与权限”。
2. 在“ChatGPT Web”中点击“准备连接”，选择 Chrome 或 Microsoft Edge。
3. 在浏览器扩展管理页开启“开发者模式”，点击“加载已解压的扩展程序”。
4. 使用 Codex Bridge 的“在访达中显示”找到扩展文件夹，或在文件选择窗口按 `Shift + Command + G` 后粘贴 App 提供的路径。
5. 打开 [chatgpt.com](https://chatgpt.com)，点击浏览器工具栏中的 Codex Bridge，选择需要的对话轮次并保存。

`Library`（资源库）文件夹在 macOS 中默认隐藏。你也可以在访达中按住 `Option` 打开“前往”菜单，再选择“资源库”。

升级 Codex Bridge 后，App 会自动更新已经准备好的扩展文件。Chrome 和 Edge 不会自动重新加载手动安装的扩展；看到更新提示时，请按提示打开扩展管理页，并在 Codex Bridge 扩展卡片中点击一次“重新加载”。

## 数据与隐私

- Codex Bridge 只处理你主动连接、导入或选择的内容
- 对话索引、置顶与归档状态保存在本机
- 在 Codex Bridge 中归档不会改动 ChatGPT 或 Codex 中的原会话
- 清除 Codex Bridge 本地数据不会删除 ChatGPT、Codex 会话或项目文件
- Codex Bridge 不保存 ChatGPT Cookie、Token 或账号密码
- App 每天最多访问一次 GitHub Releases 接口检查新版本，不会随请求上传对话内容
- 内容只会填入目标 App 的新草稿；是否发送始终由你决定
- 辅助功能权限可以随时在 macOS“系统设置 → 隐私与安全性 → 辅助功能”中撤销

在交接前，请检查预览中的对话和文件，避免发送密码、Token、个人信息或其他敏感内容。

## 从源码构建

需要 Xcode 26 或兼容 Swift 6 的新版 Xcode。

```bash
git clone https://github.com/lijingpeng/codexbridge.git
cd codexbridge
open apps/macos/CodexBridgeApp.xcodeproj
```

在 Xcode 中选择 `CodexBridge` scheme 后运行。也可以通过命令行构建和测试：

```bash
xcodebuild \
  -project apps/macos/CodexBridgeApp.xcodeproj \
  -scheme CodexBridge \
  -destination 'platform=macOS' \
  build

xcodebuild \
  -project apps/macos/CodexBridgeApp.xcodeproj \
  -scheme CodexBridge \
  -destination 'platform=macOS' \
  test
```

生成本地临时签名的 Universal DMG：

```bash
CODEX_BRIDGE_ALLOW_ADHOC=1 ./scripts/package-release.sh
```

产物会写入 `tmp/dist/`。临时签名仅适合本地测试和当前 Beta 分发；重新构建后，macOS 可能要求重新授予辅助功能权限。

## 常见问题

### 为什么 macOS 提示无法验证开发者？

当前 Beta 尚未 notarize。请确认安装包来自本仓库的 Releases 页面，尝试打开一次后，在“系统设置 → 隐私与安全性”中选择“仍要打开”。

### 已经授权辅助功能，为什么仍然无法读取？

退出 Codex Bridge，在系统辅助功能列表中移除旧条目，重新添加“应用程序”中的当前版本后再启动。临时签名版本重新构建或替换后，系统可能会把它视为新的 App。

### 浏览器扩展为什么显示不可用？

确保扩展已在 Chrome 或 Microsoft Edge 中启用，并至少在一个已打开的 `chatgpt.com` 页面中点击过 Codex Bridge 扩展。之后回到 App 的“来源与权限”重新检查。

### 浏览器扩展会自动更新吗？

Codex Bridge 会自动更新本机的扩展文件。由于扩展是通过“加载已解压的扩展程序”安装的，浏览器仍需要你在扩展管理页点击一次“重新加载”；App 检测到扩展版本变化后会给出提示和入口。

### Codex Bridge 会自动检查新版本吗？

会。App 启动后每天最多检查一次 GitHub Releases。你也可以在“设置 → 关于”中手动检查；下载和安装仍由你确认。

### Codex Bridge 会自动发送内容吗？

不会。它只新建目标任务或会话并填入草稿，最终发送由你完成。

### 本地归档会同步到 ChatGPT 或 Codex 吗？

不会。归档只影响 Codex Bridge 内的列表和项目视图，不会改动来源 App 中的原会话。

## Beta 说明

Codex Bridge 目前处于 Beta 阶段。ChatGPT、Codex、macOS 或浏览器更新后，连接和填入能力可能需要适配。遇到问题时，请在 [GitHub Issues](https://github.com/lijingpeng/codexbridge/issues) 中说明 macOS 版本、目标 App 或浏览器版本，以及可复现步骤；请勿附带私人对话、Token 或其他敏感信息。

## 免责声明

Codex Bridge 是独立开发的开源项目，与 OpenAI 无隶属、赞助或官方合作关系。ChatGPT、Codex、OpenAI 及相关标识归其各自权利人所有。

本项目目前处于 Beta 阶段，功能可能随着 macOS、ChatGPT、Codex 或浏览器更新而发生变化。Codex Bridge 只负责填入待确认的草稿，不会代替用户发送内容。用户应在发送前检查对话、文件及可能包含的敏感信息，并自行遵守相关产品和服务条款。

本软件依据 [Apache License 2.0](LICENSE) 提供，不附带任何明示或默示保证。完整条款以 `LICENSE` 为准。

## License

[Apache License 2.0](LICENSE)
