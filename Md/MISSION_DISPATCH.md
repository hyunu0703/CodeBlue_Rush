# 대기 씬과 출동 흐름

기존 `MISSION_IMPLEMENTATION.md`의 GamePlay 내부 대기·수락 흐름은 이번 씬 분리로 대체된다.

`Start → Waiting → 미션 확인 → 수락 → 출동하기 → GamePlay → 환자 픽업 → 병원 후송 → Waiting`

- `WaitingMissionController`가 실제 시간 5초 대기, 요청 알림과 브리핑을 담당한다. 거절하면 다시 5초 뒤 새 요청을 받는다.
- 수락은 출동 준비 상태로만 전환한다. `출동하기`를 눌러야 `MissionDispatchSession`에 한 번 사용할 출동 승인을 저장하고 씬을 이동한다.
- `MissionController`는 출동 승인을 소비한 뒤 기존 시드 검증, 맵 생성과 픽업·후송을 실행한다. GamePlay 직접 실행은 운전을 차단하고 Waiting으로 이동한다.
- 병원 도착 시 운전을 차단하고 차량을 정지시킨 뒤 기존 페이드로 Waiting에 복귀한다. 대기 씬 재진입은 새 대기 상태이며 수락과 시드를 유지하지 않는다.
- 씬 사이에는 출동 승인과 요청 번호만 전달한다. 차량·맵·교통 오브젝트는 유지하지 않는다.
- Waiting UI는 기존 한글 폰트와 Input System UI 입력 모듈을 사용하며 기준 해상도 480×854와 안전 영역을 반영한다.

## 검증 방법

Unity 6000.3.23f1의 EditMode Test Runner에서 `MissionDispatchFlowTests`를 실행한다. 테스트 내부에서 Play Mode로 진입한다.

- `StartWaitingDispatchPickupHospitalAndRepeat`: 실제 저장된 버튼 이벤트로 Start 진입, 거절 후 재요청, 수락만으로 이동하지 않음, 최종 출동, 실제 픽업 감지와 구출 컷신, 병원 도착 및 2회 출동 루프를 검사한다.
- `DirectGamePlayEntryRequiresDispatch`: 출동 승인 없는 GamePlay 직접 실행 시 입력 차단과 대기 씬 복귀를 검사한다.
- 픽업과 병원 이동은 테스트에서 Rigidbody 위치를 지정하며 교통은 비활성화한다. 실제 조작으로 전체 경로를 주행하거나 모바일 브라우저에서 확인하는 검증은 별도로 수행해야 한다.

수동 확인 시 Start 씬에서 Play → 시작 → 약 5초 후 미션 확인 → 수락을 누른다. 이때 대기 씬에 머물며 출동하기 버튼만 표시되어야 한다. 출동하기 → 픽업 장소에 주차 → 구출 및 탑승 → 병원 도착 순으로 진행하면 대기 씬으로 돌아와 약 5초 후 새 요청을 받아야 한다.

## 수행한 검증 — 2026-10-25

- 사용자가 열어 둔 원본 에디터를 종료하지 않고 `.utmp/WaitingValidation` 복사본에서 Unity 6000.3.23f1, Editor/Standalone 조건으로 컴파일과 Play Mode를 포함한 EditMode 테스트를 실행했다. 패키지는 프로젝트의 Input System 1.20.0, URP 17.3.0, Test Framework 1.6.0을 유지했다.
- `MissionDispatchFlowTests` 2개 통과: Start 진입, 수락·출동 분리, 거절 후 재요청, 중복 출동 차단, 픽업·후송 및 2회 반복, 승인 없는 진입 차단을 확인했다. 기존 한글 폰트로 브리핑의 계산된 높이가 표시 영역 안에 들어오는지도 검사했다.
- 관련 도로 생성 및 교통 프리팹 검사를 합쳐 15개 중 14개가 통과했다. `TrafficPrefabColliderTests.GamePlay_CustomSizeAndOffset_PersistThroughMovementAndPoolReuse`는 차량 복제 개수를 3개로 기대하지만 2개여서 실패했다. 변경 전 HEAD의 MissionController 및 해당 테스트를 복사본에 적용한 비교 실행에서도 같은 실패가 재현되어 이번 요청에서 교통 로직은 수정하지 않았다.
- Waiting 씬의 모든 로컬 YAML 참조, 컨트롤러 GUID, 필수 UI 참조 9개 및 빌드 등록을 확인했다. `git diff --check`도 통과했다.
- 실제 운전 조작으로 전체 경로를 주행하는 확인, 화면의 시각적 검수 및 WebGL/모바일 브라우저 빌드·실행은 미검증이다. 위 수동 순서로 확인해야 한다.

테스트 결과 파일은 `.utmp/waiting-tests.xml`, 변경 전 비교 결과는 `.utmp/waiting-traffic-baseline.xml`이다.
