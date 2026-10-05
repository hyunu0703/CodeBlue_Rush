# Crosswalks 개별 편집

`Assets/Prefabs/Crosswalks.prefab` 하나에 횡단보도 160개를 직접 자식으로 넣었습니다. `NewGamePlay` 씬의 기존 `Crosswalks` 위치에는 이 프리팹의 인스턴스가 연결되어 있습니다.

1. Hierarchy에서 `Crosswalks`를 펼치세요.
2. `Crosswalk_001_OuterCircuit`처럼 번호와 도로 이름이 붙은 자식을 선택하세요. 번호는 `Md/NewGamePlayCrosswalkReview/CrosswalkPlacements.json`의 `id`와 같습니다.
3. Transform의 Position, Rotation, Scale로 해당 횡단보도 전체를 이동·회전·크기 조정하세요. 각 자식의 피벗은 배치 기록의 횡단보도 중심입니다.
4. 씬에서 수정한 내용은 씬을 저장하면 유지됩니다. 프리팹에도 반영하려면 Overrides에서 해당 변경을 Apply하거나 `Crosswalks.prefab`을 더블클릭해 Prefab Mode에서 수정하고 저장하세요.

필요한 자식만 복제하거나 비활성화하거나 삭제할 수 있습니다. 각 자식의 MeshRenderer에서 재질을 따로 지정할 수도 있습니다. 개별 메시 에셋은 `Assets/MapGeometry/NewGamePlayRoadLayout/CrosswalkParts`에 있습니다.

분리할 때 기존의 모든 정점 5,394개와 삼각형 2,938개를 보존했습니다. 월드 좌표, 정점 색상, UV, 재질과 렌더링 순서도 보존했습니다. 기존 통합 `Crosswalks.asset`은 비교용으로 보존하지만 씬 렌더링에는 사용하지 않습니다.

Unity 6000.3.23f1의 복제 프로젝트에서 프리팹과 실제 `NewGamePlay` 씬을 열어 자식 160개, 개별 메시·재질·렌더링 순서, 원본과 같은 월드 삼각형, 씬의 프리팹 연결을 검증했습니다. 결과는 `Md/CrosswalkPrefabUnityQA.json`에 있습니다.

과거 `CrosswalkPlacements.json`은 원본 위치 기록입니다. 이후 Unity에서 직접 편집한 위치는 프리팹 또는 씬에 저장되며, 이 JSON에는 자동으로 반영되지 않습니다.
