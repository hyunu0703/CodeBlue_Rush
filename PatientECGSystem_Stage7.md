# 7단계 환자 ECG

- `Assets/Scenes/PatientECGDemo.unity`는 기존 픽업 데모에 ECG 데이터와 480×854 Canvas 아래 파동 패널만 추가한 씬이다.
- 기존과 동일하게 Play에서 NavigationRoute Inspector의 `Preview Generated Road Route`, PatientReport의 `Issue Report`로 신고를 시작하고 현장에서 정차한다.
- `PatientECG`의 Report는 기존 PatientReport를 참조한다. `PatientPickedUp`에서 100으로 시작하며, MissionId로 중복 초기화를 막는다. 늦게 연결되었을 때도 IsPatientOnBoard를 확인한다.
- ECG 소유자는 PatientECG 하나다. UI는 이 값을 읽어 색상, 진폭, 선 두께와 밝기를 결정한다. 숫자 카운트다운이나 UI 생존 타이머는 없다. 파동 위상만 Unity 게임 시간을 사용한다.
- 기본 `survivalTime = 180f`, 초당 `100 / survivalTime` 감소. Inspector/코드 변경은 이후 프레임부터 적용한다. 잘못된 시간(0 이하, NaN, Infinity)은 180초로 취급한다.
- 경계: 60 이상 Green, 10 이상 Yellow, 0 초과 Red, 0 Flatline. Flatline은 정지된 일자 파동이다.
- 활성 환자의 감소 Coroutine은 하나다. 픽업 전/중지/사망 시 감소 Coroutine은 없다. ECG 컴포넌트 비활성화 동안에는 일시정지하고, 재활성화 시 같은 미션의 값과 중지/사망 상태를 보존한다.
- 보고 취소 또는 새 미션에서는 이전 ECG를 정리한다. 새 환자 픽업 시 100부터 시작한다.

## 연결 API

- 8단계: `PatientECG.StopDecay()` — 현재 미션의 자연 감소를 즉시 중지한다. 같은 미션의 중복 픽업 이벤트로 재시작하지 않는다.
- 13단계: `PatientECG.TakeDamage(float amount)` — 탑승 환자에게만 유한한 양수 피해를 적용하고 0으로 Clamp한다. 자연 감소 중지 후에도 명시적 피해는 적용할 수 있다. 충돌 판정은 구현하지 않는다.
- `PatientDied` — 최초 0 도달 시 미션당 1회. GameOver나 맵 변경을 호출하지 않는다.
- 읽기 전용 `Value`, `State`, `HasPatient`, `IsDecaying`; UI 변경 알림 `Changed`.
- 다른 씬에 연결할 때 PatientECG의 Report와 ECGGraphic의 `Configure(ecg)`를 연결한다. 기존 데모에는 저장된 참조가 연결되어 있다.

## 검증

`CodeBlue Rush > Patient ECG > Validate Play`는 저장된 데모를 다시 열고 기존 PatientPickup으로 실제 탑승을 완료한 뒤 ECG를 검증한다. 180초 자연 감소는 게임 시간 배속으로 실행하고 감소량과 완료 시점을 확인한다. 경계값 테스트만 private 값 설정을 사용해 정확히 100에서 피해를 적용한다.

로그: `Logs/patient-ecg-play.log`. 화면: `Logs/ecg-green.png`, `ecg-yellow.png`, `ecg-red.png`, `ecg-flatline.png`.

1~6단계 핵심 코드와 자산은 변경하지 않는다. 기존 검증 복사본에서 7단계 테스트만 실행한다. Commit/Push는 수행하지 않는다.

검증 완료: Unity 6000.3.23f1 실제 Play에서 `Patient ECG play validation passed: 43 checks`. 픽업 전 대기, 180초 자연 감소, 60/10/0 경계, 사망 1회, 중지, Damage, 중복 이벤트, 재연결, 잘못된 입력, UI 색상/크기/강도/Flatline 및 런타임 오류 없음 확인. 480×854 렌더링 4장을 확인했다. `git diff --check` 오류 없음.
