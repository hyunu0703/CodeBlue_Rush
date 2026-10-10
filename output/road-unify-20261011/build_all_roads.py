from pathlib import Path
import hashlib
import json
import re
import shutil
import subprocess
from io import BytesIO
import numpy as np
from PIL import Image, ImageDraw, ImageChops

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
ROAD = ROOT / 'Assets/Images/Road'
DEST = OUT / 'final'
DEST.mkdir(exist_ok=True)
N, S, C = 1254, 5, 627.0
LANE, WALK, DASH, GAP = 84.6, 64.0, 30.0, 27.0
working_originals = {p.name: p.read_bytes() for p in ROAD.glob('*.png')}
originals = {name: subprocess.check_output(['git','show',f'HEAD:Assets/Images/Road/{name}'],cwd=ROOT)
             for name in working_originals}
source = Image.open(BytesIO(originals['Road_Cross_4Lane.png'])).convert('RGBA')
protected_paths = [*ROAD.glob('*.meta'), ROOT/'Assets/Scenes/SampleScene.unity',
                   ROOT/'Assets/Images/Car/Ambulance.png', ROOT/'Assets/Images/Car/Ambulance.png.meta']
protected = {str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected_paths}

def rotate(points, turn):
    for _ in range(turn):
        points = [(N-y,x) for x,y in points]
    return points

def polygon(d, points, fill):
    d.polygon([(round(x*S),round(y*S)) for x,y in points],fill=fill)

def line(d, points, fill, width):
    d.line([(round(x*S),round(y*S)) for x,y in points],fill=fill,width=round(width*S),joint='curve')

def rect(d, box, color):
    x0,y0,x1,y1=box
    q=lambda value:int(np.floor(value*S+0.5))
    d.rectangle((q(x0),q(y0),q(x1)-1,q(y1)-1),fill=color)

