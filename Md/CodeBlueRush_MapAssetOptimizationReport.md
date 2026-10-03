# Map Asset WebGL 용량 최적화 결과

기준: 최신 `main` / `origin/main` = `c772709`. Unity 6000.3.23f1. 2026-10-03.

Production Guide, Style Guide, WebGL Asset Optimization Guide 및 Master 원본을 먼저 확인했다. 이번 작업은 기존 PNG와 Import 설정 최적화이며 새 게임 이미지, Scene, Prefab, Runtime Script는 추가·변경하지 않았다. Git commit / push 없음.

## 용량 결과

| 범위 | 이전 PNG 합계 | 이후 PNG 합계 | 감소 | 감소율 |
|---|---:|---:|---:|---:|
| Maps 전체 24개, Master 포함 | 8,953,731 B | 6,006,613 B | 2,947,118 B | 32.91% |
| Master 제외 게임용 후보 23개 | 5,186,645 B | 2,239,527 B | 2,947,118 B | 56.82% |

압축 텍셀 페이로드 계산: 298,511,828 → 6,223,048 B (97.92% 감소). Unity가 실제 Import한 크기·포맷과 block 크기로 계산했으며 GPU driver/Unity overhead 및 fallback은 포함하지 않는다. Profiler 측정값이나 실제 WebGL 다운로드 감소량이 아니다. 현재 Gameplay는 이 PNG 23개를 사용하지 않으므로 현재 빌드의 즉각적인 용량 감소로 해석하지 않는다.

## 전체 PNG / Import 조사 및 적용

경로 기준: `Assets/Images/Maps/`. A = 최적화 필요, B = 원본 PNG 크기는 허용하되 Import 최적화 가능, C = 수정 불필요한 보호 Reference.

| 상대 경로 | 분류 | 이전 해상도 → 이후 해상도 | 이전 B → 이후 B | PPU 이전 → 이후 | Max Size 이전 → 이후 | 최종 WebGL 포맷 | PNG 수정 |
|---|:---:|---|---:|---:|---:|---|:---:|
| `Ground/Ground_Grass_Default_01.png` | A | 1254×1254 → 512×512 | 1,053,904 → 203,109 | 100 → 40.82934609 | 4096 → 512 | DXT1 | 수정 |
| `Nature/Tree/Tree_Medium_01.png` | A | 1254×1254 → 224×256 | 440,420 → 38,652 | 100 → 25 | 4096 → 256 | DXT5 | 수정 |
| `Reference/CodeBlueRush_Map_MasterReference.png` | C | 941×1672 → 941×1672 | 3,767,086 → 3,767,086 | 100 → 100 | 2048 → 2048 | Unchanged Reference | 보존 |
| `Road/Road_Corner_NE.png` | B | 2000×2000 → 2000×2000 | 94,146 → 94,146 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Corner_NW.png` | B | 2000×2000 → 2000×2000 | 81,808 → 81,808 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Corner_SE.png` | B | 2000×2000 → 2000×2000 | 81,842 → 81,842 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Corner_SW.png` | B | 2000×2000 → 2000×2000 | 91,964 → 91,964 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Curve_Tight_01.png` | B | 2000×2000 → 2000×2000 | 23,572 → 23,572 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Curve_Wide_01.png` | B | 2000×2000 → 2000×2000 | 30,638 → 30,638 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Entrance_Building_01.png` | B | 2000×2000 → 2000×2000 | 47,662 → 47,662 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Entrance_Harbor_01.png` | B | 2000×2000 → 2000×2000 | 133,261 → 133,261 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Entrance_Parking_01.png` | B | 2000×2000 → 2000×2000 | 170,637 → 170,637 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Entrance_Residential_01.png` | B | 2000×2000 → 2000×2000 | 95,971 → 95,971 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Intersection_4Way.png` | B | 2000×2000 → 2000×2000 | 139,000 → 139,000 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Intersection_Large_01.png` | B | 2000×2000 → 2000×2000 | 188,232 → 188,232 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_Small_Straight_01.png` | B | 204×2000 → 204×2000 | 34,228 → 34,228 | 100 → 100 | 4096 → 2048 | DXT1 | 보존 |
| `Road/Road_Straight_2Lane.png` | A | 897×1752 → 299×584 | 874,130 → 109,175 | 100 → 33.33333333 | 4096 → 1024 | RGB24 | 수정 |
| `Road/Road_Straight_4Lane.png` | B | 808×2000 → 808×2000 | 127,049 → 127,049 | 100 → 100 | 4096 → 2048 | DXT1 | 보존 |
| `Road/Road_TJunction_E.png` | B | 2000×2000 → 2000×2000 | 100,853 → 100,853 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_TJunction_N.png` | B | 2000×2000 → 2000×2000 | 94,071 → 94,071 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_TJunction_S.png` | B | 2000×2000 → 2000×2000 | 93,832 → 93,832 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_TJunction_W.png` | B | 2000×2000 → 2000×2000 | 100,896 → 100,896 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Road/Road_YJunction_01.png` | B | 2000×2000 → 2000×2000 | 104,383 → 104,383 | 100 → 100 | 4096 → 512 | DXT5 | 보존 |
| `Sidewalk/Sidewalk_Straight_01.png` | A | 724×2172 → 168×504 | 984,146 → 54,546 | 100 → 23.20441989 | 4096 → 512 | DXT1 | 수정 |

