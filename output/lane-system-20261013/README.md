# 차로 시스템 구현 및 검증

## 적용

기존 13종 `Assets/Prefabs/Road/` Prefab이 사용하는 `RoadPrefab`에 로컬 주행 경로 API와 선택 시 Gizmos를 추가했다. 프리팹의 기존 `imageSize`와 방향별 왕복 차로 수를 사용하므로 새 컴포넌트 부착이나 프리팹 재직렬화가 필요 없다. 새 코드의 소속은 기존 코드와 같은 Assembly-CSharp이며 asmdef/asmref를 추가하지 않았다.

`MissionMapGenerator`는 분기를 포함한 모든 배치 도로로 차로 그래프를 만든다. 실제 배치 전에 전체 그래프를 검증하고 성공한 그래프만 공개한다. 후송 추가 시 기존 도로를 풀에 반환하지 않고, 기존 도로와 새 도로를 함께 연결한다. 도로/경로 목록에 기존 항목이 앞부분 그대로 유지되므로 기존 경로 ID와 좌표도 유지된다. 새 미션에서는 기존 그래프를 무효화하고 새 목록을 만든다.

## 조사한 통행 규정과 게임에서의 결정

확인일: 2026-10-13. 확인한 국가법령정보센터 조문은 시행일 2026-07-01의 도로교통법이다.

