# 2단계 도로 / 차선 시스템

## 범위와 기존 코드 분석

`CodeBlueRush_DevelopmentGuide.md`, `ScriptRules.md`를 기준으로 2단계만 구현했다.
랜덤 생성, 경로 탐색, 차량 AI, 차선 변경 행동, 교차로, 신호등, 미션은 구현하지 않았다.

기존 책임은 다음과 같다.

| 파일 | 기존 책임 / 이번 연결 방식 |
| --- | --- |
| `AmbulanceController` | Rigidbody2D 가속·제동·조향·횡미끄러짐. 변경 없음 |
| `KeyboardAmbulanceInput` | 키보드 입력을 SetInput으로 전달. 변경 없음 |
| `AmbulanceCamera` | 구급차 추적과 카메라 회전. 변경 없음 |
| `CameraTurnZone` | Trigger로 카메라 회전 구간 지정. 변경 없음 |
| `SteeringWheelUI` | 조향 상태를 핸들 UI로 표시. 변경 없음 |
| `SampleScene` | 도로와 Lane 이름의 객체는 시각 요소이며 차선 데이터가 없었음. 원본 보존 |

플레이어 구급차는 기존 수동 물리 운전을 유지한다. 차선 데이터가 플레이어를 강제로 이동시키지 않는다.
향후 일반 차량의 이동은 Sprite 좌표가 아니라 이번 TrafficLane 경로를 사용해야 한다.

## 변경 및 추가 파일

기존 코드·씬·구급차 Prefab·Packages·ProjectSettings는 수정하지 않았다.
아래 파일 및 Unity 참조용 `.meta`를 추가했다.

| 경로 | 역할 |
| --- | --- |
| `Assets/Scripts/Road/TrafficLane.cs` | 경로, 진행 방향, 거리 조회, Left/Right/Next 참조, Gizmos |
| `Assets/Scripts/Road/RoadConnection.cs` | 도로 끝의 진입/진출 배열, 접속 검증, 양방향 연결·해제 |
| `Assets/Scripts/Road/RoadChunk.cs` | 도로 소유 차선·접속 지점 목록, Prefab 구성 검증 |
| `Assets/Editor/Road/RoadSamples.cs` | 고정 예제 생성과 별도 테스트 씬 생성 메뉴 |
| `Assets/Editor/Road/RoadValidation.cs` | Unity 객체를 사용하는 재실행 가능한 회귀 검증 메뉴 |
| `Assets/Prefabs/Road/Straight2Lane.prefab` | 왕복 2차선 직선, 방향별 1차선 |
| `Assets/Prefabs/Road/Corner2Lane.prefab` | 왕복 2차선 90도 코너 |
| `Assets/Prefabs/Road/Straight4Lane.prefab` | 왕복 4차선 직선, 방향별 2차선 |
| `Assets/Prefabs/Road/Corner4Lane.prefab` | 왕복 4차선 90도 코너 |
| `Assets/Prefabs/Road/*.asset` | 위 네 도로의 표면·차선 표시 메시 |
| `Assets/Prefabs/Road/RoadSurface.mat` | 정점 색상을 사용하는 공용 도로 재질 |
| `Assets/Scenes/RoadDemo.unity` | 직선 → 코너 → 회전 직선으로 연결된 고정 검증 씬 |
| `RoadSystem_Stage2.md` | 본 구현·설정·검증 보고서 |

## 구조와 데이터 흐름

```text
RoadChunk (Prefab 루트)
├─ Lane0 ... LaneN : TrafficLane
│  ├─ points : 차선 Transform 기준의 Vector2 배열
│  ├─ LeftLane / RightLane : 같은 방향의 인접 차선 직접 참조
│  └─ NextLane : RoadConnection이 런타임에 설정
├─ Start / End : RoadConnection
│  ├─ incoming : 이 지점에서 도로 안으로 들어오는 차선
│  ├─ outgoing : 이 지점에서 도로 밖으로 나가는 차선
│  └─ target : 씬의 반대편 연결 지점
└─ Surface : MeshFilter + MeshRenderer
```

