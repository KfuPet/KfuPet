# 角色图片资源渲染优化备忘（临时）

> 本文件为临时记录，聚焦后续添加图片模型资源时最值得优化的一个点。

## 1. 图片格式受限

- 现状：`SkeletonRenderer` 使用 WPF 原生 `BitmapImage`，仅支持 **PNG / JPG / BMP / GIF / ICO / TIFF**。
- 问题：
  - 不支持 **WebP**、**SVG**。
  - `SkeletonService.UploadResource` 虽然把 `webp` 映射为 `.webp` 文件，但 `BitmapImage` 无法解码，会走 catch 静默返回 null，图片不显示。
  - **GIF 只显示第一帧，不播放动画**。
- 建议：
  - 角色资源统一使用 **PNG（带透明通道）**。
  - 后续如需 WebP / 动图，引入 SkiaSharp（AGENTS.md 允许的第三方库）。
  - GIF 动画可改用原生 `GifBitmapDecoder` 逐帧解码 + 定时刷新播放（`BitmapImage` 只取首帧，需替换为解码器方案）；若与 WebP 统一处理，也可一并走 SkiaSharp。


## 涉及文件

- `Core/Rendering/SkeletonRenderer.cs`（`LoadImageForAttachment`、`_imageCache`）
- `Services/SkeletonService.cs`（`UploadResource`）
