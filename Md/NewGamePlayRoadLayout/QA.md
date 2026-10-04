# NewGamePlay 도로·보도 구현 / QA

작업 기준: 원격 `main`과 일치하는 `e77093b6ca953992b77d415b117a923c839a3445`.
세 가이드를 확인하고 `Assets/Images/Maps/Reference/Maps.png`를 최우선으로 사용했다.
요청서의 `Assets/Image`, `Assets/Prefab`는 실제 저장소의 `Assets/Images`, `Assets/Prefabs`로 해석했다.

## 원본 분석과 배치 기준

- 원본: 941 × 1672 px, 비율 약 0.563. 8 reference px = 1 Unity Unit.
- 좌표 변환: `worldX = (pixelX - 470.5) / 8`, `worldY = (836 - pixelY) / 8`.
- 형상 경계는 원본 기준 0.5 px 간격으로 작성했다. Reference PNG는 Runtime에서 참조하지 않는다.
- 외곽 북쪽 중심 y=57, 서쪽 x=38, 남쪽 y=1454. 동쪽 상단 x=902, 하단 x=910이며 y=575/632의 두 곡선과 x=795 연결부를 보존했다.
- 서측 하천 도로 x=300. 동측은 상부 x=412, 하부 x=500으로 갈라진다. 내부 주축은 x=166/638/795.
- 하천을 횡단하는 차량 도로는 y=57/197/362/575/775. y=632에서는 좌우 도로를 하천 위로 임의 연결하지 않았다.
- y=945/1115의 보행교와 남쪽 작은 섬의 꺾인 보행교는 차량 도로와 구분했다.
- 병원 회차부 중심 (795,450), 원형 도로 중심 반경 36 px. 캠퍼스 진입부·주차 공간 연결을 보존했다.
- 병원, 물류, 항만, 하천 공원, 혼합 공공 블록, 소방서 공간을 유지했다. 하천·항만·해안은 도로 위치를 확인하기 위한 단순 수역/지면 기준이며 자연환경 장식은 제작하지 않았다.
- 전체 위치, 폭, 구분, 횡단보도 좌표, 보호 공간은 [SourceTrace.json](SourceTrace.json)에 기록했다.

## 필요한 리소스 및 기존 리소스 판별

필요 역할: 직선 도로, 외곽 90° 코너, T/십자 연결, 시설 진입부, Service Road, 병원 회차부, 도로/보행 다리, 아스팔트 면, 중앙 Dash, Edge Line, Crosswalk, 직선/코너/교차로/Driveway/다리 보도, 녹지 Strip과 중앙 섬.

기존 Road/Sidewalk 이미지 및 Prefab을 검사했다. Road PNG는 최대 2000px에 방향별 변형이 있고, 코너 반경·진입 형태·폭이 원본과 달랐다. `Road_Straight_2Lane.png`는 중앙선/Edge Line이 Bake되어 교차로에서 겹침을 피하기 어려웠다. Sidewalk Corner/T/Intersection도 기존 도로 폭과 결합된 형태라 원본 좌표를 정확히 잇는 용도로 그대로 사용하지 않았다. 방향 회전/반전만으로도 폭과 반경 차이를 해결할 수 없었다.

따라서 Road Base + Marking + 연속 Sidewalk 형상 조합을 사용했다. 방향별 신규 이미지, 신규 PNG, Arrow/Y 도로, 4차로 이미지는 만들지 않았다.

재사용:

- `Assets/Images/Maps/RoadMarking/RoadMarking_Line_Solid_01.png`: 4×16 표시 Sprite의 기존 Texture를 모든 정적 면과 표시의 공용 Texture로 사용.
- `Assets/Prefabs/Road/RoadSurface.mat`: 기존 Sprite Material을 기반으로 레이어별 색상 Material을 구성.

수정한 기존 리소스: `Assets/Scenes/NewGamePlay.unity`만 변경. 기존 PNG/Prefab/Material은 수정하지 않았다.

신규 리소스: `Assets/MapGeometry/NewGamePlayRoadLayout/`의 정적 Mesh 17개와 Material 17개. 임의 폭의 블록, 큰 코너, 회차부 및 연속 보도를 기존 Texture로 정확하게 조합하기 위한 Geometry이며 신규 이미지가 아니다.

## Scene 변경

`NewGamePlay`에 `City` Grid와 `GroundTilemap`, `WaterTilemap`, `RoadBaseTilemap`, `RoadMarkingTilemap`, `SidewalkTilemap`, `ParkPathTilemap`을 구성했다. 각 Tilemap은 Unity 기본 Tilemap/TilemapRenderer이며 현재 시각 형상은 하위 정적 Mesh로 렌더링한다. 따라서 기존 고정 크기 Tile을 강제로 늘려 원본 블록 비율을 바꾸지 않는다.

