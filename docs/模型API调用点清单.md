| 调用点 | 代码位置 | 用途 | Token 分类 |
| --- | --- | --- | --- |
| 主对话 | `MainWindow.xaml.cs` `SendChatAsync` | 用户在输入框发消息（带工具调用轮次） | `Conversation` |
| 触摸反应 | `MainWindow.xaml.cs` `RunTouchReactionAsync` | 双击部位 / 抚摸头部时生成一句台词 | `TouchReaction` |
| 拖动越界回正 | `MainWindow.xaml.cs` `RunSnapBackReactionAsync` | 窗口拖出屏幕弹回时说的话 | `SnapBack` |
| 对话后情绪判断 | `MainWindow.xaml.cs` `JudgeEmotionAfterChatAsync` | 每轮对话后单独判主人态度，折算情绪增减 | `EmotionJudge` |
| 主动搭话 | `MainWindow.xaml.cs` `RunProactiveChatAsync` | 独处 / 欢迎回来 / 早晚问候的开场白，附带最近几条短期记忆 | `ProactiveChat` |
| 更新通知正文 | `App.xaml.cs` `BuildUpdateNotificationTextAsync` | 托盘气泡「发现新版本」按角色人设写一句话 | `Notification` |
| 记忆系统（5 处） | `Services/MemorySystem.cs` | 见下方「记忆系统的模型调用」 | `Memory` |

### 记忆系统的模型调用

| 位置 | 用途 |
| --- | --- |
| `SearchByLlmAsync` | 关键词未命中时，把长期记忆交给模型挑出与当前话题相关的条目（带超时兜底） |
| `ExtractCoreInfoAsync` | 命中记忆触发词时的实时核心信息提取（仅 importance ≥ 4 立即入库） |
| `ExtractFromConversationsAsync` | 归档满时批量提炼值得长期记住的信息并评分 |
| `ScoreContentAsync` | 对单条信息重新评分（总结条目丢弃前的评估） |
| `SummarizeAsync` | 把多条零散信息总结成一句话 |

### 工具调用循环

每次工具调用会多一轮模型请求，上限 3 轮（`ChatService.MaxToolRounds`）。

| 工具 | 执行内容 |
| --- | --- |
| `open_app` | 打开应用（程序路径本地解析，猜不准才交回模型） |
| `emotion_adjust` | 调整情绪 |
| `feed` | 投喂 |
| `memory_search` | 记忆检索（本地） |
| `query_balance` | 查余额（服务商余额端点，非模型 API） |
