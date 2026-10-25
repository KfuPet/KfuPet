# 模型 API 调用点清单

## 非主对话调用点

| 调用点 | 代码位置 | 用途 | Token 分类 |
| --- | --- | --- | --- |
| 触摸反应 | `MainWindow.xaml.cs` `RunTouchReactionAsync` | 双击部位 / 抚摸头部时生成一句台词 | `TouchReaction` |
| 拖动越界回正 | `MainWindow.xaml.cs` `RunSnapBackReactionAsync` | 窗口拖出屏幕弹回时说的话 | `SnapBack` |
| 对话后情绪判断 | `MainWindow.xaml.cs` `JudgeEmotionAfterChatAsync` | 每轮对话后判主人态度，折算情绪增减 | `EmotionJudge` |
| 主动搭话 | `MainWindow.xaml.cs` `RunProactiveChatAsync` | 独处 / 欢迎回来 / 早晚问候的开场白 | `ProactiveChat` |
| 更新通知正文 | `App.xaml.cs` `BuildUpdateNotificationTextAsync` | 托盘气泡「发现新版本」按角色人设写一句话 | `Notification` |
| 记忆系统（5 处） | `Services/MemorySystem.cs` | 见下方「记忆系统的模型调用」 | `Memory` |

### 记忆系统的模型调用

| 位置 | 用途 |
| --- | --- |
| `ExtractCoreInfoAsync` | 命中记忆触发词时的实时核心信息提取（仅 importance ≥ 3 立即入库） |
| `ExtractFromConversationsAsync` | 归档满时批量提炼值得长期记住的信息并评分 |
| `ScoreContentAsync` | 对单条信息重新评分（总结条目丢弃前的评估） |
| `SummarizeAsync` | 把多条零散信息总结成一句话 |

## 本地模型可用性

| 调用点 | 能否接本地 | 1.7B 是否可用 | 4B 是否可用 | 8B 是否可用 |
| --- | --- | --- | --- | --- |
| 触摸反应 | 能 | 可用 | 可用 | 可用 |
| 拖动越界回正 | 能 | 可用 | 可用 | 可用 |
| 主动搭话 | 能 | 可用 | 可用 | 可用 |
| 更新通知正文 | 能 | 可用 | 可用 | 可用 |
| `ExtractCoreInfoAsync`（实时核心信息提取） | 能 | 不可用 | 可用 | 可用 |
| `ExtractFromConversationsAsync`（归档批量提炼） | 能 | 不可用 | 可用 | 可用 |
| `ScoreContentAsync`（单条重评分） | 能 | 可用 | 可用 | 可用 |
| `SummarizeAsync`（多条总结） | 能 | 不可用 | 可用 | 可用 |

## 性能要求

以下按 Q4 量化、8K 上下文估算。

### 最低配置

| 模型 | 显存 | 内存 | 磁盘 | 说明 |
| --- | --- | --- | --- | --- |
| 1.7B | 2GB | 4GB | 1.5GB | 无独显可纯 CPU 运行 |
| 4B | 4GB | 8GB | 3GB | 纯 CPU 可运行，响应偏慢 |
| 8B | 6GB | 8GB | 5GB | 纯 CPU 基本不可用 |

### 推荐配置

| 模型 | 显存 | 内存 | 磁盘 | 说明 |
| --- | --- | --- | --- | --- |
| 1.7B | 4GB | 8GB | 2GB | |
| 4B | 6GB | 16GB | 4GB | |
| 8B | 8GB | 16GB | 6GB | |