`RoadSpecial/Bridges`, `HospitalRoundabout`, `Medians`, `TrafficIslands`를 분리했다. 병원 도로와 섬은 `HospitalRoundabout`, 다리 Deck/이음새/난간은 `Bridges`에 있다. `ParkPathTilemap`, `Buildings`, `StaticDecoration`, `DynamicObjects`, `TrafficIslands`는 후속 단계용으로 비워 두었다.

카메라는 원본 비율의 전체 도시를 확인할 수 있도록 Orthographic Size 104.5로 설정했다. 새 MonoBehaviour, Collider, Gameplay Logic은 없다. Editor 저장/캡처 도구는 무시되는 `Temp/` 안에서만 실행했으며 게임 Assets에 C#을 추가하지 않았다.

## 구현 결과

| 항목 | 결과 | 확인 |
| --- | --- | --- |
| Maps.png 도로 구조 재현 | PASS | 원본 좌표의 외곽·Grid·블록·다리·회차부를 나란히 비교 |
| Road 연결 | PASS | 전체 도로 면이 하나의 연결 성분, 외곽 연결 유지 |
| RoadMarking | PASS | 교차로 중앙 Dash 중첩 제거, Road 밖 표시 0 |
| Sidewalk | PASS | 도로 면의 연속 경계로 구성, 도로와 겹침 0, 코너·Driveway 연결 |
| Crosswalk | PASS | 명시한 원본 판독 위치 158곳, 중앙 위치가 모두 Road 위 |
| Bridge | PASS | 차량 도로 다리 5곳 양 끝 연결, 보행교 3곳 및 해안 구조 구분 |
| Hospital Roundabout | PASS | 원형 차량 동선·중앙 녹지섬·보도·주차 연결 진입부 |

비교 이미지: [ReferenceComparison.png](ReferenceComparison.png).
실제 Unity 탑뷰: [SceneTopView.png](SceneTopView.png).
Unity 480×854: [SceneMobile480x854.png](SceneMobile480x854.png).
브라우저 WebGL 480×854: [WebGLMobile480x854.png](WebGLMobile480x854.png).

## QA에서 발견 후 수정

- 남쪽 보행교의 착지 위치: 원본 확대 후 x=435의 작은 섬으로 이어지는 꺾인 구조로 수정.
- 상단 T자 교차로의 횡단보도: 존재하지 않는 북쪽 접근 방향 표시와 중복 표시 제거, 원본의 좌우 Bridge/도로 접근부로 위치 보정.
- 하천 형상: 원본의 굽이, 폭 변화, 남쪽 작은 섬을 배치 기준 형상에 반영.
- 교차로 중앙선 겹침: 실제 연결부와 Crosswalk 주변에서 Dash를 잘라 수정.
- Driveway 보도: 시설 진입부에 낮아진 포장 띠를 두어 보도 연결 유지.

## 검증 근거 및 범위

- Unity 6000.3.23f1에서 저장·재오픈·렌더링: PASS.
- 실제 저장소 GUID 참조 누락/신규 GUID 중복: 0. Native Tilemap 6개, Scene MonoBehaviour 0.
- 정적 Mesh Renderer 17개, Triangle 33,734개.
- Release WebGL, Development Off, Brotli: `Succeeded`, 오류 0개. 결과 디렉터리 전체 3,423,827 bytes = 3.42 MB. 100 MB Gate PASS.
- 480×854 모바일 브라우저 환경의 WebGL2 로딩/시각 확인: PASS. 콘솔 오류/실패 요청/HTTP 오류 0. 큰 면의 색상도 Unity 캡처와 비교했다.
- 브라우저 검증 CLI가 설치되어 있지 않아 번들 Playwright와 로컬 Edge로 동일 검증을 수행했다.
- 이 WebGL 결과는 동시 실행 중인 Unity 편집기를 방해하지 않도록 별도 검증 프로젝트에서 **변경한 NewGamePlay Scene과 실제 참조 리소스만** 빌드한 결과다. 기존 Gameplay 통합 빌드나 `docs/` 배포 갱신은 수행하지 않았다. 실제 모바일 기기의 성능 측정은 포함하지 않는다.
- QA JSON: `asset_qa`, `geometry_qa`, `connectivity_qa`, `unity_qa`, `build_qa`, `browser_qa`.

현재 도로 시각 배치에서 남아 있는 확인된 오류: 없음.

Git: **Commit / Push 하지 않음**. `main`의 작업 파일만 변경했다.
