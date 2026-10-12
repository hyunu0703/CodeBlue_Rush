"""Unity가 생성해 전달한 Prefab/Sprite/메타데이터와 공식 제원 비율을 정적 검사한다."""
from pathlib import Path
import json
import math
import re

# 최종 프로젝트의 에셋만 읽고 검사 결과는 작업 출력 폴더에 저장한다.
ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
specs = json.loads((OUT / "vehicle-specs.json").read_text(encoding="utf-8"))
folder = ROOT / "Assets/Prefabs/Car"
checks = 0
records = []

def require(value, label):
    global checks
    checks += 1
    if not value:
        raise AssertionError(label)

def guid(path):
    return re.search(r"(?m)^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8-sig")).group(1)

def vector(text, field):
    match = re.search(rf"{field}: \{{x: ([^,]+), y: ([^,]+), z: ([^}}]+)\}}", text)
    require(match is not None, field + " 누락")
    return tuple(float(v) for v in match.groups())

def scalar(text, field):
    match = re.search(rf"(?m)^\s*{field}: ([0-9.eE+-]+)$", text)
    require(match is not None, field + " 누락")
    return float(match.group(1))

# 파일 수와 모든 원본 텍스처 참조를 확인한다. Sprite 생성에 의해 원본 import 설정은 바뀌지 않아야 한다.
require(len(list(folder.glob("*.prefab"))) == 11, "차량 Prefab 11개")
require(len(list((folder / "Sprites").glob("*.asset"))) == 11, "전체 Sprite 11개")
require((folder.parent / "Car.meta").is_file(), "Car 폴더 .meta")
require((folder / "Sprites.meta").is_file(), "Sprites 폴더 .meta")
all_guids = set()
for spec in specs["vehicles"]:
    name = spec["name"]
    prefab = folder / (name + ".prefab")
    sprite = folder / "Sprites" / (name + ".asset")
    require(prefab.is_file() and sprite.is_file(), name + " 에셋 파일")
    require(prefab.with_suffix(".prefab.meta").is_file(), name + " Prefab .meta")
    require(sprite.with_suffix(".asset.meta").is_file(), name + " Sprite .meta")
    prefab_guid = guid(prefab.with_suffix(".prefab.meta"))
    sprite_guid = guid(sprite.with_suffix(".asset.meta"))
    require(prefab_guid not in all_guids and sprite_guid not in all_guids and prefab_guid != sprite_guid, name + " 고유 GUID")
    all_guids.update((prefab_guid, sprite_guid))
    text = prefab.read_text(encoding="utf-8-sig")
    sprite_text = sprite.read_text(encoding="utf-8-sig")
    transforms = [block for block in re.split(r"--- !u!", text) if block.startswith("4 &")]
    require(len(transforms) == 2, name + " 루트/Visual Transform")
    # Unity의 YAML 오브젝트 나열 순서는 계층 순서가 아니므로 부모 참조로 루트를 구분한다.
    root_transform = next(block for block in transforms if "m_Father: {fileID: 0}" in block)
    visual_transform = next(block for block in transforms if "m_Father: {fileID: 0}" not in block)
    root_scale = vector(root_transform, "m_LocalScale")
    visual_scale = vector(visual_transform, "m_LocalScale")
    require(all(math.isclose(v, 0.2, abs_tol=1e-7) for v in root_scale), name + " 루트 Scale 0.2")
    require(all(math.isclose(v, 0.0, abs_tol=1e-7) for v in vector(root_transform, "m_LocalPosition")), name + " 원점 Prefab")
    require("m_Name: " + name + "\n" in text and "m_Name: Visual\n" in text, name + " 이름")
    require(text.count("SpriteRenderer:") == 1 and "MonoBehaviour:" not in text, name + " 표시용 Prefab")
    require("guid: " + sprite_guid in text, name + " 전체 Sprite 참조")
    source_guid = guid(ROOT / "Assets/Images/Car" / (name + ".png.meta"))
    require("texture: {fileID: 2800000, guid: " + source_guid in sprite_text, name + " 원본 PNG 참조")
    require("m_Enabled: 1" in text and "m_IsActive: 1" in text and "m_SortingOrder: 1" in text, name + " Game View 표시 설정")
    ppu = scalar(sprite_text, "m_PixelsToUnits")
    rect = sprite_text.split("m_Rect:", 1)[1].split("m_Offset:", 1)[0]
    width = scalar(rect, "width") / ppu * root_scale[0] * visual_scale[0]
    length = scalar(rect, "height") / ppu * root_scale[1] * visual_scale[1]
    expected_width = 0.568 * spec["widthMm"] / 1995
    expected_length = 0.944 * spec["lengthMm"] / 5255
    require(math.isclose(width, expected_width, abs_tol=1e-5), name + " 전폭 표시 비율")
    require(math.isclose(length, expected_length, abs_tol=1e-5), name + " 전장 표시 비율")
    records.append({"name": name, "rootScale": root_scale, "worldWidth": width, "worldLength": length,
                    "prefabGuid": prefab_guid, "spriteGuid": sprite_guid})
result = {"passed": True, "assertions": checks, "prefabs": len(records), "sprites": 11, "vehicles": records}
(OUT / "delivery-verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(f"PASS: {checks} static delivery assertions; 11 Prefabs, 11 Sprites, root Scale 0.2 and relative dimensions verified.")
