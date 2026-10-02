# 10단계 차선 변경 / 끼어들기

## 구성

- 기존 VehicleAI와 TrafficSpawner만 확장했다. 새로운 런타임 Manager/도로 시스템은 없다.
- `LaneChangeDemo.unity`는 TrafficDemo를 복사하고 직선 자산만 `Assets/Prefabs/City/PassingStraight.prefab`으로 연결한다.
- 기존 도시의 도로는 방향당 한 차선이라 인접 Lane이 없다. 새 자산은 기존 경계 접점/방향을 유지하면서 양방향 각각에 분기→평행 두 차선→합류를 둔 실제 RoadChunk다. TrafficLane의 LeftLane/RightLane/internalNext와 RoadConnection만 사용한다. 기존 도로/도시 코드와 원본 자산은 변경하지 않았다.
- 기존 TrafficDemo 등 방향당 한 차선 씬에서는 차선 변경이 발생하지 않는다. 복수 차선 데모를 열거나 CityMap의 Straight에 새 자산을 연결한다.

## 동작

차량은 간헐적으로 판단한다. 현재 차선 정체가 있고 변경을 끝낼 여유 거리가 있을 때 인접 차선을 확인한다. 목표 차선의 앞뒤 차량, 실제 구급차, 다른 차량의 변경 목표까지 조회한다. 전체 차량/차선 순회는 하지 않는다.

기본값: 변경 시간 1.1초, 완료 후 Cooldown 6초, 판단 주기 0.8초, 앞 간격 4, 뒤 간격 3, 끼어들기 확률 0.15, 감지 거리 18. Inspector에서 변경 가능하다.

변경 중에는 현재 차선의 거리를 전진시키며 해당 위치를 목표 차선에 투영한다. 두 경로 사이를 SmoothStep으로 보간하고 실제 이동 방향으로 기존 회전 제한을 적용한다. 목표 차선에 도달한 뒤에만 Lane과 Distance를 바꾼다. 도로 끝을 넘기는 변경은 시작하지 않는다.

기존 앞차 감지를 유지한다. 목표 안전 공간을 잃으면 전진과 횡이동을 정지하고, 공간이 회복되면 같은 위치에서 재개한다. 목표 Lane 자체가 사라지거나 인접 관계가 깨지면 차량을 안전하게 풀로 반환한다. 새 도시/풀 반환에서는 TargetLane과 변경 진행 상태를 지운다.

끼어들기는 실제 Rigidbody2D 속도와 CityMap의 지역 차선 조회를 사용한다. 구급차가 대상 인접 차선에서 뒤쪽으로 접근하고, 속도 2 이상이며 자기 차량보다 0.2 이상 빠르고, 변경 완료 시 예상 후방 간격도 확보되는 경우에만 Seed 기반 확률을 적용한다. 구급차 Transform 방향으로 직접 이동하지 않는다.

## 연결 API

- `TryChangeLane(TrafficLane target)`: 모든 조건을 재검사하는 요청
- `TryCutIn()`: 구급차 조건과 판단 간격 및 확률을 포함한 요청
- `Lane`, `Distance`, `TargetLane`, `IsChangingLane`, `Speed`: 읽기 전용 상태
- `AllowLaneChanges`: 11/12단계에서 새로운 변경 결정을 허용/차단하는 연결 지점. 진행 중인 변경은 안전 검사와 함께 계속 처리한다.

신호등, 교차로 정차, 사이렌 감지/양보, 충돌 데미지 등은 구현하지 않는다.

## 검증

- Unity 6000.3.23f1 실제 Play: `Lane change play validation passed: 30 checks`.
- 기존 9단계 검증을 변경 없이 실행: `Traffic play validation passed: 36 checks`.
- 새 도로 및 랜덤 도시 연결 검증, 인접 Lane/앞뒤 공간, 연속 이동과 완료 후 Lane 확정, 중복/Cooldown, 안전 상실 정지·재개, 확률 0/1, 잘못된 구급차 차선/간격/상대 속도 거절, 주기적 자동 끼어들기 및 일반 차선 변경을 확인했다.
- 런타임 오류/NullReference 없음. `git diff --check` 오류 없음.
- 로그: `Logs/lane-change-play.log`, `Logs/traffic-stage10-regression.log`. 기존 검증 복사본에서 저장된 데모를 다시 열어 실행했다. 관련 없는 1~8단계 전체 회귀는 실행하지 않았다.
- 메뉴: `CodeBlue Rush > Lane Change > Validate Play`.

추천 Commit: `feat: add safe lane changes and conditional ambulance cut-ins`. Commit/Push는 수행하지 않았다.
