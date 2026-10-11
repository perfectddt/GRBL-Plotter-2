"""Full pack geometry/provenance verification; source medians are compared point-for-point."""
import hashlib, importlib.util, json, pathlib, subprocess, sys
root=pathlib.Path(__file__).resolve().parents[1]
path=root/'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-full-metrics.fontpack.json'
legacy_path=root/'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-full.fontpack.json'
legacy_before=hashlib.sha256(legacy_path.read_bytes()).hexdigest()
pilot_path=root/'GRBL-Plotter/AutoWriter/Fonts/hanziwriter-pilot.fontpack.json'
pilot_before=hashlib.sha256(pilot_path.read_bytes()).hexdigest()
before=hashlib.sha256(path.read_bytes()).hexdigest()
subprocess.run([sys.executable,str(root/'scripts/generate-full-writing-font.py')],check=True)
assert before==hashlib.sha256(path.read_bytes()).hexdigest(), 'Generation not deterministic'
assert pilot_before==hashlib.sha256(pilot_path.read_bytes()).hexdigest(), 'Pilot changed'
assert legacy_before==hashlib.sha256(legacy_path.read_bytes()).hexdigest(), 'Legacy full pack changed'
assert legacy_before=='91316f86b396ba87396691b30fabc721e015e0f9e1a86d072939c3a45851c50f'
spec=importlib.util.spec_from_file_location('pilot',root/'scripts/generate-stage2-fontpack.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);m.BASELINE_MM=1.1
pack=json.loads(path.read_text(encoding='utf-8')); glyphs={g['c']:g for g in pack['glyphs']}
count=0
for src in pathlib.Path(m.DEFAULT_SRC).glob('*.json'):
    c=src.stem
    if len(c)!=1: continue
    expected=m.convert_strokes(json.loads(src.read_text(encoding='utf-8'))['medians'],m.BODY_MM/m.CAP_UNITS,(m.EM_MM-1024*m.BODY_MM/m.CAP_UNITS)/2,c)
    assert glyphs[c]['strokes']==expected, 'Median mismatch '+c
    count+=1
assert count==9574 and len(glyphs)==9619
assert all(c in glyphs for c in '纸张尺寸、四边边距。保存项目，重新生成。2026：“永和国工王一二三”！？；（）【】《》—…')
for g in glyphs.values():
    advance=g.get('advanceMm',pack['advanceCjkMm'])
    assert 0<advance<=48
    for s in g['strokes']:
        assert len(s)>=4 and len(s)%2==0
        assert all(0<=v<=12 for v in s)
        assert len(set(zip(s[::2],s[1::2])))>1
        assert all(0<=x<=advance for x in s[::2]), 'Ink exceeds advance '+g['c']
assert all(glyphs[c]['advanceMm']==8 for c in '0123456789')
assert glyphs[',']['advanceMm']<12 and glyphs['!']['advanceMm']<12
assert 'advanceMm' not in glyphs['，']
print('PASS: 9574 source medians point-for-point; 9619 glyphs in em; deterministic SHA256 '+before+'; pilot unchanged '+pilot_before)