모든 게임용 후보의 기존/최종 설정: Sprite Single, Full Rect, Bilinear, Clamp, Mip Maps OFF, Read/Write OFF, NPOT None. 해당 설정은 이미 올바른 값이므로 유지했다. Compression None → 22개 CompressedHQ / Quality 100, 2차선 기준 도로만 None 유지. Standalone과 WebGL override를 동일 포맷·크기로 지정해 실제 Editor 압축 결과와 대응시켰다. Master PNG와 meta는 모두 byte 단위로 보존했다.

PNG meta 24개와 폴더 meta 6개를 조사했다. 폴더 meta 6개에는 Texture Import 설정이 없으며 모두 보존했다. PNG meta GUID 및 Single Sprite local fileID `21300000`을 모두 유지했고, 사용하지 않는 기존 auto-slice 이름/ID/rect 기록도 보존했다.

상세한 이전/이후 alpha bounding box, canvas, 투명 비율, PPU, pivot, mip/read/filter, GUID, 참조 목록, 실제 Unity Texture 크기·포맷은 `CodeBlueRush_MapAssetOptimizationInventory.json`에 기록했다.

## 최소 해상도와 예외 판단

- Ground: 1024/512/256/128과 DXT1을 비교. 512에서 큰 면과 명암을 유지하고 256/128에서 경계가 부드러워져 512 채택. 원본 PNG는 Lanczos 축소 후 metadata 없는 무손실 PNG encoding으로 저장했다.
- Sidewalk: 1024/512/256/128 비교. 낮은 단계에서 블록 틈과 연석 선명도가 약해져 168×504 선택. 정확한 1:3 비율, 4-pixel block 배수이므로 DXT1을 실제 적용할 수 있다.
- Tree: 512/256/128 비교. 128에서 작은 실루엣과 면 경계 손실이 보여 256 단계 채택. 원본 crop `(176,128)-(1072,1152)` = 896×1024를 224×256으로 1/4 축소했다. crop 바깥 alpha 최대값은 1/255이며 모든 눈에 보이는 잎·줄기·그림자를 포함한다. 보이는 오브젝트 주변 여백은 약 8~10%이다. PNG quantization은 적용하지 않았다.
- Road_Straight_2Lane: 299×584는 원본 897×1752의 정확한 1/3이다. 더 작은 Max Size에서 Unity의 비정사각 픽셀 반올림이 길이를 바꾸므로 Max Size 1024를 유지한다. 실제 Texture는 299×584이고 1024 Texture로 확대되지 않는다. DXT는 299-pixel 폭을 지원하지 않아 RGB24 / None 유지; 차선과 정확한 비율을 우선한다.
- 19개 Road base PNG: 기존 파일이 23~188 KB이므로 PNG 해상도·RGBA 픽셀·PPU100 모두 보존했다. 17개 정사각 타일은 Max Size 512 + DXT5를 채택했다. 256에서는 곡선/alpha 경계가 약해져 512 유지; 대형 교차로도 512면 충분하다.
- Road_Small_Straight_01 및 Road_Straight_4Lane: Max Size 512/1024에서 Unity 반올림 때문에 2.04×20 / 8.08×20 world envelope가 미세하게 바뀐다. 연결 규격 우선으로 Max Size 2048을 유지하고, 원본이 불투명이며 양쪽 해상도가 4 배수여서 DXT1으로 압축했다. 실제 Texture는 204×2000 / 808×2000이다.

