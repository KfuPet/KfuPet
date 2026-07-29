# Skeleton IPC API 文档

> 通过 Named Pipe（命名管道）跨进程调用 KfuPet 骨骼系统。
> 适用于开发者工具、管理器等独立外部程序。

## 架构概览

```
                    KfuPet.exe 
                         │ 
             ┌───────────┴───────────┐ 
             │                       │ 
             ▼                       ▼ 
        NamedPipeServer         HttpServer（后期）
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

## 完整示例

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
