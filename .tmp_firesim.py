"""临时：模拟「多根火舌 max 融合」的宽幅火焰 shader。"""
import numpy as np
from PIL import Image

W, H = 800, 620


def load_b(path):
    a = np.asarray(Image.open(path).convert('RGBA')).astype(np.float32) / 255.0
    return a[:, :, 2]


POINTY = load_b('Reed/images/vfx/fire/fire_base_pointy.png')
N1 = load_b('Reed/images/vfx/fire/triangle_noise_tile.png')
N2 = load_b('Reed/images/vfx/fire/basic_fire_noise.png')
BOTTOM = load_b('Reed/images/vfx/fire/fire_bottom_mask.png')


def sample(tex, u, v):
    h, w = tex.shape
    x = np.clip((u * (w - 1)).astype(np.int32), 0, w - 1)
    y = np.clip((v * (h - 1)).astype(np.int32), 0, h - 1)
    return tex[y, x]


def build(n_tongues, top, bottom, overlap, gain, mask_strength, t, fade=0.05, base_band=0.0,
          sway=0.01, seed=7):
    ys, xs = np.mgrid[0:H, 0:W]
    u = (xs + 0.5) / W
    v = (ys + 0.5) / H
    rng = np.random.default_rng(seed)
    tile_w = 1.0 / (n_tongues + (n_tongues - 1) * 0.0)
    tile_w = 1.0 / n_tongues

    shape = np.zeros((H, W), np.float32)
    for i in range(n_tongues):
        cx = (i + 0.5) / n_tongues + rng.uniform(-0.40, 0.40) * tile_w
        # 每根火舌的横向宽度略随机（±8%），避免整齐划一
        tw = tile_w * overlap * rng.uniform(0.94, 1.06)
        local = (u - cx) / tw + 0.5
        y0 = top + rng.uniform(-0.09, 0.09)
        y1 = bottom + rng.uniform(-0.02, 0.02)
        su = np.clip(local, 0, 1)
        sv = np.clip(y0 + v * (y1 - y0), 0, 1)
        s = sample(POINTY, su, sv)
        s = np.where((local >= 0) & (local <= 1), s, 0.0)
        shape = np.maximum(shape, s)

    # 噪声只调制内部，不改轮廓（乘在 shape 上）
    uu = u + sway * np.sin(v * 5.0)
    n1 = sample(N1, uu * 2.0, v * 2.0)
    n2 = sample(N2, uu * 1.4, v * 1.4)
    noise = n1 * n2 * gain
    mask = mask_strength * (1.0 - sample(BOTTOM, u, v))
    val = (noise + mask) * shape
    # 底部亮带：让所有火舌在根部连成一片，不再一根根「锥子」戳在地上
    val = np.maximum(val, base_band * np.clip((v - 0.72) / 0.28, 0, 1) ** 1.2)
    a = np.clip((val - t[0]) / max(1e-5, t[1] - t[0]), 0, 1)
    a *= np.clip(u / fade, 0, 1) * np.clip((1 - u) / fade, 0, 1)
    return a


def bands(a):
    return [a[int(i * H / 10):int((i + 1) * H / 10)].mean() for i in range(10)]


def preview(a, name):
    g = a
    rgb = np.zeros((H, W, 4), np.float32)
    rgb[..., 0] = np.clip(0.35 + 0.75 * g, 0, 1)
    rgb[..., 1] = np.clip(0.10 + 0.85 * np.power(g, 2.0), 0, 1)
    rgb[..., 2] = np.clip(0.02 + 0.6 * np.power(g, 3.5), 0, 1)
    rgb[..., 3] = a
    out = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8))
    bg = Image.new('RGBA', out.size, (52, 58, 64, 255))
    Image.alpha_composite(bg, out).convert('RGB').save(f'.tmp_sim_{name}.png')


PRESETS = [
    # name, n, top, bottom, width_mult, gain, mask, (t0,t1)
    ('q1', 9, 0.26, 0.93, 1.9, 1.0, 1.0, (0.10, 0.16)),
    ('q2', 11, 0.26, 0.93, 1.7, 1.0, 1.0, (0.10, 0.16)),
    ('q3', 7, 0.26, 0.93, 2.1, 1.0, 1.0, (0.10, 0.16)),
    ('q4', 13, 0.26, 0.93, 1.5, 1.0, 1.0, (0.10, 0.16)),
]

for name, n, top, bot, ov, gain, ms, t in PRESETS:
    a = build(n, top, bot, ov, gain, ms, t, base_band=0.34)
    print(name, 'coverage %.3f' % a.mean(), 'bands(top->bottom):', ' '.join('%.2f' % b for b in bands(a)))
    preview(a, name)
