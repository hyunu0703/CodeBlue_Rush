# 4단계 네비게이션

## 기존 시스템 분석

- `CityLayout`은 Seed로 셀의 북/동/남/서 연결 비트를 만든다. 이 비트는 Prefab 선택과 배치를 위한 정보이며 네비게이션 간선으로 사용하지 않는다.
- `CityMap`은 셀별 `RoadChunk`, 포트 및 생성된 `TrafficLane` 참조를 소유한다. 생성 완료 전 실제 방향 그래프의 양방향 도달 가능성을 검사한다.
- `RoadChunk`은 차선과 경계 포트 목록의 소유자다. 도로끼리 연결되어 있다고 해서 모든 차선 방향으로 이동할 수 있는 것은 아니다.
- `RoadConnection.TryConnect()`는 위치/접선/차선 순서를 검증한 뒤 진출 Lane → 이웃 진입 Lane을 연결한다. `Disconnect()`는 이 참조를 제거한다.
- 교차로는 Entry → 내부 Route → Exit의 실제 곡선 Lane으로 구성되어 있다. 분기는 `GetNext(index)`로 제공된다. `NextLane`은 단일 후속 경로에만 유효하므로 탐색에 사용하지 않는다.
- `LeftLane`/`RightLane`은 인접 차선 정보다. 이번 단계에서는 횡이동 경로를 새로 만들지 않으며 정방향 `GetNext()`만 탐색한다.

## 구현과 자료구조

`NavigationRoute`가 목적지와 경로 결과를 소유한다. 별도 도로, 복제된 인접 그래프, Manager, Interface는 없다.

현재 요구사항에서는 차선 전환 한 번을 동일 비용으로 취급한다. `Queue<TrafficLane>`와 부모 `Dictionary`를 사용하는 BFS의 탐색 비용은 O(V + E), 공간은 O(V)다. 실제 Lane 길이는 서로 다르므로 **미터 단위 최단거리나 최단시간을 보장하지 않는다**.

데이터 흐름:

`현재 구급차 Transform → CityMap.TryLocate → 실제 TrafficLane.GetNext BFS → Lanes / Points → RouteChanged → NavigationUI`

`TryLocate`는 생성기의 기존 셀 배열로 현재 셀과 주변 8셀만 검사한다. Lane 전체 목록이나 Scene 전체를 검색하지 않는다. 기본 투영 허용 거리는 2 Unity 단위, 최대 허용 값은 `CellSize / 2`다. 이 조회는 현재 프로젝트의 셀 안에 놓인 도로 Prefab 규격을 전제로 한다. 화면/물리의 Z와 관계없이 도로 위치는 XY로 투영한다.

생성 시 한 번 만드는 `CityMap`의 HashSet은 현재 도시의 Lane 소유권 확인용이며 별도 도로 그래프가 아니다. BFS는 매번 실제 연결 참조를 읽어 끊어진 연결, 비활성 Lane, 다른 도시 Lane 및 끝점/접선이 맞지 않는 간선을 배제한다.

같은 Lane의 앞쪽 목적지는 부분 경로, 같은 위치는 점 하나로 반환한다. 뒤쪽 목적지는 실제 정방향 순환으로 재진입해야 하며 순환이 없으면 `NoPath`다.

## 공개 API와 다음 단계 연결 지점

```csharp
// Inspector로 연결했다면 Configure 호출은 필요 없다
navigation.Configure(cityMap, ambulance.transform);

// 환자/병원 시스템은 접근 가능한 목적지 좌표만 전달한다
bool success = navigation.SetDestination(destinationWorldPosition);

// 교차로에서 곡선들이 겹치거나 도로변 목적지의 접근 차선을 정확히 지정할 때 사용한다
success = navigation.SetDestination(accessLane, distanceFromLaneStart);

// 외부 시스템의 명시적 재탐색 요청
success = navigation.Recalculate();

// 외부 시스템이 도로 Transform 등을 변경한 뒤 현재 경로만 검사한다
success = navigation.ValidateRoute();

navigation.ClearDestination();
```

