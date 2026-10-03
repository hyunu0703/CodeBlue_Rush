# Road Base Tiles 제작 결과

## 작업 기준

- 최신 main: `6072e41f2bcbe7eb7ff00c26713552f749093526`. 시작 시 fetch 후 로컬 HEAD와 origin/main의 일치를 확인했다.
- Master Reference, Production Guide, Style Guide 및 확정된 Ground/Road/Sidewalk/Tree PNG를 생성 전에 읽고 직접 확인했다.
- 나무 기준은 실제 저장 위치인 `Assets/Images/Maps/Nature/Tree/Tree_Medium_01.png`를 사용했다.
- `Road_Straight_2Lane.png`를 모든 생성 요청의 Secondary Reference로 고정했다.
- 사용자 승인에 따라 imagegen 생성 후 Python으로 크기, 도로 마스크, 회전 방향, 접속 가장자리를 정밀 보정했다.
- 중앙선, 가장자리 선을 포함한 모든 노면 마킹을 신규 타일에서 제외했다. 차선 수는 도로 폭과 향후 TrafficLane 구성 규격을 의미한다.
- 도로 본체 원본 13개를 생성하고, 코너/T의 추가 방향 6개는 같은 원본의 정확한 90도 회전으로 저장해 최종 PNG 19개를 만들었다.

## 생성 파일과 저장 경로

공통 경로: **`Assets/Images/Maps/Road/`**

| 파일 | 원본 픽셀 크기 | 월드 크기 (PPU 100, Scale 1) | 접속 방향과 폭 |
| --- | --- | --- | --- |
| `Road_Straight_4Lane.png` | 808 × 2000 | 8.08 × 20 | N/S: 8.08 |
| `Road_Small_Straight_01.png` | 204 × 2000 | 2.04 × 20 | N/S: 2.04 |
| `Road_Corner_NE.png` | 2000 × 2000 | 20 × 20 | N/E: 4.08 |
| `Road_Corner_NW.png` | 2000 × 2000 | 20 × 20 | N/W: 4.08 |
| `Road_Corner_SE.png` | 2000 × 2000 | 20 × 20 | S/E: 4.08 |
| `Road_Corner_SW.png` | 2000 × 2000 | 20 × 20 | S/W: 4.08 |
| `Road_TJunction_N.png` | 2000 × 2000 | 20 × 20 | E/S/W: 4.08, N 막힘 |
| `Road_TJunction_E.png` | 2000 × 2000 | 20 × 20 | N/S/W: 4.08, E 막힘 |
| `Road_TJunction_S.png` | 2000 × 2000 | 20 × 20 | N/E/W: 4.08, S 막힘 |
| `Road_TJunction_W.png` | 2000 × 2000 | 20 × 20 | N/E/S: 4.08, W 막힘 |
| `Road_Intersection_4Way.png` | 2000 × 2000 | 20 × 20 | N/E/S/W: 4.08 |
| `Road_YJunction_01.png` | 2000 × 2000 | 20 × 20 | N/E/S: 4.08 |
| `Road_Intersection_Large_01.png` | 2000 × 2000 | 20 × 20 | N/E/S/W: 8.08 |
| `Road_Entrance_Parking_01.png` | 2000 × 2000 | 20 × 20 | S: 4.08 → N: 8.08 |
| `Road_Entrance_Building_01.png` | 2000 × 2000 | 20 × 20 | S: 4.08 → E: 2.04 |
| `Road_Entrance_Harbor_01.png` | 2000 × 2000 | 20 × 20 | N/S: 8.08, W: 4.08 |
| `Road_Entrance_Residential_01.png` | 2000 × 2000 | 20 × 20 | S: 4.08 → N: 2.04 |
| `Road_Curve_Wide_01.png` | 2000 × 2000 | 20 × 20 | N/E: 4.08, 반경 8 + 접근 직선 2 |
| `Road_Curve_Tight_01.png` | 2000 × 2000 | 20 × 20 | N/E: 4.08, 반경 4 + 접근 직선 6 |

