using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>생성된 실제 도시에서 방향 경로와 실패 처리 및 결과 보호를 검증한다</summary>
public static class NavigationValidation
{
    private static int checks;
    private const string PlayKey = "CodeBlueRush.NavigationPlayValidation";
    private static NavigationRoute playRoute;
    private static TrafficLane disabledLane;
    private static int playStep;
    private static int playFrame;
    private static int changes;
    private static double deadline;
    private static bool playError;

    // 기존 도로 및 도시 회귀 검증과 네비게이션 검증을 실행한다
    [MenuItem("CodeBlue Rush/Navigation/Validate Routes")]
    public static void Run()
    {
        checks = 0;
        CityValidation.Run();
        GameObject owner = new GameObject("Navigation Validation");
        GameObject source = new GameObject("Navigation Source");
        owner.hideFlags = HideFlags.HideAndDontSave;
        source.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            CityMap map = owner.AddComponent<CityMap>();
            CitySamples.Configure(map);
            NavigationRoute route = source.AddComponent<NavigationRoute>();
            route.Configure(map, source.transform);
            Check(!route.Recalculate() && route.Status == NavigationRoute.RouteStatus.NoDestination, "목적지 없음");
            Check(!route.SetDestination(Vector3.zero) && route.Status == NavigationRoute.RouteStatus.MapUnavailable, "생성 전 요청");
            for (int seed = -3; seed <= 3; seed++)
            {
                Check(map.TryStartNewCity(seed, out string error), "Seed 생성: " + error);
                TrafficLane start = map.GetLane(0);
                start.TrySample(start.Length * 0.4f, out Vector3 position, out _);
                source.transform.position = position;
                RoadChunk first = map.GetRoad(0, 0);
                for (int target = 0; target < map.LaneCount; target += Math.Max(1, map.LaneCount / 12))
                {
                    TrafficLane end = map.GetLane(target);
                    Check(route.SetDestination(end, end.Length * 0.6f), "랜덤 도시 목적지 경로");
                    VerifyPath(route, map);
                    Check(map.GetRoad(0, 0) == first && map.Seed == seed, "목적지 변경 시 도시 보존");
                }
                Check(route.SetDestination(position) && route.Lanes.Count == 1 && route.Points.Count == 1, "현재 위치와 같은 목적지");
                Check(route.SetDestination(start, start.Length * 0.2f) && route.Lanes.Count > 1, "뒤쪽 목적지는 실제 순환 경로");
                VerifyPath(route, map);
                Check(route.Lanes[0] == start && route.Lanes[route.Lanes.Count - 1] == start, "같은 차선 재진입");
            }

            TrafficLane origin = map.GetLane(0);
            origin.TrySample(origin.Length * 0.4f, out Vector3 from, out _);
            source.transform.position = from;
            TrafficLane destination = map.GetLane(map.LaneCount - 1);
            Check(route.SetDestination(destination, destination.Length * 0.5f), "검증 경로 준비");
            int notifications = 0;
            route.RouteChanged += () => notifications++;
            Check(route.ValidateRoute() && notifications == 0, "유효 경로 검사 시 재탐색 없음");
            IList<Vector3> readOnly = (IList<Vector3>)route.Points;
            bool protectedData = false;
            try { readOnly.Add(Vector3.zero); }
            catch (NotSupportedException) { protectedData = true; }
            Check(protectedData, "외부 경로 좌표 수정 차단");
            Check(((IList<TrafficLane>)route.Lanes).IsReadOnly, "외부 차선 순서 수정 차단");
            Check(!route.SetDestination(new Vector3(float.NaN, 0f)) && route.Status == NavigationRoute.RouteStatus.InvalidDestination && readOnly.Count == 0, "NaN 및 이전 결과 제거");
            Check(!route.SetDestination(new Vector3(100000f, 100000f)) && route.Points.Count == 0, "맵 외부 목적지");
            Check(!route.SetDestination(null, 0f) && route.Status == NavigationRoute.RouteStatus.InvalidDestination, "null 목적지 차선");
            Check(!route.SetDestination(destination, -1f), "음수 목적지 거리");
            Check(!route.SetDestination(destination, float.PositiveInfinity), "무한 목적지 거리");
            source.transform.position = new Vector3(-10000f, 0f);
            Check(!route.SetDestination(destination, 0f) && route.Status == NavigationRoute.RouteStatus.InvalidStart, "잘못된 플레이어 위치");
            source.transform.position = from;
            Check(route.SetDestination(destination, 0f), "실패 후 복구");
            destination.enabled = false;
            Check(!route.ValidateRoute() && route.Points.Count == 0, "Edit 모드 비활성 차선 명시적 검사");
            Check(!route.Recalculate() && route.Status == NavigationRoute.RouteStatus.InvalidDestination, "비활성 목적지 거절");
            destination.enabled = true;
            Check(route.Recalculate(), "명시적 재탐색");
            route.ClearDestination();
            Check(!route.HasDestination && route.Lanes.Count == 0 && route.Points.Count == 0, "목적지 제거");

            // 출발 도로의 모든 외부 연결을 끊어 실제 도달 불가능 상태를 만든다
            Check(route.SetDestination(destination, 0f), "단절 전 경로");
            RoadChunk isolated = map.GetRoad(0, 0);
            for (int i = 0; i < isolated.ConnectionCount; i++)
                isolated.GetConnection(i).Disconnect();
            Check(route.Status == NavigationRoute.RouteStatus.Invalidated && route.Lanes.Count == 0, "연결 해제 즉시 경로 무효화");
            Check(!route.Recalculate() && route.Status == NavigationRoute.RouteStatus.NoPath && route.Points.Count == 0, "단절 경로 안전 실패");
            Check(!route.SetDestination(origin, origin.Length * 0.2f), "순환 없는 일방향 차선 역주행 차단");
            Check(route.SetDestination(origin, origin.Length * 0.8f) && route.Lanes.Count == 1, "단절 차선 내부 정방향 허용");
            Check(map.TryStartNewCity(45, out _), "도시 교체");
            Check(route.Status == NavigationRoute.RouteStatus.InvalidDestination && route.Lanes.Count == 0, "이전 도시 차선 목적지 거절");
            origin = map.GetLane(0);
            origin.TrySample(origin.Length * 0.5f, out from, out _);
            source.transform.position = from;
            Check(route.SetDestination(from), "새 도시 위치 목적지");
            map.enabled = false;
            map.Validate(out _);
            Check(route.Status == NavigationRoute.RouteStatus.MapUnavailable && route.Points.Count == 0, "도시 비활성 결과 제거");
            Debug.Log("Navigation validation passed: " + checks + " checks");
        }
        finally
        {
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(owner);
        }
    }

    // 결과의 모든 간선과 잘린 경로 양 끝을 실제 차선 데이터와 대조한다
    private static void VerifyPath(NavigationRoute route, CityMap map)
    {
        Check(route.Lanes.Count > 0 && route.Points.Count > 0, "빈 성공 경로 방지");
        for (int i = 0; i < route.Lanes.Count; i++)
        {
            TrafficLane lane = route.Lanes[i];
            Check(map.ContainsLane(lane), "현재 도시 소유 차선");
            if (i == route.Lanes.Count - 1)
                continue;
            bool found = false;
            for (int n = 0; n < lane.NextCount; n++)
                found |= lane.GetNext(n) == route.Lanes[i + 1];
            Check(found, "실제 정방향 간선");
            Check(Vector3.Distance(lane.EndPoint, route.Lanes[i + 1].StartPoint) <= 0.05f, "연속된 경로 기하");
        }
        route.Lanes[0].TrySample(route.StartDistance, out Vector3 first, out _);
        route.Lanes[route.Lanes.Count - 1].TrySample(route.EndDistance, out Vector3 last, out _);
        Check(Vector3.Distance(first, route.Points[0]) < 0.001f && Vector3.Distance(last, route.Points[route.Points.Count - 1]) < 0.001f, "시작 및 목적지로 자른 좌표");
    }

    // 실패한 조건을 에디터와 배치 실행에 전달한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Navigation validation failed: " + label);
        checks++;
    }

    // 별도 데모 씬에서 자동 무효화 재탐색과 UI를 실제 프레임에 걸쳐 검증한다
    [MenuItem("CodeBlue Rush/Navigation/Validate Play Lifecycle")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이 모드를 종료한 후 실행하세요.");
        NavigationEditor.CreateDemo();
        NavigationRoute route = Object.FindFirstObjectByType<NavigationRoute>();
        if (!route)
            return;
        if (Application.isBatchMode)
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(route.gameObject.scene, "Assets/Scenes/NavigationDemo.unity");
        SessionState.SetBool(PlayKey, true);
        RegisterPlay();
        EditorApplication.EnterPlaymode();
    }

    // 도메인 재로드 후 진행 중인 플레이 검증을 복원한다
    [InitializeOnLoadMethod]
    private static void RegisterPlay()
    {
        if (!SessionState.GetBool(PlayKey, false))
            return;
        playStep = 0;
        playFrame = -1;
        checks = 0;
        deadline = EditorApplication.timeSinceStartup + 120d;
        playError = false;
        Application.logMessageReceived -= ObserveLog;
        Application.logMessageReceived += ObserveLog;
        EditorApplication.update -= CheckPlay;
        EditorApplication.update += CheckPlay;
    }

    // 재탐색 예약과 도시 재활성화를 프레임 경계 이후 확인한다
    private static void CheckPlay()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("네비게이션 플레이 검증 시간 초과");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || Time.frameCount < playFrame + 3)
                return;
            playFrame = Time.frameCount;
            switch (playStep++)
            {
                case 0:
                    playRoute = Object.FindFirstObjectByType<NavigationRoute>();
                    NavigationEditor.Preview(playRoute);
                    Check(playRoute.Status == NavigationRoute.RouteStatus.Ready, "Play 경로 계산");
                    CaptureUI();
                    disabledLane = playRoute.Lanes[1];
                    disabledLane.enabled = false;
                    Check(playRoute.Points.Count == 0, "변경 프레임에서 이전 경로 제거");
                    break;
                case 1:
                    Check(playRoute.Status == NavigationRoute.RouteStatus.Ready || playRoute.Status == NavigationRoute.RouteStatus.NoPath, "다음 프레임 자동 재탐색 완료");
                    foreach (TrafficLane lane in playRoute.Lanes)
                        Check(lane != disabledLane, "비활성 차선 우회");
                    disabledLane.enabled = true;
                    Check(playRoute.Recalculate(), "명시적 복구");
                    playRoute.Map.gameObject.SetActive(false);
                    Check(playRoute.Points.Count == 0, "도시 비활성 경로 제거");
                    break;
                case 2:
                    changes = 0;
                    playRoute.RouteChanged += CountChanges;
                    playRoute.Map.gameObject.SetActive(true);
                    break;
                case 3:
                    Check(playRoute.Map.IsReady && playRoute.Status == NavigationRoute.RouteStatus.Ready, "동일 도시 재연결 후 경로 복구");
                    Check(changes == 1, "재연결 시 성공 경로 중복 계산 방지");
                    changes = 0;
                    break;
                case 4:
                    Check(changes == 0, "프레임 경과만으로 재탐색하지 않음");
                    playRoute.RouteChanged -= CountChanges;
                    disabledLane = playRoute.Lanes[1];
                    disabledLane.enabled = false;
                    playRoute.ClearDestination();
                    break;
                case 5:
                    Check(!playRoute.HasDestination && playRoute.Status == NavigationRoute.RouteStatus.NoDestination && playRoute.Points.Count == 0, "목적지 제거 시 예약된 재탐색 취소");
                    Check(!playError, "Play 검증 중 오류 로그 없음");
                    Debug.Log("Navigation play validation passed: " + checks + " checks");
                    FinishPlay(true);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            FinishPlay(false);
        }
    }

    // 안정된 프레임 동안 결과 변경 횟수를 기록한다
    private static void CountChanges()
    {
        if (playRoute.Status == NavigationRoute.RouteStatus.Ready)
            changes++;
    }

    // 조건 검사 이외의 Unity 런타임 오류도 검증 실패로 기록한다
    private static void ObserveLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            playError = true;
    }

    // 세로 해상도에서 Canvas와 실제 경로를 렌더링하여 검토 이미지를 저장한다
    private static void CaptureUI()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        Camera camera = Camera.main;
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        RenderTexture texture = new RenderTexture(480, 854, 24);
        Texture2D image = new Texture2D(480, 854, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            NavigationUI ui = Object.FindFirstObjectByType<NavigationUI>();
            Mesh mesh = ui.canvasRenderer.GetMesh();
            Check(mesh && mesh.vertexCount > 8, "UI 경로선과 두 표식 메시");
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 480f, 854f), 0, 0);
            image.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/navigation-480x854.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(texture);
        }
    }

    // 검증 구독과 상태를 정리하고 배치 또는 플레이 모드를 종료한다
    private static void FinishPlay(bool success)
    {
        SessionState.SetBool(PlayKey, false);
        EditorApplication.update -= CheckPlay;
        Application.logMessageReceived -= ObserveLog;
        if (playRoute)
            playRoute.RouteChanged -= CountChanges;
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 1);
        else
            EditorApplication.ExitPlaymode();
    }
}
