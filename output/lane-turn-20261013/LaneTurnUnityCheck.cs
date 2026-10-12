#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>실제 Unity Prefab을 읽어 추가 우회전 경로를 검증한다. 씬과 에셋은 수정하지 않는다.</summary>
public static class LaneTurnUnityCheck
{
    /// <summary>두 대상 프리팹의 2차로 연결과 네 방향 회전 배치 결과를 검사하고 JSON을 출력한다.</summary>
    public static void Verify()
    {
        // 조건 수는 이번 검증 실행에서만 보관한다.
        var assertions = 0;
        foreach (var name in new[] { "Road_T_6Lane", "Road_T_V6Lane_H4Lane" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Road/" + name + ".prefab");
            Require(asset != null, name + ": Prefab 누락", ref assertions);
            var road = asset.GetComponent<RoadPrefab>();
            Require(road != null && road.TryGetLaneLayout(out _, out _), name + ": 레이아웃 생성 실패", ref assertions);
            road.TryGetLaneLayout(out var layout, out _);
            for (var turns = 0; turns < 4; turns++)
            {
                // 원본의 남쪽 진입과 동쪽 출구를 같은 시계방향 90도 회전으로 배치한다.
                var entry = (RoadDirection)((2 + turns) % 4);
                var exit = (RoadDirection)((1 + turns) % 4);
                var offset = exit == RoadDirection.North ? Vector2.up : exit == RoadDirection.East
                    ? Vector2.right : exit == RoadDirection.South ? Vector2.down : Vector2.left;
                var straightName = name == "Road_T_6Lane" ? "Road_Straight_6Lane" : "Road_Straight_4Lane";
                var straightAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Road/" + straightName + ".prefab");
                Require(straightAsset != null, "출구 직선 Prefab 누락", ref assertions);
                var straight = straightAsset.GetComponent<RoadPrefab>();
                Require(straight != null && straight.TryGetLaneLayout(out _, out _), "출구 레이아웃 누락", ref assertions);
                straight.TryGetLaneLayout(out var exitLayout, out _);
                Require(RoadLaneNetwork.TryBuild(new[] { new RoadLanePlacement(layout, Vector2.zero, turns),
                    new RoadLanePlacement(exitLayout, offset * road.ImageSize.x, (int)exit % 2) },
                    out var graph, out var error), name + ": " + error, ref assertions);
                var incoming = graph.Paths.Single(p => p.RoadOrdinal == 0 && p.Kind == RoadLanePathKind.Approach
                    && p.Entry == entry && p.EntryLaneNumber == 2);
                var movement = graph.Paths.Single(p => p.RoadOrdinal == 0 && p.Kind == RoadLanePathKind.Intersection
                    && p.Entry == entry && p.EntryLaneNumber == 2);
                Require(incoming.Successors.Contains(movement.Id) && movement.Exit == exit && movement.ExitLaneNumber == 2
                    && movement.Maneuver == RoadLaneManeuver.Right, "2차로 우회전 누락", ref assertions);
                var departure = graph.Paths[movement.Successors.Single()];
                Require(departure.Kind == RoadLanePathKind.Departure && departure.ExitLaneNumber == 2,
                    "출구 2차로 연결 실패", ref assertions);
                var continuation = graph.Paths[departure.Successors.Single()];
                Require(continuation.RoadOrdinal == 1 && continuation.EntryLaneNumber == 2,
                    "다음 직선 도로 2차로 연결 실패", ref assertions);
                Require(Vector2.Distance(movement.Points[movement.Points.Count - 1], departure.Points[0]) < 0.001f
                    && Vector2.Distance(departure.Points[departure.Points.Count - 1], continuation.Points[0]) < 0.001f,
                    "경로 위치 단절", ref assertions);
            }
        }
        // 출력 경로는 작업 소유 프로젝트의 출력 폴더다. Unity 에셋에는 결과를 저장하지 않는다.
        var resultPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "output/lane-turn-20261013/unity-verification.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        File.WriteAllText(resultPath, JsonUtility.ToJson(new Result { Assertions = assertions, Prefabs = 2, Rotations = 8, Passed = true }, true));
        Debug.Log($"차로 추가 검증 PASS: {assertions}개 조건, 2개 Prefab, 8개 회전 배치");
    }

    /// <summary>검증 실패 시 즉시 원인과 함께 종료한다.</summary>
    private static void Require(bool condition, string error, ref int assertions)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(error);
        }
    }

    // 검증 보고서의 순수 데이터다.
    [Serializable]
    private sealed class Result
    {
        public int Assertions;
        public int Prefabs;
        public int Rotations;
        public bool Passed;
    }
}
#endif
