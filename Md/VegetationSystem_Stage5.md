# 식생 5단계 적용 결과

기준: 최신 `main` / `ac2ef4a`, 원격 main과 일치 확인 후 작업

## 생성 및 재사용

새 Transparent PNG는 아래 4개뿐이다. 모든 이미지는 imagegen 내장 도구로 생성하고 투명 여백 정리 및 축소와 PNG 무손실 저장을 적용했다. 승인된 기존 PNG와 Master Reference는 모두 원본 해시가 동일하다.

| 경로: Assets/Images/Maps/Nature/ | 해상도 | PNG 바이트 |
|---|---:|---:|
| Bush/Bush_Round_01.png | 128×128 | 18,472 |
| Bush/Hedge_Short_01.png | 128×64 | 9,196 |
| Flower/FlowerPatch_01.png | 128×128 | 26,692 |
| Planter/Planter_01.png | 128×128 | 23,493 |

최종 PNG 합계 **77,853 bytes / 76.0 KiB**. 새 이미지의 DXT5 Texture 크기는 총 **56 KiB**로 RGBA32의 224 KiB보다 75% 작다. Tree는 기존 승인 이미지 224×256, Max Size 256, PPU 25, Pivot과 WebGL Import를 그대로 사용한다.

식생 Prefab 13종은 `Assets/Prefabs/MapAssets/Nature/` 아래에 저장한다.

| 폴더 | Prefab | 구성 / 월드 폭 |
|---|---|---|
| Tree | Tree_Small_01, Tree_Medium_01, Tree_Large_01 | 동일 Tree_Medium_01 PNG 재사용 / 1.5m, 2.4m, 3.6m |
| Bush | Bush_Round_01, Bush_Low_01 | 동일 관목 PNG, Low는 크기와 높이 비율 조절 / 1.7m, 1.25m |
| Bush | Hedge_Short_01, Hedge_Long_01 | Short PNG 한 장, Long은 3개 겹쳐 조립 / 2.0m, 5.6m |
| Flower | FlowerPatch_01, FlowerPatch_02, FlowerBush_01 | 중립 꽃잎을 분홍·노랑으로 Tint, 초록 잎은 별도 Sprite / 1.45m, 1.45m, 1.7m |
| Planter | FlowerBed_Small_01, FlowerBed_Medium_01, Planter_01 | 동일 석재 베이스와 잎 및 꽃잎을 조립 / 1.6m, 2.4m, 1.25m |

방향별 PNG는 생성하지 않았다. 광원과 그림자 반전을 피하기 위해 월드 상단 왼쪽 방향을 유지한다. 나무의 크기, 관목의 높이, 울타리의 길이, 꽃 색상, 화단 크기는 Scale / 반복 / Tint로 재사용한다. 식생은 Ground 이미지에 합치지 않는다.

## Unity 및 City Generator 연결

- 새 PNG: Sprite Single, Tight Mesh, Center Pivot, PPU 64, Max Size 128, Bilinear, Clamp, Mip Maps Off, Read/Write Off, alphaIsTransparency, WebGL/Standalone DXT5 HQ
- 기존 승인 Tree의 Import / PPU / Pivot / PNG를 보존하고 Prefab의 SpriteRenderer Scale만 조절
- `MapAssetStage5.Apply`: CodeBlue Rush → Map Assets → Apply Stage 5 메뉴로 재적용 가능
- 최신 main에는 지형 이미지와 제작 스크립트만 있고 지형 Prefab / Scene 연결이 저장되어 있지 않았으므로 기존 `MapAssetStage4.Apply`를 먼저 실행해 Ground / Water / Coast / Rock 17종과 Mesh / Material 및 CityTerrain 연결을 저장
- `Gameplay.unity`의 기존 CityMap에 CityVegetation 추가, CityTerrain 완료 이벤트 이후 배치하여 초기화 순서에 의존하지 않음
- CityTerrain에 완료 이벤트, 준비 여부 및 연못 셀 조회만 추가
- 공원: 나무 3종 / 관목 / 꽃 3종 / 화단 2종, 연못의 육지 주변에 배치
- 일반 도로변: 기존 건물과 장식 사이에 Planter / Bush, Seed 기반 주거형 구간에 Hedge 배치
- 외곽 도로의 빈 육지 공간에는 긴 생울타리 배치
- `Assets/Prefabs/Environment/Tree.prefab`의 기존 단색 Placeholder 표시를 승인 Tree_Medium_01 Sprite로 교체하여 기존 EnvironmentSlot에서도 사용
- VegetationItem은 회전된 도로 밑에서도 월드 광원 방향 유지, 새 Collider 없음
- CityMap / Road / Sidewalk / Navigation / Traffic 코드와 기존 도로 Prefab은 수정하지 않음
- 도시 교체 및 컴포넌트 비활성화에서 식생 소유 루트 정리 / 숨김 처리

