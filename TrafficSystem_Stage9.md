# 9단계 일반 차량

## 파일과 사용

`Assets/Scenes/TrafficDemo.unity`는 HospitalDemo에 TrafficSpawner만 연결한 별도 씬이다. Play하면 기존 구급차 주변에 차량이 유지된다. `Assets/Prefabs/TrafficVehicle.prefab`은 노란 차체/어두운 앞유리의 내장 Sprite Placeholder이며, 진행 방향은 로컬 +Y다.

런타임 책임은 두 개다.

- `VehicleAI`: TrafficLane.TrySample의 실제 거리와 방향으로 이동한다. 끝에서는 GetNext 직접 참조로 정방향 접점이 맞는 차선에만 전환한다. 차체 회전은 제한된 각속도로 맞춘다.
- `TrafficSpawner`: CityMap, 기존 플레이어 Transform, 실제 Camera, 차량 Prefab을 직접 연결한다. 생성과 풀 반환 및 센서용 Collider→Vehicle 참조만 관리한다.

기존 TrafficLane/RoadChunk/CityMap 및 1~8단계 코드는 수정하지 않는다.

## 기본값

- 최대 활성 차량 16대
- 플레이어 거리 18~40 단위에서 생성, 60 단위 밖 반환
- 실제 Camera viewport의 상하좌우 20% 여유 영역 안에서는 생성하지 않음
- 0.5초마다 정리 및 최대 24회 주변 격자 후보 시도, 한 번에 최대 3대 생성
- 속도 5, 가속 3, 제동 8, 회전 360도/초, 기본 전방 조회 길이 7
- 원형 감지 Collider 반지름 0.9, 전방/Spawn 조회 반지름 1.5로 차체보다 여유 간격 유지

Inspector에서 생성 거리, 수량, 주기, 시도 수와 주행 값을 조절한다. 카메라가 없거나 비활성인 경우 안전하게 생성하지 않는다.

## 성능과 수명

생성은 플레이어 주변 격자 좌표로 RoadChunk를 직접 조회한다. 매 프레임 전체 Lane 검색은 없다. 차량 센서는 짧은 미래 차선 경로를 일정 간격으로 샘플링하고 할당 없는 Physics2D.OverlapCircle 결과만 검사한다. 모든 차량×모든 차량 탐색은 없다. 최대 센서 길이와 조회 버퍼가 고정되어 있고 버퍼 포화 시 보수적으로 정지/생성 거절한다.

앞차 판정은 같은 차선 또는 주행할 다음 차선의 차량을 대상으로 한다. 제동 거리로 목표 속도를 낮추고 안전 이동 거리로 최종 이동량을 제한한다. 앞차가 이동하거나 풀로 반환되면 다음 물리 주기에 재평가하여 다시 출발한다.

활성 슬롯은 마지막 차량과 교환하여 O(1)로 제거하고, 거리 정리는 유지 주기당 O(n)이다. 비활성 차량은 작은 Stack 풀에서 재사용한다. 풀 반환 시 현재 Lane과 소유 참조를 지우며, 도시 교체 이벤트는 이전 차량을 즉시 반환한다. 외부 비활성화/파괴도 등록을 해제한다.

차량 Collider는 감지용 Trigger다. 다른 교차 진로의 우선권, 차선 변경, 신호등, 사이렌 반응, 구급차 양보, 충돌 데미지/넉백은 구현하지 않는다.

## 10단계 연결 지점

- `VehicleAI.Lane`, `Distance`, `NextLane`, `Speed`: 현재 주행 상태 읽기 전용
- `TrafficSpawner.IsSpaceFree(position, ignore)`: 자기 차량을 제외한 주변 공간 조회
- 차선 변경 동작은 아직 없으며, 다음 단계에서 기존 VehicleAI의 차선 배정 책임을 확장할 수 있다.

## 검증

`CodeBlue Rush > Traffic > Validate Play`에서 저장된 씬을 다시 열어 실제 물리 주행을 확인한다. 로그는 `Logs/traffic-play.log`, 480×854 현장 렌더링은 `Logs/traffic-driving.png`다. 기존 검증 복사본을 사용하며, 공용 기반 코드를 수정하지 않았으므로 관련 없는 전체 회귀 검증은 실행하지 않는다.

검증 결과: Unity 6000.3.23f1에서 `Traffic play validation passed: 36 checks`. 직선/코너/Next Lane, 전방 정지·재가속, 경계 너머 앞차 및 반환 후 재출발, 화면 안 생성 차단, 겹침 차단, 최대 수량, 거리 정리, null/비활성/잘못된 연결, 새 도시, 외부 파괴를 확인했다. 런타임 오류/NullReference 없음. 480×854 렌더링 확인, `git diff --check` 오류 없음. Commit/Push 없음.

추천 Commit: `feat: add pooled lane-following ambient traffic`.