파일명은 영어/숫자/밑줄을 사용하고 공백과 한글을 넣지 않았다. 소형 도로는 명확한 이름인 `Road_Small_Straight_01.png`로 통일했다.

## Straight 기준과 연결 규격

### 폭과 중심

- 기존 `RoadSamples.CreateSurface`, `Straight2Lane.asset`, `Straight4Lane.asset`, City Prefab 및 생성기 셀 규격을 확인했다.
- 기존 2차선 도로: 차도 본체 폭 4, 가장자리 선을 포함한 기존 메시 외곽 폭 **4.08**, 길이 **20**. 차선 중심 x=±1.
- 기존 4차선: 차도 본체 폭 8, 메시 외곽 폭 **8.08**, 길이 **20**. 차선 중심 x=±1, ±3.
- 확정된 2차선 PNG는 897×1752이고 기존 배치 Scale (0.45484950, 1.14155251)에서 4.08×20이 된다. 기존 파일과 Import 설정은 유지했다.
- 신규 파일은 PPU 100에서 단위 스케일만으로 목표 크기가 나오도록 보정했다. 논리 차선 폭 2단위와 중심을 유지하며, 소형 접근 도로는 2.04단위 폭을 사용한다.
- 정사각 타일의 포트 중심은 N=(0,10), E=(10,0), S=(0,-10), W=(-10,0). 이는 City의 20단위 셀 규격과 일치한다.
- 기존 Road Prefab 시작점 원점에 시각 자식으로 붙이면 중심은 (0,10), City 셀 중심 원점에서는 (0,0)이다. RoadChunk와 차선 루트를 이미지 크기에 맞춰 스케일하지 않는다.

### 픽셀 접속 단면

- 1단위=100픽셀. 소형/2차선/4차선 접속 폭은 정확히 **204/408/808픽셀**.
- 2000픽셀 변 기준으로 폭 408의 접속 구간은 x 또는 y의 `[796,1204)`, 폭 808은 `[596,1404)`, 폭 204는 `[898,1102)`다. 이미지 중심과 정확히 대칭이다.
- 모든 같은 폭의 포트는 RGBA 단면까지 동일하다. 불투명 도로 픽셀은 `(83,93,109,255)`, 바깥은 `(0,0,0,0)`.
- 접속부 0.5단위는 같은 폭의 직선 구간을 유지하고 표면 색상도 통일했다. 연결 방향에는 패딩/끝막음/그림자/장식을 넣지 않았다.
- 표면 RGB는 Secondary Reference의 도장 없는 차도에서 측정한 중앙값 `(83,93,109)`로 맞추고 생성 표면의 작은 색 변화만 유지했다. 도구가 남긴 경계 잡색과 불필요한 배경은 제거했다.
- 폭이 다른 도로는 직접 맞대지 않는다. 4.08↔8.08은 Parking 또는 Harbor 입구, 4.08↔2.04는 Residential 또는 Building 입구를 통해 연결한다. 입구 타일도 회전해서 재사용할 수 있다.
- 기존 확정 Straight에는 노면 마킹이 포함되어 있다. 신규 Base 타일과 **기하 접속 규격**은 일치하지만, 기존 도장의 연속성은 다음 Road Marking 단계에서 통일해야 한다. 이번에 기존 파일을 변경하거나 새 마킹을 만들지 않았다.

### 형태와 카메라

- 모두 평면 Top-Down 도로이며 고가/교량/입체 램프, 원근 왜곡, 연석, 인도, 주변 오브젝트가 없다.
- 도로 밖의 투명 영역은 별도 Ground로 채운다. 불투명 직선 사각형 내부는 전체가 도로다.
- 기존 Corner의 중심선 반경 10을 유지했다. 추가 곡선은 동일한 외부 포트와 폭을 유지하고 내부 반경만 8/4로 다르다.
- Y는 내부의 사선 분기를 북·동·남 포트로 이어서 끝에서 직선에 수직으로 맞닿는다.

## Import와 회전 재사용

