#!/usr/bin/env python3
"""Assemble a macOS .icns container from the PNGs produced by make_icons.

Usage: make-icns.py <icons-dir> <output.icns>
"""
import struct
import sys
from pathlib import Path

# ICNS element type -> (pixel size, file name)
ELEMENTS = [
    (b"ic11", 32, "icon-32.png"),
    (b"ic12", 64, "icon-64.png"),
    (b"ic07", 128, "icon-128.png"),
    (b"ic13", 256, "icon-256.png"),
    (b"ic08", 256, "icon-256.png"),
    (b"ic14", 512, "icon-512.png"),
    (b"ic09", 512, "icon-512.png"),
    (b"ic10", 1024, "icon-1024.png"),
]


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    icons = Path(sys.argv[1])
    out = Path(sys.argv[2])

    body = bytearray()
    for kind, size, name in ELEMENTS:
        data = (icons / name).read_bytes()
        body += kind + struct.pack(">I", len(data) + 8) + data

    out.write_bytes(b"icns" + struct.pack(">I", len(body) + 8) + bytes(body))
    print(f"wrote {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
