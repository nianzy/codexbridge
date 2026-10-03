# Changelog

本文件记录 Codex Bridge 面向用户的主要变化。

## 1.0.0 - 2026-10-03

本节为 Windows x64 发布；后续 1.1.x Beta 条目为已有 macOS 发布历史。

### ChatGPT ↔ Codex Bridge

- ChatGPT Web Capture 与 Native Messaging。
- Codex 只读 app-server 集成、会话浏览和轮次选择。
- Context / Draft / Handoff 始终由用户检查和确认。

### Workspace

- 多工作区、Project Explorer、文件预览和笔记。
- Git 状态只读感知。

### Context Pack

- 支持会话轮次、项目文件、Git 差异和笔记。
- 支持预览、复制和导出。

### Workspace Snapshots

- 手动保存、Quick Save、工作区切换前自动保存和退出保护。
- 自动去重、自动保留数量、Rename、Preview、Workspace affinity 和 Restore；自动现场重命名后转为手动现场，手动/Quick Save 不受自动保留清理影响。
- 损坏现场隔离并支持安全删除。

### Snapshot Backup

- ZIP Export / Import、manifest.json、SHA256 和原子导入。
- 导入保留原工作区归属，工作区不匹配时禁止 Restore；拒绝不支持的版本/Schema、路径穿越和 malformed package。
- 导入后重建人类可读的 summary.md。

### UI

- Light / Dark / Follow Windows 主题。
- 中文界面及主题化确认、错误窗口。
- Windows 布局和工作现场预览改进。

### Safety / Control

- 不自动发送 Codex 内容。
- 不自动执行 Codex 任务。
- 不自动 Restore。
- 不执行 Git 写操作。
- 交接、切换、Restore 和导入仍需要用户确认。

## 1.1.1 Beta 3 — 2026-09-18

- 更新检查在 GitHub API 限流或暂时不可用时会自动改用公开的 Release Feed，减少共享网络或代理环境下的检查失败。
- 更新检查失败时增加发布页面入口，方便直接查看并手动下载安装包。

## 1.1.1 Beta 2 — 2026-09-18

- 增加 App 内一键更新：发现新版本后可下载并安装，安装前会校验下载来源、文件大小、SHA-256、版本和代码签名；失败时自动恢复旧版本。
- 修复 Beta 版本比较，现在可以正确识别同一产品版本下的后续 Beta，以及从 Beta 升级到正式版。
- 优化 Codex 会话增量同步，新建、状态变化和归档会更及时地反映到 Codex Bridge，同时减少重复读取与界面刷新。
- 同一个 ChatGPT 网页会话再次保存时会更新原有记录，避免因新增消息或调整选择而产生重复会话。
- 修复对话文件的定位与预览，并限制只读取仍位于允许目录中的文本文件。
- 优化 ChatGPT App 与 Codex 的草稿交接，长内容不再依赖 URL 参数传输，也不会自动点击发送。
- 诊断信息不再包含 Codex 账号邮箱或底层错误原文。

## 1.1.1 Beta 1 — 2026-09-18

- Codex Bridge 回到前台时立即同步 Codex 会话，并在前台定期检查新增、更新与归档变化。
- ChatGPT 浏览器扩展会在切换网页会话后自动刷新，同时保留手动刷新入口。
- App 升级后自动更新已准备的扩展文件，并提示用户在 Chrome 或 Edge 中重新加载扩展。
- 增加 GitHub Releases 自动更新检查和“关于”页面中的手动检查入口。

## 1.1.0 Beta 1 — 2026-09-14

- 首个公开 Beta，支持查看 ChatGPT 与 Codex 会话并进行双向交接。
- 支持选择完整对话轮次、工作目录、模型、思考强度和相关文本文件。
- 支持 ChatGPT App、Chrome 和 Microsoft Edge，并始终由用户确认最终发送。
