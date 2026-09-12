# /// script
# requires-python = ">=3.10"
# dependencies = ["resvg-py>=0.5", "pillow>=10"]
# ///
# 多尺寸 app.ico 生成管线：SVG 逐尺寸光栅化（resvg）→ 打包 ICO。
#
# 尺寸阶梯（与设计稿 preview.html 一致）：
#   16 / 24 px        → compact 简化版（去窗口外框、加粗日志行，小尺寸不糊）
#   32..256 px        → 主图标（dark 版，自带深色底板，浅色/深色背景都成立）
# ico 结构：≤128 用 32bpp BMP 帧（BITMAPINFOHEADER + XOR(BGRA 自底向上) + AND 掩码），
#          256 用 PNG 帧（Vista+ 标准）。与系统自带图标的经典结构一致，兼容性最好。
#
# 用法:
#   uv run tests/make_ico.py --dark <主图标.svg> --compact <简化版.svg> \
#       --out PCActivityLog/Assets/app.ico --workdir <渲染中间产物目录>
from __future__ import annotations

import argparse
import io
import struct
from pathlib import Path

import resvg_py
from PIL import Image

SIZES = [16, 24, 32, 48, 64, 128, 256]


def render_png(svg_text: str, size: int) -> bytes:
    """SVG → PNG bytes（resvg 直接按目标尺寸矢量渲染，不经过二次缩放）。"""
    png = bytes(resvg_py.svg_to_bytes(svg_string=svg_text, width=size, height=size))
    if not png.startswith(b"\x89PNG"):
        raise RuntimeError(f"resvg 输出不是 PNG（{size}px）")
    im = Image.open(io.BytesIO(png))
    if im.size != (size, size):
        raise RuntimeError(f"渲染尺寸不符: {im.size} != {size}")
    return png


def png_to_ico_bmp_frame(png: bytes) -> bytes:
    """PNG → ICO 的 BMP 帧：BITMAPINFOHEADER(高度×2) + XOR(BGRA 自底向上) + AND 掩码。"""
    im = Image.open(io.BytesIO(png)).convert("RGBA")
    w, h = im.size
    bgra_topdown = im.tobytes("raw", "BGRA")
    row = w * 4
    xor = b"".join(
        bgra_topdown[(h - 1 - y) * row : (h - y) * row] for y in range(h)  # 自底向上
    )
    and_row = (w + 7) // 8
    and_row += (4 - and_row % 4) % 4  # 每行 4 字节对齐
    and_mask = bytes(and_row * h)  # 全 0：不透明度完全由 alpha 通道表达
    header = struct.pack(
        "<IiiHHIIiiII",
        40,          # biSize
        w,           # biWidth
        h * 2,       # biHeight（XOR + AND）
        1,           # biPlanes
        32,          # biBitCount
        0,           # biCompression = BI_RGB
        w * row + len(and_mask),  # biSizeImage
        0, 0, 0, 0,  # 像素密度、色数、重要色
    )
    return header + xor + and_mask


def build_ico(frames: dict[int, bytes]) -> bytes:
    out = io.BytesIO()
    out.write(struct.pack("<HHH", 0, 1, len(frames)))  # reserved / type=icon / 帧数
    offset = 6 + 16 * len(frames)
    for s in SIZES:
        data = frames[s]
        dim = 0 if s >= 256 else s  # 256 在目录项里写 0
        out.write(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    for s in SIZES:
        out.write(frames[s])
    return out.getvalue()


def main() -> None:
    ap = argparse.ArgumentParser(description="生成多尺寸 app.ico")
    ap.add_argument("--dark", required=True, help="主图标 SVG（32px 及以上）")
    ap.add_argument("--compact", required=True, help="小尺寸简化版 SVG（16/24px）")
    ap.add_argument("--out", required=True, help="输出 .ico 路径")
    ap.add_argument("--workdir", default=None, help="渲染 PNG 中间产物目录（便于肉眼检查）")
    args = ap.parse_args()

    workdir = Path(args.workdir) if args.workdir else Path(__file__).parent / "icon_frames"
    workdir.mkdir(parents=True, exist_ok=True)

    dark = Path(args.dark).read_text(encoding="utf-8")
    compact = Path(args.compact).read_text(encoding="utf-8")

    frames: dict[int, bytes] = {}
    for s in SIZES:
        svg = compact if s <= 24 else dark
        name = "compact" if s <= 24 else "dark"
        png = render_png(svg, s)
        (workdir / f"{name}_{s}.png").write_bytes(png)
        frames[s] = png if s >= 256 else png_to_ico_bmp_frame(png)
        print(f"渲染 {name} {s}px OK")

    ico = build_ico(frames)
    Path(args.out).write_bytes(ico)

    # 回读校验：帧数与各帧尺寸
    with Image.open(io.BytesIO(ico)) as im:
        sizes_in_ico = set(im.info.get("sizes") or [])
    print(f"OK: {args.out}（{len(sizes_in_ico)} 帧，{len(ico) // 1024} KB）帧尺寸: {sorted(sizes_in_ico, reverse=True)}")
    if sizes_in_ico != {(s, s) for s in SIZES}:
        raise RuntimeError("ico 帧校验失败")


if __name__ == "__main__":
    main()
