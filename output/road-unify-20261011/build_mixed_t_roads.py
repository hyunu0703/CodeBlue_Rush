from pathlib import Path
from io import BytesIO
import ast
import hashlib
import json
import subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageChops

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
ROAD = ROOT / 'Assets/Images/Road'
N, S, C = 1254, 5, 627.0
LANE, WALK, DASH, GAP = 84.6, 64.0, 30.0, 27.0
PAIRS = ((6,4),(4,2),(2,4),(4,6))
names = [f'Road_T_V{v}Lane_H{h}Lane.png' for v,h in PAIRS]
assert all(not (ROAD/name).exists() for name in names), 'Requested asset already exists'
protected = {p:hashlib.sha256(p.read_bytes()).hexdigest()
             for p in ROAD.iterdir() if p.is_file()}
protected[ROOT/'Assets/Scenes/SampleScene.unity'] = hashlib.sha256((ROOT/'Assets/Scenes/SampleScene.unity').read_bytes()).hexdigest()

# Use the same original texture samples and drawing helpers as the approved set.
original = subprocess.check_output(['git','show','8239844:Assets/Images/Road/Road_Cross_4Lane.png'],cwd=ROOT)
assert len(original)==1084872
source = Image.open(BytesIO(original)).convert('RGBA')
tree = ast.parse((OUT/'build_all_roads.py').read_text())
helpers = [node for node in tree.body if isinstance(node,ast.FunctionDef)
           and node.name in ('rotate','polygon','line','rect','sample','straight_side')]
exec(compile(ast.Module(body=helpers,type_ignores=[]),'approved_road_helpers','exec'),globals())
asphalt,sidewalk = sample((520,520,730,730)),sample((445,105,486,129))
white,yellow = (238,236,233,255),(247,194,63,255)

def corner(left,top):
    end_x,end_y = left-36.8,top-36.8
    outer_x,outer_y = left-WALK-26.5,top-WALK-26.5
    points = [(left-WALK,0),(left,0),(left,end_y),(end_x,top),
              (0,top),(0,top-WALK),(outer_x,top-WALK),(left-WALK,outer_y)]
    layer = Image.new('RGBA',(N*S,N*S))
    d = ImageDraw.Draw(layer)
    for t in range(20,int(top)-17,40):
        line(d,[(left-WALK,t),(left-8,t)],(119,120,120,255),1)
        line(d,[(left-WALK,t+1),(left-8,t+1)],(203,202,200,255),0.6)
    for t in range(20,int(left)-17,40):
        line(d,[(t,top-WALK),(t,top-8)],(119,120,120,255),1)
        line(d,[(t+1,top-WALK),(t+1,top-8)],(203,202,200,255),0.6)
    for x in (left-WALK+28,left-WALK+56):
        line(d,[(x,0),(x,end_y)],(127,128,128,255),1)
    for y in (top-WALK+28,top-WALK+56):
        line(d,[(0,y),(end_x,y)],(127,128,128,255),1)
    curb = [(left,0),(left,end_y),(end_x,top),(0,top)]
    for color,width in (((33,38,41,255),16),((174,176,175,255),12),((224,223,219,255),4)):
        line(d,curb,color,width)
    for t in range(30,int(end_y),40):
        line(d,[(left-8,t),(left,t)],(58,63,65,255),1.4)
    for t in range(30,int(end_x),40):
        line(d,[(t,top-8),(t,top)],(58,63,65,255),1.4)
    line(d,[(left-WALK,0),(left-WALK,outer_y),(outer_x,top-WALK),(0,top-WALK)],(56,61,63,255),3)
    mask = Image.new('L',layer.size)
    polygon(ImageDraw.Draw(mask),points,255)
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'),mask))
    return points,layer

def approach(lanes,perpendicular_lanes):
    left,right = C-lanes*LANE/2,C+lanes*LANE/2
    end = C-perpendicular_lanes*LANE/2-36.8
    start = end-56
    layer = Image.new('RGBA',(N*S,N*S))
    d = ImageDraw.Draw(layer)
    rect(d,(C-3,0,C+3,start-18),yellow)
    for lane in range(1,lanes):
        if lane==lanes//2:
            continue
        x = left+lane*LANE
        for i in range(22):
            y = GAP/2+i*(DASH+GAP)
            if y+DASH<=start-30:
                rect(d,(x-2.5,y,x+2.5,y+DASH),white)
    rect(d,(left+10,start-28,C-8,start-18),white)
    count = int((lanes*LANE-24+9)//19)
    span = count*10+(count-1)*9
    for i in range(count):
        x = C-span/2+19*i
        rect(d,(x,start,x+10,end),white)
    for x in (left-26,right+12):
        rect(d,(x,start,x+14,end),(160,116,32,255))
        rect(d,(x+0.7,start+0.7,x+13.3,end-0.7),yellow)
        for module in range(4):
            y = start+module*14
            if module:
                rect(d,(x,y-0.5,x+14,y+0.5),(179,132,35,255))
            for dx in (3.5,7,10.5):
                for dy in (3.5,7,10.5):
                    cx,cy = (x+dx)*S,(y+dy)*S
                    r = 0.8*S
                    d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(170,126,33,255))
                    d.ellipse((cx-r,cy-r,cx+0.2*S,cy+0.2*S),fill=(255,219,103,255))
    return layer

