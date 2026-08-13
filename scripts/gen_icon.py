#!/usr/bin/env python3
# Generate a VS Code style application icon (blue rounded square + white ">" chevron)
# as a multi-size ICO file (PNG-compressed entries, supported by Windows 10+).
import math
import os
import struct
import zlib

CANVAS = 1024          # supersampled drawing canvas (edge length)
SIZES = [256, 128, 64, 48, 32, 24, 16]

# VS Code brand gradient: top #0065A9 -> bottom #007ACC
TOP = (0, 101, 169)
BOTTOM = (0, 122, 204)
WHITE = (255, 255, 255)

CORNER = 0.225         # rounded-corner radius, fraction of the tile

# The white ">" chevron: two strokes (round caps come from the distance field)
STROKES = [
    ((0.310, 0.350), (0.550, 0.500)),
    ((0.550, 0.500), (0.310, 0.650)),
]
STROKE_WIDTH = 0.088   # stroke width, fraction of the tile


def seg_dist(px, py, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    t = (wx * vx + wy * vy) / (vx * vx + vy * vy)
    t = max(0.0, min(1.0, t))
    dx, dy = px - (ax + t * vx), py - (ay + t * vy)
    return math.hypot(dx, dy)


def sample(x, y):
    """x, y in [0,1] tile coords -> RGBA tuple."""
    cx, cy = x * CANVAS, y * CANVAS
    s = CANVAS
    r = CORNER * s
    # signed distance to the rounded rectangle (outside -> transparent)
    qx = max(r - cx, cx - (s - r), 0.0)
    qy = max(r - cy, cy - (s - r), 0.0)
    if math.hypot(qx, qy) - r > 0:
        return (0, 0, 0, 0)
    # vertical brand gradient
    bg = tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * y) for i in range(3))
    # chevron coverage from the distance field (smooth edge)
    d = min(seg_dist(x, y, *a, *b) for a, b in STROKES) * CANVAS
    half = STROKE_WIDTH * s / 2.0
    edge = 1.5  # smooth-edge width in canvas pixels
    cov = max(0.0, min(1.0, (half - d) / edge + 0.5))
    col = tuple(int(bg[i] * (1.0 - cov) + WHITE[i] * cov) for i in range(3))
    return (col[0], col[1], col[2], 255)


def render(size):
    """Box-filter downsample CANVAS -> size; returns RGBA pixel list."""
    scale = CANVAS / size
    pixels = []
    for oy in range(size):
        y0 = oy * scale
        for ox in range(size):
            x0 = ox * scale
            acc = [0.0, 0.0, 0.0, 0.0]  # premultiplied color + alpha
            for sy in range(4):
                py = y0 + (sy + 0.5) * scale / 4.0
                for sx in range(4):
                    px = x0 + (sx + 0.5) * scale / 4.0
                    r, g, b, a = sample(px / CANVAS, py / CANVAS)
                    acc[0] += r * a
                    acc[1] += g * a
                    acc[2] += b * a
                    acc[3] += a
            if acc[3] == 0:
                pixels.append((0, 0, 0, 0))
            else:
                pixels.append((
                    int(acc[0] / acc[3] + 0.5),
                    int(acc[1] / acc[3] + 0.5),
                    int(acc[2] / acc[3] + 0.5),
                    int(acc[3] / 16.0 + 0.5),
                ))
    return pixels


def png_encode(size, pixels):
    raw = bytearray()
    for y in range(size):
        raw.append(0)  # filter: none
        for x in range(size):
            raw.extend(pixels[y * size + x])
    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
    ihdr = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', ihdr)
            + chunk(b'IDAT', zlib.compress(bytes(raw), 9))
            + chunk(b'IEND', b''))


def build_ico():
    pngs = [(s, png_encode(s, render(s))) for s in SIZES]
    header = struct.pack('<HHH', 0, 1, len(pngs))
    entries = bytearray()
    offset = 6 + 16 * len(pngs)
    for size, data in pngs:
        w = 0 if size == 256 else size
        h = 0 if size == 256 else size
        entries.extend(struct.pack('<BBBBHHII', w, h, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    return header + bytes(entries) + b''.join(d for _, d in pngs)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, os.pardir, 'icon.ico')
    with open(out, 'wb') as f:
        f.write(build_ico())
    print('wrote', os.path.abspath(out))


if __name__ == '__main__':
    main()
