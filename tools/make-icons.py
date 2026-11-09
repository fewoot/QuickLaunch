# -*- coding: utf-8 -*-
"""
QuickLaunch 图标生成器 v2

设计语言取自界面本身：深灰蓝底 (#2E3440 / #3B4252) + 强调蓝 (#4F7CFF) + 闪电符号。

v2 修正：
  - 闪电加粗（16px 下才认得出）
  - 高光改成平滑渐变（v1 有一条硬邦邦的横向色带）
  - 九宫格改成 2x2 大格子（16px 不糊）
  - 火箭重画比例
"""
import os
import math
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "icons"))
os.makedirs(OUT, exist_ok=True)

S = 1024
PX = S / 256.0

BLUE      = (79, 124, 255)
BLUE_LT   = (124, 162, 255)
BLUE_DARK = (38, 78, 200)
BG_DARK   = (38, 44, 56)
BG_MID    = (58, 66, 84)
WHITE     = (255, 255, 255)


def u(v):
    return v * PX


def rounded_mask(radius, box):
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius=radius, fill=255)
    return m


def vgrad(top, bottom):
    grad = Image.new("RGB", (1, S))
    px = grad.load()
    for y in range(S):
        t = y / (S - 1)
        px[0, y] = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
    return grad.resize((S, S), Image.BILINEAR)


def badge(top, bottom, radius=58, inset=3, rim=None, shadow=True):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    box = (u(inset), u(inset), S - u(inset), S - u(inset))
    r = u(radius)

    if shadow:
        sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle(
            [box[0], box[1] + u(5), box[2], box[3] + u(5)], radius=r, fill=(0, 0, 0, 96))
        img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(u(9))))

    mask = rounded_mask(r, box)
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    layer.paste(vgrad(top, bottom).convert("RGBA"), (0, 0), mask)
    img.alpha_composite(layer)

    # 平滑高光：上部 60% 从 18% 白渐变到透明（不再是硬色带）
    hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hg = Image.new("RGB", (1, S))
    hp = hg.load()
    for y in range(S):
        a = int(max(0.0, 1.0 - y / (S * 0.62)) ** 1.6 * 46)
        hp[0, y] = (255, 255, 255)
    hl = Image.merge("RGBA", (hg.split()[0], hg.split()[1], hg.split()[2],
                              hg.convert("L").point(lambda v: 0)))
    # 直接用 alpha 通道的渐变
    alpha = Image.new("L", (1, S))
    ap = alpha.load()
    for y in range(S):
        ap[0, y] = int(max(0.0, 1.0 - y / (S * 0.62)) ** 1.6 * 46)
    hl = Image.merge("RGBA", (
        Image.new("L", (S, S), 255),
        Image.new("L", (S, S), 255),
        Image.new("L", (S, S), 255),
        alpha.resize((S, S), Image.BILINEAR)))
    m2 = Image.new("L", (S, S), 0)
    m2.paste(hl.split()[3], (0, 0), mask)
    hl.putalpha(m2)
    img.alpha_composite(hl)

    d = ImageDraw.Draw(img)
    if rim:
        d.rounded_rectangle(box, radius=r, outline=rim, width=max(1, int(u(1.8))))
    else:
        d.rounded_rectangle(box, radius=r, outline=(255, 255, 255, 30), width=max(1, int(u(1.6))))
    return img


def bolt(d, cx, cy, h, color, w_ratio=0.72, rot=0.0):
    """加粗闪电。h=高度(px)"""
    pts = [(0.66, 0.00), (0.15, 0.57), (0.45, 0.57),
           (0.34, 1.00), (0.85, 0.43), (0.55, 0.43)]
    w = h * w_ratio
    ca, sa = math.cos(rot), math.sin(rot)
    out = []
    for x, y in pts:
        nx, ny = (x - 0.5) * w, (y - 0.5) * h
        out.append((cx + nx * ca - ny * sa, cy + nx * sa + ny * ca))
    d.polygon(out, fill=color)


# ------------------------------------------------------------------ 1
def concept1():
    """蓝底白闪电（延续界面 logo）"""
    img = badge(BLUE_LT, BLUE_DARK)
    d = ImageDraw.Draw(img)
    bolt(d, S / 2, S / 2, u(160), WHITE + (255,))
    return img


# ------------------------------------------------------------------ 2
def concept2():
    """深底 + 亮蓝闪电，外圈细白边（暗色任务栏上不糊成一团）"""
    img = badge((66, 74, 94), (30, 35, 46), rim=(255, 255, 255, 54))
    d = ImageDraw.Draw(img)
    bolt(d, S / 2, S / 2, u(162), (122, 160, 255, 255))
    return img