reports,images = [],{}
for v,h in PAIRS:
    vl,vr = C-v*LANE/2,C+v*LANE/2
    hl,hr = C-h*LANE/2,C+h*LANE/2
    road_mask = Image.new('L',(N*S,N*S))
    points = [(0,hl),(N,hl),(N,hr),(vr+36.8,hr),(vr,hr+36.8),
              (vr,N),(vl,N),(vl,hr+36.8),(vl-36.8,hr),(0,hr)]
    polygon(ImageDraw.Draw(road_mask),points,255)
    walk_mask = Image.new('L',road_mask.size)
    wd = ImageDraw.Draw(walk_mask)
    polygon(wd,[(0,hl-WALK),(N,hl-WALK),(N,hl),(0,hl)],255)
    surface = sidewalk.convert('RGBA')
    surface.alpha_composite(straight_side(hl).rotate(-90))
    for left,top,turn in ((vl,hl,2),(hl,vl,3)):
        points,layer = corner(left,top)
        polygon(wd,rotate(points,turn),255)
        surface.alpha_composite(layer.rotate(-90*turn))
    canvas = Image.new('RGBA',road_mask.size)
    canvas.paste(asphalt,(0,0),road_mask)
    canvas.paste(surface,(0,0),walk_mask)
    horizontal = approach(h,v)
    canvas.alpha_composite(horizontal.rotate(-90))
    canvas.alpha_composite(horizontal.rotate(-270))
    canvas.alpha_composite(approach(v,h).rotate(-180))
    im = canvas.resize((N,N),Image.Resampling.LANCZOS)
    a = np.array(im)
    vref = np.array(Image.open(ROAD/f'Road_Straight_{v}Lane.png').convert('RGBA'))[-1]
    href = np.array(Image.open(ROAD/f'Road_Straight_{h}Lane.png').convert('RGBA'))[0]
    a[-1] = vref
    a[:,0] = href
    a[:,-1] = href
    assert np.array_equal(a[-1],vref)
    assert np.array_equal(a[:,0],href) and np.array_equal(a[:,-1],href)
    assert not a[0,:,3].any() and a[:,:,3].max()==255
    name = f'Road_T_V{v}Lane_H{h}Lane.png'
    im = Image.fromarray(a)
    dest = ROAD/name
    im.save(dest,optimize=True,compress_level=9)
    images[name] = im
    reports.append({'file':name,'vertical_lanes':v,'horizontal_lanes':h,
                    'size':[N,N],'bytes':dest.stat().st_size,
                    'edge_rgba_matches_existing_straights':True,
                    'vertical_road_width_px':v*LANE,'horizontal_road_width_px':h*LANE,
                    'lane_px':LANE,'sidewalk_px':WALK,'tactile_px':[14,56],
                    'crosswalk_bars_vertical':int((v*LANE-15)//19),
                    'crosswalk_bars_horizontal':int((h*LANE-15)//19)})
    print('Added',name,dest.stat().st_size,flush=True)
assert all(hashlib.sha256(p.read_bytes()).hexdigest()==value for p,value in protected.items())
(OUT/'mixed-t-verification.json').write_text(json.dumps({'files':reports,'existing_assets_unchanged':True,
    'unity_import':'not run; new meta and Sprite setup require Unity import'},indent=2),encoding='utf-8')
sheet = Image.new('RGB',(840,900),(36,39,44))
d = ImageDraw.Draw(sheet)
for i,(name,im) in enumerate(images.items()):
    x,y = (i%2)*420+10,(i//2)*450+30
    thumb = im.resize((400,400),Image.Resampling.LANCZOS)
    sheet.paste(thumb,(x,y),thumb)
    d.text((x,y-20),name,fill=(240,240,240))
sheet.save(OUT/'mixed-t-review.png',optimize=True)
