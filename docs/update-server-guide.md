# 自建服务器更新源实现说明

> 本文档为临时实现说明，用于指导后续接入自建服务器作为 GitHub 更新源的容错回退。

## 背景

「检查更新」采用**双更新源容错**：

1. **GitHub**（已实现，`Services/GitHubUpdateSource.cs`）— 默认源
2. **自建服务器**（空壳，`Services/ServerUpdateSource.cs`）— 回退源

`Services/UpdateService.cs` 按 `GitHub → 服务器` 的顺序依次尝试：GitHub 网络不通或返回异常时，自动回退到服务器；两个源都不可用时返回 `null`，由界面提示「检查更新失败」。

## 服务器端需要提供什么

一个 HTTP GET 接口，返回 JSON，字段与 `Models/ReleaseInfo.cs` 对齐：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `version` | string | 是 | 最新版本号，如 `0.0.8` 或 `v0.0.8` |
| `releasePageUrl` | string | 是 | 发布页地址 |
| `releaseNotes` | string | 否 | 更新说明 |

响应示例：

```json
{
  "version": "0.0.8",
  "releasePageUrl": "https://example.com/kfupet/releases",
  "releaseNotes": "- 修复已知问题\n- 新增功能"
}
```

> 说明：客户端解析时要求字段名与 `ReleaseInfo` 的 C# 属性名一致（`Version` / `ReleasePageUrl` / `ReleaseNotes`），
> 因此 JSON 用 `version` / `releasePageUrl` / `releaseNotes`，后续如需不同命名可在反序列化时配置 `JsonSerializerOptions.PropertyNameCaseInsensitive` 或 `JsonPropertyName`。

## 客户端实现步骤（替换空壳）

1. 在 `Services/ServerUpdateSource.cs` 中填上服务器地址，**不要硬编码**，建议从配置文件读取（见下方「配置建议」）。
2. 用 `HttpClient` 拉取并解析 JSON，返回 `ReleaseInfo`，与 `GitHubUpdateSource.GetLatestReleaseAsync` 类似：

```csharp
public async Task<ReleaseInfo?> GetLatestReleaseAsync()
{
    using var response = await HttpClient.GetAsync(ServerUpdateUrl);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<ReleaseInfo>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}
```

3. 失败时抛出异常即可——`UpdateService.CheckAsync` 已经对每个源做了 `try/catch`，会跳过失败源继续下一个。

## 配置建议

- 参考 `ModelConfigService` 的做法，把服务器地址存到 `%AppData%\KfuPet\` 下的配置文件，启动时读取。
- 不要像 GitHub 的 `Owner` / `Repo` 那样用常量写死——服务器地址属于部署配置，会随环境变化。

## 注意事项

- 超时：给 `HttpClient` 设置合理 `Timeout`（如 10 秒），避免界面长时间卡在「检查更新」。
- 版本号格式：服务器返回的 `version` 应与 csproj 的 `<Version>`（当前 `0.0.7`）保持可比，`UpdateService.TryParseVersion` 已兼容 `v` 前缀。
- 容错顺序已固定为 GitHub 优先，如需调整顺序，改 `UpdateService._sources` 数组即可。
