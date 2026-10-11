import json, pathlib
root=pathlib.Path(__file__).resolve().parents[1]/'GRBL-Plotter/AutoWriter/Fonts'
a=json.loads((root/'hanziwriter-full-metrics.fontpack.json').read_text(encoding='utf-8'))
b=json.loads((root/'hanziwriter-full-compact.fontpack.json').read_text(encoding='utf-8'))
x={g['c']:g for g in a['glyphs']}; y={g['c']:g for g in b['glyphs']}
assert x.keys()==y.keys()
changed=0
for c,g in x.items():
    h=y[c]
    if g==h: continue
    assert c in '，。、：；！？“”‘’（）【】',c
    shift=h['strokes'][0][0]-g['strokes'][0][0]
    assert len(g['strokes'])==len(h['strokes'])
    for s,t in zip(g['strokes'],h['strokes']):
        assert len(s)==len(t)
        assert all(abs(v+(shift if i%2==0 else 0)-t[i])<1e-6 for i,v in enumerate(s)),c
    assert h['advanceMm']<12,c
    changed+=1
assert y['，']['advanceMm']<=3
assert changed==15,changed
print('PASS:',len(x),'glyph identities;',changed,'compact punctuation paths preserve geometry')