1. Prefab이 자기 내부 차선과 연결 지점 참조를 보유한다. 인스턴스마다 내부 참조가 분리된다.
2. 배치한 도로들의 연결 지점 위치와 방향을 맞춘다.
3. 씬의 Target 설정 또는 `TryConnect` 호출로 연결을 요청한다.
4. 전체 도로 구성, 차선 수, 배열 순서, 월드 끝점, 진행 방향, 기존 연결 점유를 검사한다.
5. 모든 검사를 통과한 경우에만 양쪽 `ConnectedTo`와 양방향 `NextLane`을 적용한다.
6. 이용자는 현재 차선을 직접 보유하고 `TrySample(distance, out position, out direction)`으로 경로를 조회한다.
7. 연결 해제 시 양쪽 진출 차선의 Next를 지운다. 다른 도로 끝의 연결은 유지한다.

씬 Target은 OnEnable/Start에서 연결한다. 다른 컴포넌트의 Start 실행 순서를 초기화 완료 신호로 간주하지 않는다.
향후 생성 시스템은 배치 후 TryConnect를 명시적으로 완료한 다음 네비게이션/AI에 도로를 전달해야 한다.

차선 연결 지점은 경로의 첫 점(StartPoint)과 마지막 점(EndPoint)이다.
도로 연결 지점은 RoadConnection의 Transform 위치이며, local +Y가 도로 바깥 방향이다.
Sprite 또는 렌더링 메시에서 경로를 추측하지 않는다.

Left/Right는 운전자 진행 방향 기준이다. 중앙선 너머의 반대 방향 차선은 인접 참조로 지정하지 않는다.
NextLane은 연결된 도로의 같은 진행 방향 차선이다. 연결되지 않은 경계는 null이다.
NextLane은 런타임 파생 데이터로 외부 public setter를 제공하지 않는다.

거리 단위는 월드 Unity 단위다. 거리 범위 밖은 시작/끝에 제한하고, NaN·Infinity·잘못된 경로는 false를 반환한다.
직선은 두 점, 예제 코너는 원호를 나눈 32개 선분이다. 코너의 길이와 위치는 이 폴리라인 기준이다.
위치·회전·스케일 변경 시 거리 캐시를 다시 계산한다. 경로 점은 Inspector에서 편집하며 런타임 경로 편집 API는 만들지 않았다.

Next/Left/Right와 배열 인덱스 조회는 O(1), 거리 표본 검색은 O(log P), 최초 거리 계산은 O(P)다.
P는 해당 차선의 점 개수다. 구성 검증은 연결/편집 시에만 실행하며 HashSet으로 중복을 검사한다.
런타임 Update, 전체 씬 검색, 매 프레임 GetComponent, 매번 발생하는 거리 조회 할당은 없다.

## Unity 사용 방법

### 준비된 예제

1. Unity 6000.3.23f1에서 에셋 가져오기와 스크립트 컴파일이 끝날 때까지 기다린다.
2. `Assets/Scenes/RoadDemo.unity`를 단독으로 연다. 원본 SampleScene은 그대로 유지된다.
3. Scene 뷰의 Gizmos를 켠다. 청록색 선과 화살표는 진행 경로, 초록 점은 시작, 빨간 점은 끝, 노란 원은 미연결 도로 끝이다.
4. Play하면 Target으로 연결된 두 접속부가 초록색이 된다. 외부 양 끝은 열린 상태가 정상이다.
5. 구급차 주행 확인 시 기존 `Assets/Prefabs/Ambulance.prefab`을 추가하고 위치 `(1, 3, 0)`, 회전 Z=0으로 둔다.
6. 기존 키보드 입력으로 W/S/A/D 또는 방향키 주행을 확인한다. 데모 카메라는 전체 도로를 보는 고정 카메라다.
7. 기존 추적 카메라를 사용하려면 기존 AmbulanceCamera의 Target을 연결한다. 코너에서 카메라 회전이 필요하면 기존 CameraTurnZone을 별도로 배치하고 카메라를 연결한다.

