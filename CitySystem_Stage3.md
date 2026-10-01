# 3단계 — Seed 기반 랜덤 맵 생성 보고서

## 1. 구현한 기능

프로젝트 루트의 `CodeBlueRush_DevelopmentGuide.md`, `ScriptRules.md`와 기존 차량/도로 코드, Prefab, 씬, 2단계 검증 코드를 먼저 확인했다.

- RoadChunk 기반의 규칙형 도시 생성: 외곽 순환도로 + Seed로 선택한 내부 가로/세로 도로.
- Straight / Corner / T-Junction / 4-Way Intersection 배치 및 90도 단위 회전.
- 도로 끝의 위치·방향·차선 수를 맞춘 양방향 접속.
- 교차로 Prefab 내부의 직진·좌회전·우회전 경로와 차선 분기·합류. U턴 경로는 없음.
- 도시 외곽으로 열린 도로, 끊어진 접속, 잘못된 차선 기하, 도시 밖 참조를 검출.
- 차선 그래프 전체가 **강하게 연결**되는지 검증: 모든 차선에서 모든 다른 차선으로 진행 가능해야 성공.
- 최초 한 번 생성, 반복 요청 시 도시 재사용, 새 Seed에 의한 명시적 도시 교체.
- 실패한 생성의 임시 객체 정리 및 기존 도시 보존.
- 도시 재활성화 시 동일 객체와 Seed를 유지하며 차선 연결만 복구.
- 생성기 컴포넌트 제거 시 생성기가 소유한 도시 정리.

구급차 운전, 네비게이션, 환자 위치/미션, 차량·시민 AI, 신호등은 구현하거나 변경하지 않았다.
교차로는 이번 단계에 필요한 도로 표면과 차선 경로만 가진다. 신호 제어와 교차로 통행 판단은 이후 단계의 책임이다.
이번 도시는 도로 기반 구조이며 횡단보도·보도·건물·장식의 개별 배치 시스템은 포함하지 않는다. 정적 아트는 Prefab 자식으로 추가할 수 있다.

## 2. 변경한 기존 파일

| 파일 | 변경 이유 |
| --- | --- |
| `Assets/Scripts/Road/TrafficLane.cs` | 교차로 내부 후속 경로 `internalNext`, `NextCount`, `GetNext` 추가. 기존 `NextLane`, Left/Right, 거리 조회 유지 |
| `Assets/Scripts/Road/RoadChunk.cs` | 시작/끝이 모두 외부 포트라는 기존 가정을 확장하여 내부 분기/합류와 경계 연결의 소유권·기하를 검증. 비활성 Prefab도 소유권 조회 가능 |
| `Assets/Scripts/Road/RoadConnection.cs` | Prefab 검사용 Target 존재 조회와 비활성 계층 소유권 검사 추가. 외부 연결·해제 알고리즘 유지 |
| `Assets/Editor/Road/RoadSamples.cs` | 포트 생성, Inspector 참조 배열 저장, 메시 띠 생성의 기존 도구 함수를 같은 Editor 어셈블리에서 재사용할 수 있게 접근 범위만 변경 |

기존 직선/코너 2·4차선 Prefab, RoadDemo, SampleScene, 차량 스크립트/Prefab, Packages, ProjectSettings는 변경하지 않았다.
기존 직렬화 필드의 이름은 변경하지 않았다.

## 3. 새로 생성한 파일

| 파일 | 책임 |
| --- | --- |
| `Assets/Scripts/Map/CityLayout.cs` | 불변 격자 연결 데이터, 고정 정수 난수 알고리즘, 생성 규칙 |
| `Assets/Scripts/Map/CityMap.cs` | 생성 수명, Prefab 규격 검사·배치·접속, 완성 그래프 검증, 성공한 도시 공개 |
| `Assets/Editor/Map/CitySamples.cs` | 격자 규격 도로 및 교차로 Prefab, 독립 데모 씬 작성 도구 |
| `Assets/Editor/Map/CityValidation.cs` | 재현성·접속·실패 복구·실제 Play 수명 검증 |
| `Assets/Prefabs/City/Straight.prefab` | 기존 왕복 2차선 직선의 원점을 셀 중심으로 정렬한 복사본 |
| `Assets/Prefabs/City/Corner.prefab` | 기존 왕복 2차선 코너의 원점을 셀 중심으로 정렬한 복사본 |
| `Assets/Prefabs/City/TJunction.prefab` | 3방향 진입/진출 및 내부 경로 6개 |
| `Assets/Prefabs/City/Intersection.prefab` | 4방향 진입/진출 및 내부 경로 12개 |
| `Assets/Prefabs/City/TJunction.asset`, `Intersection.asset` | 교차로 표면 메시 |
| `Assets/Scenes/CityDemo.unity` | 고정 Seed 예제로 Play 시 한 번 생성하는 시작 씬 |
| `CitySystem_Stage3.md` | 본 보고서 |

