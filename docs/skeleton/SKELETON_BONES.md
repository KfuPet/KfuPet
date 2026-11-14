# KfuPet 骨骼清单

本文档记录当前默认角色实际使用的骨骼，内容以 `MainWindow.InitializeSkeleton` 中注册的骨骼为准。

当前共使用 **11 根骨骼**。已经删除的 `hip`、`neck` 不属于当前骨架。

## 图片命名规则

- 建议直接使用骨骼 ID 作为图片文件名，例如 `head.png`。
- 骨骼 ID 使用小写字母和下划线，不要改写或缩写。
- `root` 是整副骨架的根锚点，通常不需要挂载图片；需要时可使用 `root.png`。
- API 不强制图片文件名，以下名称是用于保持资源结构一致的项目约定。

## 骨骼列表

| 序号 | 骨骼 ID | 显示名称 | 父骨骼 | 部位 | 建议图片名 |
|------|---------|----------|--------|------|------------|
| 1 | `root` | Root | 无 | 整副骨架的根锚点 | `root.png`（可选） |
| 2 | `body` | Body | `root` | 身体主干，锚点位于颈部根部 | `body.png` |
| 3 | `head` | Head | `body` | 头部 | `head.png` |
| 4 | `arm_left_upper` | LeftArmUpper | `body` | 左上臂 | `arm_left_upper.png` |
| 5 | `arm_left_lower` | LeftArmLower | `arm_left_upper` | 左小臂 | `arm_left_lower.png` |
| 6 | `arm_right_upper` | RightArmUpper | `body` | 右上臂 | `arm_right_upper.png` |
| 7 | `arm_right_lower` | RightArmLower | `arm_right_upper` | 右小臂 | `arm_right_lower.png` |
| 8 | `leg_left_upper` | LeftLegUpper | `root` | 左大腿 | `leg_left_upper.png` |
| 9 | `leg_left_lower` | LeftLegLower | `leg_left_upper` | 左小腿 | `leg_left_lower.png` |
| 10 | `leg_right_upper` | RightLegUpper | `root` | 右大腿 | `leg_right_upper.png` |
| 11 | `leg_right_lower` | RightLegLower | `leg_right_upper` | 右小腿 | `leg_right_lower.png` |

## 骨骼坐标（关节位置）

骨骼原点就是画面上的**真实关节**：部位图的 `pivot` 落在骨原点上，`attachments.json` 里的 `offset` 为 `0`。
这样转动骨骼时部位绕关节旋转；若把骨骼放在别处、靠 `offset` 补偿，转动时整块会绕着那个偏移点甩动，接缝补不上。

| 骨骼 ID | 本地坐标 `LocalPosition` | 含义 |
|---------|--------------------------|------|
| `root` | (240, 300) | 画布 480×600 的中心，世界锚点 |
| `body` | (0, -145.24) | 颈根 |
| `head` | (2.36, 0.86) | 下巴 / 颈 |
| `arm_left_upper` | (-29.47, 24.54) | 画面左侧肩点 |
| `arm_left_lower` | (-67.94, 2.04) | 画面左侧肘 |
| `arm_right_upper` | (29.04, 25.29) | 画面右侧肩点 |
| `arm_right_lower` | (55.94, 1.29) | 画面右侧肘 |
| `leg_left_upper` | (-19.4, 10.36) | 画面左侧髋 |
| `leg_left_lower` | (-0.64, 60.01) | 画面左侧膝（落在袜口，见下注） |
| `leg_right_upper` | (19.4, 10.79) | 画面右侧髋 |
| `leg_right_lower` | (0.53, 60.44) | 画面右侧膝（落在袜口） |

> 坐标是相对**父骨骼**的偏移，取自「白摇摇」部位图的关节位置。换角色时按同样规则重算：先确定每张图 `pivot` 落在画布上的位置，再让对应骨骼的本地坐标等于该位置减父骨骼位置，`offset` 保持 `0`。
>
> 小腿的枢轴取**袜口**（源图 y≈1456），不是解剖学膝盖（y≈1539）。试过下移到解剖位置，结果袜口以上 123 px 的袜筒会绕新枢轴甩出约 17 DIP，反而在大腿处裂开；这套图的可见接缝就在袜口，枢轴必须落在接缝上。

## 骨骼层级

```text
root
├── body
│   ├── head
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

> 更新于 2026-11-14：骨骼位置改为真实关节位置，部位图 `offset` 归零。