#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>열려 있는 GamePlay 씬에만 교통 AI 참조를 연결한다. 프리팹/설정 에셋을 변경하거나 자동 저장하지 않는다.</summary>
public static class GamePlayTrafficSetup
{
    // 기존 차량 외형 중 플레이어 구급차를 제외한 지정된 프리팹 목록이다.
    private static readonly string[] PrefabNames =
    {
        "Avante", "Grandeur", "SonataTaxi", "Ray", "Sorento", "Sportage", "Carnival", "PorterII", "Bus"
    };

    /// <summary>씬 참조를 검증한 뒤 Undo 가능한 컴포넌트 추가/SerializedObject 쓰기만 수행한다.</summary>
    [MenuItem("Tools/CodeBlue/Setup GamePlay Traffic AI")]
    public static void ConfigureOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("교통 AI 설정은 Play Mode 밖에서 실행해주세요.");
            return;
        }
        var scene = SceneManager.GetActiveScene();
        if (scene.path != GamePlayDrivingSetup.ScenePath)
        {
            Debug.LogError("Assets/Scenes/GamePlay.unity를 열고 교통 AI 설정 메뉴를 실행해주세요.");
            return;
        }
        // 다른 씬에 속한 오브젝트를 탐색하거나 바꾸지 않는다. 지정 씬의 기존 미션 생성기/구급차를 찾는다.
        MissionMapGenerator generator = null;
        Rigidbody2D player = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var candidate = root.GetComponentInChildren<MissionMapGenerator>(true);
            if (candidate != null)
            {
                if (generator != null)
                {
                    Debug.LogError("GamePlay에 MissionMapGenerator가 여러 개 있습니다. 교통 AI의 대상 생성기를 정해주세요.");
                    return;
                }
                generator = candidate;
            }
            if (root.name == "Ambulance")
            {
                player = root.GetComponent<Rigidbody2D>();
            }
        }
        if (generator == null || generator.LaneMapRoot == null || player == null)
        {
            Debug.LogError("GamePlay의 MissionMapGenerator/Map Root와 Ambulance Rigidbody2D를 먼저 구성해주세요.");
            return;
        }
        var prefabs = new List<GameObject>();
        foreach (var name in PrefabNames)
        {
            var path = "Assets/Prefabs/Car/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("교통 AI 외형 프리팹을 찾을 수 없습니다: " + path);
                return;
            }
            prefabs.Add(prefab);
        }
        var controller = generator.GetComponent<RoadTrafficController>();
        if (controller != null)
        {
            // 명시적으로 제외된 PCX 참조만 제거한다. 다른 프리팹 연결, 차량 수, 레이어 튜닝은 보존한다.
            var existing = new SerializedObject(controller);
            var existingPrefabs = existing.FindProperty("vehiclePrefabs");
            for (var index = existingPrefabs.arraySize - 1; index >= 0; index--)
            {
                var prefab = existingPrefabs.GetArrayElementAtIndex(index).objectReferenceValue as GameObject;
                if (prefab != null && prefab.name == "PCX")
                {
                    existingPrefabs.GetArrayElementAtIndex(index).objectReferenceValue = null;
                    existingPrefabs.DeleteArrayElementAtIndex(index);
                }
            }
            if (existing.ApplyModifiedProperties())
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
                EditorSceneManager.MarkSceneDirty(scene);
            }
            Selection.activeObject = controller;
            Debug.Log("기존 교통 AI 설정을 보존하고 PCX 생성 참조를 제외했습니다. 변경된 씬은 저장해주세요.", controller);
            return;
        }
        Undo.SetCurrentGroupName("Setup GamePlay Traffic AI");
        controller = Undo.AddComponent<RoadTrafficController>(generator.gameObject);
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("mapGenerator").objectReferenceValue = generator;
        serialized.FindProperty("player").objectReferenceValue = player;
        var array = serialized.FindProperty("vehiclePrefabs");
        array.arraySize = prefabs.Count;
        for (var index = 0; index < prefabs.Count; index++)
        {
            array.GetArrayElementAtIndex(index).objectReferenceValue = prefabs[index];
        }
        serialized.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeObject = controller;
        Debug.Log("교통 AI 참조를 연결했습니다. GamePlay 씬을 저장한 뒤 미션을 수락하면 차량과 코드 신호가 시작됩니다.", controller);
    }
}
#endif
