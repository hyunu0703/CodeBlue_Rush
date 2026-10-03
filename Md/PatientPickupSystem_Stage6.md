# 6단계 환자 픽업

## 기존 구조 확인

5단계 `PatientReport`가 활성 미션과 `SpawnPoint(Map, Lane, Distance)`를 소유한다. 위치는 `TryGetPose()`로 실제 도로 위에서 계산한다. 4단계에는 `SetDestination(Lane, 거리)`로 목적지가 전달되며, 보고 취소와 도시 교체 때 이전 목적지가 정리된다.

기존 구급차는 `AmbulanceController`가 같은 GameObject의 `Rigidbody2D.linearVelocity`로 이동한다. 6단계는 이 Rigidbody2D를 직접 참조하여 실제 선속도와 회전속도를 읽는다. 운전 코드나 가짜 속도 상태를 추가하지 않는다.

## 구현과 데이터 흐름

새 런타임 컴포넌트는 `PatientPickup` 하나다. 현장 객체 배치, 정차 판정, 짧은 이동과 숨김을 담당한다. 미션 상태는 계속 `PatientReport`가 소유한다.

`상황 보고 → SpawnPoint에 현장 배치 → 기존 네비게이션으로 접근 → 현장 영역에서 안정적 정차 → 두 객체 이동 → 뒤쪽 탑승점 도착 → 두 객체 비활성화 → 미션 PatientOnBoard → PatientPickedUp 이벤트`

- `PatientPickup.prefab`에는 환자(주황), 구급대원(파랑), 반지름 2.5의 CircleCollider2D 픽업 영역이 있다.
- 두 배우는 구분 가능한 임시 SpriteRenderer이며 AI나 물리 충돌을 갖지 않는다. 실제 아트로 교체할 수 있다.
- 현장 루트는 신고 SpawnPoint의 위치/방향으로 배치한다. 환자와 구급대원은 그 좌표계의 작은 오프셋에 배치한다.
- 구급차 Prefab의 자식 `RearBoardingPoint`는 차체 뒤쪽 `(0, -1.3, 0)`에 있다. 기존 차체 Collider는 높이 1.9이므로 범퍼 바깥이다.
- 환자는 RearBoardingPoint, 구급대원은 그 오른쪽 0.3 단위로 이동한다. 둘 다 도착해야 숨기고 탑승 상태로 바꾼다.
- 객체를 매 미션마다 생성/파괴하지 않고 같은 두 현장 객체를 재배치·비활성화하여 재사용한다.

## 정차와 이동 정책

다음 조건을 모두 충족해야 한다.

1. 현재 보고가 활성화되어 있고 아직 탑승하지 않았음.
2. 현장에 저장된 MissionId 및 SpawnPoint와 현재 미션이 일치함.
3. 현장 루트가 현재 SpawnPoint에 놓여 있음.
4. 지정된 구급차의 **중심**이 이 현장 영역 내부에 있음. 범퍼만 겹친 경우에는 시작하지 않음.
5. 실제 선속도 ≤ 0.15 단위/초, 실제 회전속도 ≤ 5도/초.
6. 해당 정차 상태가 0.35초 연속 유지됨.
7. 두 배우 및 구급차, 활성 RearBoardingPoint와 영역 참조가 유효함.

영역은 Trigger Collider이지만 `OnTriggerEnter`만으로 탑승하지 않는다. 활성 현장의 단일 Coroutine이 물리 주기마다 `area.OverlapPoint(ambulance.position)`와 실제 속도를 직접 검사한다. 이 방법은 Rigidbody가 잠들어 TriggerStay 콜백이 오지 않는 경우에도 정차 완료를 감지한다. 다른 차량 Collider나 과거 현장의 Trigger 메시지는 픽업을 시작시키지 못한다.

이동은 `Vector3.MoveTowards`를 사용하며 기본 속도는 1.5 단위/초다. 도착 허용 거리는 0.05다. 정차 중 작은 떨림은 속도 허용값 아래에서 흡수한다.

**차량이 다시 움직이거나 영역을 나가면 두 객체를 현재 위치에 멈춘다.** 시작 위치로 순간이동시키거나 이동하는 차를 계속 따라가지 않는다. 같은 현장 안에서 다시 0.35초 정차하면 멈춘 위치에서 이동을 이어간다. 차량 조작 잠금이나 복잡한 상태 머신은 추가하지 않았다.

## 미션과 다음 단계 API

`PatientReport`에 추가된 조회/이벤트:

- `MissionId`: 성공한 상황 보고마다 증가하는 식별자. 같은 SpawnPoint가 다시 선택되어도 이전 픽업과 구분한다.
- `IsPatientOnBoard`: 현재 미션의 환자 탑승 완료 여부.
- `ReportStatus.PatientOnBoard`: 기존 enum 끝에 추가하여 이전 값의 순서를 보존한다.
- `PatientPickedUp`: 탑승 완료 시 한 번 발생한다. 7단계의 심전도 시작 또는 이후 목적지 설정은 이 이벤트에 연결할 수 있다.

