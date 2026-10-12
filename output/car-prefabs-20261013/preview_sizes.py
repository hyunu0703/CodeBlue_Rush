"""제원과 구급차 표시 기준으로 크기 비교를 준비한다. Unity Game View 캡처가 아닌 계산 미리보기다."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

# 동일한 월드 유닛당 픽셀 수로 11종의 상대 표시 크기를 비교한다.
ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
data = json.loads((OUT / "vehicle-specs.json").read_text(encoding="utf-8"))
ref = data["reference"]
PIXELS_PER_UNIT = 250
CELL_WIDTH, CELL_HEIGHT, PAD = 230, 660, 20
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 15)
canvas = Image.new("RGB", (6 * CELL_WIDTH + PAD, 2 * CELL_HEIGHT + 80), "#27313b")
draw = ImageDraw.Draw(canvas)
draw.text((PAD, 12), "Calculated size preview | root Scale 0.2 | relative to existing Ambulance", font=font, fill="white")
draw.text((PAD, 34), "Not a Unity Game View capture. Sources and model assumptions: research.md", font=font, fill="#bcc9d8")
results = []
for i, car in enumerate(data["vehicles"]):
    # 차량 전체를 표시한다. 자동 Sprite 슬라이싱이나 원본 PNG/메타데이터를 수정하지 않는다.
    source = Image.open(ROOT / "Assets/Images/Car" / (car["name"] + ".png")).convert("RGBA")
    crop = source.getchannel("A").getbbox()
    source = source.crop(crop)
    width = ref["spriteWidthUnits"] * ref["rootScale"] * car["widthMm"] / ref["widthMm"]
    length = ref["spriteLengthUnits"] * ref["rootScale"] * car["lengthMm"] / ref["lengthMm"]
    size = (round(width * PIXELS_PER_UNIT), round(length * PIXELS_PER_UNIT))
    source = source.resize(size, Image.Resampling.LANCZOS)
    x, y = PAD + i % 6 * CELL_WIDTH, 70 + i // 6 * CELL_HEIGHT
    baseline = y + 575
    draw.text((x, y + 4), car["name"], font=font, fill="white")
    draw.text((x, y + 25), f'{car["lengthMm"]} x {car["widthMm"]} mm', font=font, fill="#b1c5df")
    draw.line((x, baseline, x + CELL_WIDTH - 10, baseline), fill="#8995a1")
    canvas.paste(source, (x + (CELL_WIDTH - source.width) // 2, baseline - source.height), source)
    draw.text((x, baseline + 20), f"world: {width:.4f} x {length:.4f}", font=font, fill="#d4e4ef")
    results.append({**car, "worldWidth": width, "worldLength": length, "sourceAlphaBounds": crop})
canvas.save(OUT / "calculated-size-preview.png")
(OUT / "calculated-sizes.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
assert len(results) == 11
assert max(results, key=lambda c: c["worldLength"])["name"] == "Bus"
assert min(results, key=lambda c: c["worldLength"])["name"] == "PCX"
assert abs(results[0]["worldWidth"] - 0.568) < 1e-8
assert abs(results[0]["worldLength"] - 0.944) < 1e-8
print("PASS: 11 calculated sizes; Ambulance baseline 0.568 x 0.944; Bus longest, PCX shortest.")