def sample(box):
    a=np.array(source.crop(box).convert('RGB'))
    a=np.concatenate((a,a[:,::-1]),axis=1)
    a=np.concatenate((a,a[::-1]),axis=0)
    a=np.tile(a,(N//a.shape[0]+1,N//a.shape[1]+1,1))[:N,:N]
    return Image.fromarray(a).resize((N*S,N*S),Image.Resampling.BICUBIC)

asphalt=sample((520,520,730,730))
sidewalk=sample((445,105,486,129))
white, yellow=(238,236,233,255),(247,194,63,255)

def sidewalk_corner(left):
    end=left-36.8
    outer_end=left-WALK-26.5
    corner=[(left-WALK,0),(left,0),(left,end),(end,left),
            (0,left),(0,left-WALK),(outer_end,left-WALK),(left-WALK,outer_end)]
    layer=Image.new('RGBA',(N*S,N*S))
    d=ImageDraw.Draw(layer)
    for t in range(20,int(left)-17,40):
        line(d,[(left-WALK,t),(left-8,t)],(119,120,120,255),1)
        line(d,[(left-WALK,t+1),(left-8,t+1)],(203,202,200,255),0.6)
        line(d,[(t,left-WALK),(t,left-8)],(119,120,120,255),1)
        line(d,[(t+1,left-WALK),(t+1,left-8)],(203,202,200,255),0.6)
    for p in (left-WALK+28,left-WALK+56):
        line(d,[(p,0),(p,end)],(127,128,128,255),1)
        line(d,[(0,p),(end,p)],(127,128,128,255),1)
    curb=[(left,0),(left,end),(end,left),(0,left)]
    for color,width in (((33,38,41,255),16),((174,176,175,255),12),((224,223,219,255),4)):
        line(d,curb,color,width)
    for t in range(30,int(end),40):
        line(d,[(left-8,t),(left,t)],(58,63,65,255),1.4)
        line(d,[(t,left-8),(t,left)],(58,63,65,255),1.4)
    line(d,[(left-WALK,0),(left-WALK,outer_end),(outer_end,left-WALK),(0,left-WALK)],(56,61,63,255),3)
    mask=Image.new('L',layer.size)
    polygon(ImageDraw.Draw(mask),corner,255)
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'),mask))
    return corner,layer

def straight_side(left):
    layer=Image.new('RGBA',(N*S,N*S))
    d=ImageDraw.Draw(layer)
    for t in range(20,N,40):
        line(d,[(left-WALK,t),(left-8,t)],(119,120,120,255),1)
        line(d,[(left-WALK,t+1),(left-8,t+1)],(203,202,200,255),0.6)
    for p in (left-WALK+28,left-WALK+56):
        line(d,[(p,0),(p,N)],(127,128,128,255),1)
    for color,width in (((33,38,41,255),16),((174,176,175,255),12),((224,223,219,255),4)):
        line(d,[(left,0),(left,N)],color,width)
    for t in range(30,N,40):
        line(d,[(left-8,t),(left,t)],(58,63,65,255),1.4)
    line(d,[(left-WALK,0),(left-WALK,N)],(56,61,63,255),3)
    mask=Image.new('L',layer.size)
    polygon(ImageDraw.Draw(mask),[(left-WALK,0),(left,0),(left,N),(left-WALK,N)],255)
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'),mask))
    return layer

def render(kind, lanes):
    left,right=C-lanes*LANE/2,C+lanes*LANE/2
    end=left-36.8
    cross_start=end-56
    road_mask=Image.new('L',(N*S,N*S))
    rd=ImageDraw.Draw(road_mask)
    walk_mask=Image.new('L',road_mask.size)
    wd=ImageDraw.Draw(walk_mask)
    walk_surface=sidewalk.convert('RGBA')
    if kind=='Straight':
        polygon(rd,[(left,0),(right,0),(right,N),(left,N)],255)
        side=straight_side(left)
        for turn in (0,2):
            polygon(wd,rotate([(left-WALK,0),(left,0),(left,N),(left-WALK,N)],turn),255)
            walk_surface.alpha_composite(side.rotate(-90*turn))
    else:
        if kind=='Cross':
            points=[]
            for turn in range(4):
                points.extend(rotate([(left,0),(right,0),(right,end),(N-end,left)],turn))
            turns=range(4)
        else:
            points=[(0,left),(N,left),(N,right),(N-end,right),(right,N-end),
                    (right,N),(left,N),(left,N-end),(end,right),(0,right)]
            turns=(2,3)
            polygon(wd,[(0,left-WALK),(N,left-WALK),(N,left),(0,left)],255)
            walk_surface.alpha_composite(straight_side(left).rotate(-90))
        polygon(rd,points,255)
        corner,details=sidewalk_corner(left)
        for turn in turns:
            polygon(wd,rotate(corner,turn),255)
            walk_surface.alpha_composite(details.rotate(-90*turn))
    canvas=Image.new('RGBA',road_mask.size)
    canvas.paste(asphalt,(0,0),road_mask)
    canvas.paste(walk_surface,(0,0),walk_mask)
    approach=Image.new('RGBA',canvas.size)
    d=ImageDraw.Draw(approach)
    limit=N if kind=='Straight' else cross_start-30
    rect(d,(C-3,0,C+3,N if kind=='Straight' else cross_start-18),yellow)
    for lane in range(1,lanes):
        if lane==lanes//2:
            continue
        x=left+lane*LANE
        for i in range(22):
            y=GAP/2+i*(DASH+GAP)
            if y+DASH<=limit:
                rect(d,(x-2.5,y,x+2.5,y+DASH),white)
    count=0
    if kind!='Straight':
        rect(d,(left+10,cross_start-28,C-8,cross_start-18),white)
        count=int((lanes*LANE-24+9)//19)
        span=count*10+(count-1)*9
        for i in range(count):
            x=C-span/2+19*i
            rect(d,(x,cross_start,x+10,end),white)
        for x in (left-26,right+12):
            rect(d,(x,cross_start,x+14,end),(160,116,32,255))
            rect(d,(x+0.7,cross_start+0.7,x+13.3,end-0.7),yellow)
            for module in range(4):
                y=cross_start+module*14
                if module:
                    rect(d,(x,y-0.5,x+14,y+0.5),(179,132,35,255))
                for dx in (3.5,7,10.5):
                    for dy in (3.5,7,10.5):
                        cx,cy=(x+dx)*S,(y+dy)*S
                        r=0.8*S
                        d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(170,126,33,255))
                        d.ellipse((cx-r,cy-r,cx+0.2*S,cy+0.2*S),fill=(255,219,103,255))
    turns=(0,) if kind=='Straight' else range(4) if kind=='Cross' else (1,2,3)
    for turn in turns:
        canvas.alpha_composite(approach.rotate(-90*turn))
    return canvas.resize((N,N),Image.Resampling.LANCZOS),count

images={}
reports=[]
for lanes in (2,4,6):
    for kind in ('Cross','Straight','T'):
        name=f'Road_{kind}_{lanes}Lane.png'
        im,bars=render(kind,lanes)
        images[name]=im
        reports.append({'file':name,'lanes':lanes,'lane_px':LANE,'sidewalk_px':WALK,
                        'crosswalk_bars':bars,'original_bytes':len(originals[name])})
        print('Rendered',name,flush=True)

# Identical terminal pixel profiles remove texture/antialiasing discontinuities.
# The two edge pixels contain no dashes (the periodic half-gap is 13.5px).
for lanes in (2,4,6):
    reference=np.array(images[f'Road_Cross_{lanes}Lane.png'])[0].copy()
    for kind in ('Cross','Straight','T'):
        name=f'Road_{kind}_{lanes}Lane.png'
        a=np.array(images[name])
        if kind!='T':
            a[0]=reference
        a[-1]=reference
        if kind!='Straight':
            a[:,0]=reference
            a[:,-1]=reference
        images[name]=Image.fromarray(a)

rect_pattern=r'name: (\S+)\s+rect:\s+serializedVersion: \d+\s+x: (\d+)\s+y: (\d+)\s+width: (\d+)\s+height: (\d+)'
for report in reports:
    name=report['file']
    im=images[name]
    im.save(DEST/name,optimize=True,compress_level=9)
    a=np.array(im)
    assert im.size==(N,N) and im.mode=='RGBA'
    assert a[0,0,3]==0 and a[:,:,3].max()==255
    rects=re.findall(rect_pattern,(ROAD/(name+'.meta')).read_text())
    main=max(rects,key=lambda r:int(r[3])*int(r[4]))
    _,x,y,w,h=main
    x,y,w,h=map(int,(x,y,w,h))
    yy,xx=np.where(a[:,:,3]>0)
    contained=xx.min()>=x and xx.max()<x+w and yy.min()>=N-y-h and yy.max()<N-y
    assert contained, (name,'New artwork outside main sprite')
    report.update({'main_sprite':main[0],'main_sprite_contains_all_artwork':bool(contained),
                   'bytes':(DEST/name).stat().st_size,'ratio':(DEST/name).stat().st_size/len(originals[name])})

for lanes in (2,4,6):
    ref=np.array(images[f'Road_Cross_{lanes}Lane.png'])[0]
    for kind in ('Cross','Straight','T'):
        a=np.array(images[f'Road_{kind}_{lanes}Lane.png'])
        assert np.array_equal(a[-1],ref)
        if kind!='T': assert np.array_equal(a[0],ref)
        if kind!='Straight':
            assert np.array_equal(a[:,0],ref) and np.array_equal(a[:,-1],ref)
assert (DASH+GAP)*22==N
assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==v for p,v in protected.items())
assert all((ROAD/n).read_bytes()==data for n,data in working_originals.items()), 'Concurrent original edit'
for name in images:
    shutil.copyfile(DEST/name,ROAD/name)
assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==v for p,v in protected.items())
report={'files':reports,'lane_world_units':0.846,'dash_length':DASH,'dash_gap':GAP,
        'tactile_length':56,'tactile_width':14,'canvas':[N,N],
        'edge_profiles_identical':True,'scene_vehicle_and_meta_unchanged':True,
        'unity_validation':'not run; UNITY_PATH is TODO',
        'notes':['Primary sprites retain their existing nonuniform rects/pivots; align road centerlines when placing.',
                 'Unused auxiliary slices remain in unchanged meta and may now be transparent.']}
(OUT/'batch-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2),flush=True)

# Review contact sheet is outside Assets and does not change any import settings.
sheet=Image.new('RGB',(960,1038),(36,39,44))
draw=ImageDraw.Draw(sheet)
for row,kind in enumerate(('Cross','Straight','T')):
    for col,lanes in enumerate((2,4,6)):
        name=f'Road_{kind}_{lanes}Lane.png'
        thumb=images[name].resize((310,310),Image.Resampling.LANCZOS)
        x,y=col*320+5,row*346+24
        sheet.paste(thumb,(x,y),thumb)
        draw.text((x,y-18),name,fill=(240,240,240))
sheet.save(OUT/'all-roads-review.png',optimize=True)
