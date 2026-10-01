# 5단계 상황 보고 / 환자 위치

## 3단계와 4단계 연결 확인

- `CityMap`이 Seed에 따라 실제 `RoadChunk`를 생성하고 차선 목록을 보관한다.
- 도로 경계는 `RoadConnection`, 교차로 내부는 `TrafficLane.GetNext()`로 연결된다.
- `NavigationRoute`는 `CityMap.TryLocate()`로 구급차 위치를 찾고 실제 방향 그래프를 BFS로 탐색한다.
- `SetDestination(TrafficLane, float)`는 특정 접근 차선의 특정 거리까지 경로를 계산한다. 성공 여부를 반환하며 결과는 기존 `NavigationUI`로 전달된다.
- `CityMap.StateChanged`는 생성/교체/비활성/재연결을 알린다. `Layout` 인스턴스는 새 도시를 구분할 수 있으며 같은 도시 재활성화에서는 유지된다.

5단계에서는 이 목록, API, 이벤트를 그대로 사용한다. 도로 생성, 연결 그래프, 경로 탐색 및 별도의 전체 GameFlow는 추가하지 않았다.

## 구현 책임과 위치 정보

새 런타임 컴포넌트는 `PatientReport` 하나다. 상황 보고, 후보 캐시, 랜덤 선택과 현재 미션 상태를 소유한다.

`PatientReport.SpawnPoint`는 읽기 전용 값으로 다음 정보를 보관한다.

- 현재 `CityMap` 참조
- 생성된 실제 `TrafficLane` 참조
- Lane 시작점부터의 월드 거리
- `TryGetPose(out position, out direction)`로 조회하는 생성 위치와 진행 방향

각 Lane의 길이 중간 지점을 후보 하나로 사용한다. 이 위치는 실제 차선 선분 위에 있으며 건물 내부의 임의 좌표를 만들지 않는다. 교차로 내부 경로도 후보에 포함된다. 현재 단계의 환자 위치와 접근 목적지는 같은 도로상의 지점이다. 인도/건물 출입구 또는 도로 옆 배치 보정은 추측해서 추가하지 않았다.

별도 SpawnPoint GameObject를 대량 생성하거나 Prefab을 수정할 필요가 없다. 6단계에서는 확정된 SpawnPoint에서 월드 위치/방향을 읽어 생성에 사용할 수 있다. 지금은 환자/구급대원을 생성하지 않는다.

## 데이터 흐름

`외부의 TryReport 요청 → 캐시에서 후보 선택 → SpawnPoint 유효성 검사 → NavigationRoute.SetDestination(Lane, 거리) → 성공한 경우에만 Patient / IsActive 확정 → ReportChanged 및 기존 경로 UI`

- 이미 보고 중이거나 미션이 활성화되어 있으면 중복 요청을 거절하고 기존 목적지를 보존한다.
- 출발점 오류는 후보를 바꿔도 해결되지 않으므로 즉시 실패한다.
- 비활성/파괴된 후보는 경로 탐색 전에 제외한다.
- 다른 도시로 교체된 SpawnPoint는 `Map.ContainsLane()`에서 거절된다.
- 경로가 없는 후보는 다시 뽑지 않고 다음 후보를 시도한다.
- 모든 후보가 실패하면 비활성 상태와 오류를 반환하며, 시도했던 보고 목적지와 경로를 정리한다.
- 네비게이션 목적지를 외부 시스템이 다른 것으로 바꾸면 활성 보고를 취소하되 외부 목적지는 지우지 않는다.

## 랜덤과 비용

도시 초기화 또는 새 `Layout` 인스턴스가 관찰되었을 때만 생성된 Lane 목록을 O(n)으로 순회한다. 일반적인 상황 보고는 전체 맵을 다시 검색하지 않는다. 같은 도시 재활성화와 다음 보고에서는 캐시와 난수 진행 상태를 재사용한다.

도시 Seed에서 분리한 uint LCG 상태를 사용한다. 전역 `UnityEngine.Random`이나 도시 생성 난수에는 영향을 주지 않는다. 같은 Seed, 동일한 후보 순서 및 동일한 호출/유효성 조건이면 보고 선택 순서가 재현된다. 실제 경로 가능 여부가 달라지면 시도 횟수와 이후 선택 순서도 달라질 수 있다.

선택 배열과 역색인 배열을 유지하며 각 후보를 O(1)로 무복원 추출한다. 매 보고마다 전체 배열을 새로 복사하거나 셔플하지 않는다. 직전 성공 후보는 O(1)로 마지막 순서에 예약한다. 다른 모든 후보가 실패하면 직전 후보를 최후 수단으로 한 번만 허용한다. 유일하게 접근 가능한 현장을 이유 없이 거절하지 않기 위한 규칙이다.

