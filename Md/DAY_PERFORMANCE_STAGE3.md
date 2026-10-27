# 누적 실적 3단계 구현 결과

- 작업일: 2026-10-28
- 범위: 미션당 직접 차량 충돌 기록만 구현했다. 4단계 이후는 진행하지 않았다.
- 환경: Windows, Unity 6000.3.23f1, Input System 1.20.0, URP 17.3.0, Test Framework 1.6.0, uGUI 2.0.0. 기존 프로젝트·패키지 설정을 유지했다.

## 구현 및 완료 조건

| 완료 조건 | 구현과 확인 결과 |
| --- | --- |
| 미션 기록 수명 | 출동 맵과 시작 위치 준비 완료 시 `BeginMission()`으로 기록을 초기화·시작한다. 픽업·구출·후송 전환에서는 횟수를 유지하고 병원 인계 시 기록을 멈춘다. |
| 직접 차량 접촉 | 구급차에 붙인 `MissionPerformance`가 `RoadTrafficCollision` 또는 다른 `AmbulanceDriveController` 차체와의 직접 접촉만 기록한다. 건물·교통 차량끼리의 연쇄 접촉은 제외한다. |
| 저속·고속 | 첫 진입 콜백의 상대 속도를 모든 접점 법선에 투영한 절댓값 중 최댓값을 사용한다. 1유닛=6m로 환산하고 반올림 없이 40km/h 미만 저속·이상 고속으로 구분한다. 기존 `PerformanceRules` 계산을 재사용한다. |
| 물리 처리 순서 | `AmbulanceDriveController.OnCollisionEnter2D`에서 기존 `HandleTrafficContact`보다 먼저 기록한다. 이후 기존 충격 처리와 차량 파손·연쇄 반응을 그대로 수행한다. `RoadTrafficCollision`과 `PatientHealth`는 수정하지 않았다. |
| 중복 방지 | 상대 Rigidbody2D별로 접촉 중인 콜라이더 쌍을 보관한다. 지속 접촉·추가 콜라이더 진입·일부 접점만 해제된 경우에는 첫 분류와 횟수를 유지한다. 완전히 분리된 뒤 다시 닿으면 새 속도로 1회를 추가한다. |
| 비활성 차량 정리 | Exit 콜백에서 접촉 쌍을 해제하고, FixedUpdate에서 비활성·파괴·물리 접촉 해제 쌍도 정리한다. 풀 반환 시 Exit가 생략되는 조건도 검사했다. |
| 현재 상태 보존 | 미션 컴포넌트 비활성화 시 기록을 중단하고 재활성화 시 기존 횟수로 이어간다. 새 출동에서만 횟수와 사고 여부를 초기화하며 `GameProgressSession`의 누적 실적·일차는 변경하지 않는다. |
| 씬 연결 | `GamePlay.unity`의 Ambulance에 기록 컴포넌트를 추가하고 운전 컨트롤러·MissionSystem의 MissionController가 같은 기록을 참조하도록 연결했다. 기존 GUID·차량 튜닝·프리팹은 보존했다. 별도 수동 설정은 없다. |

읽기 전용 API는 `LowCollisionCount`, `HighCollisionCount`, `HasVehicleCollision`, `IsRecording`이다. 현재 기록은 미션 씬의 메모리에만 있다. 병원 후송 후 기존 Waiting 복귀는 그대로 유지하며, 결과 화면·점수 반영·일차 진행·당일 기록 저장은 4단계 이후로 남겨두었다. 런타임 기록을 저장 데이터 형식에 추가하지 않았다.

## 실제 검증 결과

원본 프로젝트와 사용자 저장을 보호하기 위해 같은 버전의 격리된 프로젝트 복사본을 사용했다. EditMode Test Runner에서 `EnterPlayMode`로 실제 Play Mode에 진입하고, 접촉 검증은 독립된 `LocalPhysicsMode.Physics2D` 씬의 실제 시뮬레이션·Collision2D 콜백을 사용했다. 충돌 콜백 자체를 가짜 데이터로 호출하지 않았다.

