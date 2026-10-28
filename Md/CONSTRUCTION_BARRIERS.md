# 미연결 분기 공사 바리케이드

## 적용 동작

- 미션 진행에 사용하지 않는 교차로 출구의 기존 직선 두 구역은 유지한다.
- 첫 분기 구역의 입구에서 한 차로 폭 안쪽에 주황·흰색 사선 펜스와 받침대를 자동 생성한다. 펜스와 `BoxCollider2D`의 폭은 차도와 양쪽 인도의 합으로 제한한다. 원본 1254px 기준 차선당 84.6px, 한쪽 인도 64px를 사용하며 건물 영역까지 뻗지 않는다.
- `MissionMapGenerator`가 `RoadConstructionBarrier`를 자동 연결하므로 GamePlay 씬과 원본 프리팹의 수동 설정은 없다. 추가 패키지를 사용하지 않는다.
- 분기 전체를 일반 차량 생성/통행에서 제외하고, 해당 방향을 입구 또는 출구로 사용하는 교차로 경로도 제외한다. 경로 ID는 유지하고 후속 경로 및 차로 변경 연결에서 폐쇄 경로를 제거한다.
- 도로 스트리밍 반환 시 펜스도 함께 비활성화하고 복원 시 다시 설정한다. 일반 도로로 풀 재사용 시 이전 펜스의 표시와 충돌을 끈다. 후송 확장 시 기존 분기 폐쇄 상태를 유지한다.

## 주요 파일

- `Assets/Scripts/Map/RoadConstructionBarrier.cs`: 런타임 펜스 표시, 충돌 및 리소스 정리.
- `Assets/Scripts/Missions/MissionMapGenerator.cs`: 첫 분기 입구의 차단 방향과 도로 풀 연결.
- `Assets/Scripts/Traffic/RoadLaneNetwork.cs`, `RoadLanePath.cs`: 폐쇄 도로와 교차로 경로 제외.
- `Assets/Scripts/Traffic/RoadTrafficController.cs`, `RoadTrafficSimulation.cs`: 폐쇄 경로의 차량 생성 거부.
- `Assets/Scripts/Editor/ConstructionBarrierTests.cs`: 차로 수·회전·혼합 T자·후송 확장·풀 재사용·물리 차단 검증.

## 수행한 검증과 제한

2026-10-28 Windows에서 Unity 6000.3.23f1의 참조 어셈블리와 프로젝트의 WebGL 컴파일 정의를 사용하는 런타임·에디터 C# 프로젝트를 .NET SDK로 빌드했다. 새 스크립트와 테스트도 포함했으며 경고 0개, 오류 0개였다.

빌드한 실제 프로젝트 코드와 테스트를 .NET 10 관리 코드 실행기로 실행해 18개 검증이 통과했다. 왕복 2/4/6차로 × 네 방향 회전 12개, 후송 확장 시 기존 경로 ID·폐쇄 상태 유지 1개, 혼합 차로를 포함한 T자 5개(각각 네 방향 회전)를 검사했다. 폐쇄 경로의 생성 거부, 후속 경로 제외 및 정상 차량의 실제 시뮬레이션 교차로 통과를 확인했다. 이는 Unity Play Mode나 실제 물리 실행 결과가 아니다.

Unity 6000.3.23f1 EditMode 배치 실행은 초기화 중 종료 코드 1로 끝나 테스트 결과 XML이 생성되지 않았다. 첫 실행에는 WMI·라이선스 연결 오류가 있었고, 라이선스 연결에 성공한 재시도도 종료되었다. 최종 종료 원인은 확정하지 못했다. 펜스의 화면 배치, 실제 차량 충돌, 풀 재사용의 Unity 실행 테스트 및 모바일 WebGL 실행은 미검증이다.

이후 건물 영역까지 펜스가 뻗는 문제를 수정해 표시·충돌 폭을 차도와 양쪽 인도의 합으로 줄였다. 현재 원본에서 왕복 2/4/6차로의 폭은 각각 2.972/4.664/6.356유닛이다. 기존 배치 테스트도 차로 수별 폭과 받침대의 경계 내 배치를 확인하도록 바꾸고, 물리 테스트에는 인도 차단과 건물 영역 통과 조건을 반영했다. 이 수정의 Unity 실행 테스트와 외관은 미검증이며 위 18개 통과 기록은 최초 구현 시의 로직 검증 결과다.

## Unity에서 확인할 순서

1. Unity 6000.3.23f1에서 Test Runner의 EditMode `ConstructionBarrierTests`를 실행한다. 전체 테스트가 통과하고 Console 오류가 없어야 한다.
2. Start → Waiting → 미션 수락 → GamePlay로 출동한다. 안내 경로를 따라 각 교차로에 접근하면 사용하지 않는 분기에 펜스가 보이고, 정상 진행 출구는 열려 있어야 한다.
3. 막힌 분기의 중앙과 인도 쪽으로 구급차를 천천히 운전한다. 펜스 앞에서 막혀야 하며, 일반 차량은 그 분기로 회전하거나 분기 안에서 생성되지 않아야 한다.
4. 환자 픽업 후 후송 경로가 추가되어도 기존 펜스와 차량 제한이 유지되는지 확인한다. 지나온 교차로로 돌아가 도로가 복원되면 펜스도 복원되어야 한다.
5. 다음 미션을 시작한다. 일반 도로로 재사용된 구역에 이전 바리케이드나 보이지 않는 충돌이 남지 않아야 한다.

## API 참고

펜스는 [Sprite.Create](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Sprite.Create.html)의 FullRect 런타임 스프라이트와 기존 도로 재질을 사용하며, 물리 차단은 [BoxCollider2D](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/BoxCollider2D.html)를 사용한다. 공식 API 확인과 프로젝트 실행 검증은 별개이며 실제 물리·외관 검증 제한은 위와 같다.
