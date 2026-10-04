# NewGamePlay 자연환경 배치 — 가능한 범위 구현, 전체 완료 아님

기준: GitHub에서 확인한 최신 `main` `607b250d3a8c9469887cb7c0bfb41989a353b2a7`. 환경 배치 가이드 전체와 Maps.png, 현재 Scene 및 나머지 세 가이드를 확인했다.

## 리소스 조사

Assets의 모든 원본 이미지와 Sprite import 설정, Prefab, Scene 참조를 조사했다. 독립 나무·꽃나무·수풀·작은 식생·화단·강변 돌·ParkPath Sprite/Prefab은 없다. `VegetationItem.cs`는 종류를 정의하는 스크립트이며 표시할 자연물 리소스가 아니다. Maps.png에 그려진 자연물을 잘라 새 리소스로 사용하지 않았다.

기존 분수 3개와 정자 2개를 그대로 활용했다. 크기, Prefab, 정렬, Collider 및 현재 배치 좌표는 AssetInventory.json에 기록했다. 기존 포장용 BuildingSite.mat와 흰 단색 Texture를 공유해 따뜻한 베이지 산책로를 정적 Mesh로 표현했다. 이미지·Material·Prefab 복제와 런타임 스크립트 추가는 없다.

## 배치

기존 `City/ParkPathTilemap`에 7개의 정적 MeshRenderer만 추가했다. 분수 광장 3곳은 서로 다른 짧은 접근로를, 강변 4구역은 초지 산책길·굴곡 공원·서안 및 동안 정자 산책길을 구성했다. 기존 보도와 보행교에 연결하고 중앙 녹지의 여백을 유지했다. 항만 작업장, 병원 회차부, 주차장 진출입부 및 소방서 출동 야드는 보존했다.

나무, 강변 돌, 식생, 외곽 수목 군집 및 지역별 식생 밀도는 리소스 부족으로 미구현이다. 공원에는 길·기존 분수/정자·녹지 여백까지 구현했으며 나무와 식생을 갖춘 최종 공원은 미완성이다. RegionPlan.json에 6개 구역의 구현/보류 항목을 구분했다.

## 검증 근거

- LayoutQA.json: 기존 Scene 객체 중 ParkPathTilemap의 자식 목록만 변경. 도로·보도·건물·다리·하천·Camera 및 기존 Collider 유지. 새 Collider/MonoBehaviour 0개. 경로의 도로·보도·수면·건물 침범 0.
- ConnectivityQA.json: 실제 직렬화 Mesh를 기준 이미지 픽셀당 2회 샘플링하여 검사. 모든 경로 연결 요소가 기존 보도·광장·보행교와 접한다.
- UnityQA.json 및 GameView480x854.png: 원본 프로젝트를 복사한 `Temp/B`에서 실제 Unity Scene 로드 및 기존 Camera의 480×854 렌더 검사. 원본 프로젝트에 검증용 C#을 추가하지 않았다.
- BuildQA.json, BrowserQA.json 및 WebGL480x854.png: 해당 Scene의 WebGL Release 빌드와 Edge WebGL2의 480×854 모바일 viewport 검사. 실제 모바일 기기의 성능/주행 테스트는 수행하지 않았다.
- Maps_vs_NewGamePlay_480x854.png: 기준 이미지와 실제 Unity Camera 렌더 비교. 산책로 연결은 개선됐지만 자연물 밀도와 강변 돌 표현은 기준 이미지에 크게 못 미친다.

Unity 라이선스 연결이 제한된 첫 실행은 실패했고, 해당 검증 프로세스만 종료한 뒤 허용된 환경에서 재실행했다. 테스트 수치와 이미지는 재실행 결과다.

## 추가 필요 리소스

- 같은 스타일의 Tree Small/Medium/Large 및 FlowerTree
- Bush/Plant 및 선택적으로 FlowerBed
- RiverRock Small/Medium/Large
- 선택적으로 ParkPath 전용 Sprite 모듈(직선·코너·T자)

현재 정적 산책로는 동작하지만 전용 ParkPath Sprite는 없다. 핵심 자연물 리소스가 준비되기 전에는 작업지시서의 전체 완료 조건을 만족하지 않는다.

Git Commit / Push / Merge를 수행하지 않았다.
