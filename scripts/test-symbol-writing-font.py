"""Coverage, baseline preservation, finite pen paths, deterministic regeneration."""
import hashlib, importlib.util, json, math, pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
FONT=ROOT/'GRBL-Plotter/AutoWriter/Fonts'
spec=importlib.util.spec_from_file_location('symbols',ROOT/'scripts/generate-symbol-writing-font.py')
module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
path=FONT/'hanziwriter-full-symbols.fontpack.json'
pack=json.loads(path.read_text(encoding='utf-8')); glyphs={g['c']:g for g in pack['glyphs']}
assert len(glyphs)==len(pack['glyphs'])
assert pack==module.build()
required=''.join(chr(n) for n in range(33,127))+''.join(chr(n) for n in range(0xFF01,0xFF5F))+'，。！？、；：“”‘’《》〈〉「」『』【】〔〕〖〗±×÷≠≤≥≈∞√°℃‰¥￥€£¢←↑→↓↔↕•○●□■☆★✓✔✕'+''.join(chr(n) for n in range(0x2460,0x2474))
assert set(required)<=set(glyphs)
old=json.loads((FONT/'hanziwriter-full-compact.fontpack.json').read_text(encoding='utf-8'))
chinese=[g for g in old['glyphs'] if g['c'] not in old['supplementalSymbols']]
assert len(chinese)==9574
assert all(g==glyphs[g['c']] for g in chinese)
for c,g in glyphs.items():
    assert len(c)==1 and g['strokes'],repr(c)
    assert 0<g.get('advanceMm',12)<=24,c
    for stroke in g['strokes']:
        assert len(stroke)>=4 and len(stroke)%2==0,c
        assert all(math.isfinite(v) and 0<=v<=12 for v in stroke),(c,stroke)
        assert any(stroke[i:i+2]!=stroke[:2] for i in range(2,len(stroke),2)),c
for c in 'ABCDEFGHIJKLMNOPQRSTUVWXYZ': assert glyphs[c]['strokes']!=glyphs[c.lower()]['strokes']
for a,b in [('I','l'),('l','1'),('O','0')]: assert glyphs[a]['strokes']!=glyphs[b]['strokes']
for code in range(33,127):
    a=glyphs[chr(code)]; b=glyphs[chr(code+0xFEE0)]
    assert b['advanceMm']==12
    for s,t in zip(a['strokes'],b['strokes']):
        shift=t[0]-s[0]
        assert all(abs(v+(shift if i%2==0 else 0)-t[i])<1e-5 for i,v in enumerate(s))
print('PASS',len(glyphs),'glyphs; all 94 ASCII +94 fullwidth; 9574 Chinese unchanged; finite nonzero paths; deterministic SHA256',hashlib.sha256(path.read_bytes()).hexdigest())

if __name__=='__main__':
    # Actual pen strokes, rather than a substitute system font, for visual QA.
    from PIL import Image, ImageDraw, ImageFont
    groups=['ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz','0123456789','!\"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~','±×÷≠≤≥≈∞√°℃‰¥￥€£¢','←↑→↓↔↕•○●□■☆★✓✔✕','①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳','，。！？、；：“”‘’《》〈〉「」『』【】〔〕〖〗']
    width=1600; cell=72; height=sum(math.ceil(len(s)/21)*130 for s in groups)+80
    im=Image.new('RGB',(width,height),'white'); d=ImageDraw.Draw(im)
    f=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',13)
    y=30
    for chars in groups:
        for j,c in enumerate(chars):
            x=20+j%21*cell; top=y+j//21*130
            d.rectangle((x,top,x+64,top+76),outline='#dddddd')
            for s in glyphs[c]['strokes']:
                pts=[(x+5+s[i]*4.5,top+65-s[i+1]*4.5) for i in range(0,len(s),2)]
                d.line(pts,fill='#164c92',width=2)
            d.text((x+3,top+79),c,fill='black',font=f)
            d.text((x+3,top+98),f'{ord(c):04X}',fill='#666666',font=f)
        y+=math.ceil(len(chars)/21)*130
    out=ROOT/'artifacts/symbols/font-contact-sheet.png'; out.parent.mkdir(parents=True,exist_ok=True); im.save(out)
    print(out)