- `Status`: NoDestination / MapUnavailable / InvalidStart / InvalidDestination / NoPath / Ready / Invalidated
- `HasDestination`, `Destination`: 요청된 목적지 상태. 실패해도 재시도를 위해 요청은 보존된다. 무효 요청의 좌표는 `Ready`로 확인하기 전 UI 표시에 사용하지 않는다.
- `Origin`: 계산 시 플레이어 위치. `StartDistance`/`EndDistance`: 첫/마지막 Lane에서의 거리.
- `Lanes`: 실제 차선 참조의 진행 순서. 순환 경로에서는 같은 Lane이 처음과 끝에 나타날 수 있다.
- `Points`: 시작/끝 거리로 잘린 실제 도로 중심선의 월드 좌표. 도로 밖 목적지까지의 가짜 직선은 추가하지 않는다.
- 두 목록은 `ReadOnlyCollection`으로 감싸므로 외부에서 캐스팅해도 추가/삭제/교체가 불가능하다. 최신 결과의 읽기 전용 뷰이며, 새 요청/실패/해제 시 이전 내용은 즉시 비워진다.
- `RouteChanged`: 성공뿐 아니라 실패와 초기화도 전달하므로 UI가 이전 경로를 지울 수 있다.

월드 위치 API는 가장 가까운 실제 차선을 고른다. 교차로에서 동일 위치에 경로가 겹치는 경우 좌표만으로 의도한 접근 방향을 구별할 수 없으므로 목적지는 Lane/거리 API를 사용한다. 구급차는 자신의 위치에 가장 가까운 Lane으로 투영되며 차체 방향으로 도로의 일방통행 방향을 바꾸지 않는다.

## 재계산과 수명

- 목적지 설정과 `Recalculate()` 호출 때만 정상 경로를 탐색한다.
- 현재 경로에 포함된 Lane의 연결 변경 또는 비활성화는 경로를 즉시 제거한다. Play 모드에서는 도로 양쪽의 변경이 완료되도록 다음 프레임에 한 번만 재탐색한다. 중복 Coroutine은 실행하지 않는다.
- 도시 비활성화/교체/재연결은 `CityMap.StateChanged`로 처리한다. 목적지 요청에서 도시 생성 API를 호출하지 않는다.
- 새 도시에서는 이전 도시의 Lane 목적지는 거절한다. 월드 목적지는 새 도시에서 다시 투영한다.
- 목적지를 지우거나 컴포넌트를 끄면 예약된 재탐색을 취소하고 구독을 해제한다.
- 실패 후 차선을 다시 켜는 등 외부에서 상태를 복구한 경우 `Recalculate()`를 요청한다.
- 런타임 도로 Transform/내부 직렬화 데이터를 직접 편집하는 시스템은 `ValidateRoute()` 또는 `Recalculate()`를 명시적으로 호출해야 한다. 생성된 도로는 정적으로 사용한다.
- Update/FixedUpdate/LateUpdate는 없다. UI도 경로 변경 시에만 메시를 갱신한다.

현재 UI의 시작 표식은 **계산 시점의 현재 위치를 도로에 투영한 위치**다. 자동 주행 추적, 경로 이탈 감지, 이동 중 경로 소모, 도착 처리와 자동 확대는 이번 기반에 포함하지 않는다. 주행 후 현재 위치에서 다시 안내하려면 `Recalculate()`를 호출한다. 환자 생성/픽업, 병원 도착, 상황보고 등 5단계 이후 기능은 구현하지 않았다.

## Inspector와 데모 사용법

1. `Assets/Scenes/NavigationDemo.unity`를 열거나 `CodeBlue Rush > Navigation > Create Demo Scene`으로 별도 데모를 만든다.
2. Play 후 `CityMap`의 `NavigationRoute` Inspector에서 **Preview Generated Road Route**를 누른다. 에디터 테스트 버튼이며 실제 생성 Lane 위에 구급차를 배치하고 도시의 마지막 Lane까지 경로를 계산한다.
3. **Test Destination / Set Test Destination**으로 월드 목적지를 변경한다. **Recalculate From Player**, **Clear Destination**도 시험할 수 있다.
4. 초록 사각형은 출발, 하늘색 선은 실제 경로, 빨강 사각형은 목적지다. 같은 위치는 두 표식이 겹친다.

기존 게임 씬에 연결할 때:

