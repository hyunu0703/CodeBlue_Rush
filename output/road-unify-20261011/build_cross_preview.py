from pathlib import Path
import hashlib
import json
import numpy as np
from PIL import Image, ImageDraw, ImageChops

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
SOURCE = ROOT / 'Assets/Images/Road/Road_Cross_4Lane.png'
N, S = 1254, 5
C, LANE, WALK = 627.0, 84.6, 64.0
LEFT, RIGHT = C - 2 * LANE, C + 2 * LANE
source = Image.open(SOURCE).convert('RGBA')
before = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
          for p in [* (ROOT / 'Assets/Images/Road').glob('*'),
                    ROOT / 'Assets/Scenes/SampleScene.unity',
                    ROOT / 'Assets/Images/Car/Ambulance.png'] if p.is_file()}

def rotate(points, turn):
    for _ in range(turn):
        points = [(N-y, x) for x, y in points]
    return points

def polygon(draw, points, fill):
    draw.polygon([(round(x*S), round(y*S)) for x,y in points], fill=fill)

def line(draw, points, fill, width):
    draw.line([(round(x*S), round(y*S)) for x,y in points], fill=fill,
              width=round(width*S), joint='curve')

def rect(draw, box, color):
    x0,y0,x1,y1 = box
    draw.rectangle((round(x0*S),round(y0*S),round(x1*S)-1,round(y1*S)-1),fill=color)

def tile_sample(box):
    patch = np.array(source.crop(box).convert('RGB'))
    patch = np.concatenate((patch,patch[:,::-1]),axis=1)
    patch = np.concatenate((patch,patch[::-1]),axis=0)
    return Image.fromarray(np.tile(patch,(N//patch.shape[0]+1,N//patch.shape[1]+1,1))[:N,:N]).resize((N*S,N*S),Image.Resampling.BICUBIC)

# Clean original asphalt and sidewalk surfaces, without painted markings/grout.
asphalt = tile_sample((520,520,730,730))
sidewalk = tile_sample((445,105,486,129))
road_points = []
for turn in range(4):
    road_points.extend(rotate([(LEFT,0),(RIGHT,0),(RIGHT,421),(833,LEFT)],turn))
road_mask = Image.new('L',(N*S,N*S))
polygon(ImageDraw.Draw(road_mask),road_points,255)
walk_mask = Image.new('L',(N*S,N*S))
walk_draw = ImageDraw.Draw(walk_mask)
corner = [(LEFT-WALK,0),(LEFT,0),(LEFT,421),(421,LEFT),
          (0,LEFT),(0,LEFT-WALK),(367.3,LEFT-WALK),(LEFT-WALK,367.3)]
for turn in range(4):
    polygon(walk_draw,rotate(corner,turn),255)
canvas = Image.new('RGBA',(N*S,N*S))
canvas.paste(asphalt,(0,0),road_mask)

# One sidewalk corner is repeated by exact quarter turns.
details = Image.new('RGBA',(N*S,N*S))
d = ImageDraw.Draw(details)
for y in range(20,440,40):
    line(d,[(LEFT-WALK,y),(LEFT-8,y)],(119,120,120,255),1)
    line(d,[(LEFT-WALK,y+1),(LEFT-8,y+1)],(203,202,200,255),0.6)
for x in (LEFT-WALK+28,LEFT-WALK+56):
    line(d,[(x,0),(x,421)],(127,128,128,255),1)
for x in range(20,440,40):
    line(d,[(x,LEFT-WALK),(x,LEFT-8)],(119,120,120,255),1)
    line(d,[(x+1,LEFT-WALK),(x+1,LEFT-8)],(203,202,200,255),0.6)
for y in (LEFT-WALK+28,LEFT-WALK+56):
    line(d,[(0,y),(421,y)],(127,128,128,255),1)
curb = [(LEFT,0),(LEFT,421),(421,LEFT),(0,LEFT)]
line(d,curb,(33,38,41,255),16)
line(d,curb,(174,176,175,255),12)
line(d,curb,(224,223,219,255),4)
for t in range(30,421,40):
    line(d,[(LEFT-8,t),(LEFT,t)],(58,63,65,255),1.4)
    line(d,[(t,LEFT-8),(t,LEFT)],(58,63,65,255),1.4)
line(d,[(LEFT-WALK,0),(LEFT-WALK,367.3),(367.3,LEFT-WALK),(0,LEFT-WALK)],(56,61,63,255),3)
corner_mask = Image.new('L',(N*S,N*S))
polygon(ImageDraw.Draw(corner_mask),corner,255)
details.putalpha(ImageChops.multiply(details.getchannel('A'),corner_mask))
walk_surface = sidewalk.convert('RGBA')
for turn in range(4):
    rotated = details.rotate(-90*turn)
    walk_surface.alpha_composite(rotated)
canvas.paste(walk_surface,(0,0),walk_mask)

# A single approach includes both crosswalk entries; all dimensions are shared.
approach = Image.new('RGBA',(N*S,N*S))
d = ImageDraw.Draw(approach)
white, yellow = (238,236,233,255), (247,194,63,255)
rect(d,(C-3,0,C+3,347),yellow)
for x in (C-LANE,C+LANE):
    for y in range(5,318,60):
        rect(d,(x-2.5,y,x+2.5,y+30),white)
rect(d,(LEFT+10,337,C-8,347),white)
for i in range(17):
    x = 470 + 19*i
    rect(d,(x,365,x+10,421),white)
for x in (LEFT-26,RIGHT+12):
    rect(d,(x,365,x+14,421),(160,116,32,255))
    rect(d,(x+0.7,365.7,x+13.3,420.3),yellow)
    for module in range(4):
        y = 365+module*14
        if module:
            rect(d,(x,y-0.5,x+14,y+0.5),(179,132,35,255))
        for dx in (3.5,7,10.5):
            for dy in (3.5,7,10.5):
                cx,cy = (x+dx)*S,(y+dy)*S
                r = 0.8*S
                d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=(170,126,33,255))
                d.ellipse((cx-r,cy-r,cx+0.2*S,cy+0.2*S),fill=(255,219,103,255))
for turn in range(4):
    canvas.alpha_composite(approach.rotate(-90*turn))
result = canvas.resize((N,N),Image.Resampling.LANCZOS)
dest = OUT / SOURCE.name
result.save(dest,optimize=True,compress_level=9)
after = {name:hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in before}
assert before == after, 'Original project assets changed'
assert result.size == source.size
assert result.getpixel((0,0))[3] == 0
report = {
    'source':str(SOURCE),'preview':str(dest),'size':[N,N],
    'vehicle_visible_width_px':282,'vehicle_ppu':100,'vehicle_scale':0.2,
    'vehicle_world_width':0.564,'lane_world_width':0.846,
    'road_ppu':100,'road_scale':1,'lane_pixels':LANE,
    'road_edges':[LEFT,RIGHT],'sidewalk_width':WALK,
    'crosswalk':{'bar_width':10,'gap':9,'bar_length':56,'count':17,'span':314},
    'tactile':{'length':56,'width':14,'module':14,'count':8},
    'centerline_width':6,'lane_marking':{'width':5,'dash':30,'gap':30},
    'stop_line':{'width':10,'setback':18},
    'source_bytes':SOURCE.stat().st_size,'preview_bytes':dest.stat().st_size,
    'size_ratio':dest.stat().st_size/SOURCE.stat().st_size,
    'original_assets_unchanged':before == after,
    'geometry_note':'5x coverage rasterization for fractional pixel edges; 90-degree copies of markings',
}
(OUT/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
