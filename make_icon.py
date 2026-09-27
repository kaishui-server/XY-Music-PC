# 从 d:\GITHUB项目\logo.png 生成 XY-Music-PC 全套应用图标
# 规格与旧图标集保持一致(实测旧图):
#   - icon.ico / icon_preview.png / Music.png: 白底 + 22% 圆角(四角透明)
#   - Square44x44 / Square150x150 / Tile / StoreLogo / LockScreen: 全幅白底, 内容边距 = 10% 边长
#   - Wide310x150 / SplashScreen: 全幅白底, 内容 = 40% 高度, 居中
from PIL import Image, ImageDraw
import numpy as np
import os

SRC = r"D:\GITHUB项目\logo.png"
ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Assets")

RADIUS_RATIO = 0.22  # 旧 icon.ico 实测圆角比例(拟合 0.2198), 保持不变
ICO_SIZES = [256, 128, 64, 48, 32, 16]

SQUARE_FILES = [
    "Square44x44Logo.scale-100.png", "Square44x44Logo.scale-125.png",
    "Square44x44Logo.scale-150.png", "Square44x44Logo.scale-200.png",
    "Square44x44Logo.scale-400.png",
    "Square44x44Logo.targetsize-16.png", "Square44x44Logo.targetsize-24.png",
    "Square44x44Logo.targetsize-24_altform-unplated.png",
    "Square44x44Logo.targetsize-32.png", "Square44x44Logo.targetsize-48.png",
    "Square44x44Logo.targetsize-256.png",
    "Square44x44Logo.altform-unplated_targetsize-16.png",
    # 24 尺寸只有 targetsize-24_altform-unplated.png 这一种命名; 两种顺序并存会
    # 解析出相同的限定符集合 {targetsize-24, altform-unplated}, makepri 报 PRI277 冲突
    "Square44x44Logo.altform-unplated_targetsize-32.png",
    "Square44x44Logo.altform-unplated_targetsize-48.png",
    "Square44x44Logo.altform-unplated_targetsize-256.png",
    "Square44x44Logo.altform-lightunplated_targetsize-16.png",
    "Square44x44Logo.altform-lightunplated_targetsize-24.png",
    "Square44x44Logo.altform-lightunplated_targetsize-32.png",
    "Square44x44Logo.altform-lightunplated_targetsize-48.png",
    "Square44x44Logo.altform-lightunplated_targetsize-256.png",
    "Square150x150Logo.scale-100.png", "Square150x150Logo.scale-125.png",
    "Square150x150Logo.scale-150.png", "Square150x150Logo.scale-200.png",
    "Square150x150Logo.scale-400.png",
    "SmallTile.scale-100.png", "SmallTile.scale-125.png",
    "SmallTile.scale-150.png", "SmallTile.scale-200.png",
    "SmallTile.scale-400.png",
    "LargeTile.scale-100.png", "LargeTile.scale-125.png",
    "LargeTile.scale-150.png", "LargeTile.scale-200.png",
    "LargeTile.scale-400.png",
    "StoreLogo.scale-100.png", "StoreLogo.scale-125.png",
    "StoreLogo.scale-150.png", "StoreLogo.scale-200.png",
    "StoreLogo.scale-400.png",
    "LockScreenLogo.scale-200.png",
]

WIDE_FILES = [
    "Wide310x150Logo.scale-100.png", "Wide310x150Logo.scale-125.png",
    "Wide310x150Logo.scale-150.png", "Wide310x150Logo.scale-200.png",
    "Wide310x150Logo.scale-400.png",
    "SplashScreen.scale-100.png", "SplashScreen.scale-125.png",
    "SplashScreen.scale-150.png", "SplashScreen.scale-200.png",
    "SplashScreen.scale-400.png",
]

ROUNDED_FILES = ["icon_preview.png", "Music.png"]


def load_content():
    logo = Image.open(SRC).convert("RGB")
    g = np.array(logo.convert("L"))
    mask = g < 240  # 非白色即内容(含抗锯齿边缘)
    ys, xs = np.nonzero(mask)
    box = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    print(f"logo 内容边界: {box} ({box[2]-box[0]}x{box[3]-box[1]})")
    return logo.crop(box)


CONTENT = load_content()
_resized = {}


def art(size):
    if size not in _resized:
        _resized[size] = CONTENT.resize((size, size), Image.LANCZOS)
    return _resized[size]


def flat_square(side):
    m = round(side * 0.1)
    c = side - 2 * m
    canvas = Image.new("RGBA", (side, side), (255, 255, 255, 255))
    canvas.paste(art(c), (m, m))
    return canvas


def flat_wide(w, h):
    c = round(h * 0.4)
    canvas = Image.new("RGBA", (w, h), (255, 255, 255, 255))
    canvas.paste(art(c), ((w - c) // 2, (h - c) // 2))
    return canvas


def rounded(side):
    base = flat_square(side).convert("RGB")
    mask = Image.new("L", (side * 4, side * 4), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle([0, 0, side * 4 - 1, side * 4 - 1],
                        radius=int(side * 4 * RADIUS_RATIO), fill=255)
    mask = mask.resize((side, side), Image.LANCZOS)
    img = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    img.paste(base, (0, 0), mask)
    return img


def main():
    for name in SQUARE_FILES:
        side = Image.open(os.path.join(ASSETS, name)).size[0]
        flat_square(side).save(os.path.join(ASSETS, name))

    for name in WIDE_FILES:
        w, h = Image.open(os.path.join(ASSETS, name)).size
        flat_wide(w, h).save(os.path.join(ASSETS, name))

    for name in ROUNDED_FILES:
        side = Image.open(os.path.join(ASSETS, name)).size[0]
        rounded(side).save(os.path.join(ASSETS, name))

    frames = [rounded(s) for s in ICO_SIZES]
    frames[0].save(os.path.join(ASSETS, "icon.ico"), format="ICO",
                   sizes=[(s, s) for s in ICO_SIZES], append_images=frames[1:])
    print(f"完成: {len(SQUARE_FILES)} 方形 + {len(WIDE_FILES)} 宽幅 + "
          f"{len(ROUNDED_FILES)} 圆角 + icon.ico({ICO_SIZES})")


if __name__ == "__main__":
    main()
