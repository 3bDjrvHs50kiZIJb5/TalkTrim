# 字幕字体（抖音美好体）

本目录用于 ffmpeg 字幕压制（`ass=...:fontsdir=...`），字体来自字节跳动官方开源仓库。

## 文件

| 文件 | 说明 |
|------|------|
| `DouyinSansBold.ttf` | 抖音美好体 Bold（单字重） |
| `OFL.txt` | SIL Open Font License 1.1 全文 |

## 来源

- 仓库：<https://github.com/bytedance/fonts/tree/main/DouyinSans>
- 授权：OFL 1.1，个人与企业可免费商用（禁止单独出售字体文件）

## ASS 字体名

生成 ASS 时 `Fontname` 请使用字体内部名称：

```
抖音美好体
```

## ffmpeg 示例

```bash
ffmpeg -i input.mp4 \
  -vf "ass=subtitle.ass:fontsdir=/path/to/TalkTrim/fonts" \
  -c:v libx264 -crf 18 -c:a copy output.mp4
```

应用运行时可将 `fontsdir` 指向项目输出目录下的 `fonts` 文件夹（构建时会复制到 `bin/.../fonts/`）。