# ------------------------------------------------------------------ 3
def concept3():
    """2x2 大格子 + 闪电（多程序一键启动，16px 也认得出）"""
    img = badge(BLUE_LT, BLUE_DARK)
    d = ImageDraw.Draw(img)

    gap, tile = u(15), u(62)
    x0 = S / 2 - (tile * 2 + gap) / 2
    y0 = S / 2 - (tile * 2 + gap) / 2
    for i, j in [(0, 0), (1, 0), (0, 1)]:          # 右下角留给闪电
        x, y = x0 + j * (tile + gap), y0 + i * (tile + gap)
        d.rounded_rectangle([x, y, x + tile, y + tile], radius=u(14),
                            fill=(255, 255, 255, 205),
                            outline=(255, 255, 255, 90), width=max(1, int(u(1.2))))
    # 闪电严格落在右下格内，不越界
    cx = x0 + (tile + gap) + tile / 2
    cy = y0 + (tile + gap) + tile / 2
    bolt(d, cx, cy, tile * 1.02, WHITE + (255,), w_ratio=0.70)
    return img


# ------------------------------------------------------------------ 4
def concept4():
    """深底 + 蓝色圆盘 + 白闪电（圆盘在任务栏上一眼认出）"""
    img = badge(BG_MID, BG_DARK)
    d = ImageDraw.Draw(img)
    cx = cy = S / 2
    r = u(86)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=BLUE + (255,))
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(255, 255, 255, 56), width=max(1, int(u(2))))
    bolt(d, cx, cy, r * 1.32, WHITE + (255,))
    return img


# ------------------------------------------------------------------ 5
def concept5():
    """三支箭头：一个接一个启动（小尺寸最清楚）"""
    img = badge(BG_MID, BG_DARK)
    d = ImageDraw.Draw(img)
    cx, cy = S / 2, S / 2
    tones = [(255, 255, 255, 66), (255, 255, 255, 132), BLUE + (255,)]
    for i, col in enumerate(tones):
        cxi = cx + (i - 1) * u(50)
        h, w = u(84), u(50)
        d.polygon([(cxi - w / 2, cy - h / 2), (cxi + w / 2, cy), (cxi - w / 2, cy + h / 2)], fill=col)
    return img


# ------------------------------------------------------------------ 6
def concept6():
    """火箭（重画比例：粗机身 + 尾翼 + 尾焰）"""
    img = badge(BLUE_LT, BLUE_DARK)
    d = ImageDraw.Draw(img)
    cx = S / 2
    top, bot = S / 2 - u(88), S / 2 + u(52)

    # 机身
    body = [(cx, top),
            (cx + u(30), top + u(58)),
            (cx + u(26), bot - u(10)),
            (cx - u(26), bot - u(10)),
            (cx - u(30), top + u(58))]
    d.polygon(body, fill=WHITE + (255,))
    # 尾翼
    d.polygon([(cx - u(26), bot - u(34)), (cx - u(48), bot + u(6)), (cx - u(24), bot - u(6))], fill=WHITE + (255,))
    d.polygon([(cx + u(26), bot - u(34)), (cx + u(48), bot + u(6)), (cx + u(24), bot - u(6))], fill=WHITE + (255,))
    # 舷窗
    r = u(15)
    d.ellipse([cx - r, top + u(46) - r + u(20), cx + r, top + u(46) + r + u(20)], fill=BLUE + (255,))
    # 尾焰
    fy = bot - u(6)
    d.polygon([(cx - u(15), fy), (cx, fy + u(46)), (cx + u(15), fy)], fill=(255, 206, 84, 255))
    d.polygon([(cx - u(8), fy), (cx, fy + u(28)), (cx + u(8), fy)], fill=(255, 240, 190, 255))
    return img


# ------------------------------------------------------------------ 7
def concept7():
    """闪电 + 两条粗"程序条"：一键把这一排都拉起来"""
    img = badge(BLUE_LT, BLUE_DARK)
    d = ImageDraw.Draw(img)
    bolt(d, S / 2, S / 2 - u(22), u(132), WHITE + (255,))
    bar_h = u(20)
    for i, (wd, a) in enumerate([(1.00, 255), (0.74, 150)]):
        y = S / 2 + u(50) + i * (bar_h + u(13))
        L = u(124) * wd
        d.rounded_rectangle([S / 2 - L / 2, y, S / 2 + L / 2, y + bar_h], radius=bar_h / 2,
                            fill=WHITE + (a,))
    return img


