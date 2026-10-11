"""Deterministic original monoline Latin/symbol supplement. No outline tracing."""
import copy, json, math, pathlib
ROOT = pathlib.Path(__file__).resolve().parents[1]
FONT = ROOT / 'GRBL-Plotter/AutoWriter/Fonts'

def build():
    pack = json.loads((FONT/'hanziwriter-full-compact.fontpack.json').read_text(encoding='utf-8'))
    glyphs = {g['c']:g for g in pack['glyphs']}
    extra = {}
    def put(c, paths): extra[c] = paths
    def dot(x,y): return [x-.18,y,x,y+.18,x+.18,y,x,y-.18,x-.18,y]
    def ellipse(cx,cy,rx,ry):
        return [round(v,6) for i in range(25) for v in (cx+rx*math.cos(i*math.pi/12),cy+ry*math.sin(i*math.pi/12))]
    # All 52 letter paths are authored independently; lower-case x-height is 7,
    # capitals reach 10, baseline 2, descenders 0.5. I/l/1 and O/0 differ.
    upper = {
      'A':[[1,2,4,10,7,2],[2.1,5,5.9,5]], 'B':[[1,2,1,10,5,10,7,9,7,7,5,6,1,6],[5,6,7,5,7,3,5,2,1,2]],
      'C':[[7,9,5,10,3,10,1,8,1,4,3,2,5,2,7,3]], 'D':[[1,2,1,10,4,10,7,8,7,4,4,2,1,2]],
      'E':[[7,10,1,10,1,2,7,2],[1,6,6,6]], 'F':[[7,10,1,10,1,2],[1,6,6,6]],
      'G':[[7,9,5,10,3,10,1,8,1,4,3,2,7,2,7,6,4,6]], 'H':[[1,10,1,2],[7,10,7,2],[1,6,7,6]],
      'I':[[1,10,5,10],[3,10,3,2],[1,2,5,2]], 'J':[[2,10,7,10,7,4,5,2,3,2,1,4]],
      'K':[[1,10,1,2],[7,10,1,5,7,2]], 'L':[[1,10,1,2,7,2]],
      'M':[[1,2,1,10,4,5,7,10,7,2]], 'N':[[1,2,1,10,7,2,7,10]],
      'O':[ellipse(4,6,3,4)], 'P':[[1,2,1,10,5,10,7,8,7,7,5,6,1,6]],
      'Q':[ellipse(4,6,3,4),[5,4,8,1]], 'R':[[1,2,1,10,5,10,7,8,7,7,5,6,1,6],[4,6,7,2]],
      'S':[[7,9,5,10,3,10,1,8,2,6,6,5,7,3,5,2,3,2,1,3]], 'T':[[1,10,7,10],[4,10,4,2]],
      'U':[[1,10,1,4,3,2,5,2,7,4,7,10]], 'V':[[1,10,4,2,7,10]],
      'W':[[1,10,2.5,2,4,6,5.5,2,7,10]], 'X':[[1,10,7,2],[7,10,1,2]],
      'Y':[[1,10,4,6,7,10],[4,6,4,2]], 'Z':[[1,10,7,10,1,2,7,2]]}
    lower = {
      'a':[ellipse(3.5,4.5,2.5,2.5),[6,7,6,2,7,2]], 'b':[[1,10,1,2],ellipse(3.5,4.5,2.5,2.5)],
      'c':[[6,6,4,7,2,7,1,5,1,4,2,2,4,2,6,3]], 'd':[ellipse(3.5,4.5,2.5,2.5),[6,2,6,10]],
      'e':[[1,4.5,6,4.5,6,6,4,7,2,7,1,5,1,4,2,2,4,2,6,3]], 'f':[[2,2,2,9,3,10,5,10],[1,7,5,7]],
      'g':[ellipse(3.5,4.5,2.5,2.5),[6,7,6,1,5,.5,2,.5]], 'h':[[1,2,1,10],[1,5,3,7,5,7,6,6,6,2]],
      'i':[[2,7,2,2],dot(2,9)], 'j':[[3,7,3,1,2,.5,1,.5],dot(3,9)],
      'k':[[1,10,1,2],[6,7,1,4,6,2]], 'l':[[1,10,1,3,2,2,3,2]],
      'm':[[1,2,1,7],[1,6,2,7,3,7,4,6,4,2],[4,6,5,7,6,7,7,6,7,2]],
      'n':[[1,2,1,7],[1,5,3,7,5,7,6,6,6,2]], 'o':[ellipse(3.5,4.5,2.5,2.5)],
      'p':[[1,.5,1,7],ellipse(3.5,4.5,2.5,2.5)], 'q':[ellipse(3.5,4.5,2.5,2.5),[6,7,6,.5,7,1]],
      'r':[[1,2,1,7],[1,5,3,7,5,7]], 's':[[6,6,4,7,2,7,1,6,2,5,5,4,6,3,4,2,2,2,1,3]],
      't':[[3,10,3,3,4,2,6,2],[1,7,6,7]], 'u':[[1,7,1,3,2,2,4,2,6,4],[6,7,6,2]],
      'v':[[1,7,3.5,2,6,7]], 'w':[[1,7,2,2,4,5,6,2,7,7]], 'x':[[1,7,6,2],[6,7,1,2]],
      'y':[[1,7,3.5,2,6,7],[3.5,2,2,.5,1,.5]], 'z':[[1,7,6,7,1,2,6,2]]}
    extra.update(upper); extra.update(lower)
    # Existing original punctuation geometry remains the source where suitable.
    for c in '0123456789.,:;!?\"\'()[]+-=/': extra[c] = copy.deepcopy(glyphs[c]['strokes'])
    extra['0'] = [ellipse(4,6,3,4),[2,3,6,9]]
    extra['1'] = [[1,8,3,10,3,2],[1,2,5,2]]
    symbols = {
      '*':[[4,9,4,3],[1,7.5,7,4.5],[1,4.5,7,7.5]], '_':[[1,1,7,1]],
      '\\':[[1,10,7,2]], '|':[[3,11,3,1]], '^':[[1,7,4,10,7,7]], '`':[[2,10,4,8]],
      '~':[[1,5,2,6,3,6,5,4,6,4,7,5]], '<':[[7,9,1,6,7,3]], '>':[[1,9,7,6,1,3]],
      '{':[[6,11,4,11,3,10,3,7,1,6,3,5,3,2,4,1,6,1]],
      '}':[[1,11,3,11,4,10,4,7,6,6,4,5,4,2,3,1,1,1]],
      '#':[[3,10,2,2],[6,10,5,2],[1,7,7,7],[1,5,7,5]],
      '$':copy.deepcopy(upper['S'])+[[4,11,4,1]],
      '%':[ellipse(2,8,1,1.5),[1,2,7,10],ellipse(6,4,1,1.5)],
      '&':[[7,2,2,8,2,9,3,10,4,10,5,9,5,8,1,4,1,3,2,2,4,2,7,6]],
      '@':[ellipse(4,6,3.5,4),ellipse(4,6,1.5,2),[5.5,8,5.5,4,7,4,7.5,5]],
    }
    extra.update(symbols)
    extra.update({'±':copy.deepcopy(extra['+'])+[[2,1,10,1]], '×':[[1,9,7,3],[7,9,1,3]],
      '÷':[[1,6,7,6],dot(4,9),dot(4,3)], '≠':copy.deepcopy(extra['='])+[[3,2,8,10]],
      '≤':copy.deepcopy(extra['<'])+[[1,1,7,1]], '≥':copy.deepcopy(extra['>'])+[[1,1,7,1]],
      '≈':copy.deepcopy(extra['~'])+[[1,8,2,9,3,9,5,7,6,7,7,8]],
      '∞':[[4,6,2,8,1,8,0,7,0,5,1,4,2,4,4,6,6,8,7,8,8,7,8,5,7,4,6,4,4,6]],
      '√':[[1,5,3,2,5,10,10,10]], '°':[ellipse(3,9,1.2,1.2)],
      '€':copy.deepcopy(upper['C'])+[[0,7,5,7],[0,5,5,5]],
      '£':[[6,9,5,10,3,10,2,9,2,2,7,2],[1,6,5,6]],
      '¢':copy.deepcopy(lower['c'])+[[3.5,9,3.5,1]],
      '¥':copy.deepcopy(upper['Y'])+[[1,5,7,5],[1,3.5,7,3.5]],
      '‰':copy.deepcopy(extra['%'])+[ellipse(9,4,1,1.5)],
      '←':[[8,6,1,6,4,9],[1,6,4,3]], '→':[[1,6,8,6,5,9],[8,6,5,3]],
      '↑':[[4,1,4,10,1,7],[4,10,7,7]], '↓':[[4,10,4,1,1,4],[4,1,7,4]],
      '↔':[[1,6,9,6],[4,9,1,6,4,3],[6,9,9,6,6,3]],
      '↕':[[4,1,4,10],[1,7,4,10,7,7],[1,4,4,1,7,4]],
      '•':[ellipse(4,6,.5,.5)], '○':[ellipse(4,6,3,3)],
      '□':[[1,3,7,3,7,9,1,9,1,3]], '✓':[[1,5,3,2,7,9]],
      '✕':[[1,9,7,3],[7,9,1,3]], '·':[dot(3,6)]})
    for c,base in {'●':'○','■':'□','✔':'✓'}.items():
        extra[c]=copy.deepcopy(extra[base])
        if c=='●': extra[c]+=[ellipse(4,6,r,r) for r in (1,2)]
        elif c=='■': extra[c]+=[[1,y,7,y] for y in (4,5,6,7,8)]
        else: extra[c]+=[[1.4,5,3.4,2,7.4,9]]
    star=[round(v,6) for i in range(11) for v in (4+(3.7 if i%2==0 else 1.5)*math.cos(math.pi/2+i*math.pi/5),6+(3.7 if i%2==0 else 1.5)*math.sin(math.pi/2+i*math.pi/5))]
    extra['☆']=[star]; extra['★']=[star,ellipse(4,6,.6,.6)]
    aliases={'〈':'<','〉':'>','「':'[','」':']','『':'[','』':']','〔':'[','〕':']','〖':'[','〗':']','￥':'¥','−':'-','–':'-','‐':'-','‒':'-','‧':'·','∙':'·','∗':'*','∕':'/','∣':'|'}
    for c,b in aliases.items(): extra[c]=copy.deepcopy(extra[b])
    for c in '『』〖〗':
        extra[c].append([v+(.65 if i%2==0 else 0) for i,v in enumerate(extra[c][0])])
    extra['℃']=copy.deepcopy(extra['°'])+[[v+4 if i%2==0 else v for i,v in enumerate(s)] for s in upper['C']]
    # Circled numerals: scaled authored decimal glyphs within an original ring.
    for n in range(1,21):
        paths=[ellipse(6,6,5.5,5.5)]; digits=str(n)
        for j,d in enumerate(digits):
            paths += [[round(v*.35+2.1+j*3.1,6) if i%2==0 else round(v*.5+3,6) for i,v in enumerate(s)] for s in extra[d]]
        extra[chr(0x2460+n-1)]=paths
    for c,paths in extra.items():
        xs=[v for s in paths for v in s[::2]]; left,right=min(xs),max(xs)
        shift=.6-left
        glyphs[c]={'c':c,'advanceMm':round(right-left+1.2,6),'strokes':[[round(v+shift,6) if i%2==0 else v for i,v in enumerate(s)] for s in paths]}
    # Fullwidth is a centered cell, not horizontally stretched lettering.
    for code in range(33,127):
        c=chr(code); g=copy.deepcopy(glyphs[c]); g['c']=chr(code+0xFEE0)
        xs=[v for s in g['strokes'] for v in s[::2]]; shift=(12-(max(xs)-min(xs)))/2-min(xs)
        g['strokes']=[[round(v+shift,6) if i%2==0 else v for i,v in enumerate(s)] for s in g['strokes']]; g['advanceMm']=12
        glyphs[g['c']]=g
    pack['glyphs']=[glyphs[c] for c in sorted(glyphs,key=ord)]
    pack['name']='HanziWriter 全库·英文符号'
    pack['source']+='; ASCII/Latin/common symbols: original AutoWriter monoline paths'
    pack['license']+='; original supplemental paths: CC0-1.0'
    pack['supplementalSymbolSource']='Original authored geometric polylines; not human stroke order or TTF outlines.'
    pack['supplementalSymbols']=''.join(sorted(set(extra)|{chr(n) for n in range(0xFF01,0xFF5F)},key=ord))
    return pack

if __name__=='__main__':
    p=build(); out=FONT/'hanziwriter-full-symbols.fontpack.json'
    out.write_text(json.dumps(p,ensure_ascii=False,sort_keys=True,separators=(',',':')),encoding='utf-8',newline='\n')
    print('Glyphs:',len(p['glyphs']),'supplemental:',len(p['supplementalSymbols']),out)
