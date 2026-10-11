#!/usr/bin/env python3
"""Generate the distinct full Chinese writing pack, preserving the pilot byte-for-byte."""
import importlib.util, json, pathlib, subprocess, sys, tempfile
ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('pilot', ROOT / 'scripts/generate-stage2-fontpack.py')
pilot = importlib.util.module_from_spec(spec); spec.loader.exec_module(pilot)
# Shift every Chinese median by the same 0.1 mm; source floor -96 now fits em.
pilot.BASELINE_MM = 1.1
out = ROOT / 'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-full-metrics.fontpack.json'
report = ROOT / 'artifacts/stage2/full-fontpack-report.txt'
sys.argv = [sys.argv[0], '--all', '--out', str(out), '--report', str(report)]
pilot.main()
pack = json.loads(out.read_text(encoding='utf-8'))
glyphs = {g['c']: g for g in pack['glyphs']}
# Original simple monoline symbols, in the same 12 mm em, Y up.
# A small ring avoids a zero-length dot which the writing service rejects.
def dot(x,y): return [x-.3,y,x,y+.3,x+.3,y,x,y-.3,x-.3,y]
def put(chars, strokes):
    for c in chars: glyphs[c] = {'c':c,'strokes':strokes}
put('。', [[2,1.6,2.5,2.1,3,1.6,2.5,1.1,2,1.6]])
put(',.，', [dot(2.5,2),[2.6,1.7,2.1,.8]])
# ASCII period has no comma tail.
put('.', [dot(2.5,1.6)])
put('、', [[2,3,3.2,1.6]])
put(':：', [dot(3,7),dot(3,2)])
put(';；', [dot(3,7),dot(3,2),[3.1,1.7,2.6,.8]])
put('!！', [[5,10.5,5,4],dot(5,2)])
put('?？', [[3,9,4,10.5,6,10.5,7.5,9,7.5,7.5,5,5.5,5,4],dot(5,2)])
put('“', [[3,10.5,2,9.5,2,8.2],[6,10.5,5,9.5,5,8.2]])
put('”', [[3,8.2,4,9.5,4,10.5],[6,8.2,7,9.5,7,10.5]])
put('‘', [[4,10.5,3,9.5,3,8.2]])
put('’', [[4,8.2,5,9.5,5,10.5]])
put('"', [[4,10,4,8],[7,10,7,8]])
put("'", [[5,10,5,8]])
put('(（', [[7,11,5.5,9,5,6,5.5,3,7,1]])
put(')）', [[5,11,6.5,9,7,6,6.5,3,5,1]])
put('[【', [[7,11,4,11,4,1,7,1]])
put(']】', [[5,11,8,11,8,1,5,1]])
put('《', [[6,11,3,6,6,1],[9,11,6,6,9,1]])
put('》', [[3,11,6,6,3,1],[6,11,9,6,6,1]])
put('-—', [[1.5,6,10.5,6]])
put('…', [dot(2,6),dot(6,6),dot(10,6)])
put('+', [[2,6,10,6],[6,10,6,2]])
put('=', [[2,7.5,10,7.5],[2,4.5,10,4.5]])
put('/', [[3,1,9,11]])
# Decimal digits: authored seven-segment monoline geometry, not source medians.
segments = [[3,10.5,9,10.5],[9,10.5,9,6],[9,6,9,1.5],[3,1.5,9,1.5],[3,6,3,1.5],[3,10.5,3,6],[3,6,9,6]]
for c, ids in zip('0123456789', ['012345','12','01643','01236','1256','05236','054236','012','0123456','012356']):
    put(c, [segments[int(i)] for i in ids])
added = sorted(set(glyphs) - {g['c'] for g in pack['glyphs']}, key=ord)
# Keep Chinese punctuation conventionally fullwidth. ASCII symbols and digits
# use their original ink width plus 1 mm sidebearings; only translate X, never
# scale the paths. Digits share an 8 mm cell, including the narrow digit 1.
for c in added:
    if not c.isascii(): continue
    g = glyphs[c]
    xs = [x for s in g['strokes'] for x in s[::2]]
    left, right = min(xs), max(xs)
    if c.isdigit():
        shift, advance = -2.0, 8.0
    else:
        shift, advance = 1.0-left, right-left+2.0
    g['strokes'] = [[round(v+shift, 6) if i%2==0 else v for i,v in enumerate(s)] for s in g['strokes']]
    g['advanceMm'] = round(advance, 6)
pack['glyphs'] = [glyphs[c] for c in sorted(glyphs,key=ord)]
pack['name'] = 'HanziWriter 全库9574字·标点数字·宽度优化'
pack['source'] += '; punctuation and digits: original AutoWriter monoline definitions'
pack['license'] += '; original punctuation/digits: CC0-1.0 (Fonts/LICENSE-autowriter-symbols.txt)'
pack['supplementalSymbols'] = ''.join(added)
pack['supplementalSymbolSource'] = 'Original simple monoline paths; digits use seven-segment geometry; not HanziWriter stroke medians.'
pack['baselineCalibrationNote'] = 'Full Chinese medians translated uniformly +0.1mm from pilot; baseline 1.1mm keeps minimum source y=-96 inside em without clipping.'
pack['advanceNote'] = 'Optional advanceMm: digits 8mm; ASCII punctuation ink width plus 1mm sidebearings; Chinese punctuation fullwidth 12mm. Stroke geometry only translated in X, never stretched.'
out.write_text(json.dumps(pack,ensure_ascii=False,sort_keys=True,separators=(',',':')),encoding='utf-8',newline='\n')
with report.open('a',encoding='utf-8') as f:
    f.write('supplemental symbols (%d): %s\nfinal glyphs: %d\nLatin letters: not included\n' % (len(added), ''.join(added),len(glyphs)))
print('Final glyphs:',len(glyphs),'Supplemental:',len(added))