신규 19개 `.meta`: Sprite / Single / PPU 100 / Full Rect / Center Pivot / Bilinear / Clamp / Mipmaps Off / Compression None / Max Size 4096 / NPOT Scale None / Alpha Is Transparency On. 원본 픽셀 규격이 Import 과정에서 축소되지 않도록 했다.

모든 파일은 그림자와 방향 텍스트가 없으므로 90도 Transform 회전으로 재사용 가능하다. 포트 종류와 TrafficLane 방향도 같은 회전을 적용해야 한다.

- 코너의 별도 방향 PNG: NE 원본에서 NW=반시계 90°, SW=180°, SE=270°. 요청한 네 이름을 모두 저장했다.
- T의 별도 방향 PNG: N 원본에서 W=반시계 90°, S=180°, E=270°. 요청한 네 이름을 모두 저장했다. 접미사는 막힌 방향이다.
- 위 여섯 회전 파생 파일은 기준 PNG를 회전한 결과와 모든 픽셀이 동일함을 검증했다.
- Straight, Small, 두 Intersection, Y, Entrance 4종, Curve 2종도 포트 배치에 따라 회전 재사용한다. 별도 회전 Variant를 추가 생성하지 않았다.

## 검증

- 최종 19개 PNG의 이름, 크기, RGBA, 표면 색 범위, 불필요한 도장 픽셀, 연결된 실루엣, 닫힌 변, 정확한 회전, Import 메타 및 포트 단면 검사: **935개 통과**.
- 포트 **48개**, 같은 폭 포트의 쌍 비교 **637개**에서 RGBA 단면 일치.
- Unity **6000.3.23f1**의 별도 임시 프로젝트에서 Sprite 로드, 원본 크기, PPU, Full Rect, Center Pivot, Bilinear, 무압축, Scale 1의 월드 크기와 포트 중심 검사: **183개 통과**.
- 코너/T/십자 9개를 실제 Unity 렌더러로 연결한 미리보기를 확인했다.
- 기존 City `Corner`→`Road_Corner_SE`, `TJunction`→`Road_TJunction_W`, `Intersection`→`Road_Intersection_4Way`의 TrafficLane 경로점 총 **544개**(66/162/316)가 도로 영역 안에 들어오는 것을 확인했다. 이 검사는 차선 중심 경로점의 영역 포함 검사이며 차량 외곽/충돌/AI 전체 검증은 아니다.
- 검증 파일은 `Temp/RoadBaseTileProduction/`에 있다: `pixel-validation.json`, `existing-path-validation.json`, `unity-validation.txt`, `unity.log`, `road-base-contact-sheet.png`, `unity-connected-preview.png`.
- 생성 프롬프트: `Md/CodeBlueRush_RoadBaseTilesPrompts.json`.
- 파일별 픽셀/월드 크기, 포트 폭, 원본/회전 관계, SHA-256: `Md/CodeBlueRush_RoadBaseTilesManifest.json`.

## 다음 단계 추천 순서

1. 기존 Road/City의 시각 자식에 대표 Base 타일을 적용하는 별도 Prefab 작업. 포트와 TrafficLane 데이터를 기준으로 크기/회전을 확인한다.
2. Y, 소형, 입구, 추가 곡선, 대형 교차로의 전용 TrafficLane/포트 Prefab을 구성하고 City Generator가 선택할 수 있도록 연결한다. 현 생성기는 기본 Straight/Corner/T/Intersection을 선택하므로 PNG만 추가해서 새 형태가 자동 생성되는 것은 아니다.
3. Road Marking Overlay의 중앙선/차선/정지선/횡단보도/화살표를 공통 규격으로 제작하고 기존 Straight 도장과 통일한다.
4. Sidewalk/연석 연결 세트 제작 후 조립 검증, URP/WebGL에서 주행 화면과 텍스처 메모리 확인.

이번 단계는 Road Base PNG 19종과 Import 설정/제작 기록까지 완료했다. 기존 Prefab, 기준 PNG, Master Reference를 수정하거나 삭제하지 않았다. Git commit/push는 수행하지 않았다.
