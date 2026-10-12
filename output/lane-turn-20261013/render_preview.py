"""실제 런타임 경로 계산 결과를 기존 도로 PNG 위에 표시한다. 원본 이미지는 수정하지 않는다."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

# 프로젝트 루트와 검토용 출력 폴더다.
ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
DATA = json.loads((OUT / "layout-paths.json").read_text(encoding="utf-8"))
TILE, PAD, HEAD = 380, 20, 42
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 15)
colors = {"Left": "#ffc64a", "Right": "#62f08d", "Straight": "#cbd9ff"}
canvas = Image.new("RGB", (4 * (TILE + PAD) + PAD, 4 * (TILE + HEAD + PAD) + HEAD), "#20252d")
draw = ImageDraw.Draw(canvas)
draw.text((PAD, 12), "Lane paths: cyan = road lanes | gold = left | green = right | red = stop boundary", font=font, fill="white")

# 13종 모두 같은 좌표계/차로 폭으로 시각화한다.
for ordinal, road in enumerate(DATA):
    x = PAD + ordinal % 4 * (TILE + PAD)
    y = HEAD + ordinal // 4 * (TILE + HEAD + PAD)
    draw.text((x, y + 6), road["name"], font=font, fill="white")
    image = Image.open(ROOT / "Assets/Images/Road" / (road["name"] + ".png")).convert("RGBA").resize((TILE, TILE))
    canvas.paste(image, (x, y + HEAD), image)
    project = lambda p: (x + (p[0] / 12.54 + 0.5) * TILE, y + HEAD + (0.5 - p[1] / 12.54) * TILE)
    for path in road["paths"]:
        points = [project(p) for p in path["points"]]
        color = colors[path["maneuver"]] if path["kind"] == "Intersection" else "#4ee4f2"
        draw.line(points, fill=color, width=2)
        # 진행 방향 화살표는 경로 중앙의 실제 선분 방향을 따른다.
        i = max(1, len(points) // 2)
        a, b = points[i - 1], points[i]
        dx, dy = b[0] - a[0], b[1] - a[1]
        length = (dx * dx + dy * dy) ** 0.5
        if length:
            dx, dy = dx / length, dy / length
            center = ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5)
            tip = (center[0] + 5 * dx, center[1] + 5 * dy)
            draw.line([tip, (center[0] - 4 * dx + 3 * dy, center[1] - 4 * dy - 3 * dx)], fill=color, width=2)
            draw.line([tip, (center[0] - 4 * dx - 3 * dy, center[1] - 4 * dy + 3 * dx)], fill=color, width=2)
        if path["stop"]:
            a, b = points[-2], points[-1]
            dx, dy = b[0] - a[0], b[1] - a[1]
            length = (dx * dx + dy * dy) ** 0.5
            width = TILE * 84.6 / 1254 * 0.45
            draw.line([(b[0] + dy / length * width, b[1] - dx / length * width),
                       (b[0] - dy / length * width, b[1] + dx / length * width)], fill="#ff6065", width=3)
canvas.save(OUT / "lane-paths-review.png")
print(OUT / "lane-paths-review.png")
