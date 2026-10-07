"""Offline visual QA: source font, both packs, and factory-direction G-code."""
import json
import re
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/stage2/font-quality-20261006'
OUT.mkdir(parents=True, exist_ok=True)
CHARS = '永和国工王一二三'
FONT = ImageFont.truetype(r'C:\Windows\Fonts\simkai.ttf', 140)
LABEL = ImageFont.truetype(r'C:\Windows\Fonts\simhei.ttf', 20)
packs = [json.loads((ROOT / p).read_text(encoding='utf-8')) for p in (
    'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-pilot.fontpack.json',
    'artifacts/stage2/fontpack-ttf-kaiti.json')]
canvas = Image.new('RGB', (1600, 1150), 'white')
d = ImageDraw.Draw(canvas)
for row, title in enumerate(('TTF 源字形（粗细来自字体）', 'A：HanziWriter 中位线（按源笔顺）', 'B：现有 TTF 骨架（启发式笔顺）')):
    top = row * 245
    d.text((12, top + 4), title, font=LABEL, fill='black')
    for col, ch in enumerate(CHARS):
        x = col * 200
        d.text((x + 12, top + 30), ch, font=LABEL, fill='black')
        if row == 0:
            d.text((x + 20, top + 52), ch, font=FONT, fill='black')
        else:
            glyph = next(g for g in packs[row-1]['glyphs'] if g['c'] == ch)
            for stroke in glyph['strokes']:
                pts = [(x + 10 + stroke[i]*15, top + 225-stroke[i+1]*15)
                       for i in range(0, len(stroke), 2)]
                d.line(pts, fill='#b00000', width=2)
            d.text((x+12, top+220), f"{len(glyph['strokes'])} strokes", font=LABEL, fill='black')

def gcode_paths(path):
    strokes, current = [], []
    x = y = 0.0
    down = False
    for line in path.read_text(encoding='utf-8').splitlines():
        z = re.search(r'\bZ(-?[\d.]+)', line)
        if z:
            new_down = float(z[1]) == 5
            if down and not new_down and current:
                strokes.append(current)
                current = []
            down = new_down
            if down: current = [(x,y)]
        xy = re.search(r'\bX(-?[\d.]+) Y(-?[\d.]+)', line)
        if xy:
            x,y = float(xy[1]),float(xy[2])
            if down: current.append((x,y))
    if current: strokes.append(current)
    return strokes

for col, name in enumerate(('stage2-fontpack-ttf-sample-6mm.before.nc', 'stage2-fontpack-ttf-sample-6mm.nc')):
    path = OUT/name if col==0 else ROOT/'artifacts/stage2'/name
    if not path.exists(): continue
    strokes = gcode_paths(path)
    pts = [p for s in strokes for p in s]
    minx = min(p[0] for p in pts); miny = min(p[1] for p in pts)
    d.text((col*800+12, 750), ('旧样本：厂商方向纸面（+Y朝下）' if col==0 else '修正样本：厂商方向纸面（+Y朝下）'),font=LABEL,fill='black')
    for stroke in strokes:
        d.line([(col*800+20+(x-minx)*21, 805+(y-miny)*17) for x,y in stroke],fill='#b00000',width=2)
canvas.save(OUT/'comparison.png')

# Route A must preserve every source median and point, allowing only declared mm rounding.
route_a_pack = packs[0]
scale = 10/880; offset = (12-1024*scale)/2
for glyph in route_a_pack['glyphs']:
    source = json.loads((ROOT/'artifacts/stage2/src/package'/f"{glyph['c']}.json").read_text(encoding='utf-8'))
    expected = [[v for x,y in stroke for v in (round(x*scale+offset,3),round(y*scale+1,3))]
                for stroke in source['medians']]
    assert expected == glyph['strokes'], glyph['c']
direction_checks = {}
profile = json.loads((ROOT/'artifacts/stage1/machine-profile-continuous-pen-confirmed.json').read_text(encoding='utf-8'))
current_checks = {}
for index, name in enumerate(('stage2-fontpack-sample-6mm', 'stage2-fontpack-ttf-sample-6mm')):
    lookup = {g['c']:g['strokes'] for g in packs[index]['glyphs']}
    expected = []
    for row, line in enumerate(('永和国工', '王一二三')):
        for col, char in enumerate(line):
            for stroke in lookup[char]:
                expected.append([(stroke[i]*0.6+col*9.2, stroke[i+1]*0.6-row*12.4)
                                 for i in range(0,len(stroke),2)])
    first = expected[0][0]
    expected = [[((x-first[0])*profile['axisMapping']['xDirection'],
                  (y-first[1])*profile['axisMapping']['yDirection']) for x,y in path]
                for path in expected]
    actual = gcode_paths(ROOT/'artifacts/stage2'/(name+'.nc'))
    assert len(expected) == len(actual), name
    for a,b in zip(expected,actual):
        assert len(a) == len(b), name
        assert all(abs(x-u)<0.00006 and abs(y-v)<0.00006 for (x,y),(u,v) in zip(a,b)), name
    current_checks[name] = len(actual)
    old = (OUT/(name+'.before.nc')).read_text(encoding='utf-8').splitlines()
    new = (ROOT/'artifacts/stage2'/(name+'.nc')).read_text(encoding='utf-8').splitlines()
    # Keep the historical direction-only regression where the glyph geometry is unchanged.
    if len(old) != len(new):
        direction_checks[name] = 'geometry changed; verified against current pack instead'
        continue
    count = 0
    for before, after in zip(old, new):
        b = re.search(r'\bY(-?[\d.]+)', before)
        n = re.search(r'\bY(-?[\d.]+)', after)
        if b:
            assert n and abs(float(b[1])+float(n[1])) < 0.000001, name
            assert before[:b.start()] == after[:n.start()] and before[b.end():] == after[n.end():], name
            count += 1
        else:
            assert before == after, name
    direction_checks[name] = count
report = {'routeA_source_medians_exact': len(route_a_pack['glyphs']), 'routeB_glyphs':len(packs[1]['glyphs']),
          'current_pack_to_gcode_verified_strokes':current_checks,
          'sample_Y_negation_only_verified_moves': direction_checks,
          'sample_stroke_counts':{ch:[len(next(g for g in p['glyphs'] if g['c']==ch)['strokes']) for p in packs] for ch in CHARS},
          'visual_quality_note':'Geometry tests do not establish handwriting quality; visually inspect comparison.png.'}
(OUT/'quality.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