새 Unity 에셋과 폴더의 `.meta`도 포함한다. 공용 RoadSurface 재질과 직선/코너 메시를 재사용한다.
런타임 신규 최상위 클래스는 2개다. Interface, 별도 Manager, 상속 기반 시스템을 추가하지 않았다.

## 4. 2단계와의 연결

기존 RoadChunk가 도로 내부 차선과 RoadConnection을 소유한다. CityMap은 해당 참조를 읽어 도로를 배치하고 기존 `TryConnect`를 호출한다.
Sprite나 렌더링 메시의 모양을 분석해 차선을 추측하지 않는다.

기존 차선의 외부 후속은 `RoadConnection`만 변경한다. 새 `internalNext`는 Prefab에 직렬화된 내부 후속 목록이다.

```text
이웃 도로 차선
  → RoadConnection이 연결한 교차로 Entry
  → Entry.GetNext(index)로 지정된 Route
  → Route의 단일 Exit
  → RoadConnection이 연결한 이웃 도로 차선
```

T자 Entry는 2개, 사거리 Entry는 3개의 후속 Route를 가진다. Route는 Exit 하나로 이어지며 여러 Route가 같은 Exit로 합류할 수 있다.
`RoadChunk.Validate`는 내부 참조가 같은 도로 안에 있는지, 끝점과 방향이 이어지는지, 진출 포트와 내부 후속을 동시에 지정하지 않았는지 검사한다.
중복 참조, 자기 참조, 경계 진입 차선으로의 내부 연결, 미등록 시작/끝도 거절한다.

**NextLane 호환 규칙:** 후속이 정확히 하나일 때 그 차선을 반환한다. 후속이 없거나 여러 개이면 null이다.
기존 직선/코너에서는 동작이 그대로다. 새 소비자는 `NextCount`와 `GetNext(index)`를 사용해야 하며, 교차로의 NextLane=null을 막다른 길로 해석하면 안 된다.
Left/Right는 같은 방향의 인접 차선 참조라는 기존 의미를 유지한다. 이번 격자 예제는 왕복 2차선이므로 Left/Right는 null이다.

## 5. 랜덤 맵 생성 데이터 흐름

```text
EnsureGenerated / TryStartNewCity(seed)
 → 크기·밀도·생성기 Transform 검사
 → CityLayout: 외곽 순환도로 + 내부 가로·세로 도로 선택
 → Prefab 포트 규격 검사 + 회전별 16개 마스크 조회표
 → 행/열 순서로 RoadChunk 인스턴스 배치
 → 북·동 이웃끼리 TryConnect (각 접속 한 번)
 → 모든 접속, 차선 소유권·기하 검사
 → 정방향/역방향 BFS로 전체 차선 도달 가능성 검사
 → 성공한 도시 공개, 이전 도시 해제, Generated 이벤트
```

각 셀의 방향은 4비트(북=1, 동=2, 남=4, 서=8)다. 비어 있는 셀은 0이다.
서로 인접해 보여도 생성 규칙에서 정한 연결 비트만 사용한다.
외곽 순환도로가 모든 내부 도로의 양 끝을 받아 주므로 차수 1의 막다른 길을 만들지 않는다.
내부 가로/세로 도로를 적어도 하나씩 보장해 교차 분기가 없는 독립 방향 순환을 피한다.

새 도시는 임시 루트 아래에서 완성한다. 검증 실패 시 해당 루트만 정리하며 기존 Seed·도로 객체·준비 상태는 보존한다.
성공 시 기존 루트를 즉시 비활성화하여 충돌/참조 사용을 막고 Play에서는 프레임 끝에 파괴한다. 에디터 검증에서는 즉시 해제한다.
`Generated`는 완성된 새 도시를 공개한 후 발생하며 반복 EnsureGenerated나 재활성화에서는 발생하지 않는다.

### 자료구조와 실행 비용

