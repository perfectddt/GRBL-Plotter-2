"""Offline topology and fragment-stitching regressions; no controller IO."""
import importlib.util
from collections import Counter
from pathlib import Path

import numpy as np

source = Path(__file__).with_name('generate-stage2-fontpack-ttf.py')
spec = importlib.util.spec_from_file_location('ttf_paths', source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def mask(points, size=32):
    image = np.zeros((size, size), dtype=bool)
    for row, col in points:
        image[row, col] = True
    return image


def verify_edges(image, expected_strokes=None, closed=None):
    """Every source graph edge occurs exactly once, and every pixel survives."""
    graph = module.skeleton_graph(image)
    expected = Counter({tuple(sorted((a, b))): 1
                        for a, neighbors in graph.items() for b in neighbors})
    paths = module.extract_strokes(image)
    actual = Counter(tuple(sorted((a, b))) for path in paths
                     for a, b in zip(path, path[1:]))
    assert actual == expected, (expected - actual, actual - expected)
    assert set(p for path in paths for p in path) == set(graph)
    if expected_strokes is not None:
        assert len(paths) == expected_strokes, paths
    if closed is not None:
        assert sum(len(p) > 2 and p[0] == p[-1] for p in paths) == closed, paths
    return paths


# Pure orthogonal crossings have two traversals, not four triangular detours.
cross = mask([(r, 16) for r in range(3, 30)] +
             [(16, c) for c in range(3, 30)])
verify_edges(cross, 2)
# A right-angle bend is one path, not a shortcut triangle.
elbow = mask([(8, c) for c in range(8, 24)] +
             [(r, 23) for r in range(8, 24)])
verify_edges(elbow, 1)
# A diagonal-only chain must remain connected.
verify_edges(mask([(k, k) for k in range(4, 28)]), 1)
# Diagonal stair steps preserve all pixels without diagonal corner chords.
verify_edges(mask([(k, k) for k in range(4, 25)] +
                  [(k, k + 1) for k in range(4, 24)]), 1)
# A real enclosure stays closed. Concentric enclosures keep both rings.
ring_points = ([(5, c) for c in range(5, 27)] +
               [(26, c) for c in range(5, 27)] +
               [(r, 5) for r in range(5, 27)] +
               [(r, 26) for r in range(5, 27)])
ring = mask(ring_points)
verify_edges(ring, 1, 1)
inner = ([(11, c) for c in range(11, 21)] +
         [(20, c) for c in range(11, 21)] +
         [(r, 11) for r in range(11, 21)] +
         [(r, 20) for r in range(11, 21)])
verify_edges(mask(ring_points + inner), 2, 2)
# An attached ring keeps its closure and the outgoing branch.
verify_edges(mask(ring_points + [(r, 16) for r in range(1, 5)]), 2, 1)
# A dot and a tiny independent line survive pruning and extraction.
short = mask([(5, 5), (20, 20), (20, 21), (20, 22)])
assert np.array_equal(module.prune_spurs(short), short)
verify_edges(short, 2)
# Tiny attached hairs are removable, but the shared junction is never removed.
hair = mask([(16, c) for c in range(4, 28)] + [(15, 16), (14, 16)])
pruned = module.prune_spurs(hair)
assert pruned[16, 16] and not pruned[14, 16] and not pruned[15, 16]
verify_edges(pruned, 1)
assert np.array_equal(module.prune_spurs(ring), ring)
# Closing includes the centre and treats borders as empty space, not a torus.
for point in ((0, 0), (0, 31), (31, 0), (16, 16)):
    pixel = mask([point])
    assert np.array_equal(module.binary_close(pixel), pixel)
assert not module.binary_close(mask([(15, 0)]))[15, -1]

assert module._direction([(0, 0), (0, 5), (0, 10)], False) == (0.0, 1.0)
forward = module.stitch_fragments([[(0, 0), (0, 10)], [(0, 12), (0, 20)]])
assert forward == [[(0, 0), (0, 10), (0, 12), (0, 20)]]
assert len(module.stitch_fragments([[(0, 0), (0, 10)], [(0, 8), (0, 15)]])) == 2
assert len(module.stitch_fragments([[(0, 0), (0, 10)], [(4, 8), (4, 15)]])) == 2
assert len(module.stitch_fragments([[(0, 0), (0, 10)], [(0, 17), (0, 25)]])) == 2
assert len(module.stitch_fragments([[(0, 0), (0, 10)], [(2, 12), (2, 20)]])) == 1
loop = module.simplify(module.extract_strokes(ring)[0], module.SIMPLIFY_EPS_PX)
assert loop[0] == loop[-1] and len(loop) >= 5
assert len(module.stitch_fragments([loop, [(5, 28), (5, 31)]])) == 2

# Deterministic irregular graphs stress paired-chain cycles and adjacent nodes.
rng = np.random.default_rng(42)
for _ in range(100):
    verify_edges(rng.random((12, 12)) < 0.18)
print('PASS: orthogonal cross/bend, diagonal/stair, true/attached/nested loops, '
      'short dot/stroke, safe spur/closing, edge conservation, forward bridges '
      'and 100 deterministic irregular graphs')
