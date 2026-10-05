# CenterDashes 개별 편집

`Assets/Prefabs/CenterDashes.prefab` 하나 아래에 노란 중앙선 본체 565개를 직접 자식으로 넣었습니다. 선 주변의 투명한 그라데이션 테두리 오브젝트와 메시 565개는 제거했습니다. 노란선 하나가 자식 오브젝트 하나에 해당합니다. `NewGamePlay` 씬의 기존 `CenterDashes`는 이 프리팹의 인스턴스로 연결했습니다.

Hierarchy에서 `CenterDashes`를 펼치고 `CenterDash_0018` 같은 자식을 선택해 Position, Rotation, Scale을 조정하세요. 각 조각의 피벗은 해당 조각의 경계 상자 중심입니다. 기존 본체의 이름과 파일 ID를 유지했으므로 번호에는 제거된 테두리의 빈 번호가 있습니다.

여러 자식을 동시에 선택해 함께 이동할 수 있고, 필요한 조각만 복제·비활성화·삭제할 수도 있습니다. 씬에서 수정한 뒤 씬을 저장하면 유지됩니다. 프리팹에도 반영하려면 Overrides에서 해당 변경을 Apply하거나 `CenterDashes.prefab`을 더블클릭하여 Prefab Mode에서 편집하고 저장하세요.

개별 메시들은 `Assets/MapGeometry/NewGamePlayRoadLayout/CenterDashParts.asset` 하나에 서브 에셋으로 저장했습니다. 메시마다 독립된 참조를 사용하므로 개별 자식 편집이 가능합니다. 기존 통합 `CenterDashes.asset`은 원본 비교용으로 보존합니다.

본체 메시의 정점과 삼각형, 정점 색상과 UV, 재질, Transform, 렌더링 순서 및 Static 설정은 그대로 유지했습니다. 이번 변경은 화면 표시용 중앙선의 구조 변경이며 차량 차선이나 시민 보행 경로를 자동으로 수정하지 않습니다. 기존 통합 메시의 테두리는 비교용 원본에만 남아 있고 씬 렌더링에는 사용되지 않습니다.

검증 결과는 `Md/CenterDashesPrefabUnityQA.json`에 있습니다. 씬에서 사용자가 삭제한 본체 3개와 추가한 본체 2개는 유지하므로 씬 인스턴스에는 564개의 본체 자식이 있습니다.
