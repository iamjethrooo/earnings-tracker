"""Render the code-native N mark to a multi-size ICO using only the standard library."""
import argparse
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zlib

ROOT = Path(__file__).resolve().parents[1]


def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))


def render(size):
    svg = ET.parse(ROOT / 'Resources/AppIcon.svg').getroot()
    rect = svg.find('{http://www.w3.org/2000/svg}rect')
    group = svg.find('{http://www.w3.org/2000/svg}g')
    color = lambda value: tuple(bytes.fromhex(value.lstrip('#')))
    background, accent = color(rect.attrib['fill']), color(group.attrib['fill'])
    x, y, width, height, radius = (float(rect.attrib[k]) for k in ['x', 'y', 'width', 'height', 'rx'])
    polygons = [[tuple(map(float, point.split(','))) for point in p.attrib['points'].split()] for p in group]

    def inside_polygon(px, py, points):
        signs = [(b[0]-a[0])*(py-a[1]) - (b[1]-a[1])*(px-a[0]) for a, b in zip(points, points[1:] + points[:1])]
        return all(v >= 0 for v in signs) or all(v <= 0 for v in signs)

    rows = bytearray()
    for iy in range(size):
        rows.append(0)
        for ix in range(size):
            count = 0
            rgb = [0, 0, 0]
            for sy in range(4):
                for sx in range(4):
                    px, py = (ix + (sx+.5)/4)*64/size, (iy + (sy+.5)/4)*64/size
                    if not (x <= px <= x+width and y <= py <= y+height):
                        continue
                    cx = min(max(px, x+radius), x+width-radius)
                    cy = min(max(py, y+radius), y+height-radius)
                    if (px-cx)**2 + (py-cy)**2 > radius**2:
                        continue
                    c = accent if any(inside_polygon(px, py, p) for p in polygons) else background
                    count += 1
                    rgb = [a+b for a, b in zip(rgb, c)]
            rows.extend([round(v/count) if count else 0 for v in rgb] + [round(255*count/16)])
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b'')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'Resources/App.ico')
    parser.add_argument('--preview', type=Path)
    args = parser.parse_args()
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [render(size) for size in sizes]
    offset = 6 + 16*len(frames)
    directory = bytearray(struct.pack('<HHH', 0, 1, len(frames)))
    for size, frame in zip(sizes, frames):
        directory.extend(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(frame), offset))
        offset += len(frame)
    args.output.write_bytes(bytes(directory) + b''.join(frames))
    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        args.preview.write_bytes(frames[-1])


if __name__ == '__main__':
    main()