| 검증 | 결과 |
| --- | --- |
| 충돌 분류·기록 수명 | `MissionPerformanceTests` 15개 모두 통과. 14개는 실제 물리 접촉이며 1개는 정확히 40km/h의 계산 경계를 확인한다. 실제 물리 경계는 39.99·40.01km/h 양쪽을 검사했다. |
| 지속 접촉 | 저속 첫 충돌 뒤 같은 접촉을 유지하며 속도를 높이고 콜라이더를 추가해도 저속 1·고속 0을 유지했다. |
| 복합 콜라이더 | 구급차와 상대 차량 양쪽의 자식 콜라이더가 여러 개여도 차량 충돌은 1회였다. 일부 콜라이더를 끄고 다시 켜도 다른 접촉이 남아 있으면 추가 집계하지 않았다. |
| 분리·재충돌 | 같은 차량에 저속으로 접촉한 뒤 실제 분리 물리 스텝을 거치고 고속으로 다시 접촉하면 저속 1·고속 1이었다. |
| 동시 차량·제외 대상 | 두 차량 동시 접촉은 고속 2회, 건물 접촉·교통 차량끼리의 연쇄 사고는 0회였다. 큰 접선 속도와 작은 법선 충격의 접촉은 저속이었다. |
| 풀 재사용 | 비활성화 Exit 콜백을 끈 테스트 조건에서도 반환된 차량의 접촉을 정리하고, 재활성화 후 재접촉을 추가 집계했다. 전역 설정은 테스트 종료 시 복원했다. |
| 기존 환자 피해 | 픽업 전 충돌은 기록하되 HP는 유지됐다. 후송 중 고속 접촉에서는 기존 최대 30% 피해를 유지하면서 기록도 추가됐다. 피해 배율과 파손 기준은 변경하지 않았다. |
| 실제 씬 미션 흐름 | 1개 통과. Start→Waiting 수락·출동→GamePlay에서 저속 접촉→픽업·컷신→고속 접촉→병원 인계·Waiting 복귀→다음 출동을 실행했다. 저속 1·고속 1 유지, 비활성화·재활성화 시 횟수 보존, 인계 시 기록 중단, 다음 출동 0회 초기화와 누적 진행 객체 보존을 확인했다. |
| 기존 Start 연결 | 저장 없음·중복 클릭, 손상 오류와 재시도, 평가 대기·해고 요청 차단의 실제 씬 검증 4개 통과. |
| 기존 차량·조향 회귀 | 62개 중 53개 통과·9개 기존 실패. 실패 이름은 준비·2단계의 질량 관련 실패 목록과 동일하며 새 실패는 없다. 기존 교통 차량 질량의 1.2배 계수와 테스트 기대값 차이는 이번 범위 밖이므로 수정하지 않았다. |
| WebGL C# 분기 | Unity 참조와 WebGL 정의를 사용한 컴파일에서 오류 0·경고 0. 실제 IL2CPP·WebGL 빌드 검증은 아니다. |

위 실행 결과는 총 82개 중 73개 통과·9개 기존 실패다. 컴파일 오류·NullReferenceException·MissingReferenceException은 관찰되지 않았다. 테스트가 의도한 저장 읽기 오류 경고와 기존 Unity 라이선스·종료 진단은 전체 로그의 무경고를 뜻하지 않는다.

실제 씬 미션 검증은 픽업·병원까지의 이동을 순간이동으로 단축하고 테스트 소유 차량을 실제 씬에서 접촉시켰다. 일반 운전 플레이나 모든 랜덤 맵에 대한 검증으로 해석하지 않는다. 접촉 테스트는 0.005초 수동 물리 스텝, 실제 씬 연결은 프로젝트의 자동 물리 진행을 사용했다.

영구 추가한 테스트는 `Assets/Scripts/Editor/MissionPerformanceTests.cs`와 메타뿐이다. 삭제된 `PerformanceRulesTests.cs`·`ProgressStoreTests.cs` 및 메타는 복원하지 않았다. 실제 씬 전 구간 검증용 소스는 무시되는 검증 복사본에만 두었고 결과 XML·로그는 `.utmp/PerformanceStage3/`에 기록했다.

## 변경 범위와 남은 확인

기존 Assets·Packages·ProjectSettings의 작업 전후 SHA-256 비교에서 바뀐 기존 파일은 `GamePlay.unity`, `AmbulanceDriveController.cs`, `MissionController.cs` 3개뿐이다. 새 기록·테스트 소스와 각각의 메타를 추가했으며 기존 메타·저장 데이터·설정·패키지·다른 씬은 보존했다.

실제 WebGL·모바일 브라우저 빌드의 물리 동작과 수동 운전은 미검증이다. 수동으로 재확인하려면 C# 디버거를 Unity Editor에 연결하고 `RecordContact`의 접촉 기록 종료 지점에 중단점을 설정한 뒤 Start에서 출동한다. Ambulance의 `MissionPerformance.LowCollisionCount`·`HighCollisionCount`를 관찰한다. 차량 지속 접촉은 횟수가 유지되고 완전 분리 후 재접촉은 1회 늘어나야 한다. 픽업 후에도 기존 횟수가 유지되며 다음 출동에서는 0회여야 한다. 병원 도착 후 기존 Waiting 복귀가 유지되는 것이 현재 단계의 기대 결과다. 현재 단계에는 기록 표시 UI를 추가하지 않았다.

## API 근거

첫 접촉 데이터와 콜라이더 쌍 식별은 [Unity 6000.3 Collision2D](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Collision2D.html)를 따른다. [Collider2D.IsTouching](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Collider2D.IsTouching.html)은 마지막 물리 업데이트의 접촉 결과를 사용하므로 접촉 정리도 물리 스텝과 콜백에 맞춰 수행했다. 비활성화 시 Exit 생략 조건은 [Physics2D.callbacksOnDisable](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics2D-callbacksOnDisable.html)을 근거로 실제 테스트에서 재현했다. 공식 API 근거와 위 프로젝트 검증 결과는 별도로 구분한다.
