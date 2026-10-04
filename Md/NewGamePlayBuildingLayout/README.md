# NewGamePlay 건물 배치

기준 main: `42b948309c1fcd31022b540ee0a531c8d45e2d91`. 기준 이미지는 `Assets/Images/Maps/Reference/Maps.png`이며 BuildingPlacementGuide, CityRoadDesignGuide, MapAssetStyleGuide를 먼저 읽고 적용했다.

## 리소스와 재사용

독립 투명 PNG 20종: 일반 본체 12종, 랜드마크 4종, 필수 시설 소품 4종. 일반 본체는 House_A/B/C, ResidentialMidrise_A/B, Office_A/B, CivicMidrise_A, Commercial_A, Warehouse_A/B, LogisticsCenter. 랜드마크는 Hospital_Main, Hospital_Secondary, FireStation_Main, ContainerCrane. 소품은 Container_Base, Tank_Silo, Pavilion_A, Fountain_A.

최종 PNG 최대 변 128~512px, 합계 1,896,170 bytes. 새 Normal/AO/Mask/방향별/색상별 PNG는 없다. 컨테이너는 중립 회색 본체 1종을 회전·Tint로 20회 재사용한다. 주거 아파트는 기존 Sprite 두 동으로 단지 실루엣을 구성하고, 상업 랜드마크는 주변 상업 건물과 빈 분수 광장의 조합으로 구성했다. 별도 랜드마크를 만들기 위해 기준 이미지에 없는 대형 건물을 추가하지 않았다.

생성 도구는 built-in imagegen이며 대상마다 1개 독립 Sprite를 요청했다. 전체 프롬프트 기준과 대상별 설명은 [AssetPlan.json](AssetPlan.json)에 있다. 후속 수정은 병원 본관에서 함께 생성된 전면 의료동 제거, 병원 두 이미지의 빨간 +/흰 판 통일, 컨테이너의 Tint용 중립 회색 변환, 일부 이미지의 투명 윤곽 재확인이다. 투명 영역의 RGB 잔광은 알파 합성 시 표시되지 않음을 확인했다. 원본을 편집 도구로 다시 생성한 뒤 알파 여백을 잘라 최종 크기로 축소했다.

기존 `RoadSurface.mat`를 건물 SpriteRenderer 전체가 공유한다. 부지 Mesh는 기존 흰 도로 표식 Texture를 사용하는 Material 1개와 정적 Mesh로 묶었다. 건물 별 Material 복제나 Runtime .material 호출은 없다.

## Scene 구성

기존 `City/Buildings` 아래 Residential 14, Commercial 16, Hospital 6, IndustrialPort 32, ParkRiver 2, FireStation 9: 총 79개 SpriteRenderer, 단순 BoxCollider2D 79개. FireStation 하위 그룹에는 기준 이미지의 우측 혼합 업무·공공 블록도 포함된다. 정확히 6개의 사각형 구역으로 도시를 재분할하지 않았다.

Prefab 20종은 Transform/SpriteRenderer/BoxCollider2D만 사용한다. 새 MonoBehaviour, Manager, Update, Rigidbody2D, PolygonCollider2D는 없다. 소방서 Collider는 타워 투영 영역을 제외한 본관 바닥에, 정자와 크레인은 작은 지지 바닥에 맞췄다. 부지/주차선/출동선에는 Collider를 추가하지 않았다.

주차장 8개와 적재 부지 2개, 분수 광장 3개, 항만 부두와 컨테이너 야드, 넓은 소방서 출동 야드만 최소 부지 형태로 표현했다. 차량·시민·나무·화단·벤치·가로등과 동작 로직은 이번 작업 범위에 없다.

## Import / Atlas

Sprite Single, PPU 64, Mip Map Off, Read/Write Off, Alpha Is Transparency On, Clamp, Bilinear, NPOT None. WebGL Override ETC2 RGBA8, 자산별 Max Size 128/256/512.

| Atlas | Sprite | 최대 크기 |
| --- | ---: | ---: |
| BuildingCommonAtlas | 12 | 2048 (실제 패킹 2048×1024) |
| BuildingLandmarkAtlas | 4 | 1024 (실제 패킹 1024×1024) |
| PropAtlas | 4 | 1024 (실제 패킹 512×512) |