한 요청의 후보 시도 수는 캐시 크기 이하로 제한된다. 무한 재시도는 없다. **선택 한 번이 O(1)이며 경로 검증까지 O(1)인 것은 아니다.** 후보 k개를 시험하면 기존 네비게이션 BFS가 최대 k번 실행되므로 최악 비용은 O(k × (V + E)), k ≤ 후보 수다. 정상 연결 도시에서는 보통 첫 유효 후보에서 끝난다. 극단적으로 단절된 대형 도시의 탐색 비용을 줄이는 일괄 경로 API는 이번 단계에 추가하지 않았다.

## 공개 API와 UI 연결

```csharp
// Inspector 연결을 사용하면 Configure는 필요 없다
report.Configure(navigation);

if (report.TryReport(out string error))
{
    // 위치를 조회하는 예제일 뿐 실제 환자 생성은 이번 단계에 없다
    PatientReport.SpawnPoint point = report.Patient;
    if (point.TryGetPose(out Vector3 position, out Vector3 direction))
    {
        // 6단계의 생성 코드가 사용할 데이터
    }
}

// 취소/초기화 연결 지점이며 도착이나 성공 판정은 수행하지 않는다
report.CancelReport();
```

- `IsActive`: 경로 검증을 통과한 현재 보고가 활성 상태인지 여부
- `Patient`: 현재 현장 SpawnPoint. 비활성 상태에서는 기본값
- `Message`: 활성 상태에서 `환자가 발생했습니다\n현장으로 이동하세요`, 그 외 빈 문자열
- `Status`: Idle, Active, NavigationUnavailable, MapUnavailable, NoCandidates, NoReachablePoint, InvalidStart, Cancelled
- `CandidateCount`: 캐시된 후보 수. 비활성/파괴 후 아직 캐시에 남아 있는 후보도 수에 포함될 수 있으나 선택 시 거절된다.
- `ReportChanged`: UI가 위 상태를 다시 읽을 수 있는 이벤트. OnEnable에서 구독하고 현재 상태를 한 번 읽으며 OnDisable에서 해제한다.

기존 UI에는 경로 Graphic만 있으므로 큰 상황 보고 UI를 새로 만들지 않았다. 메시지/상태/위치 API와 이벤트, 테스트 Inspector를 제공한다. 실제 경로선은 기존 `NavigationUI`에 바로 표시된다.

`NavigationRoute`에 추가한 `DestinationLane`, `DestinationDistance`는 목적지 소유권을 확인하기 위한 읽기 전용 조회다. 경로 알고리즘은 변경하지 않았다. 네비게이션 무효화 이벤트 도중 보고가 목적지를 취소할 수 있으므로 목적지가 남아 있을 때만 재탐색 Coroutine을 예약하도록 한 조건을 추가했다.

## 수명과 예외 처리

- 도시/네비게이션 준비 전 요청: 오류 반환. 생성 API를 대신 호출하지 않는다.
- 후보가 없음: NoCandidates. 실제 후보가 모두 무효하거나 경로가 없음: NoReachablePoint.
- 직전 위치: 다른 위치를 우선하며 다른 후보가 모두 실패할 때만 재사용한다.
- 선택된 Lane이 비활성화/파괴됨: 기존 네비게이션 이벤트로 보고와 소유 목적지를 취소한다.
- 중간 경로만 변경됨: 네비게이션의 Invalidated 상태에서는 재탐색을 기다리고, 실제 실패 상태가 확정되면 보고를 취소한다.
- 새 도시: 활성 보고와 직전 위치를 정리하고 새 차선 목록으로 캐시를 다시 만든다. 외부에 복사해 둔 이전 SpawnPoint도 TryGetPose가 false를 반환한다.
- 컴포넌트/도시 비활성화: 보고를 정리하고 구독을 해제한다. 자동으로 새 보고를 발행하지 않는다.
- 런타임 참조를 교체할 때는 Configure를 사용한다. 이미 생성된 도로는 정적으로 사용하며, 외부에서 도로 Transform/직렬화 경로를 직접 편집하면 4단계의 ValidateRoute/Recalculate 계약을 따른다.
- Update/Find/GetComponent 반복, 실제 환자 AI/픽업/정차/심전도/병원 전환/도착/평가/교통/충돌은 없다.

## Inspector와 데모

