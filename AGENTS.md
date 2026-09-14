# AGENTS.md

本文件约束所有在本仓库中工作的 AI Agent、自动化工具和协作者。其作用域为仓库根目录及全部子目录；若子目录存在更具体的 `AGENTS.md`，则以更具体的约束为补充。

## 项目定位

- 产品正式名称为 `Codex Bridge`；用户可见的 App 名称、窗口标题、菜单、提示、PRD 和设计产出统一使用此名称与大小写。
- 新增的 target、module 和 Swift 类型使用 `CodexBridge`，database、protocol、test fixture、缓存 key 和文件名使用语境适配的 `codexbridge` 或 `codex-bridge`，不得重新引入 `Sidely`、`Conversation Atlas`、`Atlas` 等旧产品名；旧名称仅可出现在兼容迁移代码中。
- `ChatGPT`、`Codex`、`Handoff` 等保留为平台或功能术语，不作为 Codex Bridge 的替代产品名。
- 本项目主要开发一个与 Codex 深度结合的原生 macOS App。
- 默认优先采用 Apple 平台原生技术与交互规范，例如 Swift、SwiftUI、AppKit、Xcode 和 Swift Package Manager。
- 实现应重视 macOS 原生体验、可访问性、隐私、安全性与长期可维护性。

## 沟通与文档语言

- 面向用户的说明、计划、注释和项目文档默认使用中文。
- API、framework、class、method、CLI command、file name 等技术术语可保留英文，避免生硬翻译。
- 表述应简洁、明确，先说明结果或结论，再补充必要的实现细节。

## 工作方式

- 修改前先阅读相关代码、配置和现有约束，避免覆盖用户已有改动。
- 优先进行范围小、可验证、可回退的修改，不因局部需求进行无关重构。
- 未经用户明确授权，不执行 commit、push、发布、部署或其他影响外部状态的操作。
- 完成修改后，应进行与风险匹配的验证，并准确说明已验证和未验证的部分。
- 引入 dependency 前应说明用途与权衡，优先使用系统能力和已有 dependency。

## 中间思考与临时产出

- 不得将 chain-of-thought、中间推理、分析草稿、调试随笔或未整理的研究材料写入仓库。
- 不得为了存放临时内容而创建 `docs/`、`design/`、`research/`、`spec/`、`notes/` 等具有业务或长期维护含义的目录。
- 临时脚本、截图、日志、实验文件和一次性生成物统一放入仓库根目录的 `tmp/`；任务结束后应清理不再需要的内容。
- `tmp/` 不纳入 Git。确需保留的正式产出，应先确认其业务归属和最终路径，再写入对应目录。
- 未经明确要求，不新增 README、方案文档、总结文档或其他衍生文档。

## 目录与文件约束

- 新建目录前，先确认它属于产品结构或工程结构，而非仅为当前任务提供方便。
- 不得使用含糊的业务目录承载临时文件，也不得把临时内容混入源码、资源或测试目录。
- 文件命名应反映真实职责，并遵循现有工程的命名风格。
- 敏感信息、token、credential、certificate、个人配置和本机路径不得提交到 Git。

## macOS App 开发约束

- UI 默认遵循 Apple Human Interface Guidelines，并优先支持 keyboard、menu command、window management、Dark Mode。
- 涉及 Codex、文件系统、shell、network 或 credential 的能力时，应采用最小权限，并让高风险操作可见、可确认、可追溯。
- 不在 MainActor 上执行可能阻塞 UI 的长任务；并发逻辑优先使用 Swift Concurrency，并明确 cancellation 与 error handling。
- 用户可见文案应集中、稳定、可本地化，避免将调试信息直接暴露给最终用户。

## 测试与交付

- 新功能和 bug fix 应补充与改动规模相称的测试；优先覆盖关键状态、错误路径和用户可见行为。
- 不得把“代码可编译”表述为“功能已完整验证”，也不得把 unit test 通过表述为 UI 或 integration 已通过。
- 交付说明应列出实际变更、验证结果、已知限制和必要的后续步骤，不应包含未执行事项的完成声明。