`CodeBlue Rush > Road > Create Demo Scene`도 같은 구조의 **새 저장되지 않은 씬**을 만든다.
이 메뉴는 기존 수정 씬에 대해 Unity의 저장/취소 확인 후 새 씬을 연다.
`Create Missing Samples`는 없는 예제만 생성하며 기존 Prefab을 덮어쓰지 않는다.
에디터 생성 도구는 고정 테스트 에셋 작성용이며 런타임 맵 생성기가 아니다.

### 도로 배치와 연결

| 인스턴스 | 위치 | Z 회전 |
| --- | --- | --- |
| Straight4Lane A | (0, 0, 0) | 0 |
| Corner4Lane B | (0, 20, 0) | 0 |
| Straight4Lane C | (10, 30, 0) | -90 |

A.End ↔ B.Start, B.End ↔ C.Start를 Target으로 지정한다.
초기 연결은 한쪽 Target만 있어도 가능하지만, 어느 쪽을 비활성화했다 다시 켜도 복구하려면 양쪽 Target을 설정한다.
코드에서 연결한 도로는 비활성화로 연결이 해제되면 재활성화 후 `TryConnect`를 다시 호출한다.

접속 위치 허용 오차는 0.05 월드 단위, 방향 내적 기준은 0.98이다.
도로 폭/차선 위치와 진출 → 진입 배열 순서를 맞춰야 한다. 차선 수가 다른 도로를 임의로 합치지 않는다.
연결된 도로의 Transform/차선 데이터를 수정하려면 먼저 Disconnect하고, 편집 후 다시 연결한다.
실시간으로 움직이는 도로를 자동 재접속하는 기능은 없다.

### 새 Prefab 작성

1. 루트에 RoadChunk를 추가한다.
2. 자식 TrafficLane마다 points를 **차량 진행 순서**로 설정한다. 모든 차선은 같은 부모 아래에 둔다.
3. 같은 방향 차선의 Left/Right를 상호 참조한다. 왕복 2차선에서는 둘 다 null이 정상이다.
4. 도로 끝마다 자식 RoadConnection을 만들고 local +Y가 도로 밖을 향하도록 회전한다.
5. incoming/outgoing을 방향별로 중앙선에서 바깥 순서로 지정한다. 각 차선의 시작과 끝을 정확히 한 번씩 등록한다.
6. 루트 lanes/connections에 모든 참조를 지정한다. Prefab의 Target은 비워 두고 씬 인스턴스에서 지정한다.
7. 루트 컴포넌트 메뉴의 `Validate Road`로 설정을 검사한다.
8. 도로 외형은 Surface 자식으로 분리한다. 차선 경로를 편집해도 메시가 자동 변경되지는 않으므로 시각 자료도 맞춘다.

차선 배열에는 2개 이상의 차선을 설정할 수 있다. 방향별 차선 수를 늘리려면 동일 규칙으로 경로와 참조를 추가한다.
시각 표면은 주행 가능한 바닥이며 차량을 막는 Collider를 넣지 않았다.

## 테스트

- `CodeBlue Rush > Road > Validate Samples`: 53개 검증. 실패하면 예외와 해당 검증명이 Console에 표시된다.
- 검증 내용: 2/4차선 구성, 직선 길이·표본, 코너와 반대 방향, 직선/코너/회전 도로 연결, 중복 연결, 양방향 Next, 열린 경계, 점유 보존, 해제, 위치·방향·차선 수 불일치, null/자기 연결, 음수·초과·NaN 거리, 스케일·회전 캐시, 인접 참조, Prefab 인스턴스 분리, 부분 연결 방지, 0길이·빈 경로·누락 차선.
- Play에서 가운데 코너를 비활성화하면 양쪽 직선의 코너행 Next가 null이 되는지 확인한다. 재활성화하면 양쪽 Target으로 복구되는지 확인한다.
- 코너 삭제 후 살아 있는 도로에 파괴된 차선 연결이 남지 않는지 확인한다.
- 연결 지점을 1 단위 옮기거나 차선 수가 다른 도로로 바꾸면 연결이 거절되는지 확인한다.
- 구급차를 추가해 기존 가속·제동·조향이 유지되는지 수동 확인한다. 차선 자동 추종은 아직 구현 대상이 아니다.