| 규정 | 차로 데이터와 향후 AI의 역할 |
| --- | --- |
| [제13조: 차마의 통행](https://law.go.kr/lsLinkCommonInfo.do?chrClsCd=010202&lsJoLnkSeq=1030975107) | 중앙선 오른쪽 통행을 방향별 차로 위치로 표현한다. 보도에는 경로를 만들지 않는다. |
| [제14조: 차로의 설치 등](https://www.law.go.kr/LSW/lsLinkCommonInfo.do?chrClsCd=010202&lsJoLnkSeq=1031180107) | 일자 도로에서는 해당 차로를 따라 정방향으로 진행한다. 반대편 도로나 반대 방향 차로를 연결하지 않는다. |
| [제19조: 안전거리 확보 등](https://www.law.go.kr/lsLawLinkInfo.do?chrClsCd=010202&lsJoLnkSeq=1000719984) | 인접 차로는 차로 변경 후보 정보다. 정상 통행을 방해하지 않는지, 앞뒤 차량 간격이 안전한지는 향후 AI가 판단해야 한다. |
| [제25조: 교차로 통행방법](https://www.law.go.kr/lsLinkCommonInfo.do?chrClsCd=010202&lsJoLnkSeq=1030974847) | 오른쪽 가장자리 우회전, 중앙선 쪽 진입 후 교차로 중심 안쪽 좌회전을 회전 곡선으로 표현한다. 서행 제어와 신호교차로 출구 정체 시 진입 금지는 AI 영역이다. `RequiresExitClearance`와 경로 충돌 정보를 제공한다. |
| [제26조: 교통정리가 없는 교차로에서의 양보운전](https://www.law.go.kr/LSW/lsLinkCommonInfo.do?lsJoLnkSeq=1007526125) | 먼저 진입한 차량, 넓은 도로, 동시 진입 시 오른쪽 도로, 좌회전 차량의 직진/우회전 차량에 대한 양보를 확인했다. 경로의 Entry/Exit, RoadOrdinal, 왕복 차로 수와 Conflicts로 향후 AI가 판단할 수 있다. 이 단계에서 통행 우선권을 자동 부여하지 않는다. |
| [경찰청 우회전 안내](https://www.korea.kr/news/policyNewsView.do?newsId=148910893) | 전방 적색 시 일시정지 후 다른 통행을 방해하지 않는 우회전, 별도 우회전 신호가 있으면 해당 신호 준수가 필요하다. 따라서 회전 경로가 있다는 것은 현재 신호에서 통행할 수 있다는 뜻이 아니다. |

법규가 모든 교차로의 회전 가능 차로 수나 목적지 차로를 하나로 지정하는 것은 아니다. 다음은 사용자가 승인한 게임의 배치 정책이다.

- 왕복 차로를 방향별 절반으로 나누고 중앙선 쪽부터 1번으로 번호를 부여한다.
- 직진 출구가 있으면 같은 번호를 유지한다.
- 좌회전: 진입 1차로 → 출구 1차로. 우회전: 진입 바깥 차로 → 출구 바깥 차로.
- 편도 1차로는 존재하는 직진·좌회전·우회전 경로를 함께 제공한다.
- 직진 출구가 없는 6차로 T의 가운데 진입 차로는 후속 경로가 없다. 향후 AI가 교차로 전에 차로를 변경해야 한다.
- 유턴 경로는 생성하지 않는다. 지도/추가 분기의 끝은 연결이 없는 경계로 남는다. 차량의 스폰·종료 정책은 향후 AI가 담당한다.
- 교차로 경로에서는 차로 변경을 허용하지 않는다. 접근 정지선 앞과 출구 횡단보도 직후의 한 차로 폭도 게임의 보수적인 제한 구간이다. 이 거리 제한은 전국 공통 법정 거리라는 의미가 아니다.

## 향후 Car AI에서 사용할 API

| API | 계약 |
| --- | --- |
| `RoadPrefab.TryGetLaneLayout` | 도로 한 개의 원본 로컬 경로. 영구 에셋을 수정하지 않는다. |
| `MissionMapGenerator.LaneNetwork` | 활성 생성기의 최신 그래프. 생성 전/비활성 상태에는 null. |
| `MissionMapGenerator.LaneNetworkVersion` | 성공한 생성/후송 추가마다 증가. 버전이 바뀌면 그래프를 다시 획득한다. |
| `RoadLaneNetwork.Paths[id]` | ID는 해당 그래프 내부 인덱스다. `RoadOrdinal`은 생성기의 `ActiveRoads` 인덱스다. |
| `RoadLanePath.TrySample(distance, ...)` | 누적 호 길이에 따른 위치·단위 주행 방향. 거리 범위를 양 끝으로 제한하며 NaN/무한대는 거부한다. |
| `MissionMapGenerator.TrySampleLane(id, distance, ...)` | 그래프 좌표를 월드 위치·방향으로 변환한다. 입력 거리는 여전히 맵 로컬 유닛이다. |
| `RoadLanePath.Successors` | 같은 도로 내 진입→교차로→출발 및 맞닿은 도로의 정방향 연결. 갈림길에서 어떤 후속 경로를 선택할지는 AI가 결정한다. |
| `RoadLaneNetwork.TryFindRoute` | 차로 변경 없이 이어지는 정방향 경로만 탐색. 거리 최단 경로/동적 정체 비용 탐색은 아니다. |
| `RoadLaneNetwork.TryFindNearest` / `RoadLanePath.TryProject` | 초기 경로 획득/재획득 및 경로 위 거리 계산. 네트워크 검색 입력은 맵 로컬 좌표다. forward가 0이면 방향 필터가 없고 그 외에는 60도 이내 경로만 선택한다. |
| `LeftNeighborId` / `RightNeighborId` / `CanChangeLaneAt` | 같은 주행 방향의 인접 차로와 차로 변경 후보 구간. 안전 판정이나 실제 변경 궤적은 구현하지 않았다. |
| `HasStopLine` / `StopLineDistance` | 접근 차로 끝에 원본 도색의 정지선 바깥 경계가 있다. 차량 앞범퍼가 선을 넘지 않게 차체 크기를 보정해야 한다. |
| `Conflicts` / `RequiresExitClearance` | 같은 교차로에서 차로 폭 통행 영역이 겹치거나 같은 입구/출구를 공유하는 경로. 실제 차량 점유, 차체 길이, 신호·보행자·예약 여부는 향후 AI가 검사한다. |

모든 그래프 좌표·길이·폭은 `Map Root`의 로컬 유닛이다. 실제 미터 단위를 가정하지 않는다. 맵 부모 스케일이 1이 아니면 차량의 월드 속도를 로컬 거리 증가량으로 변환해야 한다. `TrySampleLane`은 위치·방향 변환만 한다. 이 단계에서는 Rigidbody 이동, Car Prefab, 신호 제어, 차량 예약·대기열·정체 AI를 추가하지 않았다.

차로 그래프는 생성기가 소유한다. 후송 추가/새 미션/Configure/파괴 시 이전 스냅숏은 `IsValid == false`가 된다. 후송 때 기존 ID는 유지되지만 새 경계 연결은 새 스냅숏에 있으므로 반드시 최신 그래프를 다시 얻어야 한다. 비활성화 동안에는 `LaneNetwork`가 null이므로 차량 소비자는 생성기의 활성 상태와 최신 그래프를 확인해야 한다. 읽기 전용 목록을 변경하거나 Id만 새 미션에 넘겨 사용하지 않는다.

## 검증(Verification)

실제 런타임 코드와 실제 13종 프리팹 YAML을 읽는 .NET 검사 프로그램을 사용했다. Unity 오브젝트를 생성하지 않는다.

- 전체 런타임 C# 근사 빌드: 오류 0, 경고 0. 기존 Unity DLL 참조를 사용한 별도 출력 프로젝트이다.
- 차로 검사: 164,217개 조건 통과. 13종 도로, 156개 회전·경계 경우, 100개 픽업/후송 시드 쌍.
- 정지선/우측통행/직진 차로 유지/회전 차로 제한/유턴 제외/닫힌 면 거부/잘못된 폭 및 겹침 거부/교차 충돌 대칭/근접 검색의 역주행 거부/오래된 그래프 무효화 검사.
- 시드 쌍은 네 출구 방향과 왕복 2·4·6차로를 모두 포함한다. 기존 경로 19,024개의 ID와 점 좌표가 유지됨을 검사했다.
- 후송 접합부는 픽업→후송과 후송→픽업 양방향의 모든 차로 위치·번호·접선을 검사했다.
- 상세 시드 기록: `seed-results.json`. 실제 계산한 로컬 경로: `layout-paths.json`.
- 원본 도로 이미지 위에 실제 경로를 겹친 `lane-paths-review.png`로 13종 모두의 차로 중심선과 회전/정지선 위치를 시각 검토했다. 멀리 배치된 교차로의 평행 직진 차로가 좌표 오차로 충돌 처리되지 않는 검사도 통과했다.
- 근사 빌드/순수 기하 검사는 Unity 컴파일, 에디터 생명주기, 실제 물리 주행, 플레이어 빌드 검증을 대신하지 않는다.

재실행(프로젝트 루트에서):

```powershell
dotnet build output/lane-system-20261013/RuntimeCheck.csproj --nologo
dotnet run --project output/lane-system-20261013/LaneChecks.csproj --no-launch-profile
```

## 수동 에디터 작업 — 아직 실행하지 않음

AGENTS.md의 `UNITY_PATH`가 TODO이므로 Unity 실행 파일을 탐색하거나 Unity 컴파일/테스트/import를 실행하지 않았다. 검증 승인만으로 실행 경로를 추측하지 않았다.

1. 프로젝트 버전 `6000.3.23f1`에서 새 `Assets/_Project/Scripts/Traffic/` 폴더를 import한다. 새 폴더 및 다음 스크립트의 `.meta`는 Unity가 생성해야 한다: `RoadLanePath.cs.meta`, `RoadLaneLayout.cs.meta`, `RoadLaneNetwork.cs.meta`. 손으로 만들지 않았다.
2. Console의 컴파일 오류가 없는지 확인한다. 13종 도로 Prefab을 선택하고 Scene의 Gizmos를 켜서 차로 중심 경로·방향 화살표·좌/우회전·빨간 정지선이 표시되는지 확인한다.
3. GamePlay에서 미션을 수락하고 환자 픽업을 완료한다. 픽업 도로가 남아 있고 새 후송 도로가 출구에 붙는지, 차로 경로가 경계에서 이어지는지 확인한다.
4. 여러 미션을 반복해 도로 풀 재사용 후 이전 그래프가 무효화되고 새 그래프의 RoadOrdinal이 ActiveRoads에 대응하는지 확인한다. 비활성화/재활성화, 씬 종료, Domain Reload가 꺼진 반복 Play도 확인한다.
5. 신규 Car를 연결할 때 차체 전체가 회전 경로 주변 통행면에 들어가는지 검증한다. 차량 크기/회전 반경은 아직 주어지지 않았고 물리 주행은 이번 순수 검사에서 실행하지 않았다.

## API / 직렬화 변경

기존 공개 API, 기존 직렬화 필드 이름·타입·기본값과 기존 enum 값을 유지했다. 프리팹, 씬, 이미지, ProjectSettings, Packages, 기존 .meta는 수정하지 않았다. 신규 API/managed 모델만 추가했다. 생성된 솔루션은 수정하지 않았고 Unity 런타임 어셈블리 마이그레이션도 수행하지 않았다.

새 MonoBehaviour를 추가하지 않아 별도 컴포넌트 설정은 없다. 스크립트가 import되면 기존 `RoadPrefab`을 사용하는 13종 모두 동일한 경로 기능을 제공한다. 바이너리/외부 호출부와 실제 Unity 실행은 검증하지 않았다.

## AGENTS.md 제안

`UNITY_PATH`와 Unity 배치 타임아웃은 TODO다. Standalone 스크립팅 백엔드도 저장 설정에 명시되지 않았다. 현재 생성 csproj의 대상 표기는 WebGL이므로 PC 출시 정책과 현재 에디터 선택을 구분해야 한다. AGENTS.md와 해당 설정은 수정하지 않았다.