탑승 완료를 확정하는 `TryCompletePickup`은 내부 API다. 픽업 현장이 조건을 검증한 후 같은 MissionId로 호출한다. 탑승 완료 후에도 미션의 `IsActive`는 true다. 따라서 새 보고가 기존 미션을 덮어쓰지 못한다.

현장 네비게이션 목적지는 탑승 완료 시 지운다. 이후 외부에서 네비게이션 목적지를 변경해도 탑승 미션이 취소되지 않도록 5단계의 경로 이벤트 처리를 보완했다. **병원 선택이나 실제 병원 목적지 설정은 없다.**

```csharp
// 7단계 등의 소비자는 자신의 OnEnable/OnDisable에서 구독과 해제를 수행한다
report.PatientPickedUp += HandlePatientPickedUp;

// 이벤트 구독 시점에 이미 탑승했을 수 있으므로 초기 상태도 읽는다
bool boarded = report.IsPatientOnBoard;

// 향후 Game Over 또는 명시적 미션 취소/초기화 연결 지점
report.CancelReport();
```

`PatientPickup`의 API:

- `Configure(report, rigidbody, rearPoint)`: 직접 참조 연결. 기존 현장과 구독부터 정리한다.
- `TryStartPickup()`: 외부 요청도 같은 영역/정차 시간/미션 조건을 만족해야 한다. 중복 요청은 false.
- `RefreshSite()`: 누락 설정을 복구한 후 현재 SpawnPoint에서 현장을 다시 준비하는 명시적 복구 API.
- `IsBoarding`, `IsPaused`, `Error`: 읽기 전용 상태. 실패를 매 프레임 로그로 출력하지 않는다.

## 정리 및 예외 처리

- 환자/구급대원/RearBoardingPoint 누락: 안전하게 시작을 거절하고 Error를 제공한다.
- RearBoardingPoint는 지정된 Rigidbody2D의 자식이어야 한다. 배우 둘은 서로 다른 현장 루트의 직계 자식이어야 한다.
- 이동 중 배우 비활성화/파괴 또는 필수 참조 소실: 이동을 중단하고 남은 배우를 숨긴다. 탑승 완료는 발생하지 않는다. 설정을 복구한 뒤 RefreshSite로 다시 준비한다.
- 보고 취소/새 보고: 이전 Coroutine과 SpawnPoint 참조를 즉시 정리하고 새 현장으로 배치한다.
- 새 맵: 기존 PatientReport의 도시 교체 이벤트가 보고를 취소하므로 이전 픽업도 정리된다.
- 픽업 컴포넌트 비활성화: 현장을 숨기고 구독과 Coroutine을 해제한다. 재활성화 시 아직 유효한 미션만 복원한다.
- 탑승 완료 후 재진입: 영역과 배우가 비활성화되고 미션 탑승 상태가 시작을 차단한다.
- 새로운 상태의 주인은 PatientReport 하나이며 외부에서 상태 setter를 사용하지 않는다.

Update/FixedUpdate/LateUpdate, 반복 Find나 전체 Scene 검색은 없다. Collider의 GetComponent는 Awake 한 번뿐이다. 정차를 감지하기 위한 단일 Coroutine은 유효한 미탑승 현장에만 존재하고 물리 주기마다 O(1) 참조 검사와 두 객체 이동만 수행한다.

## Inspector / Prefab 설정

기존 게임 씬:

1. `Assets/Prefabs/PatientPickup.prefab`을 하나 배치한다. 현장 루트를 CityMap의 생성되는 도로 자식이나 구급차 자식으로 배치하지 않는다.
2. Report에 기존 PatientReport, Ambulance에 실제 구급차 Rigidbody2D를 연결한다.
3. Rear Boarding Point에 구급차 자식 RearBoardingPoint를 연결한다.
4. 환자/구급대원은 현장 루트의 직계 자식 Transform으로 연결한다. Prefab에는 이미 지정되어 있다.
5. CircleCollider2D는 Trigger 상태를 유지한다. 반지름/정차 속도/정차 유지 시간/이동 속도는 Inspector에서 조정 가능하다.

독립 데모 `Assets/Scenes/PatientPickupDemo.unity`:

- Play 후 CityMap의 NavigationRoute Inspector에서 `Preview Generated Road Route`로 테스트 차량을 실제 도로 위에 배치한다.
- PatientReport의 `Issue Report`로 현장을 선택한다.
- 키보드로 현장에 이동해 픽업 영역 안에 차 중심을 놓고 정차한다. 두 색상 객체가 차 뒤로 이동한 뒤 사라진다.
- 데모 카메라는 기존 AmbulanceCamera로 차량을 추적하며 크기는 7.5다. 기존 도시 전체 보기보다 두 객체의 짧은 이동을 확인하기 쉽다.
- 자동 재현은 `CodeBlue Rush > Patient Pickup > Validate Play Lifecycle`을 사용한다. 이 검증은 차량의 실제 Rigidbody 속도를 직접 설정하며 운전 입력은 테스트 중에만 비활성화한다.
- 자산 복구 메뉴: `CodeBlue Rush > Patient Pickup > Create Assets`. 데모 작성 메뉴: `Create Demo Scene`.

기존 5단계 데모, SampleScene 및 빌드 씬 목록은 수정하지 않는다. 구급차 Prefab에는 RearBoardingPoint 자식만 추가한다.

## 파일과 검증

수정:

- `Assets/Scripts/Map/PatientReport.cs`: 미션 식별자, 탑승 상태, 완료 이벤트, 탑승 후 네비게이션 요청 보존.
- `Assets/Prefabs/Ambulance.prefab`: 뒤쪽 탑승 기준점.

추가:

- `Assets/Scripts/Map/PatientPickup.cs`: 단일 현장의 정차·이동·정리 책임.
- `Assets/Editor/Map/PatientPickupEditor.cs`: 자산·데모 설정과 오류/복구 Inspector.
- `Assets/Editor/Map/PatientPickupValidation.cs`: 회귀 및 실제 Play 검증.
- `Assets/Prefabs/PatientPickup.prefab`, `Assets/Scenes/PatientPickupDemo.unity`, 해당 `.meta`.
- 이 문서.

검증 로그는 `Logs/patient-pickup-regression.log`, `Logs/patient-pickup-play.log`다. 사용자의 열린 Unity 편집기를 유지하기 위해 기존 독립 검증 복사본 `Logs/Stage5ValidationProject`를 최신 소스로 동기화해 사용한다.

### 자산 반영 및 실제 Play 검증 결과 (2026-10-02)

- Unity 6000.3.23f1의 그래픽 렌더링을 포함한 배치 Play Mode에서 저장된 `PatientPickupDemo.unity`를 다시 열어 검증했다. 실제 Rigidbody2D와 기존 미션/네비게이션/도시 데이터를 사용했다.
- **`Patient pickup play validation passed: 40 checks`** 확인. 런타임 오류 및 NullReference 없음.
- A~F: 이동/회전 중 시작 거절, 연속 정차 후 이동, 재출발 시 일시정지와 재정차 후 재개, 두 객체 숨김 및 단일 완료 이벤트, 영역 이탈 후 재진입 차단, 이전 MissionId 거절 모두 통과했다.
- 누락된 배우/탑승점, 이동 중 배우 비활성화·파괴, 미션 취소, 새 미션, 새 도시 정리를 확인했다.
- 기존 구급차 컨트롤러의 가속·조향·제동도 Play에서 확인했다. 운전 스크립트와 Rigidbody2D 설정은 변경하지 않았다.
- 기존 회귀 검증을 변경 없이 재실행했다: Road 53, City 286, Navigation 5,764, PatientReport 378개 통과.
- 실제 프로젝트에 Prefab/Scene 및 `.meta`를 반영했다. 구급차 자산 변경은 뒤쪽 자식 Transform 추가뿐이다.
- 재사용 Prefab의 Patient/Paramedic은 내부 참조이며, Report/Ambulance/RearBoardingPoint는 Scene 인스턴스에서 연결된다. MissionId는 Inspector 상수가 아니라 현재 보고에서 런타임에 받는다. Collider는 같은 객체에서 Awake에 한 번 캐싱한다.
- Placeholder는 Unity 내장 `UI/Skin/UISprite.psd`를 사용하는 주황 환자와 파랑 구급대원이다. `Logs/patient-pickup-before.png`, `patient-pickup-moving.png`, `patient-pickup-boarded.png`에 480×854 렌더링 결과를 저장하고 이동 및 숨김을 확인했다.
- 발견한 문제는 검증 복사본 Prefab의 이전 스크립트 GUID, 도메인 재로드 비활성 설정에서의 검증 시작 누락, 테스트용 Transform/물리 위치 동기화였다. 자산 참조와 검증 도구만 수정했으며 이번 후속 작업에서 픽업 런타임 코드는 재작성하지 않았다.
- `git diff --check` 오류 없음. 로그와 화면 캡처는 Git 추적 대상이 아니다.

심전도 감소/UI, 병원 선택/도착, 평가, 다른 차량/시민/신호/충돌 데미지, 전체 GameFlow는 구현하지 않는다.

추천 Commit 제목: `feat: add stopped ambulance patient boarding flow`. Commit/Push는 수행하지 않는다.