- `byte[]`: W×H 연결 마스크. 셀 조회 O(1).
- `RoadChunk[]`, `RoadConnection[,]`: 도로·방향별 포트 직접 조회 O(1).
- 길이 16의 Prefab 회전 조회표: 셀마다 모든 Prefab/도로를 탐색하지 않음.
- `TrafficLane[]`: 완성 도시의 안정된 생성 순서로 O(1) 인덱스 접근.
- 검증 시 `Dictionary<TrafficLane, int>`, 인접 목록, Queue: 정방향·역방향 BFS 각각 O(V+E).
- 전체 생성/검증: 고정된 모듈 크기에서 O(W×H+P+V+E), 메모리도 같은 차수. P는 차선 경로 점 수.
- 포트당 기존 RoadChunk 검증을 다시 수행하지만 도로의 포트 수는 최대 4개이며 도시 전체를 중첩 검색하지 않는다.
- 런타임 Update/FixedUpdate/LateUpdate, 전체 씬 Find는 없다.
- 재활성화 시에만 1프레임 대기 코루틴 한 개를 사용한다. OnDisable에서 중단하여 중복 실행을 방지한다.
- 매 프레임 맵을 다시 검증하지 않는다. 생성 완료와 명시적 Validate에서 검사한다.

## 6. Seed와 생성 수명

CityLayout은 `unchecked uint` LCG(`state = state * 1664525 + 1013904223`)를 고정된 순서로 진행한다.
`UnityEngine.Random`과 런타임별 `System.Random` 구현에 의존하지 않는다.
같은 **Seed + Width + Height + Density + Cell Size + 생성 알고리즘 버전/Prefab 데이터**이면 같은 연결 구조와 배치를 재현한다.
`CityLayout.Version`은 1이다. 생성 알고리즘을 변경할 때 재현성이 바뀐다면 버전도 변경해야 한다.
딕셔너리 열거 순서, 프레임 시간, 오브젝트 InstanceID는 생성 결정에 사용하지 않는다.

- 일반 CityMap 컴포넌트: Random Seed On Start 기본값 true.
- 제공 데모: 재현용으로 false, Initial Seed=12345.
- `EnsureGenerated(out error)`: 최초만 생성. 성공 후 여러 번 호출해도 같은 도시를 반환.
- 상황보고마다 생성기를 호출할 필요가 없다. 호출하더라도 EnsureGenerated가 기존 도시를 유지한다.
- `TryStartNewCity(newSeed, out error)`: 명시적인 새 도시 요청. 현재와 같은 Seed는 거절.
- `CreateSeed()`: Unity 전역 난수 상태를 건드리지 않고 새 Seed 발급. 현재 Seed와 같지 않도록 처리.

Game Over 시스템은 아직 없으므로 실패 여부를 생성기가 추측하지 않는다. 나중의 Game Over 처리자가 다음과 같이 호출한다.

```csharp
int seed = cityMap.CreateSeed();
bool generated = cityMap.TryStartNewCity(seed, out string error);
```

다른 Seed가 항상 다른 모양을 보장하지는 않는다. 유한한 구조 공간에서 같은 배치가 나올 수 있지만, 성공한 새 도시 요청은 새 인스턴스를 만든다.
비활성화/재활성화는 새 게임으로 취급하지 않는다. 복구 코루틴 완료까지 IsReady=false이며, 동일 Seed·객체를 유지한다.

## 7. Unity 설정

### 바로 실행

1. Unity 6000.3.23f1에서 가져오기와 컴파일이 끝나면 `Assets/Scenes/CityDemo.unity`를 단독으로 연다.
2. Game 뷰를 480×854로 설정한다. 제공 카메라는 전체 도시를 보는 고정 카메라다.
3. Play하면 CityMap 아래 `City_12345` 루트 하나가 생긴다. 실행 전 씬에 도시를 미리 직렬화하지 않는다.
4. Scene Gizmos로 차선 화살표와 초록색 연결 지점을 확인한다. 도시 경계에도 노란색 미연결 포트가 없어야 한다.
5. 설정을 바꾸려면 Play를 종료한 뒤 CityMap Inspector의 초기 Seed/크기/밀도를 바꿔 다시 실행한다.

### Inspector

