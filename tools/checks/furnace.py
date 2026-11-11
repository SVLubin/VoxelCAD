#!/usr/bin/env python3
"""Measures a furnace render: how far each white body is from the room it stands in.

A white surface in an environment of one colour must come back at exactly that
colour, so every cube should read 0%. It runs in two steps, because the picture
cannot answer either of them alone.

**Where the bodies are.** Render the scene once with the backdrop blacked out, so
they are unmissable whatever they are made of — the measured render cannot be used
for this, since what passing looks like is the cubes vanishing into the sky. Then:

    python3 tools/checks/furnace.py locate tmp/furnace-lit.png

which prints the `sample=` argument for the next render, the sky first. Finding six
bodies is also the framing check.

**What they are worth.** Render again with that argument and hand the answer back:

    python3 tools/checks/furnace.py check '<the sampled array from the render>'

The numbers measured are **radiance**, straight out of the tracer, because an error
read off an encoded picture comes back about a third smaller than it is.

The scene is tools/checks/furnace.txt and both sets of render arguments are in
docs/render.md.

Reads PNG with the standard library alone: this is a check that must run wherever
the repository does, not one that needs a pillow installed first.
"""

import json
import struct
import sys
import zlib

# What the scene holds, in the order the cubes stand along x, with how far each is
# allowed to be from the room.
#
# Every one is held to one per cent, and there is no longer a body with a residue
# of its own: the metals' loss is paid back from a table integrated over the very
# lobe that loses it, and the diffuse half gives up exactly what that lobe returns
# rather than the Fresnel at the view angle — which is what used to leave a white
# matte surface 2.8% dark.
#
# Anything above the bound is one of those two having drifted from the other.
BODIES = [
    ("metal r=0.10", 1.0),
    ("metal r=0.35", 1.0),
    ("metal r=0.60", 1.0),
    ("metal r=0.85", 1.0),
    ("metal r=1.00", 1.0),
    ("diffuse r=1.00", 1.0),
]

# How far from the sky a column has to be before it counts as a body. Two levels
# of 255: below that is the render's own noise, above it is something there.
THRESHOLD = 2

# Dropped from each side of a body before it is averaged, so no edge pixel — half
# sky, half surface — is ever part of the answer.
INSET = 6


def read_png(path):
    """A PNG as (width, height, rows of (r, g, b) tuples). Eight bits, RGB or RGBA."""
    with open(path, "rb") as handle:
        data = handle.read()

    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path} is not a PNG.")

    width = height = None
    channels = 0
    compressed = bytearray()
    offset = 8

    while offset < len(data):
        (length,) = struct.unpack(">I", data[offset:offset + 4])
        kind = data[offset + 4:offset + 8]
        body = data[offset + 8:offset + 8 + length]
        offset += 12 + length

        if kind == b"IHDR":
            width, height, depth, colour = struct.unpack(">IIBB", body[:10])

            if depth != 8 or colour not in (2, 6):
                raise ValueError("Only eight-bit RGB or RGBA is read here.")

            channels = 3 if colour == 2 else 4
        elif kind == b"IDAT":
            compressed += body
        elif kind == b"IEND":
            break

    raw = zlib.decompress(bytes(compressed))
    stride = width * channels
    rows = []
    previous = bytearray(stride)
    at = 0

    for _ in range(height):
        filter_kind = raw[at]
        line = bytearray(raw[at + 1:at + 1 + stride])
        at += 1 + stride

        unfilter(filter_kind, line, previous, channels)

        rows.append([tuple(line[x * channels:x * channels + 3]) for x in range(width)])
        previous = line

    return width, height, rows


def unfilter(kind, line, previous, channels):
    """Undoes one scanline's filter, in place, as the format defines them."""
    if kind == 0:
        return

    for i in range(len(line)):
        left = line[i - channels] if i >= channels else 0
        above = previous[i]
        corner = previous[i - channels] if i >= channels else 0

        if kind == 1:
            line[i] = (line[i] + left) & 0xFF
        elif kind == 2:
            line[i] = (line[i] + above) & 0xFF
        elif kind == 3:
            line[i] = (line[i] + (left + above) // 2) & 0xFF
        elif kind == 4:
            line[i] = (line[i] + paeth(left, above, corner)) & 0xFF
        else:
            raise ValueError(f"Unknown filter {kind}.")


def paeth(left, above, corner):
    estimate = left + above - corner
    da = abs(estimate - left)
    db = abs(estimate - above)
    dc = abs(estimate - corner)

    if da <= db and da <= dc:
        return left

    return above if db <= dc else corner


def luminance(pixel):
    return 0.2126 * pixel[0] + 0.7152 * pixel[1] + 0.0722 * pixel[2]


def announce(lit_path):
    """Prints the sample argument for the measuring render: the sky, then each body."""
    width, height, rows = read_png(lit_path)
    runs, top, bottom = find(width, height, rows)

    print(f"bodies found {len(runs)} of {len(BODIES)}", file=sys.stderr)

    if len(runs) != len(BODIES):
        print("  The lit render does not hold what the scene does — check the "
              "framing before going on.", file=sys.stderr)

        return 1

    # A corner no body reaches, which is the room itself.
    rects = [f"8,8,{width // 8},{height // 8}"]

    for left, right in runs:
        rects.append(
            f"{left + INSET},{top + INSET},"
            f"{right - left - 2 * INSET},{bottom - top - 2 * INSET}")

    print(";".join(rects))

    return 0


def check(answer):
    """Reads the render's own sampled array: the sky first, then one body each."""
    sampled = json.loads(answer)

    if len(sampled) != len(BODIES) + 1:
        print(f"{len(sampled)} rectangles came back, expected {len(BODIES) + 1} — "
              "the sky and one per body.")

        return 1

    sky = sampled[0]["luminance"]
    failed = 0

    print(f"sky {sky:.6f}")

    for entry, (label, allowed) in zip(sampled[1:], BODIES):
        value = entry["luminance"]
        error = 100.0 * (value / sky - 1.0)
        over = abs(error) > allowed

        if over:
            failed += 1

        print(f"  {label:16s} {value:.6f}   {error:+6.2f}%"
              f"   {'OVER ' + format(allowed, '.1f') + '%' if over else ''}")

    print("passed" if failed == 0 else f"{failed} over their bounds")

    return 0 if failed == 0 else 1


def find(width, height, rows):
    """Where the bodies stand, read off the lit render as columns and rows."""
    backdrop = mean(rows, 8, 8, width // 8, height // 8)

    columns = [
        any(abs(luminance(rows[y][x]) - backdrop) > THRESHOLD for y in range(height))
        for x in range(width)
    ]

    runs = []
    x = 0

    while x < width:
        if not columns[x]:
            x += 1
            continue

        start = x

        while x < width and columns[x]:
            x += 1

        if x - start > 2 * INSET:
            runs.append((start, x))

    inside = [
        any(
            abs(luminance(rows[y][x]) - backdrop) > THRESHOLD
            for left, right in runs
            for x in range(left, right)
        )
        for y in range(height)
    ]

    found = [y for y, value in enumerate(inside) if value]
    top, bottom = (min(found), max(found) + 1) if found else (0, height)

    return runs, top, bottom


def mean(rows, x0, y0, x1, y1):
    values = [luminance(rows[y][x]) for y in range(y0, y1) for x in range(x0, x1)]

    return sum(values) / len(values)


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "locate":
        sys.exit(announce(sys.argv[2]))

    if len(sys.argv) == 3 and sys.argv[1] == "check":
        sys.exit(check(sys.argv[2]))

    print(__doc__)
    sys.exit(2)
