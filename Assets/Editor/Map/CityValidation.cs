using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>저장된 고정 맵의 차선과 보도 및 후보 참조를 검증한다</summary>
public static partial class CityValidation
{
    private static int checks;

    // 생성 없이 고정 World를 읽어 연결 데이터와 초기화 반복 안전성을 검증한다
    [MenuItem("CodeBlue Rush/City/Validate Fixed Map")]
    public static void Run()
    {
        checks = 0;
        GameObject root = new GameObject("Fixed Map Validation");
        try
        {
            CityMap map = root.AddComponent<CityMap>();
            FixedMapScene.AttachWorld(map);
            Check(map.Initialize(out string error) && map.Validate(out error), error);
            RoadChunk first = map.GetRoad(0, 0);
            Check(map.Initialize(out _) && map.GetRoad(0, 0) == first, "중복 초기화는 도로를 보존한다");
            Check(map.IncidentSlotCount > 1 && map.SidewalkPathCount > 0, "환자 후보와 보도 경로 유지");
            map.enabled = false;
            map.enabled = true;
            Check(map.Validate(out error) && map.GetRoad(0, 0) == first, error);
            Debug.Log("Fixed map validation passed: " + checks);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 검증 실패를 실행 호출자에게 전달한다
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
}