기존 게임 씬에서는 `PatientReport` 컴포넌트를 하나 추가하고 Navigation에 기존 `NavigationRoute`를 지정하면 된다. `NavigationRoute`의 Map/Player와 기존 `NavigationUI` 연결은 그대로 사용한다. RoadChunk/TrafficLane Prefab 변경은 필요 없다.

별도 데모:

1. `Assets/Scenes/PatientReportDemo.unity`를 연다. 또는 `CodeBlue Rush > Patient Report > Create Demo Scene` 메뉴로 기존 NavigationDemo 기반의 데모를 만든다.
2. Play 후 CityMap의 NavigationRoute Inspector에서 `Preview Generated Road Route`를 눌러 테스트 구급차를 실제 Lane 위에 배치한다.
3. PatientReport Inspector의 `Issue Report`를 누른다. 임의의 실제 현장이 선택되고 기존 네비게이션 경로가 그 현장으로 교체된다. Inspector에 보고 문구/위치/상태가 보인다.
4. 다시 Issue Report를 누르면 중복 요청이 거절된다. `Cancel Report` 후 다시 요청하면 같은 도시에서 새 현장을 선택한다.

테스트 버튼은 에디터용이며 런타임 자동 보고나 전체 미션 진행 흐름을 구현하지 않는다. 기존 NavigationDemo와 SampleScene 및 빌드 씬 목록은 변경하지 않는다.

## 변경 파일

- 수정: `Assets/Scripts/Map/NavigationRoute.cs` — 목적지 읽기 전용 조회와 취소 이후 불필요한 재탐색 예약 방지.
- 추가: `Assets/Scripts/Map/PatientReport.cs` — 보고 책임, SpawnPoint 값, Seed 선택, 후보 캐시와 수명.
- 추가: `Assets/Editor/Map/PatientReportEditor.cs` — 보고 API 시험과 연결된 데모 생성.
- 추가: `Assets/Editor/Map/PatientReportValidation.cs` — 실제 도시/네비게이션의 보고 및 Play 검증.
- 추가: `Assets/Scenes/PatientReportDemo.unity`, 새 자산의 `.meta` — 독립 데모와 GUID.
- 추가: 이 문서 — 분석, 구조, 설정, 다음 단계 API 및 검증 기록.

## 검증 방법

- `CodeBlue Rush > Patient Report > Validate Reports`: 기존 2~4단계와 보고 검증. Seed 재현, 중복 거절, 후보 없음, 경로 없음, 유일 후보의 최후 재사용, 비활성/파괴 후보, 이전 도시 참조와 네비게이션 목적지를 검사한다.
- `CodeBlue Rush > Patient Report > Validate Play Lifecycle`: 실제 Play 프레임에서 선택 위치 비활성/파괴, 도시 교체, 재활성화 및 기존 UI 경로 메시를 검사한다.
- 로그: `Logs/patient-report-validation.log`, `Logs/patient-report-play-validation.log`.
- 검증은 사용자 Unity 편집기를 닫지 않고 `Logs/Stage5ValidationProject` 복사본에서 실행했다.

2026-10-01 Unity 6000.3.23f1 검증 결과:

- 컴파일 성공, 기존 도로 53개 / 도시 286개 / 네비게이션 5,764개 검증 통과.
- 상황 보고 검증 378개 통과. 동일 Seed의 두 도시에서 30회 보고 순서를 비교했으며 중복/실패/직전 위치/도시 교체 조건을 검사했다.
- Play 수명 및 기존 UI 연결 검증 17개 통과. 최종 Play 실행 구간에 런타임 오류가 없었다.
- 첫 복사본 초기화에서 Unity 에디터 검색 인덱스 예외가 발생했다. 테스트 시작을 초기 에디터 콜백 이후로 옮겨 검증 대상의 Play 수명과 분리했고, 최종 실행에서는 해당 예외도 재현되지 않았다. 오류 로그 검사는 그대로 유지한다.
- 4단계 무효화 이벤트에서 5단계가 목적지를 취소하면 불필요한 재탐색이 예약될 수 있어 `HasDestination` 조건을 추가했다.
- 최종 복사본과 원본의 런타임 스크립트 SHA256 일치 및 `git diff --check` 확인.
- WebGL 빌드와 실제 환자 생성/픽업은 검증 대상이 아니다. 선택/BFS 복잡도는 코드 검토 결과이며 Profiler 측정값은 아니다.

Git Commit/Push는 수행하지 않는다. 추천 제목: `feat: add seeded patient reports using reachable lane spawn points`.
