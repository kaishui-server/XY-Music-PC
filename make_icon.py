# 从 XYmusic logo 生成大圆角多尺寸应用图标
# 圆角比例 25%(比 iOS 22.5% 稍大), 四角透明
from PIL import Image, ImageDraw, ImageOps

SRC = r"D:\Github\新logo.jpg"
OUT_ICO = r"d:\Github\original-sound-hq-player\Assets\icon.ico"
OUT_PNG = r"d:\Github\original-sound-hq-player\Assets\icon_preview.png"

RADIUS_RATIO = 0.25  # 圆角半径 = 边长 25%, 稍大圆角
SIZES = [256, 128, 64, 48, 32, 16]

def make_rounded(size: int) -> Image.Image:
    # 先做高质量缩放
    base = Image.open(SRC).convert("RGB")
    side = min(base.size)
    base = ImageOps.fit(base, (side, side), Image.LANCZOS, centering=(0.5, 0.5))
    base = base.resize((size, size), Image.LANCZOS)

    # 大圆角遮罩(抗锯齿: 4x 超采样)
    mask = Image.new("L", (size * 4, size * 4), 0)
    d = ImageDraw.Draw(mask)
    r = int(size * 4 * RADIUS_RATIO)
    d.rounded_rectangle([0, 0, size * 4 - 1, size * 4 - 1], radius=r, fill=255)
    mask = mask.resize((size, size), Image.LANCZOS)

    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    img.paste(base, (0, 0), mask)
    return img

imgs = [make_rounded(s) for s in SIZES]

# 16px 圆角会糊成一团, 用完整方形轻微圆角保辨识度
imgs[-1] = make_rounded(16)

imgs[0].save(OUT_ICO, format="ICO", sizes=[(s, s) for s in SIZES], append_images=imgs[1:])
imgs[0].save(OUT_PNG)

# 验证
ico = Image.open(OUT_ICO)
print("ICO sizes:", ico.info.get("sizes"))
print("OK ->", OUT_ICO)
