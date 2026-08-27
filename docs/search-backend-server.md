# 自建搜索后端服务设计

> 本文档描述 KfuPet 接入「自建搜索后端服务」的方案与接口协议。
> 自建服务是独立项目，不属于 KfuPet 桌面端代码库；KfuPet 只作为 HTTP 客户端调用它。

## 1. 概述

KfuPet 的 AI 助手需要联网搜索能力，用来回答新闻、天气、资讯等实时问题，避免模型凭训练数据乱编。

为不绑定单一第三方搜索服务，本项目支持自建搜索后端：由自建服务负责「查询 → 检索 → 返回结果」，KfuPet 端只发 HTTP 请求并解析结果。

## 2. 架构

```text
KfuPet（桌面端 WPF）
    │  HTTP POST /search  （自定义协议）
    ▼
自建搜索后端服务（独立项目，任意语言/框架）
    │  调用数据源
    ▼
数据源（第三方搜索 API / SearXNG / 自建索引等）
```

- KfuPet 端：`WebSearchTool` 负责把模型传入的 `query` 发给自建服务，并把结果格式化后回填给模型。
- 自建服务端：实现本文档定义的 HTTP 接口，内部可自由选择数据源与实现方式。

## 3. 接口协议

### 3.1 请求

`POST {endpoint}`，`Content-Type: application/json`。

```json
{
  "query": "今天有什么新闻",
  "max_results": 5
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| query | string | 是 | 搜索关键词或问题 |
| max_results | number | 否 | 期望返回的最大结果条数，默认 5 |

### 3.2 响应

`200 OK`，`Content-Type: application/json`。

```json
{
  "results": [
    {
      "title": "结果标题",
      "url": "https://example.com/article",
      "content": "结果摘要片段"
    }
  ]
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| results | array | 搜索结果数组，可为空数组 |
| results[].title | string | 结果标题 |
| results[].url | string | 结果链接 |
| results[].content | string | 结果摘要，用于让模型理解内容 |

### 3.3 错误约定

- 非 `2xx` 状态码、响应无法解析、或返回空 `results` 时，KfuPet 端统一回退为「搜索暂不可用」提示，并让模型如实转述，不编造结果。
- 建议自建服务返回结构化错误信息，便于排查，例如：

```json
{ "error": "搜索数据源超时" }
```

## 4. KfuPet 端配置

- `SearchConfig.Provider = "custom"` 表示使用自建服务。
- `SearchConfig.Endpoint` 填写自建服务地址，例如 `http://localhost:8080/search` 或线上域名。
- `SearchConfig.ApiKey` 在自建服务场景下可选（若自建服务需要鉴权）。

切换后端只需改配置：`provider` 取值 `tavily` / `bing` / `custom`，KfuPet 端调用方无需改动。

## 5. 数据源方案对比

自建服务最终要解决「query → 返回真实网页结果」，可选数据源如下：

| 方案 | 说明 | 优点 | 缺点 |
| --- | --- | --- | --- |
| A. 聚合第三方搜索 API | 底层调用 Bing/Google/搜狗等 API，自建服务做聚合与格式化 | 结果质量高、开发快 | 仍需要第三方 key，等于加一层代理 |
| B. 自建爬取 + 索引 | 自己抓取网页并建立索引 | 完全自主、无外部依赖 | 工作量大，接近自研搜索引擎，不推荐先做 |
| C. 开源/自托管搜索 | 使用 SearXNG 等自托管搜索聚合服务 | 免费、可控、可定制 | 需要自己部署与维护 |

建议：先用 A 或 C 快速跑通，再按需演进。

## 6. 实现建议

自建服务建议实现以下能力：

- 缓存：相同 query 短时间内复用结果，降低数据源调用成本。
- 超时：对数据源设置超时，避免拖慢 KfuPet 的对话响应。
- 结果截断：`content` 控制长度，避免返回过长文本占用模型上下文。
- 结果过滤：可限定搜索范围（如只搜特定站点）、过滤低质/无关内容。

## 7. 与第三方后端的切换

KfuPet 端 `WebSearchTool` 按 `Provider` 分发：

- `tavily`：调用 Tavily 搜索接口（需 API Key）。
- `bing`：调用 Bing Web Search 接口（需 API Key）。
- `custom`：调用本文档定义的自建服务接口。

三种方式共用同一套结果格式，切换时仅修改 `search.json` 配置，无需改动调用方代码。

## 8. KfuPet 端已预留的空壳（现状）

当前代码已搭好工具调用框架，但搜索后端尚未接入。已存在的空壳：

| 文件 | 说明 |
| --- | --- |
| `Services/Tools/ITool.cs` | 工具接口：`Name` / `Description` / `ParametersSchemaJson` / `ExecuteAsync` |
| `Services/Tools/ToolRegistry.cs` | 工具注册与分发，含 `ToolDefinition` |
| `Services/Tools/WebSearchTool.cs` | 占位实现：`Name = "web_search"`，参数已定义 `query`；`ExecuteAsync` 当前返回「联网搜索后端暂未接入」固定提示 |
| `Services/ChatService.cs` | `SendWithToolsAsync` 已实现工具调用循环（携带 tools、解析 tool_calls、执行、回填、不支持时降级） |
| `Models/ChatMessage.cs` | 已扩展 `ToolCall` / `ToolCallId` / `ToolCalls` |

接入真实后端时还需补充（当前**尚未实现**）：

1. `SearchConfig` / `SearchConfigService`：`search.json` 配置存储（Provider / Endpoint / ApiKey）。
2. `WebSearchTool.ExecuteAsync`：改为按 `Provider` 分发到真实后端（`tavily` / `bing` / `custom`）。
3. 设置界面「搜索」配置页：Provider 下拉 + Endpoint/ApiKey 输入 + 保存。
