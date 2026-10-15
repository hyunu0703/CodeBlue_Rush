#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>생성 대상 차량에만 기존 충돌 영역과 같은 콜라이더를 저장한다.</summary>
public static class GamePlayTrafficColliderSetup
{
    private static readonly string[] PrefabNames =
    {
        "Avante", "Grandeur", "SonataTaxi", "Ray", "Sorento", "Sportage", "Carnival", "PorterII", "Bus"
    };

    /// <summary>기존 콜라이더와 사용자 튜닝을 보존하고 누락된 루트 콜라이더만 추가한다.</summary>
    [MenuItem("Tools/CodeBlue/Add Missing Traffic Prefab Colliders")]
    public static void AddMissingColliders()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Play Mode 밖에서 차량 프리팹 콜라이더를 설정해주세요.");
        }
        var added = 0;
        foreach (var name in PrefabNames)
        {
            var path = "Assets/Prefabs/Car/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<BoxCollider2D>() != null)
                {
                    continue;
                }
                var renderer = root.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer == null || renderer.sprite == null
                    || root.GetComponentsInChildren<Collider2D>(true).Length != 0
                    || root.GetComponentsInChildren<Rigidbody2D>(true).Length != 0
                    || root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                {
                    throw new InvalidOperationException(path + ": 기존 외형/물리 구성을 확인해주세요. 자동으로 덮어쓰지 않습니다.");
                }
                // 기존 런타임 계산을 한 번만 적용한다. 이후 Size와 Offset은 프리팹 Inspector가 소유한다.
                var visual = renderer.sprite.bounds;
                var bounds = new Bounds(root.transform.InverseTransformPoint(renderer.transform.TransformPoint(visual.min)), Vector3.zero);
                bounds.Encapsulate(root.transform.InverseTransformPoint(renderer.transform.TransformPoint(visual.max)));
                bounds.Encapsulate(root.transform.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(visual.min.x, visual.max.y))));
                bounds.Encapsulate(root.transform.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(visual.max.x, visual.min.y))));
                var collider = Undo.AddComponent<BoxCollider2D>(root);
                collider.offset = bounds.center;
                collider.size = (Vector2)bounds.size * 0.88f;
                collider.isTrigger = false;
                PrefabUtility.SaveAsPrefabAsset(root, path, out var success);
                if (!success)
                {
                    throw new InvalidOperationException(path + ": 프리팹 저장에 실패했습니다.");
                }
                added++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        Debug.Log($"TRAFFIC_PREFAB_COLLIDERS_ADDED count={added}");
    }
}
#endif
