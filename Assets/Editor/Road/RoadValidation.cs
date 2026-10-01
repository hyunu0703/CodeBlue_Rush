using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>도로 예제의 기하와 연결 실패 및 해제 동작을 실제 Unity 객체로 검증한다</summary>
public static class RoadValidation
{
    // 기존 씬을 수정하지 않고 독립 예제 인스턴스로 회귀 검증을 실행한다
    [MenuItem("CodeBlue Rush/Road/Validate Samples")]
    public static void Run()
    {
        RoadSamples.CreateSamples();
        List<GameObject> instances = new List<GameObject>();
        int checks = 0;
        try
        {
            foreach (int count in new[] { 2, 4 })
            {
                RoadChunk a = Spawn("Straight" + count + "Lane", instances);
                RoadChunk b = Spawn("Corner" + count + "Lane", instances);
                RoadChunk c = Spawn("Straight" + count + "Lane", instances);
                b.transform.position = new Vector3(0f, 20f, 0f);
                c.transform.SetPositionAndRotation(new Vector3(10f, 30f, 0f), Quaternion.Euler(0f, 0f, -90f));
                Check(a.Validate(out _) && b.Validate(out _) && c.Validate(out _), "Prefab 구성", ref checks);
                Check(a.LaneCount == count && b.LaneCount == count, "차선 수", ref checks);
                Check(Mathf.Abs(a.GetLane(0).Length - 20f) < 0.001f, "직선 길이", ref checks);
                Check(a.GetLane(0).TrySample(7f, out Vector3 point, out Vector3 direction) && Vector3.Distance(point, new Vector3(1f, 7f, 0f)) < 0.001f && direction == Vector3.up, "거리 표본", ref checks);
                Check(b.GetLane(0).TrySample(b.GetLane(0).Length * 0.5f, out point, out direction) && direction.x > 0f && direction.y > 0f, "코너 진행 방향", ref checks);
                Check(b.GetLane(count / 2).StartDirection.x < -0.9f, "반대편 차선 방향", ref checks);
                Check(a.GetConnection(1).TryConnect(b.GetConnection(0), out _), "직선에서 코너 연결", ref checks);
                Check(b.GetConnection(1).TryConnect(c.GetConnection(0), out _), "코너에서 회전 직선 연결", ref checks);
                Check(a.GetConnection(1).TryConnect(b.GetConnection(0), out _), "중복 연결 안정성", ref checks);
                for (int i = 0; i < count / 2; i++)
                {
                    Check(a.GetLane(i).NextLane == b.GetLane(i) && b.GetLane(i).NextLane == c.GetLane(i), "정방향 연결", ref checks);
                    Check(c.GetLane(i + count / 2).NextLane == b.GetLane(i + count / 2) && b.GetLane(i + count / 2).NextLane == a.GetLane(i + count / 2), "역방향 연결", ref checks);
                }
                Check(c.GetLane(0).NextLane == null, "열린 끝점", ref checks);
                Check(!a.GetConnection(1).TryConnect(c.GetConnection(0), out _) && a.GetLane(0).NextLane == b.GetLane(0), "점유 연결 보존", ref checks);
                a.GetConnection(1).Disconnect();
                Check(a.GetLane(0).NextLane == null && b.GetLane(count / 2).NextLane == null && !b.GetConnection(0).ConnectedTo, "양방향 해제", ref checks);
                Check(b.GetLane(0).NextLane == c.GetLane(0), "다른 끝 연결 보존", ref checks);
                b.transform.position += Vector3.right;
                Check(!a.GetConnection(1).TryConnect(b.GetConnection(0), out _) && !a.GetLane(0).NextLane, "벌어진 끝점 거절", ref checks);
                b.GetConnection(1).Disconnect();
                b.transform.position -= Vector3.right;
                b.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
                Check(!a.GetConnection(1).TryConnect(b.GetConnection(0), out _), "잘못된 방향 거절", ref checks);
                Check(!a.GetConnection(0).TryConnect(null, out _) && !a.GetConnection(0).TryConnect(a.GetConnection(0), out _), "null과 자기 연결 거절", ref checks);
                Check(a.GetLane(0).TrySample(-5f, out point, out _) && point == a.GetLane(0).StartPoint, "음수 거리 제한", ref checks);
                Check(a.GetLane(0).TrySample(100f, out point, out _) && point == a.GetLane(0).EndPoint, "끝 거리 제한", ref checks);
                Check(!a.GetLane(0).TrySample(float.NaN, out _, out _), "NaN 거절", ref checks);
                a.transform.localScale = new Vector3(2f, 2f, 1f);
                a.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                Check(Mathf.Abs(a.GetLane(0).Length - 40f) < 0.001f && a.GetLane(0).TrySample(20f, out _, out direction) && Vector3.Dot(direction, Vector3.left) > 0.999f, "Transform 변경 캐시", ref checks);
                if (count == 4)
                    Check(a.GetLane(0).RightLane == a.GetLane(1) && a.GetLane(1).LeftLane == a.GetLane(0) && !a.GetLane(0).LeftLane && a.GetLane(2).RightLane == a.GetLane(3), "진행 기준 인접 참조", ref checks);
            }
            ValidateFailures(instances, ref checks);
            Debug.Log("Road validation passed: " + checks + " checks");
        }
        finally
        {
            foreach (GameObject instance in instances)
                Object.DestroyImmediate(instance);
        }
    }

