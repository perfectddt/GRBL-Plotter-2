#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Stage 2 route-B glyph generator: TTF outline -> rasterize -> skeletonize ->
trace polylines -> simplify -> heuristic stroke order -> the SAME font pack
schema as route A (doc/阶段2-字库路线.md section 3). Pure offline.

Pipeline per character:
  PIL/FreeType render at RASTER_PX em  ->  threshold to binary
  -> Zhang-Suen thinning (vectorized numpy)
  -> topology-safe graph tracing (each undirected edge exactly once)
  -> Douglas-Peucker simplification, forward-only fragment stitching
  -> orientation (top->down within stroke) + order (top->down, left->right)
  -> mm mapping identical to route A: baseline -> 1mm, cap-height median of
     reference chars -> BodySizeMm, em centered in the 12mm advance.

License boundary: the DEFAULT font is the Windows-bundled KaiTi (simkai.ttf).
Its digitized stroke data must stay OUT of the repository - output goes to
artifacts/ (gitignored) and only this script plus the tests are committed.
Pass --font with a properly licensed font (e.g. 全字库楷体 TW-Kai, 政府資料
開放授權-1.0) to produce a committable pack.

Known limitation (doc section 3): the stroke ORDER is a heuristic
(top-down / left-right), NOT the true stroke order route A provides, and
dense glyphs can carry skeleton junction artifacts. Validate on paper.
"""
import argparse
import importlib.util
import json
import math
import os
import statistics
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_FONT = r'C:\Windows\Fonts\simkai.ttf'
DEFAULT_OUT = os.path.join(REPO_ROOT, 'artifacts', 'stage2', 'fontpack-ttf-kaiti.json')
DEFAULT_REPORT = os.path.join(REPO_ROOT, 'artifacts', 'stage2', 'fontpack-ttf-report.txt')

SCHEMA_VERSION = 1
SOURCE_EM_UNITS = 1024.0          # declared, matching route A metadata semantics
CAP_UNITS = 880.0                 # declared, matching route A metadata semantics
BODY_MM = 10.0
BASELINE_MM = 1.0
EM_MM = 12.0
ADVANCE_CJK_MM = 12.0
ADVANCE_SPACE_MM = 6.0
MM_ROUND = 3

RASTER_PX = 400                   # em square rendered size
INK_THRESHOLD = 100               # antialiased 'L' mask -> binary
SIMPLIFY_EPS_PX = 1.5             # Douglas-Peucker epsilon
REF_CHARS = '十中木金永国回园围因固'   # full-height calibration references

ROUTE_A_SCRIPT = os.path.join(REPO_ROOT, 'scripts', 'generate-stage2-fontpack.py')


def fail(message):
    raise SystemExit('FAIL: ' + message)


def load_pilot_chars():
    spec = importlib.util.spec_from_file_location('route_a_generator', ROUTE_A_SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.PILOT_CHARS


def zhang_suen(mask):
    """Vectorized Zhang-Suen thinning. Input/output: bool 2D array (True = ink)."""
    img = mask.astype(np.uint8)
    changed = True
    while changed:
        changed = False
        for step in (0, 1):
            p = np.pad(img, 1)                       # zero background border
            p2 = p[:-2, 1:-1]; p3 = p[:-2, 2:]; p4 = p[1:-1, 2:]
            p5 = p[2:, 2:];     p6 = p[2:, 1:-1]; p7 = p[2:, :-2]
            p8 = p[1:-1, :-2]; p9 = p[:-2, :-2]
            b = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9
            a = ((p2 == 0) & (p3 == 1)).astype(np.uint8)
            a += (p3 == 0) & (p4 == 1); a += (p4 == 0) & (p5 == 1)
            a += (p5 == 0) & (p6 == 1); a += (p6 == 0) & (p7 == 1)
            a += (p7 == 0) & (p8 == 1); a += (p8 == 0) & (p9 == 1)
            a += (p9 == 0) & (p2 == 1)
            delete = (img == 1) & (b >= 2) & (b <= 6) & (a == 1)
            if step == 0:
                delete &= (p2 * p4 * p6 == 0) & (p4 * p6 * p8 == 0)
            else:
                delete &= (p2 * p4 * p8 == 0) & (p2 * p6 * p8 == 0)
            if delete.any():
                img[delete] = 0
                changed = True
    return img.astype(bool)


OFFSETS = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]


def binary_close(mask):
    """True 3x3 closing with zero padding, including the centre pixel.

    np.roll wraps opposite image borders and an eight-neighbour structuring
    element omits the centre; neither describes a square morphological close.
    """
    image = np.pad(mask.astype(bool), 2, constant_values=False)
    def neighborhood(image):
        padded = np.pad(image, 1, constant_values=False)
        height, width = image.shape
        return [padded[r:r + height, c:c + width]
                for r in range(3) for c in range(3)]
    dilated = np.logical_or.reduce(neighborhood(image))
    return np.logical_and.reduce(neighborhood(dilated))[2:-2, 2:-2]


def skeleton_graph(skel):
    """Pixel graph without diagonal corner shortcuts.

    Retain a diagonal only when neither orthogonal common neighbour exists.
    This preserves diagonal-only strokes and removes triangles around every
    ordinary stair step, bend and orthogonal crossing. Edges are symmetric.
    """
    points = {(int(r), int(c)) for r, c in zip(*np.nonzero(skel))}
    neighbors = {}
    for r, c in sorted(points):
        linked = []
        for dr, dc in OFFSETS:
            other = (r + dr, c + dc)
            if other not in points:
                continue
            if dr and dc and ((r + dr, c) in points or (r, c + dc) in points):
                continue
            linked.append(other)
        neighbors[(r, c)] = sorted(linked)
    return neighbors


def prune_spurs(skel, max_len_px=3.0):
    """Remove only tiny endpoint-to-junction hairs, retaining the junction.

    Entire short components (dots and short independent strokes) survive.
    The three-pixel default bounds the omission near a retained attachment;
    pruning never consumes the junction itself or repeatedly shortens a tip.
    """
    skel = skel.copy()
    neighbors = skeleton_graph(skel)
    remove = set()
    for start in sorted(neighbors):
        if len(neighbors[start]) != 1:
            continue
        branch = [start]
        prev, current = start, neighbors[start][0]
        length = math.dist(start, current)
        while len(neighbors[current]) == 2 and length <= max_len_px:
            branch.append(current)
            following = next(n for n in neighbors[current] if n != prev)
            length += math.dist(current, following)
            prev, current = current, following
        if len(neighbors[current]) >= 3 and length <= max_len_px:
            remove.update(branch)  # current is the attachment and must survive
    for row, col in remove:
        skel[row, col] = False
    return skel


def extract_strokes(skel, cluster_px=6.0, merge_turn_max=1.3):
    """Trace each graph edge once, pairing smooth continuations at junctions.

    Pairing is a fixed relation between chain ports. No BFS bridge is inserted
    across junction clusters: such bridges reused already traced graph edges
    and created tiny backtracks. Closed components and short chains survive.
    cluster_px remains a compatibility argument; contraction is unnecessary
    once diagonal corner shortcuts are removed.
    """
    neighbors = skeleton_graph(skel)
    nodes = {p for p in neighbors if len(neighbors[p]) != 2}
    consumed = set()
    def edge_key(a, b):
        return tuple(sorted((a, b)))
    def walk(start, first):
        path = [start, first]
        consumed.add(edge_key(start, first))
        previous, current = start, first
        while current not in nodes:
            following = next((n for n in neighbors[current]
                              if n != previous and edge_key(current, n) not in consumed), None)
            if following is None:
                break
            consumed.add(edge_key(current, following))
            path.append(following)
            previous, current = current, following
        return path
    chains = []
    for node in sorted(nodes):
        for other in neighbors[node]:
            if edge_key(node, other) not in consumed:
                chains.append(walk(node, other))
    for node in sorted(neighbors):
        for other in neighbors[node]:
            if edge_key(node, other) not in consumed:
                chains.append(walk(node, other))
    ports = {}
    for index, chain in enumerate(chains):
        if chain[0] == chain[-1]:
            continue  # retain a closed chain independently of attached branches
        for end in (0, -1):
            ports.setdefault(chain[end], []).append((index, end))
    pairs = {}
    for node, items in sorted(ports.items()):
        candidates = []
        for position, a in enumerate(items):
            seq_a = chains[a[0]] if a[1] == 0 else chains[a[0]][::-1]
            da = _direction(seq_a, True)
            for b in items[position + 1:]:
                if a[0] == b[0]:
                    continue
                seq_b = chains[b[0]] if b[1] == 0 else chains[b[0]][::-1]
                db = _direction(seq_b, True)
                turn = math.acos(max(-1., min(1., -(da[0]*db[0] + da[1]*db[1]))))
                if turn <= merge_turn_max:
                    candidates.append((turn, a, b))
        for _, a, b in sorted(candidates):
            if a not in pairs and b not in pairs:
                pairs[a] = b
                pairs[b] = a
    used = set()
    strokes = []
    def trace(port):
        path = []
        while port[0] not in used:
            index, end = port
            used.add(index)
            chain = chains[index] if end == 0 else chains[index][::-1]
            path.extend(chain if not path else chain[1:])
            exit_port = (index, -1 if end == 0 else 0)
            if exit_port not in pairs:
                break
            port = pairs[exit_port]
        return path
    # Open paths first, then remaining cycles of paired chains.
    for index in range(len(chains)):
        for end in (0, -1):
            if index not in used and (index, end) not in pairs:
                strokes.append(trace((index, end)))
    for index in range(len(chains)):
        if index not in used:
            strokes.append(trace((index, 0)))
    strokes.extend([[p] for p in sorted(neighbors) if not neighbors[p]])
    return strokes


def simplify(points, eps):
    """Douglas-Peucker on [(row, col)]."""
    if len(points) < 3:
        return points[:]
    def segment_distance(pt, a, b):
        ax, ay = a; bx, by = b; px, py = pt
        dx, dy = bx - ax, by - ay
        length2 = dx * dx + dy * dy
        if length2 == 0:
            return math.hypot(px - ax, py - ay)
        t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / length2))
        return math.hypot(px - (ax + t * dx), py - (ay + t * dy))
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        lo, hi = stack.pop()
        if hi <= lo + 1:
            continue
        best_i, best_d = -1, eps
        for i in range(lo + 1, hi):
            d = segment_distance(points[i], points[lo], points[hi])
            if d > best_d:
                best_i, best_d = i, d
        if best_i >= 0:
            keep[best_i] = True
            stack.append((lo, best_i))
            stack.append((best_i, hi))
    return [pt for pt, k in zip(points, keep) if k]


def arc_length(points):
    return sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(points, points[1:]))


def _direction(points, from_start):
    """Average direction over up to the first/last few steps."""
    seq = points if from_start else points[::-1]
    for k in (6, 3, 1):
        if len(seq) > k:
            dr = seq[k][0] - seq[0][0]
            dc = seq[k][1] - seq[0][1]
            norm = math.hypot(dr, dc)
            if norm > 0:
                sign = 1.0 if from_start else -1.0
                return (sign * dr / norm, sign * dc / norm)
    return (0.0, 0.0)


def stitch_fragments(paths, gap_px=6.0, turn_max=1.4):
    """Join fragment chains: when one path's end sits close to another path's
    start and the travel direction continues, concatenate them. Repairs the
    near-disconnections the skeleton leaves at dense crossings, so a bar is
    written with one pen-down instead of three."""
    paths = [p[:] for p in paths]
    changed = True
    while changed:
        changed = False
        for i in range(len(paths)):
            if len(paths[i]) < 2 or paths[i][0] == paths[i][-1]:
                continue
            dir_a = _direction(paths[i], from_start=False)
            best = None
            for j in range(len(paths)):
                if i == j or len(paths[j]) < 2 or paths[j][0] == paths[j][-1]:
                    continue
                for reversed_j in (False, True):
                    fj = paths[j][::-1] if reversed_j else paths[j]
                    gap = math.hypot(paths[i][-1][0] - fj[0][0],
                                     paths[i][-1][1] - fj[0][1])
                    if gap > gap_px:
                        continue
                    dir_b = _direction(fj, from_start=True)
                    dot = dir_a[0] * dir_b[0] + dir_a[1] * dir_b[1]
                    turn = math.acos(max(-1.0, min(1.0, dot)))
                    if turn > turn_max:
                        continue
                    if gap > 0:
                        bridge = ((fj[0][0] - paths[i][-1][0]) / gap,
                                  (fj[0][1] - paths[i][-1][1]) / gap)
                        # Parallel tangents alone do not make a forward join:
                        # the connecting move must continue both trajectories.
                        if any(bridge[0] * direction[0] + bridge[1] * direction[1]
                               < math.cos(turn_max) for direction in (dir_a, dir_b)):
                            continue
                    if best is None or turn < best[0]:
                        best = (turn, j, reversed_j)
            if best is not None:
                _, j, reversed_j = best
                paths[i] = paths[i] + (paths[j][::-1] if reversed_j else paths[j])
                del paths[j]
                changed = True
                break
    return paths


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--font', default=DEFAULT_FONT, help='TTF path (must be licensed for your use)')
    parser.add_argument('--out', default=DEFAULT_OUT)
    parser.add_argument('--report', default=DEFAULT_REPORT)
    parser.add_argument('--chars-file', default=None)
    parser.add_argument('--limit', type=int, default=None, help='first N charset chars (quick tests)')
    args = parser.parse_args()

    if not os.path.isfile(args.font):
        fail('字体文件不存在：%s' % args.font)

    if args.chars_file:
        charset = set(open(args.chars_file, encoding='utf-8').read()) - set(' \t\r\n')
    else:
        charset = set(load_pilot_chars()) - set(' \t\r\n')
    if args.limit:
        charset = set(sorted(charset, key=ord)[:args.limit])
    if not charset:
        fail('字符表为空。')

    tt = TTFont(args.font, fontNumber=0)
    cmap = tt.getBestCmap()
    family = tt['name'].getDebugName(1) or os.path.basename(args.font)
    upm = tt['head'].unitsPerEm
    missing = sorted(c for c in charset if ord(c) not in cmap)
    if missing:
        fail('字体 %s 缺 %d 个字：%s（不静默跳过。）' % (family, len(missing), ' '.join(missing)))

    pil_font = ImageFont.truetype(args.font, RASTER_PX)
    ascent, descent = pil_font.getmetrics()
    pad = RASTER_PX // 4
    canvas_w = RASTER_PX + 2 * pad
    canvas_h = ascent + descent + 2 * pad
    baseline_row = pad + ascent

    def render(ch):
        image = Image.new('L', (canvas_w, canvas_h), 0)
        ImageDraw.Draw(image).text((pad, pad), ch, font=pil_font, fill=255)
        return np.array(image) >= INK_THRESHOLD

    # Calibration: median ink height of full-height reference chars -> BodySizeMm.
    ref_heights = {}
    for ch in REF_CHARS:
        ink = np.nonzero(render(ch))
        ref_heights[ch] = int(ink[0].max() - ink[0].min() + 1)
    cap_px = statistics.median(ref_heights.values())
    scale = BODY_MM / cap_px
    x_offset = (EM_MM - RASTER_PX * scale) / 2.0

    def to_mm(path_px):
        return [(round((col - pad) * scale + x_offset, MM_ROUND),
                 round(BASELINE_MM + (baseline_row - row) * scale, MM_ROUND))
                for row, col in path_px]

    glyphs = []
    total_strokes = 0
    total_points = 0
    dropped_spurs = 0
    stroke_counts = []
    ink_min_x = ink_min_y = math.inf
    ink_max_x = ink_max_y = -math.inf

    for ch in sorted(charset, key=ord):
        skel = prune_spurs(zhang_suen(binary_close(render(ch))))
        if not skel.any():
            fail('「%s」栅格化后没有墨迹。' % ch)
        raw_paths = extract_strokes(skel)
        kept = []
        for path in raw_paths:
            simplified = simplify(path, SIMPLIFY_EPS_PX)
            if len(simplified) == 1:
                # A one-pixel independent dot still needs a drawable segment.
                row, col = simplified[0]
                simplified = [(row, col), (row, col + 0.5)]
            kept.append(simplified)
        if not kept:
            fail('「%s」骨架化后没有保留任何笔画。' % ch)
        kept = stitch_fragments(kept)
        # Heuristic writing order: top->down, left->right (NOT true stroke order).
        kept.sort(key=lambda path: (min(p[0] for p in path), min(p[1] for p in path)))
        # Within a stroke: horizontal strokes run left->right, vertical strokes
        # top->down (writing habit; also keeps slanted bars from poking left of
        # the origin after start alignment).
        oriented = []
        for path in kept:
            dr = abs(path[-1][0] - path[0][0])
            dc = abs(path[-1][1] - path[0][1])
            if dc >= dr:                       # horizontal-ish
                if path[0][1] > path[-1][1]:
                    path = path[::-1]
            else:                              # vertical-ish
                if path[0][0] > path[-1][0]:
                    path = path[::-1]
            oriented.append(path)
        strokes = []
        for path in oriented:
            flat = []
            for row, col in path:
                x, y = to_mm([(row, col)])[0]
                flat.append(x); flat.append(y)
                ink_min_x = min(ink_min_x, x); ink_max_x = max(ink_max_x, x)
                ink_min_y = min(ink_min_y, y); ink_max_y = max(ink_max_y, y)
            strokes.append(flat)
            total_points += len(flat) // 2
        glyphs.append({'c': ch, 'strokes': strokes})
        total_strokes += len(strokes)
        stroke_counts.append(len(strokes))

    tolerance = 0.5
    if ink_min_x < -tolerance or ink_min_y < -tolerance or \
       ink_max_x > EM_MM + tolerance or ink_max_y > EM_MM + tolerance:
        fail('墨迹超出 em 框（容差 %.1fmm）：X %.3f..%.3f  Y %.3f..%.3f'
             % (tolerance, ink_min_x, ink_max_x, ink_min_y, ink_max_y))

    pack = {
        'schemaVersion': SCHEMA_VERSION,
        'name': 'TTF 骨架提取 v3·%s·试点 %d 字' % (family, len(glyphs)),
        'source': '%s (%s, upm %d), PIL/FreeType raster %dpx -> Zhang-Suen skeleton -> v3 topology-safe graph' % (
            os.path.basename(args.font), family, upm, RASTER_PX),
        'license': '见字体本体授权；Windows 自带 KaiTi 衍生数据仅限本机使用，不入仓库（--font 换授权干净的字体可入库）',
        'sourceEmUnits': SOURCE_EM_UNITS,
        'capUnits': CAP_UNITS,
        'bodySizeMm': BODY_MM,
        'baselineMm': BASELINE_MM,
        'emSizeMm': EM_MM,
        'advanceCjkMm': ADVANCE_CJK_MM,
        'advanceSpaceMm': ADVANCE_SPACE_MM,
        'glyphs': glyphs,
    }
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    payload = json.dumps(pack, sort_keys=True, ensure_ascii=False, separators=(',', ':'))
    with open(args.out, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(payload)

    def ink_height_mm(symbol):
        entry = next(e for e in glyphs if e['c'] == symbol)
        ys = [v for s in entry['strokes'] for v in s[1::2]]
        return round(max(ys) - min(ys), 3)

    references = {e['c']: ink_height_mm(e['c']) for e in glyphs if e['c'] in '木国王一'}
    lines = [
        'PASS: route-B font pack generated.',
        'pack: %s (%.1f KB)' % (args.out, os.path.getsize(args.out) / 1024.0),
        'font: %s (%s, upm %d)' % (os.path.basename(args.font), family, upm),
        'glyphs: %d   strokes: %d (mean %.1f, max %d)   points: %d'
        % (len(glyphs), total_strokes, statistics.mean(stroke_counts), max(stroke_counts), total_points),
        'calibration: ref ink heights px %s -> capPx %d -> scale %.6f mm/px, x offset %.3fmm'
        % (ref_heights, cap_px, scale, x_offset),
        'graph: v3 orthogonal-corner-safe adjacency; each undirected edge traced once; no cluster BFS bridges',
        'post-trace dropped paths: %d; only attached hairs <= 3px pruned before tracing' % dropped_spurs,
        'ink extents in mm: X %.3f..%.3f  Y %.3f..%.3f (em box 0..%.0f)'
        % (ink_min_x, ink_max_x, ink_min_y, ink_max_y, EM_MM),
        'reference ink heights mm: %s' % references,
        'NOTE: stroke order is heuristic (top-down/left-right), not the true stroke order of route A.',
    ]
    os.makedirs(os.path.dirname(args.report), exist_ok=True)
    with open(args.report, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


if __name__ == '__main__':
    main()
