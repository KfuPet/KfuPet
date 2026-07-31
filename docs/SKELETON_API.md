# KfuPet IPC API 文档

> 通过 Named Pipe（命名管道）跨进程调用 KfuPet 的骨骼系统与图片渲染。
> 适用于开发者工具、管理器等独立外部程序。

## 架构概览

```
                    KfuPet.exe 
                         │ 
             ┌───────────┴───────────┐ 
             │                       │ 
             ▼                       ▼ 
        NamedPipeServer         HttpServer（后期，安卓调试用，Kestrel）
             │                       │ 
             └───────────┬───────────┘ 
                         ▼ 
                  CommandDispatcher 
                         ▼ 
    ┌──────────┬──────────┬──────────┬──────────┐
    ▼          ▼          ▼          ▼
Skeleton   Memory     Emotion     Vision
Service    Service    Service     Service
```

**安卓调试流程**：安卓设备通过有线/无线 ADB 连接电脑后，安卓端的开发者工具通过 HttpServer 远程调用 KfuPet API，实现骨骼调试与控制。

所有服务共用同一个管道和命令分发器，通过 `service` 字段区分目标服务。本文档主要描述 `skeleton`（骨骼）服务。

## 目录

- [快速开始](#快速开始)
- [通信协议](#通信协议)
- [骨骼查询](#骨骼查询)
- [位置操作](#位置操作)
- [旋转操作](#旋转操作)
- [缩放操作](#缩放操作)
- [激活控制](#激活控制)
- [重置操作](#重置操作)
- [心跳检测](#心跳检测)
- [批量操作](#批量操作)
- [世界坐标](#世界坐标)
- [骨骼图片挂载](#骨骼图片挂载)
- [调试控制](#调试控制)
- [Action 列表](#action-列表)
- [骨骼 ID 列表](#骨骼-id-列表)

---

## 快速开始

### 管道信息

| 项目 | 值 |
|------|-----|
| 管道名称 | `KfuPet.Skeleton` |
| 方向 | 双向（InOut） |
| 传输模式 | Message |
| 数据格式 | JSON（逐行传输） |

### 准备工作

将客户端 SDK 复制到你的项目中：

- 文件路径：`Services/Ipc/SkeletonPipeClient.cs`
- 命名空间：`KfuPet.Ipc.Client`
- 依赖：`System.IO.Pipes`（.NET 内置）、`System.Text.Json`（.NET 内置）

### 最简单的调用

```csharp
using KfuPet.Ipc.Client;

// 创建客户端（可长期复用）
using var client = new SkeletonPipeClient();

// 同步调用
client.SetRotation("arm_left_upper", 45);

// 异步调用
await client.SetRotationAsync("arm_left_upper", 45);
```

调用后 KfuPet 端会自动重新计算骨骼变换并刷新渲染。

---

## 通信协议

使用 JSON 格式的请求/响应模型，通过命名管道逐行传输。

### 请求格式

```json
{
  "service": "skeleton",
  "action": "SetRotation",
  "params": {
    "boneId": "arm_left_upper",
    "degrees": 45
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| service | string | 目标服务名：`skeleton` / `memory` / `emotion` / `vision` |
| action | string | 操作名称 |
| params | object | 操作参数（可选） |

### 响应格式

成功：
```json
{
  "success": true,
  "data": true,
  "error": null
}
```

失败：
```json
{
  "success": false,
  "data": null,
  "error": "Unknown action: xxx"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| success | bool | 是否成功 |
| data | object | 返回数据（可选） |
| error | string | 错误信息（失败时存在） |

---

## 骨骼查询

### GetBoneIds

获取所有骨骼的 ID 列表。

```csharp
var boneIds = client.GetBoneIds();
```

**对应 action**：`GetBoneIds`

**返回值**：`string[]` — 所有骨骼 ID 数组

---

### BoneExists

检查指定骨骼是否存在。

```csharp
bool exists = client.BoneExists("head");
```

**对应 action**：`BoneExists`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`bool` — `true` 存在，`false` 不存在

---

### GetBoneName

获取骨骼的显示名称。

```csharp
string? name = client.GetBoneName("arm_left_upper");
// 返回 "LeftArmUpper"
```

**对应 action**：`GetBoneName`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`string` — 骨骼名称，若不存在返回 `null`

---

### GetParentBoneId

获取骨骼的父骨骼 ID。

```csharp
string? parentId = client.GetParentBoneId("arm_left_lower");
// 返回 "arm_left_upper"
```

**对应 action**：`GetParentBoneId`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`string` — 父骨骼 ID，根骨骼返回 `null`

---

### GetChildBoneIds

获取骨骼的所有子骨骼 ID 列表。

```csharp
var children = client.GetChildBoneIds("body");
// 返回 ["neck", "arm_left_upper", "arm_right_upper", "hip"]
```

**对应 action**：`GetChildBoneIds`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`string[]` — 子骨骼 ID 数组

---

## 位置操作

### SetPosition

设置骨骼的本地位置（相对于父骨骼的偏移）。

```csharp
bool success = client.SetPosition("head", 0, -20);
```

**对应 action**：`SetPosition`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |
| x | double | X 轴偏移（逻辑像素） |
| y | double | Y 轴偏移（逻辑像素） |

**返回值**：`bool` — `true` 设置成功，`false` 骨骼不存在

---

### GetPosition

获取骨骼的本地位置。

```csharp
var pos = client.GetPosition("head");
if (pos.HasValue)
{
    Console.WriteLine($"X: {pos.Value.X}, Y: {pos.Value.Y}");
}
```

**对应 action**：`GetPosition`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`(double X, double Y)?` — 本地位置，骨骼不存在返回 `null`

---

### Translate

平移骨骼（在当前位置基础上偏移）。

```csharp
bool success = client.Translate("head", 5, -10);
```

**对应 action**：`Translate`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |
| deltaX | double | X 方向偏移量 |
| deltaY | double | Y 方向偏移量 |

**返回值**：`bool` — `true` 设置成功，`false` 骨骼不存在

---

## 旋转操作

### SetRotation

设置骨骼的本地旋转角度（角度制）。

```csharp
bool success = client.SetRotation("arm_left_upper", 45);
```

**对应 action**：`SetRotation`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |
| degrees | double | 旋转角度（度），正值顺时针，负值逆时针 |

**返回值**：`bool` — `true` 设置成功，`false` 骨骼不存在

> **注意**：旋转会传递给所有子骨骼。例如旋转上臂，小臂会跟随一起旋转。

### SetRotationAsync（异步版本）

```csharp
bool success = await client.SetRotationAsync("arm_left_upper", 45);
```

---

### GetRotation

获取骨骼的本地旋转角度（角度制）。

```csharp
double? degrees = client.GetRotation("arm_left_upper");
```

**对应 action**：`GetRotation`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`double?` — 旋转角度（度），骨骼不存在返回 `null`

---

### Rotate

相对旋转骨骼（在当前角度基础上增加）。

```csharp
bool success = client.Rotate("arm_left_upper", 15);
```

**对应 action**：`Rotate`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |
| deltaDegrees | double | 相对旋转角度（度） |

**返回值**：`bool` — `true` 设置成功，`false` 骨骼不存在

---

## 缩放操作

### SetScale

设置骨骼的本地缩放。

```csharp
bool success = client.SetScale("body", 1.0, 1.2);
```

**对应 action**：`SetScale`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |
| scaleX | double | X 方向缩放比例 |
| scaleY | double | Y 方向缩放比例 |

**返回值**：`bool` — `true` 设置成功，`false` 骨骼不存在

---

### GetScale

获取骨骼的本地缩放。

```csharp
var scale = client.GetScale("body");
if (scale.HasValue)
{
    Console.WriteLine($"ScaleX: {scale.Value.X}, ScaleY: {scale.Value.Y}");
}
```

**对应 action**：`GetScale`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`(double X, double Y)?` — 缩放值，骨骼不存在返回 `null`

---

## 激活控制

### SetActive

设置骨骼的激活状态（非激活的骨骼不参与渲染）。

```csharp
client.SetActive("arm_left_lower", false); // 隐藏左小臂
```

**对应 action**：`SetActive`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |
| isActive | bool | 是否激活 |

**返回值**：`bool` — `true` 设置成功，`false` 骨骼不存在

> **注意**：父骨骼未激活时，子骨骼即使处于激活状态也不会被渲染。

---

### IsActive

获取骨骼的激活状态。

```csharp
bool? active = client.IsActive("arm_left_lower");
```

**对应 action**：`IsActive`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`bool?` — 激活状态，骨骼不存在返回 `null`

---

## 重置操作

### ResetBone

恢复单个骨骼到默认状态（骨骼首次创建时的位置、旋转、缩放和激活状态）。

```csharp
bool success = client.ResetBone("arm_left_upper");
```

**对应 action**：`ResetBone`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`bool` — `true` 恢复成功，`false` 骨骼不存在

> **注意**：默认值在骨骼被添加到骨架（`AddBone`）时自动记录，恢复时回到该状态，而非简单归零。
> 每个骨骼的默认值不同（如 root 骨骼默认位置在画布中心），因此恢复全部骨骼可还原角色初始姿态。

---

### ResetAll

恢复所有骨骼到默认状态。

```csharp
client.ResetAll();
```

**对应 action**：`ResetAll`

所有骨骼恢复到各自首次创建时的位置、旋转、缩放和激活状态。

---

## 心跳检测

### Ping

检查 KfuPet 服务是否存活。不修改任何数据，仅返回连接状态。

```csharp
// 同步
bool alive = client.Ping();

// 异步
bool alive = await client.PingAsync();
```

**对应 action**：`Ping`

**返回值**：`bool` — `true` 服务存活，`false` 连接失败（服务未启动或已关闭）

**双向感知**：服务端也会通过心跳来跟踪工具端的连接状态（5 秒超时窗口）。工具端一旦停止发送 `Ping`，服务端将自动标记为已断开，后续可配合开发者开关等依赖工具端的功能使用。

**使用场景**：工具端可定时（如每 2-3 秒）调用 `Ping` 检查连接状态，根据返回值更新 UI 连接指示器。

**示例：定时心跳**

```csharp
// 在工具端使用 Timer 定期检查
var timer = new System.Timers.Timer(3000); // 3 秒一次
timer.Elapsed += async (s, e) =>
{
    bool alive = await client.PingAsync();
    Dispatcher.Invoke(() =>
    {
        statusLabel.Text = alive ? "已连接" : "已断开";
        statusLabel.Color = alive ? Green : Red;
    });
};
timer.Start();
```

---

## 批量操作

### Batch

批量修改多个骨骼属性，所有修改完成后只触发一次渲染更新。

```csharp
client.Batch(b =>
{
    b.SetRotation("arm_left_upper", 30);
    b.SetRotation("arm_right_upper", -30);
    b.SetPosition("head", 0, -10);
    b.SetScale("body", 1.0, 1.1);
});
```

**对应 action**：`Batch`

**可用方法**：
- `SetPosition(boneId, x, y)`
- `SetRotation(boneId, degrees)`
- `SetScale(boneId, scaleX, scaleY)`
- `Translate(boneId, deltaX, deltaY)`
- `Rotate(boneId, deltaDegrees)`
- `SetActive(boneId, isActive)`
- `ResetBone(boneId)`
- `AddAttachment(boneId, attachmentId, name, resourcePath, offsetX, offsetY, pivotX, pivotY, zOrder)` — 挂载图片

**使用场景**：
- 同时修改多个骨骼属性
- 减少渲染刷新次数以提升性能
- 确保多个骨骼状态变更的原子性

### BatchAsync（异步版本）

```csharp
await client.BatchAsync(b =>
{
    b.SetRotation("arm_left_upper", 30);
    b.SetRotation("arm_right_upper", -30);
});
```

---

## 世界坐标

### GetWorldPosition

获取骨骼的世界坐标（屏幕空间位置）。

```csharp
var worldPos = client.GetWorldPosition("head");
if (worldPos.HasValue)
{
    Console.WriteLine($"X: {worldPos.Value.X}, Y: {worldPos.Value.Y}");
}
```

**对应 action**：`GetWorldPosition`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`(double X, double Y)?` — 世界坐标（相对于画布左上角），骨骼不存在或未计算变换时返回 `null`

---

## 骨骼图片挂载

将图片挂载到骨骼上的机制。图片会跟随骨骼移动和旋转，用于渲染角色的身体部位（头部、躯干、四肢等）。

### 完整流程

从工具端上传图片并挂载到骨骼的标准流程：

```csharp
using var client = new SkeletonPipeClient();

// 1. 上传图片到指定骨骼目录
string? path = client.UploadResource("data:image/png;base64,...", boneId: "head");

// 2. 用返回的路径挂载到骨骼
if (path != null)
{
    client.AddAttachment("head", "face", "Face", path);
}
```

### UploadResource

将 base64 编码的图片上传到 KfuPet 资源缓存目录，返回本地文件路径。

```csharp
// 按骨骼分类存储（推荐）
string? path = client.UploadResource("data:image/png;base64,...", boneId: "head");
// → Resources/Cache/head/a1b2c3.png

// 不指定骨骼则存入根目录
string? path = client.UploadResource("data:image/png;base64,...");
// → Resources/Cache/a1b2c3.png
```

**对应 action**：`UploadResource`

| 参数 | 类型 | 说明 |
|------|------|------|
| base64Data | string | base64 编码的图片数据，支持 data URI 格式 |
| boneId | string | 可选，指定后存入 `Resources/Cache/{boneId}/` 子目录 |

**返回值**：`string` — 保存后的本地文件路径，失败返回 `null`

**支持的图片格式**：PNG / JPEG / GIF / WebP / BMP

**缓存目录结构**：
```
Resources/Cache/
├── head/
│   └── a1b2c3.png
├── body/
│   └── d4e5f6.png
├── arm_left_upper/
│   └── g7h8i9.png
└── ...
```

---

### DeleteResource

显式删除缓存目录下的资源文件（仅限 `Resources/Cache/` 内的文件，保证安全）。

```csharp
// 先移除附件，再删除文件
client.RemoveAttachment("face");
client.DeleteResource(path);
```

**对应 action**：`DeleteResource`

| 参数 | 类型 | 说明 |
|------|------|------|
| resourcePath | string | 文件路径（必须位于缓存目录下） |

**返回值**：`bool` — `true` 删除成功，`false` 文件不存在或不在缓存目录

> **注意**：只有缓存目录（`Resources/Cache/`）下的文件才能通过此 API 删除，防止误删用户自定义路径。

---

### AddAttachment

为指定骨骼挂载图片。

```csharp
bool success = client.AddAttachment(
    boneId: "head",
    attachmentId: "face",
    name: "Face",
    resourcePath: @"C:\path\to\face.png",
    offsetX: 0,
    offsetY: 0,
    pivotX: 0.5,
    pivotY: 0.5,
    zOrder: 0
);
```

**对应 action**：`AddAttachment`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 要绑定的骨骼 ID |
| attachmentId | string | 唯一 ID |
| name | string | 图片名称 |
| resourcePath | string | 图片文件路径（支持绝对路径和 pack URI） |
| offsetX | double | X 方向偏移（默认 0） |
| offsetY | double | Y 方向偏移（默认 0） |
| pivotX | double | 旋转锚点 X（0-1，默认 0.5 居中） |
| pivotY | double | 旋转锚点 Y（0-1，默认 0.5 居中） |
| zOrder | int | 渲染层级（默认 0） |

**返回值**：`bool` — `true` 添加成功，`false` 骨骼不存在

> **注意**：`pivotX` / `pivotY` 定义了图片的旋转中心（0=左上角，1=右下角）。默认 `0.5, 0.5` 为图片中心。

### AddAttachmentAsync（异步版本）

```csharp
bool success = await client.AddAttachmentAsync("head", "face", "Face", @"C:\path\to\face.png");
```

---

### RemoveAttachment

移除指定图片。

```csharp
bool success = client.RemoveAttachment("face");
```

**对应 action**：`RemoveAttachment`

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 图片 ID |

**返回值**：`bool` — `true` 移除成功，`false` 图片不存在

---

### SetAttachmentResource

切换图片的当前图片资源（支持运行时换装）。

```csharp
bool success = client.SetAttachmentResource("face", "happy", @"C:\path\to\face_happy.png");
```

**对应 action**：`SetAttachmentResource`

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 图片 ID |
| resourceId | string | 资源标识符 |
| resourcePath | string | 图片文件路径 |

**返回值**：`bool` — `true` 设置成功，`false` 图片不存在

---

### SetAttachmentOffset

调整图片的偏移位置。

```csharp
bool success = client.SetAttachmentOffset("face", 5, -10);
```

**对应 action**：`SetAttachmentOffset`

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 图片 ID |
| x | double | X 方向偏移（逻辑像素） |
| y | double | Y 方向偏移（逻辑像素） |

**返回值**：`bool` — `true` 设置成功，`false` 图片不存在

---

### SetAttachmentVisible

控制图片的显示/隐藏。

```csharp
client.SetAttachmentVisible("face", false); // 隐藏脸部
```

**对应 action**：`SetAttachmentVisible`

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 图片 ID |
| visible | bool | 是否显示 |

**返回值**：`bool` — `true` 设置成功，`false` 图片不存在

---

### GetAttachment

获取指定图片的详细信息。

```csharp
// 需通过原始 JSON 请求，返回完整图片属性对象
```

**对应 action**：`GetAttachment`

**请求示例**：
```json
{ "service": "skeleton", "action": "GetAttachment", "params": { "attachmentId": "face" } }
```

**成功响应**：
```json
{
  "success": true,
  "data": {
    "id": "face",
    "boneId": "head",
    "name": "Face",
    "resourcePath": "C:\\path\\to\\face.png",
    "offsetX": 0,
    "offsetY": 0,
    "pivotX": 0.5,
    "pivotY": 0.5,
    "zOrder": 0,
    "visible": true
  }
}
```

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 图片 ID |

**返回值**：`object` — 包含所有图片属性的 JSON 对象，图片不存在返回 `null`

---

### GetBoneAttachments

获取指定骨骼上所有图片的 ID 列表。

```csharp
var attachmentIds = client.GetBoneAttachments("head");
// 返回 ["face", "hair"]
```

**对应 action**：`GetBoneAttachments`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`string[]` — 图片 ID 数组

---

## Action 列表

> 以下 action 均属于 `skeleton` 服务，请求时需设置 `"service": "skeleton"`。

| action | 参数 | 返回类型 | 说明 |
|--------|------|----------|------|
| `GetBoneIds` | 无 | `string[]` | 获取所有骨骼 ID |
| `BoneExists` | `boneId` | `bool` | 检查骨骼是否存在 |
| `GetBoneName` | `boneId` | `string` | 获取骨骼名称 |
| `GetParentBoneId` | `boneId` | `string` | 获取父骨骼 ID |
| `GetChildBoneIds` | `boneId` | `string[]` | 获取子骨骼 ID 列表 |
| `SetPosition` | `boneId`, `x`, `y` | `bool` | 设置本地位置 |
| `GetPosition` | `boneId` | `{x, y}` | 获取本地位置 |
| `Translate` | `boneId`, `deltaX`, `deltaY` | `bool` | 平移骨骼 |
| `SetRotation` | `boneId`, `degrees` | `bool` | 设置旋转角度（度） |
| `GetRotation` | `boneId` | `double` | 获取旋转角度（度） |
| `Rotate` | `boneId`, `deltaDegrees` | `bool` | 相对旋转 |
| `SetScale` | `boneId`, `scaleX`, `scaleY` | `bool` | 设置缩放 |
| `GetScale` | `boneId` | `{x, y}` | 获取缩放 |
| `SetActive` | `boneId`, `isActive` | `bool` | 设置激活状态 |
| `IsActive` | `boneId` | `bool` | 获取激活状态 |
| `ResetBone` | `boneId` | `bool` | 恢复单个骨骼到默认值 |
| `ResetAll` | 无 | - | 恢复所有骨骼到默认值 |
| `Ping` | 无 | `bool` | 心跳检测，检查服务是否存活 |
| `Batch` | `operations` (数组) | `bool` | 批量操作 |
| `GetWorldPosition` | `boneId` | `{x, y}` | 获取世界坐标 |
| `AddAttachment` | `boneId`, `attachmentId`, `name`, `resourcePath`, `offsetX`, `offsetY`, `pivotX`, `pivotY`, `zOrder` | `bool` | 为骨骼挂载图片 |
| `UploadResource` | `base64Data`, `boneId`（可选） | `{path}` | 上传图片到资源缓存 |
| `DeleteResource` | `resourcePath` | `bool` | 删除缓存目录下的资源文件 |
| `SetDebugSkeleton` | `show` | - | 开关骨骼调试线框 |
| `GetDebugSkeleton` | 无 | `bool` | 获取调试线框状态 |
| `RemoveAttachment` | `attachmentId` | `bool` | 移除图片 |
| `SetAttachmentResource` | `attachmentId`, `resourceId`, `resourcePath` | `bool` | 切换图片资源 |
| `SetAttachmentOffset` | `attachmentId`, `x`, `y` | `bool` | 设置图片偏移 |
| `SetAttachmentVisible` | `attachmentId`, `visible` | `bool` | 设置图片显隐 |
| `SetAttachmentScale` | `attachmentId`, `scaleX`, `scaleY` | `bool` | 设置图片 X/Y 缩放 |
| `GetAttachmentScale` | `attachmentId` | `{scaleX, scaleY}` | 获取图片缩放值 |
| `GetAttachment` | `attachmentId` | `object` | 获取图片详情 |
| `GetBoneAttachments` | `boneId` | `string[]` | 获取骨骼上的图片 ID 列表 |

---

### SetAttachmentScale

设置图片的 X/Y 轴缩放比例（默认为 1.0，即原始大小）。

```csharp
// 将头部图片横向拉伸 1.5 倍，纵向缩小到 0.8 倍
client.SetAttachmentScale("face", 1.5, 0.8);
```

**对应 action**：`SetAttachmentScale`

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 附件 ID |
| scaleX | double | X 轴缩放比例（1.0 = 原始大小） |
| scaleY | double | Y 轴缩放比例（1.0 = 原始大小） |

**返回值**：`bool` — `true` 成功，`false` 附件不存在

---

### GetAttachmentScale

获取图片当前的 X/Y 轴缩放值。

```csharp
var scale = client.GetAttachmentScale("face");
if (scale.HasValue)
    Console.WriteLine($"ScaleX: {scale.Value.ScaleX}, ScaleY: {scale.Value.ScaleY}");
```

**对应 action**：`GetAttachmentScale`

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 附件 ID |

**返回值**：`(double ScaleX, double ScaleY)?` — 缩放值，附件不存在返回 `null`

---

### GetAttachment

获取附件的完整详情。

```csharp
var detail = client.GetAttachment("face");
```

**对应 action**：`GetAttachment`

| 参数 | 类型 | 说明 |
|------|------|------|
| attachmentId | string | 附件 ID |

**返回值**：`JsonElement?` — 包含 id、boneId、name、resourcePath、offset、pivot、zOrder、visible、scaleX、scaleY

---

### GetBoneAttachments

获取指定骨骼上的所有附件 ID 列表。

```csharp
var ids = client.GetBoneAttachments("head");
// 返回 ["face", "hair"]
```

**对应 action**：`GetBoneAttachments`

| 参数 | 类型 | 说明 |
|------|------|------|
| boneId | string | 骨骼 ID |

**返回值**：`string[]` — 附件 ID 数组

---

## 骨骼 ID 列表

当前默认角色骨骼结构：

| 骨骼 ID | 名称 | 父骨骼 | 说明 |
|---------|------|--------|------|
| root | Root | 无 | 根骨骼，位于画布中心 |
| body | Body | root | 身体主干 |
| neck | Neck | body | 颈部 |
| head | Head | neck | 头部 |
| arm_left_upper | LeftArmUpper | body | 左上臂 |
| arm_left_lower | LeftArmLower | arm_left_upper | 左小臂 |
| arm_right_upper | RightArmUpper | body | 右上臂 |
| arm_right_lower | RightArmLower | arm_right_upper | 右小臂 |
| hip | Hip | body | 臀部 |
| leg_left_upper | LeftLegUpper | hip | 左大腿 |
| leg_left_lower | LeftLegLower | leg_left_upper | 左小腿 |
| leg_right_upper | RightLegUpper | hip | 右大腿 |
| leg_right_lower | RightLegLower | leg_right_upper | 右小腿 |

### 骨骼层次结构

```
root
└── body
    ├── neck
    │   └── head
    ├── arm_left_upper
    │   └── arm_left_lower
    ├── arm_right_upper
    │   └── arm_right_lower
    └── hip
        ├── leg_left_upper
        │   └── leg_left_lower
        └── leg_right_upper
            └── leg_right_lower
```

---

## 调试控制

### SetDebugSkeleton

开关骨骼调试线框（蓝色线条 + 关节圆点）。调试线框默认关闭，开启后可在图片挂载的同时可视化骨骼结构，方便开发调试。

```csharp
// 开启调试线框
client.SetDebugSkeleton(true);

// 关闭调试线框
client.SetDebugSkeleton(false);
```

**对应 action**：`SetDebugSkeleton`

| 参数 | 类型 | 说明 |
|------|------|------|
| show | bool | `true` 显示调试线框，`false` 隐藏 |

---

### GetDebugSkeleton

获取调试线框的当前状态。

```csharp
bool visible = client.GetDebugSkeleton();
```

**对应 action**：`GetDebugSkeleton`

**返回值**：`bool` — 当前是否显示调试线框

---

### 示例 1：挥手动作

```csharp
using var client = new SkeletonPipeClient();

// 将右手举起（大臂上抬90度，小臂略弯）
client.Batch(b =>
{
    b.SetRotation("arm_right_upper", -90);
    b.SetRotation("arm_right_lower", -30);
});
```

### 示例 2：点头动作

```csharp
using var client = new SkeletonPipeClient();

// 头部向下点头
client.SetRotation("neck", 15);

// 稍后复位
Thread.Sleep(200);
client.SetRotation("neck", 0);
```

### 示例 3：行走姿势

```csharp
using var client = new SkeletonPipeClient();

client.Batch(b =>
{
    b.SetRotation("leg_left_upper", 20);    // 左腿向前
    b.SetRotation("leg_left_lower", -10);
    b.SetRotation("leg_right_upper", -20);  // 右腿向后
    b.SetRotation("leg_right_lower", 10);
    b.SetRotation("arm_left_upper", -15);   // 右臂摆动
    b.SetRotation("arm_right_upper", 15);
});
```

### 示例 4：绑定角色图片

```csharp
using var client = new SkeletonPipeClient();

// 批量绑定身体各部件的图片
client.Batch(b =>
{
    b.AddAttachment("body", "body_img", "Body", @"C:\char\body.png", zOrder: 0);
    b.AddAttachment("head", "head_img", "Head", @"C:\char\head.png", zOrder: 1);
    b.AddAttachment("arm_left_upper", "lua_img", "LeftUpperArm", @"C:\char\arm_upper.png", zOrder: 1);
    b.AddAttachment("arm_left_lower", "lla_img", "LeftLowerArm", @"C:\char\arm_lower.png", zOrder: 2);
    b.AddAttachment("arm_right_upper", "rua_img", "RightUpperArm", @"C:\char\arm_upper.png", zOrder: 1);
    b.AddAttachment("arm_right_lower", "rla_img", "RightLowerArm", @"C:\char\arm_lower.png", zOrder: 2);
});

// 旋转手臂 — 图片会自动跟随骨骼
client.SetRotation("arm_left_upper", 45);
```

---

## 注意事项

1. **连接方式**：每次 API 调用会建立一个新的管道连接，调用完成后自动断开。客户端实例可长期复用。
2. **坐标单位**：位置参数使用逻辑像素（WPF 设备无关像素）。
3. **角度单位**：所有对外 API 使用角度制（度），KfuPet 内部自动转换为弧度。
4. **旋转方向**：正值为顺时针旋转，负值为逆时针旋转。
5. **父子关系**：修改父骨骼的变换会自动传递给所有子骨骼。
6. **超时设置**：默认连接超时 5 秒，可通过构造函数 `SkeletonPipeClient(pipeName, timeoutMs)` 调整。
7. **线程安全**：`SkeletonPipeClient` 不是线程安全的，多线程并发调用请使用独立实例或加锁。
8. **性能优化**：同时修改多个骨骼时，请使用 `Batch` 方法减少 IPC 通信次数和渲染刷新次数。
