"""Generates res/InterfaceTools.ico: a 9-slice frame (the editor's subject) in a few sizes."""
import struct, zlib, sys

def png(w, h, px):
    raw = b"".join(b"\x00" + bytes(px[y * w * 4:(y + 1) * w * 4]) for y in range(h))
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

def draw(s):
    px = bytearray(s * s * 4)
    def put(x, y, c):
        o = (y * s + x) * 4
        px[o:o + 4] = bytes(c)
    m = max(1, s // 16)                 # outer margin
    b = max(2, s // 5)                  # border thickness (slice size)
    bg = (0x2b, 0x3a, 0x55, 255)
    frame = (0xe8, 0xa3, 0x3c, 255)
    corner = (0xff, 0xd2, 0x6e, 255)
    centre = (0x4f, 0x7c, 0xc4, 255)
    line = (0xff, 0xff, 0xff, 255)
    for y in range(m, s - m):
        for x in range(m, s - m):
            inx, iny = x - m, y - m
            n = s - 2 * m
            cx = 0 if inx < b else (2 if inx >= n - b else 1)
            cy = 0 if iny < b else (2 if iny >= n - b else 1)
            c = centre if (cx, cy) == (1, 1) else (corner if cx != 1 and cy != 1 else frame)
            put(x, y, c)
    # slice lines
    n = s - 2 * m
    for k in (m + b, m + n - b):
        for t in range(m, s - m):
            if s >= 32 or True:
                put(k, t, line); put(t, k, line)
    # outer edge
    for t in range(m, s - m):
        for (x, y) in ((t, m), (t, s - m - 1), (m, t), (s - m - 1, t)):
            put(x, y, bg)
    return px

sizes = [16, 24, 32, 48, 64, 256]
imgs = [png(sz, sz, draw(sz)) for sz in sizes]
hdr = struct.pack("<HHH", 0, 1, len(imgs))
off = 6 + 16 * len(imgs)
dirs = b""
for sz, d in zip(sizes, imgs):
    dirs += struct.pack("<BBBBHHII", sz % 256, sz % 256, 0, 0, 1, 32, len(d), off)
    off += len(d)
open(sys.argv[1], "wb").write(hdr + dirs + b"".join(imgs))
