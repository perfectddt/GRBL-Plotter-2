"""Derive a compact punctuation variant without rewriting existing project fonts."""
import json, pathlib
root = pathlib.Path(__file__).resolve().parents[1]
source = root / 'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-full-metrics.fontpack.json'
pack = json.loads(source.read_text(encoding='utf-8'))
for glyph in pack['glyphs']:
    if glyph['c'] not in '，。、：；！？“”‘’（）【】':
        continue
    xs = [x for stroke in glyph['strokes'] for x in stroke[::2]]
    left, right = min(xs), max(xs)
    shift = 1.0-left
    glyph['strokes'] = [[round(v+shift,6) if i%2==0 else v for i,v in enumerate(stroke)] for stroke in glyph['strokes']]
    glyph['advanceMm'] = round(right-left+2,6)
pack['name'] = 'HanziWriter 全库·紧凑标点'
pack['advanceNote'] = 'Digits and narrow punctuation: original ink width plus sidebearings; glyphs only translated, never scaled. Chinese comma no longer reserves a full 12mm cell.'
out = source.with_name('hanziwriter-full-compact.fontpack.json')
out.write_text(json.dumps(pack,ensure_ascii=False,sort_keys=True,separators=(',',':')),encoding='utf-8',newline='\n')
print(out)
