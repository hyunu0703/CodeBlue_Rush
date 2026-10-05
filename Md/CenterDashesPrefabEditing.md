# CenterDashes 개별 편집

`Assets/Prefabs/CenterDashes.prefab` 하나 아래에 중앙선 조각 1,130개를 직접 자식으로 분리했습니다. `NewGamePlay` 씬의 기존 `CenterDashes`는 이 프리팹의 인스턴스로 연결했습니다.

Hierarchy에서 `CenterDashes`를 펼치고 `CenterDash_0001` 같은 자식을 선택해 Position, Rotation, Scale을 조정하세요. 각 조각의 피벗은 해당 조각의 경계 상자 중심입니다. 번호는 분리 당시 북쪽에서 남쪽으로, 같은 높이에서는 서쪽에서 동쪽으로 부여했습니다.

여러 자식을 동시에 선택해 함께 이동할 수 있고, 필요한 조각만 복제·비활성화·삭제할 수도 있습니다. 씬에서 수정한 뒤 씬을 저장하면 유지됩니다. 프리팹에도 반영하려면 Overrides에서 해당 변경을 Apply하거나 `CenterDashes.prefab`을 더블클릭하여 Prefab Mode에서 편집하고 저장하세요.

개별 메시들은 `Assets/MapGeometry/NewGamePlayRoadLayout/CenterDashParts.asset` 하나에 서브 에셋으로 저장했습니다. 메시마다 독립된 참조를 사용하므로 개별 자식 편집이 가능합니다. 기존 통합 `CenterDashes.asset`은 원본 비교용으로 보존합니다.

원본 정점 11,534개와 삼각형 10,404개, 정점 색상과 UV, 재질, 렌더링 순서 및 Static 설정을 보존했습니다. 이번 변경은 화면 표시용 중앙선의 구조 변경이며 차량 차선이나 시민 보행 경로를 자동으로 수정하지 않습니다.

Unity 6000.3.23f1의 복제 프로젝트에서 프리팹과 실제 씬을 열어 자식 1,130개의 메시 참조, 재질과 렌더링 순서, 원본 삼각형 및 면 방향, 씬 프리팹 연결을 검증했습니다. 피벗을 나누면서 생긴 부동소수점 좌표 오차는 최대 약 0.00000853 Unity 단위입니다. 기존 Crosswalks 자식 160개도 유지됨을 확인했습니다. 검증 결과는 `Md/CenterDashesPrefabUnityQA.json`에 있습니다.