| 항목 | 설정 |
| --- | --- |
| Straight / Corner / T Junction / Intersection | `Assets/Prefabs/City`의 대응 RoadChunk Prefab |
| Width / Height | 각 5~24, 기본 9 |
| Density | 0~100, 기본 35. 내부 가로/세로 도로 선택 확률이며 점유 셀 비율은 아님 |
| Cell Size | 예제는 20. Prefab 포트 위치와 일치해야 함 |
| Random Seed On Start | 새 게임마다 임의 Seed면 true, 재현 테스트는 false |
| Initial Seed | 고정 Seed 모드에서 사용할 정수 |

CityMap 루트는 월드 스케일 (1,1,1), XY 평면이어야 한다. 이동 및 Z축 회전은 지원한다.
기존 구급차를 시험 배치할 경우 기본 생성기 Transform에서 `(1, 0, 0)`, Z=0은 남서쪽 코너의 북향 차선 출구다.
구급차 입력/물리는 기존 Prefab을 그대로 사용한다. 플레이어 자동 배치·최근접 차선 탐색은 구현하지 않았다.

### Prefab 규격

- 하나의 모듈은 20×20 셀 중심을 원점으로 사용한다.
- 포트 위치는 북(0,10), 동(10,0), 남(0,-10), 서(-10,0) 중 해당 방향이며 local +Y가 바깥 방향이다.
- 방향별 포트는 하나이며 Prefab의 Target은 비어 있어야 한다. 생성기가 접속 상대를 결정한다.
- 맞닿는 포트의 진입/진출 차선 수와 순서, 끝점 위치, 방향이 일치해야 한다.
- Prefab 루트 스케일은 1이며 도로와 필요한 포트·차선 컴포넌트는 활성 상태여야 한다.
- 원래 2단계 Prefab은 끝점 원점 규격이므로 그대로 CityMap 슬롯에 넣으면 규격 검사에서 거절된다. 정렬된 City Prefab을 사용한다.
- 격자 예제는 왕복 2차선이다. 기존 왕복 4차선 도로 지원은 유지되지만, 4차선 도시를 만들려면 교차로를 포함해 네 종류 모두 같은 규격으로 제작해야 한다.
- 생성기는 교차로 내부 경로를 런타임에 만들지 않는다. 완료된 Prefab에 저장된 경로를 사용한다.

`CodeBlue Rush > City > Create Missing Prefabs`는 없는 예제만 작성한다.
`Create Demo Scene`은 기존 씬 저장/취소 확인 후 새로운 미저장 데모 씬을 만든다.

## 8. 테스트 방법과 결과

- `CodeBlue Rush > City > Validate Generation`: 기존 2단계 53개 + 도시 286개 검증.
- 200개 Seed의 정수 구조 재현·양쪽 방향 비트·경계 폐쇄.
- 32개 Seed의 실제 Prefab 배치·연결과 차선 강연결 검사.
- 동일 Seed 두 인스턴스의 도로 이름/회전 반영 경로 좌표/분기 수 비교.
- 정수 Seed 양 끝값, 최소 5×5와 최대 24×24, 밀도 0/100, 루트 이동/회전.
- 중복 생성 방지, 동일 Seed 교체 거절, 전역 Unity Random 상태 보존.
- Prefab 누락·잘못된 셀 크기·잘못된 맵 크기·실제 접속 불일치 시 기존 도시 보존 및 임시 도시 정리.
- 분기 API, 열린 접속 검출, 인덱스 범위, 생성 완료 이벤트 횟수.
- `CodeBlue Rush > City > Validate Play Lifecycle`: 독립 데모 씬에서 8개 검증 후 Play 종료.
- Play 검증: Start 자동 생성, 반복 요청 재사용, 비활성화 연결 해제, 재활성화 보존/복구, 컴포넌트 재활성화, 새 Seed 교체, 이전 도시 지연 파괴, 생성기 제거 시 소유 도시 정리.

Unity 6000.3.23f1의 독립 임시 프로젝트(`Temp/CityValidation`)에서 실행했다. 열려 있는 원본 프로젝트를 닫거나 재시작하지 않았다.
최종 컴파일 및 도로 53개 + 도시 생성 286개 + Play 수명 8개, **총 347개 검증을 통과**했다. 배치 프로세스는 성공 코드 0으로 종료했다.
로그는 `Temp/city-validation.log`, `Temp/city-play-validation.log`에 보관된다. Temp는 Git 제외 경로다.
그래픽 장치가 있는 플레이 검증은 해당 검증 프로젝트의 `Logs/city-demo.png`에 480×854 이미지를 기록한다.
같은 위치의 `Logs/city-detail.png`에는 접속부 확대 이미지를 기록한다. 기본 렌더러에서 실제 도시 전체 배치와 확대된 코너·교차로 연결을 확인했다.
원본 URP/WebGL 빌드, 구급차 수동 주행, WebGL 기기별 성능 프로파일링은 이번 자동 검증에 포함하지 않았다.

