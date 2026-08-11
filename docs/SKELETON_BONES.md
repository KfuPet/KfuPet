# KfuPet 骨骼清单

本文档记录当前默认角色实际使用的骨骼，内容以 `MainWindow.InitializeSkeleton` 中注册的骨骼为准。

当前共使用 **12 根骨骼**。已经删除的 `hip` 不属于当前骨架。

## 图片命名规则

- 建议直接使用骨骼 ID 作为图片文件名，例如 `head.png`。
- 骨骼 ID 使用小写字母和下划线，不要改写或缩写。
- `root` 是整副骨架的根锚点，通常不需要挂载图片；需要时可使用 `root.png`。
- API 不强制图片文件名，以下名称是用于保持资源结构一致的项目约定。

## 骨骼列表

| 序号 | 骨骼 ID | 显示名称 | 父骨骼 | 部位 | 建议图片名 |
|------|---------|----------|--------|------|------------|
| 1 | `root` | Root | 无 | 整副骨架的根锚点 | `root.png`（可选） |
| 2 | `body` | Body | `root` | 身体主干 | `body.png` |
| 3 | `neck` | Neck | `body` | 颈部 | `neck.png` |
| 4 | `head` | Head | `neck` | 头部 | `head.png` |
| 5 | `arm_left_upper` | LeftArmUpper | `body` | 左上臂 | `arm_left_upper.png` |
| 6 | `arm_left_lower` | LeftArmLower | `arm_left_upper` | 左小臂 | `arm_left_lower.png` |
| 7 | `arm_right_upper` | RightArmUpper | `body` | 右上臂 | `arm_right_upper.png` |
| 8 | `arm_right_lower` | RightArmLower | `arm_right_upper` | 右小臂 | `arm_right_lower.png` |
| 9 | `leg_left_upper` | LeftLegUpper | `root` | 左大腿 | `leg_left_upper.png` |
| 10 | `leg_left_lower` | LeftLegLower | `leg_left_upper` | 左小腿 | `leg_left_lower.png` |
| 11 | `leg_right_upper` | RightLegUpper | `root` | 右大腿 | `leg_right_upper.png` |
| 12 | `leg_right_lower` | RightLegLower | `leg_right_upper` | 右小腿 | `leg_right_lower.png` |

## 骨骼层级

```text
root
├── body
│   ├── neck
│   │   └── head
│   ├── arm_left_upper
│   │   └── arm_left_lower
│   └── arm_right_upper
│       └── arm_right_lower
├── leg_left_upper
│   └── leg_left_lower
└── leg_right_upper
    └── leg_right_lower
```

## 图片文件清单

| 文件名 | 对应部位 |
|--------|----------|
| `body.png` | 身体主干 |
| `neck.png` | 颈部 |
| `head.png` | 头部 |
| `arm_left_upper.png` | 左上臂 |
| `arm_left_lower.png` | 左小臂 |
| `arm_right_upper.png` | 右上臂 |
| `arm_right_lower.png` | 右小臂 |
| `leg_left_upper.png` | 左大腿 |
| `leg_left_lower.png` | 左小腿 |
| `leg_right_upper.png` | 右大腿 |
| `leg_right_lower.png` | 右小腿 |

`root.png` 不在必需图片清单中，仅在需要给根锚点挂载图片时使用。

> 更新于 8/11 