자동 검증은 열려 있는 원본 Unity와 충돌하지 않도록 `Temp/RoadValidation`의 독립 프로젝트에서 실행했다.
검증 도구와 실제 Unity 엔진을 사용했으며, 물리·렌더링 API의 가짜 구현으로 대체하지 않았다.
Unity 6000.3.23f1에서 컴파일 오류 없이 53개 회귀 검증 및 플레이 모드 생명주기 5개 검증, 총 58개를 통과했다.
플레이 모드 항목은 직렬화된 Target의 초기 정방향/역방향 연결, 비활성화 정리, 재활성화 복구, 삭제 정리다.
실행 로그는 `Temp/road-validation.log`, `Temp/road-play-validation.log`, 렌더링 확인 이미지는 `Temp/road-demo.png`에 있다.
Temp는 Git 제외 디렉터리이며 플레이 모드 배치 검증용 보조 스크립트도 그 안에만 있다.
직선·코너 연결부와 표면·차선 표시를 실제 카메라 이미지로 확인했다. 이 렌더링 확인은 임시 프로젝트의 기본 렌더러 기준이다.
원본 프로젝트의 URP/WebGL 최종 빌드와 구급차 키보드 주행은 별도 수동 검증 대상이다.

## 발견한 사항과 다음 단계 연결 지점

- 기존 컨트롤러와 구급차 Prefab의 maxSpeed는 8이며 개발 지침의 향후 충돌 기준 설명은 10이다. 이번 도로 단계에서 기존 주행 값을 바꾸지 않았다.
- 기존 카메라는 CameraTurnZone에 의존한다. 이번 도로 Prefab은 씬의 카메라를 직접 참조하지 않는다.
- 임시 프로젝트의 플레이 모드 진입 시 Unity Editor SearchDatabase 인덱싱에서 ArgumentOutOfRangeException이 한 번 기록되었다. 도로 스크립트의 예외는 아니며 이후 5개 생명주기 검증은 완료되었다. 에디터 자체 로그까지 오류가 전혀 없었다는 의미는 아니다.
- 경계 끝의 Next=null은 정상 상태다. 향후 이동 이용자가 정차/경로 종료로 처리해야 한다.
- 도로 표면 메시의 모양과 차선 데이터는 별도다. 예제는 같은 계산으로 생성해 일치시켰다.
- 3단계는 RoadChunk를 배치한 후 `GetConnection(index).TryConnect(other, out error)`를 사용하면 된다. 배치 실패 처리와 맵 완결성 검사는 그 단계의 책임이다.
- 네비게이션은 실제 NextLane 연결과 Length를 읽어 경로 그래프를 구성할 수 있다. 검색 알고리즘과 현재 구급차의 최근접 차선 검색은 아직 없다.
- 차량 AI는 CurrentLane과 누적 이동 거리를 보유하고 TrySample을 조회하면 된다. 차선 끝에서는 NextLane을 확인하고 남은 거리를 넘기는 이동 로직이 필요하다.
- Left/Right는 추후 차선 변경 후보 조회용이다. 공간 검사·차선 변경 움직임은 구현하지 않았다.
- 현재 NextLane은 단일 후속 차선이다. 교차로 분기 선택이 필요한 단계에서 분기 연결 표현을 확장해야 한다. 예상 교차로/신호 시스템을 미리 만들지 않았다.

추천 Commit 제목: `feat: add stage 2 road and traffic lane system`

Git Commit / Push는 수행하지 않았다.