## 9. 발견한 문제와 주의점

- 기존 도로 검증은 활성 씬 객체의 부모 조회를 가정했다. Prefab 자산도 검증하도록 비활성 부모를 포함해 조회하게 수정했다.
- 기존 단일 NextLane만으로 교차로 분기를 표현할 수 없었다. 새 내부 후속 목록으로 확장하고 기존 단일 경로 동작은 유지했다.
- 부모 OnEnable에서 자식 포트가 활성화되기 전에 연결할 수 있었다. 재활성화 복구는 한 프레임 뒤 실행하여 콜백 순서 의존을 제거했다.
- 임시 Unity 프로젝트에서 Editor SearchDatabase 인덱싱의 ArgumentOutOfRangeException이 기록되었으나 도시 검증은 이어서 완료되었다. 도로/도시 코드의 예외는 아니다.
- IsReady는 생성/검증 완료 상태다. 외부에서 도시의 자식이나 차선 데이터를 임의 수정하지 않아야 한다. 변형 여부를 상시 감시하는 Update는 없다.
- 명시적 Validate 실패 시 IsReady를 false로 바꾼다. 오류를 고쳤다면 Validate 또는 새 Seed 생성을 통해 다시 검사해야 한다.
- 생성은 동기식이다. 크기 상한은 24×24이며 최대 구성은 인스턴스와 검증 할당이 많으므로 실제 WebGL 대상에서 측정 후 기본 크기를 정한다.
- Seed만 저장하고 생성 설정이나 Prefab 버전을 바꾸면 같은 맵이 보장되지 않는다.
- 도로 데이터의 강연결은 경로의 연속성과 이동 가능성을 뜻한다. 교차로 차량 충돌 방지나 신호 규칙을 구현했다는 뜻이 아니다.
- 각 도로의 시각적 표면과 물리적 경계 Collider는 별개다. 이번 도로 표면에는 차량을 막는 Collider가 없다.
- 전체 도시를 480×854 한 화면에 축소하면 얇은 차선 표시는 서브픽셀 크기가 되어 일부가 보이지 않을 수 있다. 확대 화면에서 연결을 확인했으며 주행용 카메라는 도시 전체 보기보다 가까운 배율이 필요하다.

## 10. 4단계 네비게이션 연결 지점

| API | 사용 목적 |
| --- | --- |
| `CityMap.IsReady` | 초기 생성 또는 재활성화 복구 완료 확인 |
| `CityMap.Generated` | 성공한 새 도시 공개 후 캐시/목적지 참조 교체 |
| `CityMap.Seed`, `Layout`, `CellSize` | 생성된 도시의 실제 메타데이터 |
| `GetRoad(x,y)` | 격자 셀 도로 직접 조회 |
| `LaneCount`, `GetLane(index)` | 현재 도시의 차선 그래프 정점 접근 |
| `TrafficLane.NextCount`, `GetNext(index)` | 실제 내부 분기 및 외부 연결 간선 접근 |
| `TrafficLane.Length`, `TrySample` | 경로 길이와 월드 좌표/진행 방향 조회 |
| `RoadConnection.ConnectedTo` | 실제 도로 접속 관계 조회 |

후속 시스템은 Generated를 구독하고 이미 IsReady라면 현재 도시를 읽는 방식으로 초기화 순서에 대응할 수 있다.
재활성화는 새 도시 생성 이벤트를 발생시키지 않으므로 복구 프레임 후 IsReady를 확인하고 기존 그래프를 재사용한다.
구독자는 자신의 OnDisable 등에서 이벤트 구독을 해제해야 한다.
새 도시 공개 후 이전 도시의 Lane/RoadChunk 참조는 폐기해야 한다.
네비게이션용 별도 길은 만들지 않았다. 경로 탐색, 현재 구급차 차선 찾기, 목적지 선택, 경로 UI는 4단계 이후 구현 대상이다.

## 11. 추천 Git Commit 제목

`feat: generate seeded connected cities from road chunks`

Git Commit / Push는 수행하지 않았다.