## 월드 크기 / 연결 규격

PPU는 임의 값이 아니라 정확한 축소 비율로 계산했다. 기존 Transform scale은 그대로 사용한다.

| 기준 에셋 | PPU 계산 | 기존 배치 규격 | 결과 |
|---|---|---|---|
| Ground | `100 × 512 / 1254` | 20×20, scale 20/12.54 | 보존 |
| Straight 2Lane | `100 / 3` | 4.08×20, scale (4.08/8.97, 20/17.52) | 보존 |
| Sidewalk | `100 × 168 / 724` | 0.6×10, scale (0.6/7.24, 10/21.72) | 보존 |
| Tree | `100 × 1/4` | 수관 약0.9, scale 0.9/7.12 | 외형·그림자·원점 보존 |

Tree는 투명 canvas crop 때문에 Sprite의 빈 사각형 bounds가 12.54×12.54 → 8.96×10.24로 줄었다. 보이는 오브젝트를 축소 배치한 것이 아니다. Pivot을 `(451/896, 525/1024)`로 보정하여 원래 중심 원점을 유지했다. 새 pixel pivot `(112.75,131.25)`, local bounds 최소점 `(-4.51,-5.25)`는 원본 crop 좌표와 정확히 일치한다. 외형 크기·화면 위치가 보존되는 것을 480×854 비교로 확인했다.

Road base 19개는 source hash가 전부 같고, 중심·포트 N/E/S/W ±10 units·도로 폭 2.04/4.08/8.08 및 PPU100이 그대로다. 기준 Straight의 월드 폭 4.08과 길이20, 인도 폭0.6과 길이10 및 접합 간격0도 확인했다.

## Scene / Prefab / Runtime / Master 조사

Maps PNG 24개 모두 현재 Assets 아래 Scene, Prefab, .asset 및 runtime 코드에서 GUID 직접 참조가 없고 파일명/경로 문자열 사용도 없었다. Resources / StreamingAssets 경로가 아니며 assetBundleName도 비어 있다. 현재 City/Road Prefab은 기존 mesh 및 prototype sprite를 사용한다.

Master Reference는 Gameplay의 `AssetDatabase.GetDependencies(..., true)` 결과에 없다. 현재 활성 Build Scene은 Gameplay이며 현재 참조 구조상 Master를 Runtime Build에 포함시키지 않는 상태다. Master SHA-256: `E99B3C14ABBC68A851A3BE858862238F9987D8F9FF47D0B186BCFA48DF824701`. 실제 WebGL BuildReport는 이번 범위에서 생성하지 않았다.

## 검증 결과

