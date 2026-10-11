# 도로 프리팹 Index

이미지 하나에 도로 프리팹 하나를 대응한다. 도로 프리팹은 이후 도로·도보·건물 구역 프리팹의 구성 요소로 사용한다. 이번 작업에는 구역 조립, 시드 기반 맵 생성, 풀링을 포함하지 않는다.

- 프리팹: `Assets/_Project/Prefabs/Road/`
- Index: `Assets/_Project/Scripts/Map/RoadIndex.cs`
- 카탈로그: `Assets/_Project/Data/Road/RoadPrefabCatalog.asset`
- 원본: `Assets/Images/Road/` (기존 Sprite 분할 및 참조 유지)
- 전체 이미지 Sprite: `Assets/_Project/Data/Road/Sprites/` (원본 Texture를 참조하며 PNG를 복제하지 않음)

## 고정 번호와 연결

차선 수는 양방향 합계다. `0`은 연결 불가다. 북쪽은 이미지 위쪽(+Y), 동쪽은 오른쪽(+X)이다. 표는 회전 전 기준이다. 기존 번호는 바꾸지 않고 새 이미지는 새 번호로 추가한다. `None = 0`은 유효한 도로가 아니다.

| Index | 이미지 / 프리팹 이름 | 북 | 동 | 남 | 서 |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | Road_Straight_2Lane | 2 | 0 | 2 | 0 |
| 2 | Road_Straight_4Lane | 4 | 0 | 4 | 0 |
| 3 | Road_Straight_6Lane | 6 | 0 | 6 | 0 |
| 4 | Road_Cross_2Lane | 2 | 2 | 2 | 2 |
| 5 | Road_Cross_4Lane | 4 | 4 | 4 | 4 |
| 6 | Road_Cross_6Lane | 6 | 6 | 6 | 6 |
| 7 | Road_T_2Lane | 0 | 2 | 2 | 2 |
| 8 | Road_T_4Lane | 0 | 4 | 4 | 4 |
| 9 | Road_T_6Lane | 0 | 6 | 6 | 6 |
| 10 | Road_T_V2Lane_H4Lane | 0 | 4 | 2 | 4 |
| 11 | Road_T_V4Lane_H2Lane | 0 | 2 | 4 | 2 |
| 12 | Road_T_V4Lane_H6Lane | 0 | 6 | 4 | 6 |
| 13 | Road_T_V6Lane_H4Lane | 0 | 4 | 6 | 4 |

## 다음 도로 선택

`RoadPrefab.TryGetStraightRoad(direction, clockwiseQuarterTurns, out index, out straightRotation)`으로 연결 방향에 맞는 일자 도로 Index와 회전을 구한다. 막힌 방향은 `false`를 반환한다. `RoadPrefabCatalog.TryGetPrefab(index, out prefab)`으로 실제 프리팹을 얻는다.

예: Index 10의 동·서 방향은 Index 2(4차선 일자)를 회전 1로, 남 방향은 Index 1(2차선 일자)를 회전 0으로 사용한다. 북 방향은 연결 불가다.

회전 값은 **시계방향 90도 단위**이며 Z 회전은 `-90f * clockwiseQuarterTurns`다. 일자 도로는 기본 세로이며 가로 배치는 회전 1이다. 교차로도 동일한 회전 규칙을 따른다. 연결을 조회할 때 배치에 사용한 회전을 전달해야 한다. 임의 각도·비균일 스케일은 지원 범위에 포함하지 않는다.

`TryGetConnectionPosition`은 회전된 이미지 중심 기준 연결 위치를 반환한다. 프리팹은 중심 pivot, 원본 Pixels Per Unit, 스케일 1을 사용한다. 구역에 배치할 때 구역의 위치·스케일을 별도로 적용해야 한다. 위치와 차선 수 일치는 도로 연결 기준이며 교통 규칙이나 네비게이션 그래프를 의미하지 않는다.

## 에디터 도구

- `CodeBlue Rush > Roads > Create All Prefabs`: 지정된 13개만 생성한다. 기존 출력 에셋이 있으면 덮어쓰지 않고 중단한다.
- `CodeBlue Rush > Roads > Validate All Prefabs`: 전체 PNG 영역, 카탈로그 참조, 회전별 차선·연결 위치·일자 도로 호환성을 검사한다.

런타임 스크립트는 Assembly-CSharp, Editor 폴더의 생성·검증 도구는 Assembly-CSharp-Editor에 속한다. 별도 패키지나 asmdef는 추가하지 않는다.

## 검증 기록 (2026-10-11)

- Unity `6000.3.23f1`의 별도 프로젝트 복사본에서 생성했다. 현재 작업 프로젝트의 잠금 파일과 사용자 에디터를 유지했다.
- 더 짧은 경로의 새 복사본에서 스크립트 컴파일 및 저장 에셋 재로드 검사를 실행했다. 프로세스 종료 코드는 0이며 컴파일 오류와 패키지 import 예외는 없었다.
- 프리팹 13개, 전체 이미지 Sprite 13개, 카탈로그 참조와 방향별 회전 연결 검사 468건을 확인했다. 이는 명시적 에디터 검증 도구의 검사이며 NUnit 테스트 결과는 아니다.
- Unity 시작 로그에 라이선스 채널/토큰 오류가 있었고, 이후 버전별 라이선스 채널 연결과 entitlement 확인을 거쳐 작업을 완료했다. 오류 없는 Unity 실행으로 분류하지 않는다.
- 처음의 제한된 실행은 라이선스 초기화 대기로 중지했으며, 첫 생성용 복사본은 긴 경로 때문에 일부 패키지 import에 실패했다. 두 실행을 깨끗한 컴파일 통과 근거로 사용하지 않았다.
- 전달된 런타임 코드·프리팹·Sprite·카탈로그는 재로드 검사를 수행한 복사본과 해시가 일치한다. 원래 에디터가 생성한 Editor 폴더와 도구 스크립트의 메타데이터는 덮어쓰지 않았다.
- Play Mode 화면 표시, 실제 구역 연결, WebGL 빌드는 아직 검증하지 않았다. 맵 생성은 이번 작업 범위에 포함하지 않는다.

AGENTS.md 제안: `UNITY_PATH`의 TODO를 확인된 `C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe`로 명시할 수 있다. AGENTS.md는 이 작업에서 편집하지 않았다.
