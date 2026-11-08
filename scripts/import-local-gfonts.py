"""Read the unencrypted glyph ZIP in local GFont v5 files; never decrypt metadata.
Format reference: https://github.com/jiangmuran/CNCDraw (gfont/reader.py).
No third-party font data is included in this script or committed to the repository.
"""
import argparse, hashlib, io, json, math, pathlib, struct, zipfile

def read_font(path):
    raw=path.read_bytes()
    version,length=struct.unpack_from('>ii',raw)
    if version==5:
        if length<0 or length>len(raw)-8:raise ValueError('Invalid metadata length')
        offset=8+length
    elif 1<=version<5:
        offset=raw.find(b'PK\x03\x04',4)
        if offset<0:raise ValueError('No unencrypted glyph ZIP')
    else:raise ValueError('Unsupported GFont header')
    glyphs=[]
    with zipfile.ZipFile(io.BytesIO(raw[offset:])) as archive:
        for entry in archive.infolist():
            if entry.is_dir(): continue
            data=archive.read(entry)
            cp,n=struct.unpack_from('>Hi',data)
            if cp<=32 or 0xD800<=cp<=0xDFFF: continue
            if n==0:continue
            if n<0 or n%2 or n>1000000: raise ValueError('Invalid coordinates')
            coords=struct.unpack_from('>'+str(n)+'f',data,6)
            offset=6+4*n
            count=struct.unpack_from('>i',data,offset)[0]
            if count!=n//2 or offset+4+count>len(data): raise ValueError('Invalid commands')
            commands=data[offset+4:offset+4+count];strokes=[];current=[]
            for i,cmd in enumerate(commands):
                if cmd not in (0,1): raise ValueError('Unsupported path command '+str(cmd))
                if cmd==0 and current:
                    if len(current)==2: current+=current
                    strokes.append(current);current=[]
                x,y=coords[2*i:2*i+2]
                if not math.isfinite(x) or not math.isfinite(y): raise ValueError('Non-finite coordinate')
                current += [x,y]
            if current:
                if len(current)==2: current+=current
                strokes.append(current)
            if strokes:glyphs.append({'c':chr(cp),'strokes':strokes})
    if not glyphs:raise ValueError('No glyphs')
    return glyphs

def convert(path,out):
    glyphs=read_font(path);xs=[v for g in glyphs for s in g['strokes'] for v in s[::2]];ys=[v for g in glyphs for s in g['strokes'] for v in s[1::2]]
    left,right,top,bottom=min(xs),max(xs),min(ys),max(ys);scale=10/max(right-left,bottom-top)
    for g in glyphs:
        for s in g['strokes']:
            for i in range(0,len(s),2):s[i],s[i+1]=round(1+(s[i]-left)*scale,6),round(1+(bottom-s[i+1])*scale,6)
    digest=hashlib.sha256(path.read_bytes()).hexdigest();target=out/(path.stem+'-'+digest[:10]+'.fontpack.json')
    pack=dict(schemaVersion=1,name=path.stem,source='Local GFont: '+str(path)+'; sha256='+digest,license='Original font license applies; local use; not redistributed',emSizeMm=12,bodySizeMm=10,baselineMm=1,advanceCjkMm=12,advanceSpaceMm=6,glyphs=glyphs)
    target.write_text(json.dumps(pack,ensure_ascii=False,separators=(',',':'),sort_keys=True),encoding='utf-8')
    pathlib.Path(str(target)+'.info.json').write_text(json.dumps(dict(name=pack['name'],glyphCount=len(glyphs),length=target.stat().st_size),ensure_ascii=False),encoding='utf-8')
    try:
        from PIL import Image, ImageDraw
        im=Image.new('RGB',(110,90),'white');draw=ImageDraw.Draw(im);lookup={g['c']:g for g in glyphs}
        sample=[lookup[c] for c in '永和国工王一' if c in lookup][:4]
        if not sample:sample=glyphs[:4]
        for j,g in enumerate(sample):
            for s in g['strokes']:draw.line([(5+(j%2)*52+s[i]*4,5+(j//2)*40+(12-s[i+1])*3) for i in range(0,len(s),2)],fill='black',width=1)
        im.save(str(target)+'.preview.png')
    except ImportError:pass
    return dict(file=path.name,status='ok',glyphs=len(glyphs),output=str(target),sha256=digest,scale=scale,sourceBounds=[left,top,right,bottom])

def main():
    parser=argparse.ArgumentParser();parser.add_argument('source');parser.add_argument('output');args=parser.parse_args();out=pathlib.Path(args.output);out.mkdir(parents=True,exist_ok=True);report=[]
    for path in sorted(pathlib.Path(args.source).glob('*.gfont')):
        try:row=convert(path,out)
        except Exception as error:row=dict(file=path.name,status='failed',error=str(error))
        report.append(row);print(path.name,row['status'],flush=True)
    (out/'import-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Imported',sum(r['status']=='ok' for r in report),'of',len(report))
    if any(r['status']!='ok' for r in report):raise SystemExit(1)
if __name__=='__main__':main()
