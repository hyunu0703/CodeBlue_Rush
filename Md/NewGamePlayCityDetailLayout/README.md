# NewGamePlay 소방서·외곽 수목, 공장 컨테이너, 살구색 산책로

사용자 후속 요청에 따라 기존 나무·강변 돌 배치를 유지하면서 소방서와 외곽 도로 주변의 수목, 공장 주변의 컨테이너, 나무 군집 주변의 살구색 보행길을 보강했다. 작업 시작 시 GitHub main과 로컬 HEAD는 모두 `34575d290f756faf95a6aee69a129437fd276579`였다. `Assets/Images/Maps/Reference/Maps.png`와 환경·리소스·건물·도로 가이드를 기준으로 작업했다.

## 변경 내용

- **나무 193개 추가**: 소방서 뒤·옆 녹지 28개, 외곽 도로 녹지의 빈 구간 165개. 기존 724개와 합쳐 총 917개다. 훈련탑 양옆의 투명 여백은 실제 건물 Sprite 알파 윤곽을 확인해 녹지로 사용했다. 소방서 출동 마당과 진입로에는 추가하지 않았다.
- **컨테이너 24개 추가**: 북서쪽 창고 3동·물류센터 주변에 21개, 남동쪽 공장 뒤쪽에 3개. 기존 항구 20개와 합쳐 총 44개다. `Container_Base.prefab`과 원본 Sprite를 재사용하고 파랑·노랑·주황 Tint를 적용했다. 새 장식 인스턴스의 BoxCollider2D만 제거하는 Prefab override를 적용해 기존 Prefab과 기존 충돌체는 유지했다. 건물 정면의 출입 공간과 적재장의 가운데 통로를 비워뒀다.
- **살구색 보행길 7개 구역 Mesh 추가**: 북쪽 정원, 병원 녹지, 남동쪽 정원, 강변 정원, 소방서 녹지, 외곽 녹지, 산업 구역 가장자리. 기존 산책로 7개는 위치·형태·UV·삼각형을 유지하고 Vertex Color만 동일한 살구색 `#EBC097`로 맞췄다. 모든 길은 기존 `BuildingSite.mat`와 단색 Texture를 공유한다. 새 Material·이미지·Atlas는 필요하지 않았다.

나무 수관의 가장자리가 길에 그늘을 드리울 수 있지만 줄기 주변에는 잔디 여백을 남겼다. 기존 보도 또는 산책로로 접근하는 짧은 연결 구간도 추가했다. 안전하게 연결할 수 없는 후보 조각은 제거했다. 길을 도로·보도·하천·건물·주차장·출동 마당 위에 그리지 않는다. 경로 계산은 작업 과정에서만 사용한 오프라인 도구이며 최종 Scene에는 고정 Mesh와 Prefab 인스턴스만 저장했다.

`City/StaticDecoration/Nature`에 `FireStationGardens`, `OuterRoadTreeInfill` 그룹을 추가했다. 컨테이너는 `City/StaticDecoration/FactoryContainers`, 길은 기존 `City/ParkPathTilemap` 아래에서 관리한다. 런타임 스크립트, 생성기, 새 Sorting Layer와 추가 충돌체는 없다.

## 위치와 검증 기록

- [SourcePlacements.json](SourcePlacements.json): 추가 나무·컨테이너의 종류, 위치, 표시 크기, Tint와 최종 길 Mesh의 좌표. 원본 픽셀 → Unity 좌표는 `x=(px-470.5)/8, y=(836-py)/8`이다.
- [LayoutQA.json](LayoutQA.json): 보호 영역 침범 검사, 기존 Scene 블록 보존, Scene 및 길 Mesh의 SHA256. 최종 길 Mesh를 다시 읽어 41개 보행길 연결 구역의 보도 접근을 확인했다(기준 이미지 2px, Game View 약 1px의 경계 샘플링 허용). 접근이 불가능한 후보 조각 2개는 제외했다. 기존 Scene 컴포넌트의 변경은 `StaticDecoration`, `Nature`, `ParkPathTilemap` Transform의 자식 목록 3개뿐이다. 나머지 기존 건물·도로·수면·돌·나무·Camera는 그대로다.
- [UnityQA.json](UnityQA.json): 실제 Unity 로드, 리소스 연결, Prefab 연결, 기존 배치와 Camera, Collider 수 검사.
- [GameView480x854.png](GameView480x854.png), [Camera941x1672.png](Camera941x1672.png): 기존 Camera의 실제 Unity 렌더.
- [Maps_vs_NewGamePlay_480x854.png](Maps_vs_NewGamePlay_480x854.png): Maps.png와 실제 Unity 화면 비교.
- [BuildQA.json](BuildQA.json), [BrowserQA.json](BrowserQA.json), [WebGL480x854.png](WebGL480x854.png): 최종 배치의 WebGL 빌드 및 모바일 크기 브라우저 로드 검사.

기준 이미지처럼 건물과 나무 사이에 따뜻한 보행길이 생기고, 소방서 뒤쪽 수목과 공장 적재 공간이 보강됐다. 기존 도로와 지형을 유지하므로 원본보다 매우 좁은 좌측·우측 하단 외곽 녹지에서는 작은 나무를 사용했다. 특히 좌측 녹지 폭은 원본 기준 약 4px여서 큰 수관의 숲을 동일하게 재현할 수 없다. 실제 휴대폰의 성능과 차량 주행 검사는 별도다.

검증용 프로젝트·C#·오프라인 작업 도구는 ignored `Temp`에만 보관했다. 원본 Assets에는 C#을 추가하지 않았다. 추가 필요 리소스 없음. Commit·Push·Merge는 수행하지 않았다.