## 통합 검증

모든 이미지와 Prefab 구성 이후에 Unity 6000.3.23f1의 검증 복제본에서 실제 Gameplay Scene을 실행했다. 복제본의 검증 완료 결과만 원본 프로젝트에 저장했다.

- Seed: 12345, -8, -1, 0, 1, 8, int.MinValue, int.MaxValue
- 동일 Seed의 배치 / 크기 재현, Unity 전역 Random 상태 보존
- 20m 도로 셀과 차선 연결 및 보도 / 횡단보도 연결 유지
- 식생의 수면 / 건물 침범 및 보행 경로 검사, Collider 없음
- 실제 GameFlow DrivingToPatient와 NavigationRoute 준비 확인
- Play Mode 240 프레임과 신규 미션으로 도시 교체 / 이전 식생 제거 / 비활성 및 재활성 검사
- 480×854, orthographicSize 16 실제 Gameplay 카메라로 공원 / 연못 / 도로변 / 식생 전체 촬영
- 128 DXT5 / 64 DXT5 / 128 RGBA32를 전체 식생 배치에서 비교: 64는 생울타리 및 석재 화단 윤곽이 흐려져 128 유지, DXT5 압축 사용
- 연못 주변 화단 위치와 회전 도로의 생울타리 크기 및 간격 보정
- 기존 가로수 / 주차 / 버스 구역의 Sprite 영역을 검사하고 점유된 슬롯에는 추가 식생을 배치하지 않음
- 검증 도구의 편집 모드 생명주기 가정 및 Z축 없는 Sprite Bounds 판정 수정 후 재검증
- Master Reference가 Gameplay 의존성에 포함되지 않음

최종 자동 검사: 5,212,496회 / 문제 0 / Console Error 0 / Play Mode 240 프레임.

검증 자료는 `Temp/MapStage5/review/`의 `result.json`, `problems.txt`, `console-errors.txt`, `gallery-128-dxt.png`, `gallery-64-dxt.png`, `gallery-128-raw.png`, `pond-play.png`, `streetscape-play.png`, `gameplay-play.png`에 보관한다. 통합 실행 로그는 `Temp/MapStage5/final-integrated.log`이다. WebGL 대상 Import 압축을 확인했으며 이번 단계에서 WebGL 브라우저 빌드를 실행한 것은 아니다.

식생 전체 화면: [480×854 검증 이미지](Previews/Vegetation_Stage5_480x854.png)

Git commit / push는 수행하지 않았다.

## 사용한 imagegen 프롬프트

모드: 내장 imagegen. Reference 1은 Master Reference, Reference 2는 기존 승인 `Nature/Tree/Tree_Medium_01.png`이다. References는 시각 기준으로만 사용하며 수정 대상이 아니다. 최종 프로젝트 파일은 모두 128px 이하이다.

### Bush_Round_01

