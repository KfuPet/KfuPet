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
| `SearchByLlmAsync` | 关键词未命中时，把长期记忆交给模型挑出与当前话题相关的条目（带超时兜底） |
| `ExtractCoreInfoAsync` | 命中记忆触发词时的实时核心信息提取（仅 importance ≥ 4 立即入库） |
| `ExtractFromConversationsAsync` | 归档满时批量提炼值得长期记住的信息并评分 |
| `ScoreContentAsync` | 对单条信息重新评分（总结条目丢弃前的评估） |
| `SummarizeAsync` | 把多条零散信息总结成一句话 |