Sprite 중복 포함 없음. Atlas 회전/타이트 패킹 Off, Padding 4, Mipmap/ReadWrite Off, WebGL ETC2 RGBA8. Include In Build On.

## 배치와 QA

[SourcePlacements.json](SourcePlacements.json)은 기준 이미지 픽셀 좌표를 `x=(px-470.5)/8, y=(836-py)/8`로 변환한 실제 배치와 도로 회피 보정 내역이다. 새 도로를 만들거나 도로 폭/하천/다리/보도를 변경하지 않았다.

Unity에서 Ground/Water/RoadBase/RoadMarking/Sidewalk/ParkPath/RoadSpecial의 Transform·Mesh·Material·정렬 순서, Camera 설정이 배치 전후 동일함을 확인했다. Maps.png는 Scene 의존성에 포함되지 않는다.

첫 검사에서 상업 블록의 인접 Collider 1쌍과 탱크 4개의 작은 수면 침범을 발견했다. 상업 건물을 같은 블록 내 10px 이동하고 탱크 줄을 2px 안쪽으로 이동했다. 크레인의 기준 이미지 상단 투영이 확정 도로와 겹치는 부분과 우측 작업장의 폭은 도로 유지 조건에 따라 같은 블록 안에서 보정했다. 부지 Mesh는 도로·보도 마스크 밖으로 클리핑했다. WebGL의 안티앨리어싱 Off에서 사라지던 부지 표식은 기준 좌표 2px(480 화면에서 약 1px) 두께로 보정했다. 기존 도로 표식은 수정하지 않았다.

최종 실제 Unity Collider 검사: 도로 침범 0, 수면 침범 0, 건물 Collider 겹침 0. 전체 Sprite 외곽 사각형과 부지에도 도로/보도 침범 0. 병원 회차부와 소방서 출동 야드 유지. 최종 검사 결과는 [LayoutQA.json](LayoutQA.json), [UnityQA.json](UnityQA.json), [AssetQA.json](AssetQA.json)에 있다.

검증은 전체 Assets/Packages/ProjectSettings를 복사한 별도 로컬 Unity 프로젝트에서 진행했다. 검증용 Editor 생성 도구는 `Temp`에만 있으며 실제 게임 Assets에는 추가하지 않았다. 깊은 검증 프로젝트 경로로 발생했던 패키지 Import 경로 길이 오류는 프로젝트 경로를 `Temp/B`로 줄여 해결했다. 관련 없는 기존 게임 코드는 수정하지 않았다.

Unity Game View를 고정 480×854로 설정하고, 기존 Main Camera의 실제 RenderTexture 480×854 및 941×1672 출력으로 검사했다. [비교 이미지](Maps_vs_NewGamePlay_480x854.png)의 왼쪽은 Maps.png, 오른쪽은 Unity Camera 480×854 렌더다. Camera Transform/Orthographic Size는 원본대로 유지했다.

전체 기존 프로젝트 코드·Packages·Resources를 포함한 NewGamePlay WebGL Release 빌드 성공, Build Error 0. 최종 배포 폴더 전체 13,463,651 bytes (13.46 MB), 신규 소스 자산 2,186,321 bytes (2.19 MB), Atlas Texture 비압축 데이터 약 3.25 MiB. 100 MB 제한과 85 MB 목표, 건물 8 MB 예산을 충족했다. 빌드 결과는 `Builds/NewGamePlayWebGL`에 보관했다. 근거는 [BuildQA.json](BuildQA.json)에 있다.

Edge WebGL2를 480×854 모바일 viewport/touch/Android UA로 실행해 실제 Canvas 480×854, 로드 완료, Console Error/실패 요청 0을 확인했다. 최종 주차선과 출동 야드 선도 표시된다. [BrowserQA.json](BrowserQA.json), [WebGL480x854.png](WebGL480x854.png). 첫 sandbox 실행의 Unity 기본 원격 설정 요청 네트워크 차단은 일반 네트워크 조건에서 재검증하여 정상 확인했다. 네트워크 요청을 가짜 응답으로 대체하지 않았다. 실제 모바일 기기의 FPS/메모리 측정은 하지 않았다.

Git commit/push는 하지 않았다. 기존 Build Settings는 삭제된 Gameplay.unity를 가리키고 있어, 실제 작업 Scene인 NewGamePlay.unity의 경로와 GUID로 해당 항목만 바로잡았다. 기존 플레이 로직은 수정하지 않았다.
