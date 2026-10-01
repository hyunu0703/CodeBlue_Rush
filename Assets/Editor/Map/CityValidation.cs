using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Seed 재현성과 도시 연결 및 교체 수명을 실제 Unity 객체로 검증한다</summary>
public static class CityValidation
{
    private static int checks;
    private const string PlayKey = "CodeBlueRush.CityPlayValidation";
    private static int playStep;
    private static int playFrame;
    private static double deadline;
    private static CityMap playMap;
    private static RoadChunk oldRoad;
    private static int originalSeed;

    // 2단계 회귀와 다중 Seed 및 생성 실패의 원자성을 검증한다
    [MenuItem("CodeBlue Rush/City/Validate Generation")]
    public static void Run()
    {
        checks = 0;
        RoadValidation.Run();
        CitySamples.CreatePrefabs();
        ValidateLayouts();
        GameObject owner = new GameObject("City Validation");
        GameObject replica = new GameObject("City Replica");
        owner.hideFlags = HideFlags.HideAndDontSave;
        replica.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            CityMap map = owner.AddComponent<CityMap>();
            CitySamples.Configure(map);
            CityMap copy = replica.AddComponent<CityMap>();
            CitySamples.Configure(copy);
            int notifications = 0;
            map.Generated += _ => notifications++;
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            string beforeRandom = JsonUtility.ToJson(randomState);
            Check(map.EnsureGenerated(out string error), "초기 생성: " + error);
            Check(copy.EnsureGenerated(out error), "복제 생성: " + error);
            Check(Signature(map) == Signature(copy), "동일 Seed 인스턴스 재현");
            RoadChunk first = map.GetRoad(0, 0);
            Check(map.EnsureGenerated(out _) && map.GetRoad(0, 0) == first && notifications == 1, "상황보고 중복 생성 방지");
            Check(!map.TryStartNewCity(map.Seed, out _) && map.GetRoad(0, 0) == first, "같은 Seed 교체 거절");
            Check(!map.GetRoad(-1, 0) && !map.GetLane(-1) && !map.GetLane(map.LaneCount), "조회 경계");
            Check(JsonUtility.ToJson(UnityEngine.Random.state) == beforeRandom, "Unity Random 상태 보존");
            UnityEngine.Random.state = randomState;
            HashSet<string> signatures = new HashSet<string>();
            for (int seed = -16; seed < 16; seed++)
            {
                Check(map.TryStartNewCity(seed, out error), "Seed " + seed + " 생성: " + error);
                Check(map.Validate(out error), "Seed " + seed + " 연결: " + error);
                signatures.Add(Signature(map));
            }
            Check(signatures.Count > 1, "다른 Seed의 구조 변화");
            Check(map.TryStartNewCity(int.MinValue, out error) && map.TryStartNewCity(int.MaxValue, out error), "Seed 정수 경계: " + error);
            Check(map.CreateSeed() != map.Seed, "새 Seed 발급");
            first = map.GetRoad(0, 0);
            int oldSeed = map.Seed;
            int oldNotifications = notifications;
            SerializedObject settings = new SerializedObject(map);
            settings.FindProperty("cellSize").floatValue = 21f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(!map.TryStartNewCity(72, out _) && map.IsReady && map.Seed == oldSeed && map.GetRoad(0, 0) == first && notifications == oldNotifications, "규격 오류 시 기존 도시 보존");
            settings.FindProperty("cellSize").floatValue = 20f;
            settings.FindProperty("intersection").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(!map.TryStartNewCity(73, out _) && map.GetRoad(0, 0) == first, "누락 Prefab 거절");
            CitySamples.Configure(map);
            ValidateRollback(map);
            settings.Update();
            settings.FindProperty("width").intValue = 4;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(!map.TryStartNewCity(74, out _) && map.GetRoad(0, 0) == first, "잘못된 크기 거절");
            settings.FindProperty("width").intValue = 5;
            settings.FindProperty("height").intValue = 5;
            settings.FindProperty("density").intValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(map.TryStartNewCity(75, out error) && map.Validate(out error), "최소 크기와 밀도: " + error);
            settings.FindProperty("width").intValue = 24;
            settings.FindProperty("height").intValue = 24;
            settings.FindProperty("density").intValue = 100;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(map.TryStartNewCity(76, out error) && map.Validate(out error), "최대 크기와 밀도: " + error);
            Check(map.transform.childCount == 1, "이전 도시 해제");
            settings.FindProperty("width").intValue = 5;
            settings.FindProperty("height").intValue = 7;
            settings.ApplyModifiedPropertiesWithoutUndo();
            owner.transform.SetPositionAndRotation(new Vector3(-50f, 20f, 0f), Quaternion.Euler(0f, 0f, 90f));
            Check(map.TryStartNewCity(77, out error) && map.Validate(out error), "도시 루트 이동 회전: " + error);
            ValidateBranches(map);
            map.GetRoad(0, 0).GetConnection(0).Disconnect();
            Check(!map.Validate(out _), "끊어진 도시 검출");
            Debug.Log("City generation validation passed: " + checks + " checks (plus 53 road checks)");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(replica);
        }
    }

    // 배치 후 접속 실패가 발생해도 기존 도시와 객체 수가 보존되는지 검사한다
    private static void ValidateRollback(CityMap map)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(CitySamples.Folder + "/Straight.prefab");
        GameObject incompatible = Object.Instantiate(source);
        try
        {
            RoadChunk road = incompatible.GetComponent<RoadChunk>();
            for (int i = 0; i < road.LaneCount; i++)
            {
                SerializedObject data = new SerializedObject(road.GetLane(i));
                SerializedProperty points = data.FindProperty("points");
                for (int p = 0; p < points.arraySize; p++)
                {
                    Vector2 point = points.GetArrayElementAtIndex(p).vector2Value;
                    point.x *= 1.5f;
                    points.GetArrayElementAtIndex(p).vector2Value = point;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            Check(road.Validate(out _), "접속 불일치 테스트용 도로 자체의 유효성");
            SerializedObject settings = new SerializedObject(map);
            settings.FindProperty("straight").objectReferenceValue = road;
            settings.ApplyModifiedPropertiesWithoutUndo();
            RoadChunk previous = map.GetRoad(0, 0);
            Check(!map.TryStartNewCity(812, out _) && map.IsReady && map.GetRoad(0, 0) == previous && map.transform.childCount == 1, "접속 실패 시 임시 도시 정리");
        }
        finally
        {
            CitySamples.Configure(map);
            Object.DestroyImmediate(incompatible);
        }
    }

    // 생성 데이터의 외곽 폐쇄와 상대 방향 및 Seed 재현성을 검사한다
    private static void ValidateLayouts()
    {
        for (int seed = -100; seed < 100; seed++)
        {
            CityLayout first = new CityLayout(seed, 9, 11, 35);
            CityLayout second = new CityLayout(seed, 9, 11, 35);
            bool valid = true;
            for (int y = 0; y < first.Height; y++)
            {
                for (int x = 0; x < first.Width; x++)
                {
                    int mask = first.GetMask(x, y);
                    valid &= mask == second.GetMask(x, y);
                    int degree = 0;
                    for (int d = 0; d < 4; d++)
                    {
                        if ((mask & (1 << d)) == 0)
                            continue;
                        degree++;
                        Vector2Int delta = CityLayout.Direction(d);
                        valid &= (first.GetMask(x + delta.x, y + delta.y) & (1 << ((d + 2) % 4))) != 0;
                    }
                    valid &= degree == 0 || degree >= 2;
                }
            }
            Check(valid, "Seed 구조 재현과 외곽 폐쇄 " + seed);
        }
    }

    // 분기에서 임의의 첫 경로를 기본 진행으로 숨기지 않는지 검사한다
    private static void ValidateBranches(CityMap map)
    {
        bool found = false;
        for (int i = 0; i < map.LaneCount; i++)
        {
            TrafficLane lane = map.GetLane(i);
            if (lane.NextCount <= 1)
                continue;
            found = true;
            Check(!lane.NextLane && lane.GetNext(0) && lane.GetNext(1), "분기 조회 호환성");
            break;
        }
        Check(found, "교차로 내부 분기 존재");
    }

    // 실제 배치와 차선 좌표를 문화권과 무관한 문자열로 기록한다
    private static string Signature(CityMap map)
    {
        StringBuilder text = new StringBuilder();
        for (int y = 0; y < map.Layout.Height; y++)
        {
            for (int x = 0; x < map.Layout.Width; x++)
            {
                RoadChunk road = map.GetRoad(x, y);
                text.Append(map.Layout.GetMask(x, y)).Append(':');
                if (road)
                    text.Append(road.name).Append(':');
            }
        }
        for (int i = 0; i < map.LaneCount; i++)
        {
            TrafficLane lane = map.GetLane(i);
            text.Append(lane.name).Append('/').Append(lane.NextCount).Append(':');
            for (int p = 0; p < lane.PointCount; p++)
            {
                Vector3 point = lane.GetWorldPoint(p);
                text.Append(Mathf.RoundToInt(point.x * 1000f)).Append(',').Append(Mathf.RoundToInt(point.y * 1000f)).Append(';');
            }
        }
        return text.ToString();
    }

    // 실패한 검증을 배치 실행의 실패로 전달한다
    private static void Check(bool result, string label)
    {
        if (!result)
            throw new InvalidOperationException("City validation failed: " + label);
        checks++;
    }

    // 배치 실행에서 검증 후 시작용 데모 씬을 저장한다
    public static void RunBatch()
    {
        Run();
        CitySamples.CreateDemo();
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/Scenes/CityDemo.unity");
    }

    // 독립 데모 씬에서 실제 Start와 재활성화 및 새 Seed 교체를 검증한다
    [MenuItem("CodeBlue Rush/City/Validate Play Lifecycle")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이 모드를 종료한 후 실행하세요.");
        if (!CitySamples.CreateDemoMap())
            return;
        SessionState.SetBool(PlayKey, true);
        RegisterPlayCheck();
        EditorApplication.EnterPlaymode();
    }

    // 도메인 재로드 후에도 진행 중인 플레이 검증을 재개한다
    [InitializeOnLoadMethod]
    private static void RegisterPlayCheck()
    {
        if (!SessionState.GetBool(PlayKey, false))
            return;
        playStep = 0;
        playFrame = -1;
        checks = 0;
        deadline = EditorApplication.timeSinceStartup + 120d;
        EditorApplication.update -= CheckPlay;
        EditorApplication.update += CheckPlay;
    }

    // 프레임 경계를 넘어 생성 및 해제 완료 상태를 검사한다
    private static void CheckPlay()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("도시 플레이 검증 시간 초과");
            if (!EditorApplication.isPlaying || Time.frameCount < playFrame + 2 || Time.frameCount < 2)
                return;
            playFrame = Time.frameCount;
            switch (playStep++)
            {
                case 0:
                    playMap = GameObject.Find("CityMap").GetComponent<CityMap>();
                    Check(playMap.IsReady && playMap.Validate(out _), "Start 자동 생성");
                    oldRoad = playMap.GetRoad(0, 0);
                    originalSeed = playMap.Seed;
                    Check(playMap.EnsureGenerated(out _) && playMap.GetRoad(0, 0) == oldRoad, "진행 중 도시 재사용");
                    playMap.gameObject.SetActive(false);
                    Check(!playMap.IsReady && !oldRoad.GetConnection(0).ConnectedTo, "비활성화 연결 해제");
                    break;
                case 1:
                    playMap.gameObject.SetActive(true);
                    break;
                case 2:
                    Check(playMap.IsReady && playMap.Validate(out _) && playMap.GetRoad(0, 0) == oldRoad && playMap.Seed == originalSeed, "재활성화 시 도시 보존 및 재연결");
                    playMap.enabled = false;
                    playMap.enabled = true;
                    break;
                case 3:
                    Check(playMap.IsReady && playMap.Validate(out _), "컴포넌트 재활성화");
                    CaptureDemo();
                    int seed = playMap.CreateSeed();
                    Check(playMap.TryStartNewCity(seed, out _) && playMap.Seed == seed && !oldRoad.gameObject.activeInHierarchy, "새 Seed 교체 및 이전 도시 즉시 비활성화");
                    break;
                case 4:
                    Check(!oldRoad && playMap.transform.childCount == 1 && playMap.Validate(out _), "프레임 이후 이전 도시 해제");
                    oldRoad = playMap.GetRoad(0, 0);
                    Object.Destroy(playMap);
                    break;
                case 5:
                    Check(!oldRoad, "생성기 제거 시 소유 도시 정리");
                    Debug.Log("City play validation passed: " + checks + " checks");
                    FinishPlay(true);
                    break;
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            FinishPlay(false);
        }
    }

    // 세로 화면 도로 배치의 실제 렌더링을 임시 파일로 저장한다
    private static void CaptureDemo()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        Camera camera = Camera.main;
        RenderTexture target = new RenderTexture(480, 854, 24);
        Texture2D image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        Vector3 originalPosition = camera.transform.position;
        float originalSize = camera.orthographicSize;
        try
        {
            camera.targetTexture = target;
            System.IO.Directory.CreateDirectory("Logs");
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    camera.orthographicSize = 25f;
                    camera.transform.position = new Vector3(10f, 10f, -10f);
                }
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 480, 854), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(pass == 0 ? "Logs/city-demo.png" : "Logs/city-detail.png", image.EncodeToPNG());
            }
        }
        finally
        {
            camera.targetTexture = null;
            camera.transform.position = originalPosition;
            camera.orthographicSize = originalSize;
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }

    // 검증 콜백과 상태를 정리하고 실행 환경에 맞게 종료한다
    private static void FinishPlay(bool success)
    {
        SessionState.SetBool(PlayKey, false);
        EditorApplication.update -= CheckPlay;
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 1);
        else
            EditorApplication.ExitPlaymode();
    }
}
