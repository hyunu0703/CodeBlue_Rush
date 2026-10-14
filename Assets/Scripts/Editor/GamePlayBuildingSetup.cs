#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>GamePlay 미션 생성기에 지정된 건물 16종만 연결한다.</summary>
public static class GamePlayBuildingSetup
{
    /// <summary>씬의 건물 목록을 Undo 가능한 방식으로 연결하고 지정된 GamePlay 씬만 저장한다.</summary>
    [MenuItem("Tools/CodeBlue/Setup GamePlay Buildings")]
    public static void ConfigureOpenScene()
    {
        // 사용자 미저장 편집이 있으면 자동 저장하지 않는다.
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/GamePlay.unity" || scene.isDirty)
        {
            throw new InvalidOperationException("Play Mode 밖에서 GamePlay 씬의 기존 편집을 저장한 뒤 실행해주세요.");
        }
        var generator = UnityEngine.Object.FindFirstObjectByType<MissionMapGenerator>();
        if (generator == null || generator.gameObject.scene != scene)
        {
            throw new InvalidOperationException("GamePlay 씬의 MissionMapGenerator가 필요합니다.");
        }
        // 기존 프리팹의 Index와 컴포넌트는 변경하지 않고 참조만 연결한다.
        var prefabs = new BuildingPrefab[16];
        for (var index = 0; index < prefabs.Length; index++)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Building/Building_" + (index + 1).ToString("00") + ".prefab");
            if (root == null || !root.TryGetComponent(out prefabs[index]))
            {
                throw new InvalidOperationException("BuildingPrefab이 붙은 건물 16종이 필요합니다.");
            }
        }
        if (!MissionBuildingLayout.Validate(prefabs, out var error))
        {
            throw new InvalidOperationException(error);
        }
        var serialized = new SerializedObject(generator);
        var array = serialized.FindProperty("buildingPrefabs");
        array.arraySize = prefabs.Length;
        for (var index = 0; index < prefabs.Length; index++)
        {
            array.GetArrayElementAtIndex(index).objectReferenceValue = prefabs[index];
        }
        serialized.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("GamePlay 씬 저장에 실패했습니다.");
        }
        Debug.Log("GAMEPLAY_BUILDINGS_CONFIGURED count=16", generator);
    }
}
#endif
