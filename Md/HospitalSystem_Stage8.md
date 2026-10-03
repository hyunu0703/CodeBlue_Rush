# 8단계 병원 이송

## 연결

`Assets/Scenes/HospitalDemo.unity`는 기존 ECG 데모에 `Hospital.prefab`과 `HospitalTransfer`를 연결한 별도 씬이다. 기존 1~7단계 코드/자산과 빌드 씬 목록은 변경하지 않는다.

- `HospitalArrivalZone`: CityMap의 StateChanged에 연결된다. 도시 생성 시 Seed와 차선 배열 색인으로 실제 차선 중간 지점을 확보한다. CityMap은 생성 과정에서 방향 그래프의 모든 차선 상호 도달 가능성을 이미 검증하므로 도로 그래프를 재구현하지 않는다. 데모에는 병원 하나가 있으며 새 도시에서도 재등록된다.
- `HospitalPoint`, `Lane`, `Distance`는 읽기 전용이다. 반지름 2.5의 Trigger와 파란 바탕/흰 십자 임시 Sprite가 병원의 도로 진입점을 표시한다. 건물이나 차량 장애물을 추가하지 않는다.
- `HospitalTransfer`: PatientReport, NavigationRoute, PatientECG, 구급차 Rigidbody2D, 병원 배열을 Inspector로 참조한다. 픽업 이벤트에서 배열 후보를 유한하게 검사하고 첫 유효 Navigation 경로에서 종료한다. 정상 도시의 병원 하나라면 경로 요청은 한 번이다.
- 경로 실패 시 자동 반복하지 않는다. 설정/도로 복구 후 `TryBeginTransfer()`로 명시적으로 재요청할 수 있다. NoHospital / NoRoute / MissingReferences 상태로 실패 이유를 구분한다.
- 매 프레임 병원 탐색이나 별도 길찾기/Manager는 없다. 물리 Trigger는 실제 attachedRigidbody를 전달하고 차량 중심이 지정 영역 안에 있는지 확인한다.

## 도착 확정과 이후 연결

활성 미션, 동일 MissionId, 환자 탑승, 살아 있는 ECG, 현재 도시의 지정 병원, 실제 구급차 영역 진입을 모두 확인해야 도착한다. 정차는 병원 도착의 추가 조건으로 요구하지 않는다.

`HospitalTransfer.HospitalArrived`는 미션당 1회 발생한다. 이벤트 전에 Status를 Arrived로 확정하고 `PatientECG.StopDecay()`를 호출한다. ECG를 회복/초기화하지 않는다. 사망이 먼저라면 PatientDied 상태 또는 ECG 0 검사로 도착을 차단하고, 도착이 먼저라면 감소가 즉시 중단된다.

14단계에서는 다음 읽기 전용 데이터와 이벤트를 사용한다.

- `HospitalArrived`
- `Status`, `MissionId`, `Destination`
- `ArrivalECG`, `ArrivalState`: 도착 순간의 값/상태 스냅샷

자연 감소 중단은 7단계 StopDecay의 기존 의미를 유지한다. 향후 충돌 Damage 호출 여부는 이송 확정 상태를 확인하는 상위 흐름에서 결정해야 한다. 현재 단계는 충돌, 별 평가, 다음 신고, 미션 전체 Reset, GameOver를 구현하지 않는다.

보고 취소/미션 교체/도시 변경은 이전 병원 및 소유 경로를 정리한다. 같은 미션의 재활성화와 재연결은 이미 확정한 도착/사망을 다시 확정하지 않는다.

## 실행

HospitalDemo를 열어 Play 후 기존 NavigationRoute의 Preview Generated Road Route와 PatientReport의 Issue Report를 사용한다. 현장에서 픽업하면 병원 경로로 전환된다. 자동 검증은 `CodeBlue Rush > Hospital > Validate Play`다.

검증 로그: `Logs/hospital-play.log`. 사용자 편집기를 유지하기 위해 기존 검증 복사본에서 저장된 씬을 다시 열어 실제 Play/물리 이벤트로 검증한다. 기존 기반 코드가 변경되지 않아 관련 없는 전체 회귀는 실행하지 않는다.

검증 결과: Unity 6000.3.23f1에서 `Hospital play validation passed: 41 checks`. 실제 병원 Trigger, ECG 고정, 도착/사망 선후, 병원 없음, 도로 밖 시작 및 실제 단절 NoPath, 참조 누락, 동일 미션 재연결, 새 도시와 파괴된 병원 처리가 통과했다. 런타임 오류/NullReference 없음. `git diff --check` 오류 없음. Commit/Push 없음.
