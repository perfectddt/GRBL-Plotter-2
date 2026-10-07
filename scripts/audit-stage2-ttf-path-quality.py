"""Independent raster/geometry comparison for route-B path candidates. No serial IO."""
import argparse
import importlib.util
import json
import math
import statistics
from collections import Counter
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--candidate', required=True)
parser.add_argument('--out-dir', default='artifacts/stage2/font-quality-20261006/v3-audit')
parser.add_argument('--full-coverage', action='store_true', help='check every candidate glyph against its source raster skeleton')
args = parser.parse_args()
out = ROOT / args.out_dir
out.mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location('ttf_generator', ROOT/'scripts/generate-stage2-fontpack-ttf.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)
font = ImageFont.truetype(generator.DEFAULT_FONT, generator.RASTER_PX)
ascent, descent = font.getmetrics()
pad = generator.RASTER_PX//4
size = (generator.RASTER_PX+2*pad, ascent+descent+2*pad)

def raster(ch):
    image = Image.new('L', size, 0)
    ImageDraw.Draw(image).text((pad,pad), ch, font=font, fill=255)
    return np.array(image) >= generator.INK_THRESHOLD

cap = statistics.median(np.ptp(np.nonzero(raster(ch))[0])+1 for ch in generator.REF_CHARS)
scale = 10/cap
x_offset = (12-generator.RASTER_PX*scale)/2
baseline = pad+ascent
archived = ROOT/'artifacts/stage2/font-quality-20261006/fontpack-ttf-kaiti-v1-723.json'
files = [archived if archived.exists() else ROOT/'artifacts/stage2/fontpack-ttf-kaiti.json',
         ROOT/'artifacts/stage2/font-quality-20261006/route-b-v2-eight-glyphs.json',
         ROOT/args.candidate]
packs = [json.loads(p.read_text(encoding='utf-8')) for p in files]
chars = '永和国工王一二三'
reports = []
label = ImageFont.truetype(r'C:\Windows\Fonts\simhei.ttf',20)
preview = Image.new('RGB',(1600,780),'white')
draw = ImageDraw.Draw(preview)
for version,pack in enumerate(packs,1):
    rows = []
    draw.text((8,(version-1)*260+4),f'B v{version}',font=label,fill='black')
    for col,ch in enumerate(chars):
        glyph = next(g for g in pack['glyphs'] if g['c']==ch)
        ink = raster(ch)
        raw = generator.zhang_suen(generator.binary_close(ink))
        pruned = generator.prune_spurs(raw)
        path_raster = Image.new('L',size,0)
        painter = ImageDraw.Draw(path_raster)
        length = 0
        sharp = 0
        duplicates = 0.0
        edges = Counter()
        for flat in glyph['strokes']:
            pts = list(zip(flat[::2],flat[1::2]))
            painter.line([(round((x-x_offset)/scale+pad),round(baseline-(y-1)/scale)) for x,y in pts],fill=255,width=1)
            draw.line([(col*200+10+x*15,(version-1)*260+235-y*15) for x,y in pts],fill='#b00000',width=2)
            for a,b in zip(pts,pts[1:]):
                distance = math.dist(a,b)
                length += distance
                edge = tuple(sorted((a,b)))
                if edges[edge]: duplicates += distance
                edges[edge] += 1
            for a,b,c in zip(pts,pts[1:],pts[2:]):
                v=(b[0]-a[0],b[1]-a[1]); w=(c[0]-b[0],c[1]-b[1])
                norm=math.hypot(*v)*math.hypot(*w)
                if norm and (v[0]*w[0]+v[1]*w[1])/norm < -0.5: sharp += 1
        covered = np.array(path_raster.filter(ImageFilter.MaxFilter(7))) > 0
        mask_near = np.array(Image.fromarray((ink*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))) > 0
        drawn = np.array(path_raster)>0
        def coverage(skeleton):
            return float(np.count_nonzero(covered & skeleton)/np.count_nonzero(skeleton)) if skeleton.any() else 1.0
        rows.append({'char':ch,'strokes':len(glyph['strokes']),'sharp_turns':sharp,
                     'length_mm':length,'exact_retraced_mm':duplicates,
                     'raw_skeleton_coverage_3px':coverage(raw),
                     'pruned_skeleton_coverage_3px':coverage(pruned),
                     'path_inside_ink_3px':float(np.count_nonzero(drawn & mask_near)/np.count_nonzero(drawn))})
        draw.text((col*200+8,(version-1)*260+30),ch,font=label,fill='black')
    reports.append({'version':version,'pack':str(files[version-1]),'characters':rows,
                    'sharp_turns':sum(r['sharp_turns'] for r in rows),
                    'strokes':sum(r['strokes'] for r in rows),
                    'length_mm':sum(r['length_mm'] for r in rows),
                    'exact_retraced_mm':sum(r['exact_retraced_mm'] for r in rows),
                    'min_raw_coverage_3px':min(r['raw_skeleton_coverage_3px'] for r in rows),
                    'min_pruned_coverage_3px':min(r['pruned_skeleton_coverage_3px'] for r in rows)})
preview.save(out/'comparison.png')
(out/'metrics.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2),encoding='utf-8')
for report in reports:
    print(json.dumps({k:v for k,v in report.items() if k!='characters'},ensure_ascii=False))

if args.full_coverage:
    full = []
    for glyph in packs[-1]['glyphs']:
        raw = generator.zhang_suen(generator.binary_close(raster(glyph['c'])))
        paths = Image.new('L', size, 0)
        painter = ImageDraw.Draw(paths)
        assert glyph['strokes'], glyph['c']
        for flat in glyph['strokes']:
            assert len(flat) >= 4 and len(flat) % 2 == 0, glyph['c']
            assert all(math.isfinite(value) for value in flat), glyph['c']
            painter.line([(round((flat[i]-x_offset)/scale+pad),
                           round(baseline-(flat[i+1]-1)/scale))
                          for i in range(0,len(flat),2)],fill=255,width=1)
        near = np.array(paths.filter(ImageFilter.MaxFilter(7))) > 0
        coverage = float(np.count_nonzero(raw & near)/np.count_nonzero(raw))
        full.append({'char':glyph['c'], 'coverage_3px':coverage})
        if len(full) % 100 == 0:
            print('coverage checked:', len(full), flush=True)
    summary = {'glyphs':len(full), 'min_raw_coverage_3px':min(row['coverage_3px'] for row in full),
               'below_95_percent':[row for row in full if row['coverage_3px'] < 0.95],
               'characters':full}
    (out/'full-coverage.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k!='characters'},ensure_ascii=False),flush=True)
    assert not summary['below_95_percent'], 'Candidate omitted source skeleton beyond tolerance'