- 실제 프로젝트의 Assets/Packages/ProjectSettings 복제본에서 Unity 6000.3.23f1로 컴파일, Gameplay scene 로딩 및 CityMap seed12345 생성/Validate 통과.
- Gameplay 카메라 orthographic size16, portrait480×854, 기존 URP2D renderer와 Global Light2D 사용. Maps PNG가 미사용이므로 복제 Gameplay scene의 임시 배치로 전후 품질을 검사했다. 원본 Scene/Prefab에는 배치 변경을 저장하지 않았다.
- 기존 Gameplay 렌더링은 최적화 전후 pixel 단위로 동일했다. 임시 기준 세트와 도로 19개는 색감·면·alpha·연결을 육안 비교했으며, 고해상도에서의 수치 차이는 있으나 480×854에서 거슬리는 품질 저하는 관찰하지 못했다.
- 최종 Unity 243 checks PASS: import, 월드 크기, Tree 원점 보정, GUID/localID, WebGL format/size, 도로·인도 접합, CityMap. 검증 실행 중 신규 Console Error/Exception/Assert 0개.
- 원본 Road 검사 935 checks PASS: 48개 접속부, 동일 폭 접속부 비교637쌍, 회전 파생6개 byte-exact, 19개 연결 실루엣.
- 기존 City Corner/T/Intersection 차선 경로544점을 Road alpha mask와 비교: 이탈0. Road System / City Generator source와 Prefab은 변경하지 않았다.
- 최종 파일 보호 검사 92개 PASS: Master PNG/meta 불변, Road base PNG19개 불변, PNG GUID 및 Sprite fileID 보존, 기존 Gameplay pixel 일치.

첫 복제 프로젝트 전체 import 때 긴 Windows PackageCache 경로에 따른 2D tooling editor UXML import 오류가 있었고, 첫 후보 테스트는 비정사각 도로의 반올림을 검출해 중단했다. 최종 재검증 로그에는 asset import/compile/Gameplay 오류가 없으며, licensing 초기 연결 재시도 로그는 Gameplay Console 결과와 구분했다.

## WebGL 검증 범위

DXT 포맷의 압축 품질을 Editor에서 같은 Standalone override로 검증하고 WebGL Import override를 일치시켰다. 이번 작업에서 브라우저 WebGL 빌드 실행이나 실기기 GPU/다운로드 Profiler 측정은 하지 않았다.

DXT는 desktop 브라우저에 적합하고, 모바일 대상에는 ASTC 등 별도 포맷 검증이 필요하다. 선택 포맷을 지원하지 않는 기기는 Unity의 software decompression fallback으로 메모리가 늘 수 있다. 따라서 위 텍셀 감소 수치는 DXT 지원 환경의 값이다. [Unity 6000.3 Web texture compression](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/webgl/develop/texture-compression)

## 재사용 후보와 다음 단계

- Corner NE의 90° 회전으로 NW/SW/SE, TJunction N의 회전으로 W/S/E 재사용 가능. 요청된 6개 방향 PNG를 삭제하지 않았다.
- Straight의 가로/세로는 회전, Intersection4Way 및 Large는 90° 회전 가능. 곡선·진입로·Y는 포트 방향에 맞게 회전 가능하나 서로 다른 폭/형태를 동일 Sprite로 대체하지 않는다.
- Tree 크기 차이는 scale 우선. 광원/그림자 때문에 Tree flip과 전체 tint는 이번 단계에서 적용하지 않았다. 별도 대량 refactor 없음.
- 다음 순서: 최적화된 기준 에셋을 실제 map prefab에 적용해 실사용 검증 → 기존 world 폭에 맞는 Road Marking 대표 overlay → 횡단보도/정지선/화살표 → 인도 코너/폭 variant → 연석 → target browser별 WebGL BuildReport 및 실기기 검증. 새 이미지 생성은 다음 단계에서 별도로 진행한다.

## 재검토 자료

- 전체 수치: `Md/CodeBlueRush_MapAssetOptimizationInventory.json`
- 전후 원본 백업·후보·스크립트·Unity 로그: `Temp/MapAssetOptimization/` (Git ignored)
- 전후 조립 비교: `Temp/MapAssetOptimization/before-after.png`
- 최종 연결 보기: `Temp/MapAssetOptimization/final/road-connected.png`
- 최종 Unity 결과: `Temp/MapAssetOptimization/final/results.json`, `final-unity-3.log`
- 기존 Road source 검사: `Temp/RoadBaseTileProduction/pixel-validation.json`, `existing-path-validation.json`