    // 잘못된 차선 구성과 실패 시 부분 연결 방지를 검사한다
    private static void ValidateFailures(List<GameObject> instances, ref int checks)
    {
        RoadChunk two = Spawn("Straight2Lane", instances);
        RoadChunk four = Spawn("Straight4Lane", instances);
        four.transform.position = new Vector3(0f, 20f, 0f);
        Check(!two.GetConnection(1).TryConnect(four.GetConnection(0), out _) && !two.GetLane(0).NextLane && !four.GetLane(2).NextLane, "차선 수 불일치 원자성", ref checks);
        RoadChunk other = Spawn("Straight4Lane", instances);
        Check(other.GetLane(0).RightLane == other.GetLane(1) && other.GetLane(0).RightLane != four.GetLane(1), "Prefab 인스턴스 참조 분리", ref checks);
        other.transform.position = new Vector3(0f, 40f, 0f);
        SerializedObject lane = new SerializedObject(other.GetLane(1));
        SerializedProperty points = lane.FindProperty("points");
        points.GetArrayElementAtIndex(0).vector2Value += Vector2.right;
        lane.ApplyModifiedPropertiesWithoutUndo();
        Check(!four.GetConnection(1).TryConnect(other.GetConnection(0), out _) && !four.GetLane(0).NextLane, "일부 끝점 불일치 원자성", ref checks);
        points.GetArrayElementAtIndex(1).vector2Value = points.GetArrayElementAtIndex(0).vector2Value;
        lane.ApplyModifiedPropertiesWithoutUndo();
        Check(!other.GetLane(1).TrySample(0f, out _, out _) && !other.Validate(out _), "길이 0 경로 거절", ref checks);
        points.arraySize = 0;
        lane.ApplyModifiedPropertiesWithoutUndo();
        Check(!other.GetLane(1).TrySample(0f, out _, out _), "빈 경로 거절", ref checks);
        SerializedObject port = new SerializedObject(two.GetConnection(0));
        port.FindProperty("incoming").GetArrayElementAtIndex(0).objectReferenceValue = null;
        port.ApplyModifiedPropertiesWithoutUndo();
        Check(!two.Validate(out _) && !two.GetConnection(0).TryConnect(four.GetConnection(1), out _), "누락 차선 거절", ref checks);
    }

    // 검증 전용 Prefab 복제본을 추적한다
    private static RoadChunk Spawn(string name, List<GameObject> instances)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Road/" + name + ".prefab");
        GameObject instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        instances.Add(instance);
        return instance.GetComponent<RoadChunk>();
    }

    // 실패한 검증을 배치 실행에도 전달한다
    private static void Check(bool result, string label, ref int checks)
    {
        if (!result)
            throw new InvalidOperationException("Road validation failed: " + label);
        checks++;
    }
}