- `NavigationRoute`의 Map에 기존 `CityMap`, Player에 기존 구급차 Transform을 연결한다.
- CanvasScaler를 Scale With Screen Size, Reference Resolution **480 × 854**로 설정한다.
- 상단 앵커의 UI 패널에 `NavigationUI`를 추가하고 Navigation에 위 컴포넌트를 연결한다. 권장 패널 크기는 화면 폭에서 좌우 16씩 제외, 높이 200이다.
- `NavigationUI`는 도시 전체 비율을 유지해 경로만 표시한다. 입력을 가로채지 않도록 Raycast Target은 꺼진다.
- 데모의 Canvas/구급차 Prefab은 실제 참조로 연결되어 있다. 기존 SampleScene과 빌드 씬 목록은 변경하지 않는다.

## 변경 파일과 이유

기존 파일:

- `Assets/Scripts/Map/CityMap.cs`: 기존 셀 기반 위치 조회, Lane 소유권 조회와 도시 상태 이벤트.
- `Assets/Scripts/Road/TrafficLane.cs`: 실제 선분 투영과 연결/활성 상태 변경 이벤트.

새 파일:

- `Assets/Scripts/Map/NavigationRoute.cs`: 목적지 API, BFS, 읽기 전용 경로와 무효화 처리.
- `Assets/Scripts/Map/NavigationUI.cs`: 이벤트 기반 경로선 및 양 끝 표식.
- `Assets/Editor/Map/NavigationEditor.cs`: 목적지 테스트 Inspector 및 연결된 데모 생성.
- `Assets/Editor/Map/NavigationValidation.cs`: 실제 객체를 사용하는 회귀/경로/Play/UI 검증.
- `Assets/Scenes/NavigationDemo.unity` 및 새 Unity 자산의 `.meta`: 설정된 데모와 GUID.
- `NavigationSystem_Stage4.md`: 분석, 사용법, 설계 한계와 검증 기록.

## 검증 실행

- `CodeBlue Rush > Navigation > Validate Routes`: 기존 2/3단계 회귀와 다중 Seed, 모든 결과 간선의 실제 정방향 참조, 경로 양 끝 절단, 순환/역주행 금지, null/NaN/범위 밖 목적지, 결과 초기화, 데이터 수정 방지, 도시 교체를 검사한다.
- `CodeBlue Rush > Navigation > Validate Play Lifecycle`: 별도 데모에서 실제 비활성 이벤트, 다음 프레임 자동 재탐색, 동일 도시 재활성화, 유휴 프레임 재탐색 방지, 예약 취소와 UI 메시를 검사한다.
- 배치 실행 로그: `Logs/navigation-validation.log`, `Logs/navigation-play-validation.log`.
- UI 검토 이미지: `Logs/navigation-480x854.png`.

2026-10-01 Unity 6000.3.23f1 실행 결과:

- 컴파일 성공, 기존 Road 53개와 City 286개 검증 통과.
- 네비게이션 5,764개 조건 검증 통과. 7개 Seed에서 여러 목적지를 계산하고 결과의 각 간선을 검사했다.
- Play 수명/UI 11개 검증 통과. 테스트 실행 구간에 Unity 오류 로그가 없는 것도 검사했다.
- 480×854 렌더 이미지를 직접 확인했다. 상단 패널 안에 초록 출발점, 하늘색 경로선, 빨강 목적지가 표시된다.
- 검증 과정에서 도시 비활성화 콜백 도중 Coroutine 시작이 시도되는 문제를 발견해 `activeInHierarchy` 조건으로 수정했다. 재연결 시 상태 이벤트가 두 번 발생하는 문제도 단일 알림으로 수정했다.
- Edit 모드는 일반 MonoBehaviour의 비활성화 콜백 검증을 대신하지 않으므로 명시적 검사와 실제 Play 수명 검증을 분리했다.
- `git diff --check` 통과. 네비게이션 런타임 코드에 Update, Find, GetComponent 또는 도시 재생성 호출이 없다.
- WebGL 빌드와 실제 모바일 터치 주행 검증은 수행하지 않았다. 성능 복잡도는 코드 검토 결과이며 Profiler 측정 수치는 아니다.

Git Commit / Push는 수행하지 않는다. 추천 제목: `feat: add lane-based BFS navigation and portrait route UI`.