Create ONE independent transparent game sprite for CodeBlueRush. Use reference 1 ONLY as the final visual style guide, never reproduce its scene or text. Reference 2 is the approved tree palette/facet/shadow guide. Bright casual low poly, broad flat polygon color faces, near-orthographic almost top-down view, exactly the same green family and upper-left illumination with lower-right shade as the approved references. No photorealism, leaf texture, fine noise, outlines, buildings, road, ground tile, pots unless specifically requested, text, or extra objects. Center the entire isolated silhouette including a compact translucent lower-right cast shadow, minimal clear padding. Designed for a final 128-pixel sprite, simple large readable facets; do not design fine detail. Return true RGBA transparent background, not a checkerboard. A compact rounded low shrub, no visible tree trunk. Low spreading domed canopy, lime yellow-green upper-left faces, medium vivid greens, deep forest-green lower-right facets. Approximately 8-14 broad faces, gently lobed round silhouette. Only the shrub and compact translucent shadow.

### Hedge_Short_01

Create ONE independent transparent game sprite for CodeBlueRush. Use reference 1 ONLY as the final visual style guide, never reproduce its scene or text. Reference 2 is the approved tree palette/facet/shadow guide. Bright casual low poly, broad flat polygon color faces, near-orthographic almost top-down view, exactly the same green family and upper-left illumination with lower-right shade as the approved references. No photorealism, leaf texture, fine noise, outlines, buildings, road, ground tile, pots unless specifically requested, text, or extra objects. Center the entire isolated silhouette including a compact translucent lower-right cast shadow, minimal clear padding. Designed for a final 128-pixel sprite, simple large readable facets; do not design fine detail. Return true RGBA transparent background, not a checkerboard. A single short trimmed low hedge segment, horizontal elongated rectangular capsule footprint approximately 2:1 aspect ratio. Soft irregular clipped green silhouette, 8-12 large polygon facets, bright lime upper-left crown and forest-green lower-right sides, low height. No fence, flowers, soil or planter. Entire horizontal hedge segment with short translucent shadow.

### FlowerPatch_01

Create ONE independent transparent game sprite for CodeBlueRush. Use reference 1 ONLY as the final visual style guide, never reproduce its scene or text. Reference 2 is the approved tree palette/facet/shadow guide. Bright casual low poly, broad flat polygon color faces, near-orthographic almost top-down view, exactly the same green family and upper-left illumination with lower-right shade as the approved references. No photorealism, leaf texture, fine noise, outlines, buildings, road, ground tile, pots unless specifically requested, text, or extra objects. Center the entire isolated silhouette including a compact translucent lower-right cast shadow, minimal clear padding. Designed for a final 128-pixel sprite, simple large readable facets; do not design fine detail. Return true RGBA transparent background, not a checkerboard. An isolated reusable FLOWER PETALS overlay: a compact irregular oval cluster of about nine broad stylized pale ivory-white blossom heads, each with only 3-5 large low-poly petal facets and a tiny warm pale center. Top-down, no green foliage, stems, soil, vase or container. White petals intentionally neutral so Unity can tint this sprite pink or yellow while separate approved green shrub sprites beneath provide foliage. Several small transparent gaps among flower heads, big readable shapes, extremely short neutral soft shadow.

### Planter_01

Create ONE independent transparent game sprite for CodeBlueRush. Use reference 1 ONLY as the final visual style guide, never reproduce its scene or text. Reference 2 is the approved tree palette/facet/shadow guide. Bright casual low poly, broad flat polygon color faces, near-orthographic almost top-down view, exactly the same green family and upper-left illumination with lower-right shade as the approved references. No photorealism, leaf texture, fine noise, outlines, buildings, road, ground tile, pots unless specifically requested, text, or extra objects. Center the entire isolated silhouette including a compact translucent lower-right cast shadow, minimal clear padding. Designed for a final 128-pixel sprite, simple large readable facets; do not design fine detail. Return true RGBA transparent background, not a checkerboard. One small square low stone planter/raised flowerbed base viewed nearly straight from above with only a short front/right side visible. Warm light beige-gray polygonal stone rim matching reference sidewalks, simple broad highlight upper-left and muted shadow lower-right. Dark warm brown soil inset inside the square opening. EMPTY of plants or flowers; Unity will layer independent vegetation sprites into this base. No surrounding ground or pavement, no additional objects. Modest bevels, 6-10 large color faces, entire planter and compact translucent lower-right shadow.