CONCEPTS = [
    ("蓝底闪电",   concept1, "延续界面左上角 logo，最统一"),
    ("深底蓝闪",   concept2, "最贴界面底色，暗色桌面上不刺眼"),
    ("四格闪电",   concept3, "强调“多个程序一起开”"),
    ("圆盘闪电",   concept4, "圆盘最醒目，任务栏一眼认出"),
    ("三箭头",     concept5, "强调“一个接一个”串行启动"),
    ("火箭",       concept6, "最直白的“启动”隐喻"),
    ("闪电+进度条", concept7, "强调“一键把这一排都启动”"),
]

SIZES = [256, 128, 64, 48, 32, 24, 16]


def main():
    made = []
    for idx, (name, fn, desc) in enumerate(CONCEPTS, start=1):
        big = fn()
        big.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "concept-%d.png" % idx))
        frames = [big.resize((s, s), Image.LANCZOS) for s in SIZES]
        frames[0].save(os.path.join(OUT, "concept-%d.ico" % idx), format="ICO",
                       sizes=[(s, s) for s in SIZES])
        made.append((idx, name, desc))
        print("  [%d] %-12s %s" % (idx, name, desc))

    pad = 22
    big_w = big_h = 256
    col_sizes = [96, 64, 48, 32, 16]
    row_h = big_h + 52
    sheet_w = pad * 2 + big_w + 34 + sum(cs + 30 for cs in col_sizes)
    sheet = Image.new("RGBA", (sheet_w, row_h * len(CONCEPTS) + pad * 2 + 52), (24, 27, 33, 255))
    sd = ImageDraw.Draw(sheet)

    from PIL import ImageFont
    try:
        f = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 17)
        fb = ImageFont.truetype("C:/Windows/Fonts/msyhbd.ttc", 21)
        fs = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 14)
    except Exception:
        f = fb = fs = ImageFont.load_default()

    sd.text((pad, 14), "QuickLaunch 图标方案 v2   右侧 = 96/64/48/32/16 px 实际观感",
            font=fb, fill=(235, 239, 247))

    y = pad + 52
    for idx, name, desc in made:
        img = Image.open(os.path.join(OUT, "concept-%d.png" % idx)).convert("RGBA")
        sheet.alpha_composite(img, (pad, y))
        sd.text((pad, y + big_h + 6), "%d. %s" % (idx, name), font=f, fill=(210, 220, 236))
        sd.text((pad, y + big_h + 28), desc, font=fs, fill=(120, 132, 152))

        x = pad + big_w + 34
        for cs in col_sizes:
            small = img.resize((cs, cs), Image.LANCZOS)
            sheet.alpha_composite(small, (x, y + (big_h - cs) // 2))
            sd.text((x, y + big_h + 20), "%d" % cs, font=fs, fill=(110, 120, 140))
            x += cs + 30
        y += row_h

    sheet.save(os.path.join(OUT, "contact-sheet.png"))
    print("\n总览: icons/contact-sheet.png")

    # ---- 任务栏模拟：深色 / 浅色 各一排 32px，帮助判断实际观感
    sim_h = 132
    sim = Image.new("RGBA", (sheet_w, sim_h), (24, 27, 33, 255))
    dm = ImageDraw.Draw(sim)
    # 深色条
    dm.rectangle([0, 0, sheet_w, sim_h // 2], fill=(31, 31, 31, 255))
    # 浅色条
    dm.rectangle([0, sim_h // 2, sheet_w, sim_h], fill=(243, 243, 243, 255))
    dm.text((pad, 6), "深色任务栏 32px", font=fs, fill=(150, 150, 150))
    dm.text((pad, sim_h // 2 + 6), "浅色任务栏 32px", font=fs, fill=(110, 110, 110))

    x = pad + 150
    for idx, name, desc in made:
        img = Image.open(os.path.join(OUT, "concept-%d.png" % idx)).convert("RGBA")
        s32 = img.resize((32, 32), Image.LANCZOS)
        sim.alpha_composite(s32, (x, 12))
        sim.alpha_composite(s32, (x, sim_h // 2 + 12))
        dm.text((x - 2, 12 + 34), str(idx), font=fs, fill=(120, 120, 120))
        x += 64
        if x > sheet_w - 40:
            break
    sim.save(os.path.join(OUT, "taskbar-sim.png"))
    print("任务栏模拟: icons/taskbar-sim.png")


if __name__ == "__main__":
    main()
