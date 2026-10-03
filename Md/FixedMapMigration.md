# 고정 맵 전환 분석

기준: main 13686b9 (원격 main 일치 확인). Commit / Push / 새 Branch 없음.

## 기존 참조와 처리

| 대상 | 참조 | 처리 |
| --- | --- | --- |
| CityMap | NavigationRoute, PatientReport, HospitalArrivalZone, HospitalTransfer, TrafficSpawner, CitizenSpawner, AmbulanceCamera | 클래스와 조회 API 유지, 런타임 생성·Seed·환경 배치 제거, Scene 직렬화 참조 사용 |
| CityLayout | CityMap 및 Layout/Seed를 캐싱하는 소비자, 에디터 생성 검증 | 생성 데이터 제거, 소비자는 고정 CityMap 참조 사용 |
| CityTerrain, CityVegetation | Gameplay의 City, MapAssetStage4/5 에디터 자산 도구 | 환경을 고정 World에 저장, Scene 컴포넌트와 에디터 연결 코드 제거 후 스크립트 삭제 |
| EnvironmentSlot.Populate | CityMap.Populate | 배치 기능 제거, 환자 후보 및 도로 접근 데이터 유지 |
| RoadConnection / TrafficLane | 도로 Prefab, Navigation, VehicleAI | 편집 시 연결된 Target / Next를 저장, 런타임 도로 자동 연결 제거 |
| SidewalkPath | CitizenSpawner / CitizenAI / 도로 Prefab | 기존 경로 유지, 인접 경로를 직렬화 |
| HospitalArrivalZone | Hospital Prefab, HospitalTransfer, GameFlow | 고정 Slot 직접 참조, 검색·Seed 위치 선택 제거 |
| GameFlow | Mission 객체와 결과 UI | 기존 평가·이벤트 유지, 성공·실패 모두 미션만 초기화하고 소방서 복귀 |
| PatientReport | Pickup / ECG / Transfer / UI / Collision | 미션 소유권과 이벤트 유지, 사전 검증한 고정 후보 중 인덱스 한 번 선택 |
| PatientPickup / PatientECG / VehicleAI / TrafficSignalController | 기존 Scene / Prefab 및 이벤트 | 알고리즘 유지 |
| CameraTurnZone | 도로 Corner / 교차로 Prefab, AmbulanceCamera | 고정 위치·크기 보존 |

기존 Gameplay는 완성된 지리 기반 서울 Scene을 포함하지 않고 실행 시 도시를 생성했다.
현재 아트와 차선이 연결된 도시를 편집 데이터로 고정한다. 실제 서울 지형을 재현하는 GIS 작업은 포함하지 않는다.
런타임에는 World 인스턴스화, 도로 연결 탐색, 환경 랜덤 배치를 수행하지 않는다.

사용자 후속 지시에 따라 실행 테스트는 수행하지 않는다. 삭제한 지형·물·식생 Prefab은 복구하지 않는다.
