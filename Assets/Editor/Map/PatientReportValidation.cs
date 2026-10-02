using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>실제 도시와 네비게이션으로 환자 후보 선택 및 보고 수명을 검증한다</summary>
public static class PatientReportValidation
{
    private static int checks;
    private const string PlayKey = "CodeBlueRush.PatientReportPlayValidation";
    private static PatientReport playReport;
    private static NavigationRoute playRoute;
    private static CityMap playMap;
    private static PatientReport.SpawnPoint oldPoint;
    private static int playStep;
    private static int playFrame;
    private static double deadline;
    private static bool playError;
    private static double playStartTime;

    // 기존 네비게이션 회귀와 보고의 정상 및 실패 경계를 검증한다
    [MenuItem("CodeBlue Rush/Patient Report/Validate Reports")]
    public static void Run()
    {
        NavigationValidation.Run();
        checks = 0;
        GameObject owner = new GameObject("Report Test");
        GameObject replica = new GameObject("Report Replica");
        try
        {
            PatientReport report = owner.AddComponent<PatientReport>();
            Check(!report.TryReport(out _) && report.Status == PatientReport.ReportStatus.NavigationUnavailable, "네비게이션 누락");
            CityMap map = owner.AddComponent<CityMap>();
            CitySamples.Configure(map);
            NavigationRoute route = owner.AddComponent<NavigationRoute>();
            Transform player = new GameObject("Source").transform;
            player.SetParent(owner.transform, false);
            route.Configure(map, player);
            report.Configure(route);
            Check(!report.TryReport(out _) && report.Status == PatientReport.ReportStatus.MapUnavailable, "도시 생성 전 보고");
            Check(map.EnsureGenerated(out _), "도시 생성");
            PlaceSource(map, player, 0.2f);
            CityMap copy = replica.AddComponent<CityMap>();
            CitySamples.Configure(copy);
            Check(copy.EnsureGenerated(out _), "동일 Seed 도시 복제");
            Transform copyPlayer = new GameObject("Copy Source").transform;
            copyPlayer.SetParent(replica.transform, false);
            PlaceSource(copy, copyPlayer, 0.2f);
            NavigationRoute copyRoute = replica.AddComponent<NavigationRoute>();
            copyRoute.Configure(copy, copyPlayer);
            PatientReport copyReport = replica.AddComponent<PatientReport>();
            copyReport.Configure(copyRoute);

            string randomBefore = JsonUtility.ToJson(UnityEngine.Random.state);
            RoadChunk first = map.GetRoad(0, 0);
            int count = report.CandidateCount;
            TrafficLane previous = null;
            for (int i = 0; i < 30; i++)
            {
                Check(report.TryReport(out string error), "상황 보고 " + i + ": " + error);
                Check(copyReport.TryReport(out _), "복제 보고");
                Check(report.IsActive && report.Status == PatientReport.ReportStatus.Active && report.Message.Length > 0, "보고 데이터 활성화");
                Check(report.Patient.TryGetPose(out Vector3 position, out Vector3 direction), "실제 SpawnPoint 위치");
                Check(copyReport.Patient.TryGetPose(out Vector3 copyPosition, out _) && Vector3.Distance(position, copyPosition) < 0.0001f, "같은 Seed와 요청 순서 재현");
                Check(direction.sqrMagnitude > 0.99f && map.ContainsLane(report.Patient.Lane), "환자 위치 방향과 도시 소유권");
                Check(route.Status == NavigationRoute.RouteStatus.Ready && route.DestinationLane == report.Patient.Lane && route.EndDistance == report.Patient.Distance, "4단계 목적지와 경로 연결");
                report.Patient.Lane.TrySample(report.Patient.Distance, out Vector3 access, out _);
                Check(Vector3.Distance(route.Points[route.Points.Count - 1], access) < 0.001f && report.Patient.Slot && Vector3.Distance(position, access) < 2f, "도로변 현장과 네비게이션 접근 도착점");
                Check(previous != report.Patient.Lane, "직전 위치 즉시 반복 방지");
                previous = report.Patient.Lane;
                Check(!report.TryReport(out _) && report.Patient.Lane == previous && report.IsActive, "중복 보고가 기존 미션 보존");
                Check(map.GetRoad(0, 0) == first && report.CandidateCount == count, "동일 도시와 후보 캐시 보존");
                report.CancelReport();
                copyReport.CancelReport();
                Check(!report.IsActive && !report.Patient.Lane && !route.HasDestination && route.Points.Count == 0, "취소 시 보고와 경로 정리");
            }
            Check(JsonUtility.ToJson(UnityEngine.Random.state) == randomBefore, "전역 Unity Random 보존");

            Check(report.TryReport(out _), "외부 목적지 변경 전 보고");
            TrafficLane other = report.Patient.Lane == map.GetLane(0) ? map.GetLane(1) : map.GetLane(0);
            Check(route.SetDestination(other, other.Length * 0.3f), "외부 네비게이션 목적지 설정");
            Check(!report.IsActive && report.Status == PatientReport.ReportStatus.Cancelled && route.DestinationLane == other, "타 시스템 목적지는 취소하지 않음");
            route.ClearDestination();
            player.position = new Vector3(-10000f, -10000f);
            Check(!report.TryReport(out _) && report.Status == PatientReport.ReportStatus.InvalidStart && !route.HasDestination, "잘못된 출발점 즉시 실패");
            PlaceSource(map, player, 0.2f);

            TrafficLane broken = map.GetLane(map.LaneCount - 1);
            broken.enabled = false;
            Check(report.TryReport(out _) && report.Patient.Lane != broken, "비활성 후보 제외");
            report.CancelReport();
            Object.DestroyImmediate(broken.gameObject);
            Check(report.TryReport(out _) && report.Patient.TryGetPose(out _, out _), "파괴된 캐시 후보 안전 처리");
            PatientReport.SpawnPoint old = report.Patient;
            Check(map.TryStartNewCity(97, out _), "외부 요청에 의한 도시 교체");
            Check(!report.IsActive && !report.Patient.Lane && !old.TryGetPose(out _, out _), "이전 도시 SpawnPoint 제거");
            PlaceSource(map, player, 0.2f);
            Check(report.TryReport(out _) && report.Patient.Map == map && map.ContainsLane(report.Patient.Lane), "새 도시 후보 재구축");
            report.CancelReport();

            // 실제 출발 도로를 고립시켜 다른 후보 재시도와 최종 실패를 검사한다
            EnvironmentSlot isolatedSlot = map.GetIncidentSlot(0);
            RoadChunk isolated = isolatedSlot.Road;
            for (int i = 0; i < isolated.ConnectionCount; i++)
                isolated.GetConnection(i).Disconnect();
            isolatedSlot.Lane.TrySample(isolatedSlot.Distance + 0.1f, out Vector3 afterSlot, out _);
            player.position = afterSlot;
            Check(!report.TryReport(out _) && report.Status == PatientReport.ReportStatus.NoReachablePoint && !report.IsActive && !route.HasDestination, "모든 후보 경로 실패의 유한 종료");
            isolatedSlot.Lane.TrySample(isolatedSlot.Distance - 0.1f, out Vector3 beforeSlot, out _);
            player.position = beforeSlot;
            Check(report.TryReport(out _) && report.Patient.Lane == isolatedSlot.Lane, "실패 후보를 건너뛰고 유일한 도달 가능 지점 선택");
            report.CancelReport();
            Check(report.TryReport(out _) && report.Patient.Lane == isolatedSlot.Lane, "다른 후보가 모두 실패하면 직전 위치만 최후 재사용");
            report.CancelReport();

            copyReport.Configure(null);
            for (int i = 0; i < copy.LaneCount; i++)
            {
                TrafficLane lane = copy.GetLane(i);
                if (lane)
                    Object.DestroyImmediate(lane.gameObject);
            }
            copyReport.Configure(copyRoute);
            Check(copyReport.CandidateCount == 0 && !copyReport.TryReport(out _) && copyReport.Status == PatientReport.ReportStatus.NoCandidates, "SpawnPoint 없음");
            Debug.Log("Patient report validation passed: " + checks + " checks");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(replica);
        }
    }

    // 실제 첫 차선 위에 테스트 구급차 위치를 배치한다
    internal static void PlaceSource(CityMap map, Transform player, float fraction)
    {
        TrafficLane lane = map.GetLane(0);
        lane.TrySample(lane.Length * fraction, out Vector3 position, out Vector3 direction);
        player.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, direction));
    }

    // 실패한 조건을 배치 실행에 전달한다
    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException("Patient report validation failed: " + label);
        checks++;
    }

    // 기존 네비게이션 UI에 연결된 보고 데모에서 실제 이벤트 수명을 검증한다
    [MenuItem("CodeBlue Rush/Patient Report/Validate Play Lifecycle")]
    public static void RunPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 모드 종료 후 실행하세요.");
        PatientReportEditor.CreateDemo();
        if (!Object.FindFirstObjectByType<PatientReport>())
            return;
        // 에디터 시작 인덱싱과 검증 대상인 Play 수명을 분리한다
        playStartTime = EditorApplication.timeSinceStartup + 5d;
        EditorApplication.update -= WaitForEditor;
        EditorApplication.update += WaitForEditor;
    }

    // 초기 에디터 콜백이 끝난 뒤 Play 검증의 오류 수집을 시작한다
    private static void WaitForEditor()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < playStartTime)
            return;
        EditorApplication.update -= WaitForEditor;
        SessionState.SetBool(PlayKey, true);
        RegisterPlay();
        EditorApplication.EnterPlaymode();
    }

    // 도메인 재로드 이후에도 검증 진행 상태를 준비한다
    [InitializeOnLoadMethod]
    private static void RegisterPlay()
    {
        if (!SessionState.GetBool(PlayKey, false))
            return;
        checks = 0;
        playStep = 0;
        playFrame = -1;
        playError = false;
        deadline = EditorApplication.timeSinceStartup + 120d;
        EditorApplication.update -= CheckPlay;
        EditorApplication.update += CheckPlay;
        Application.logMessageReceived -= ObserveLog;
        Application.logMessageReceived += ObserveLog;
    }

    // 실제 프레임에서 위치 파괴와 도시 교체 및 보고 중복을 검사한다
    private static void CheckPlay()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("상황 보고 Play 검증 시간 초과");
            if (!EditorApplication.isPlaying || Time.frameCount < 2 || Time.frameCount < playFrame + 3)
                return;
            playFrame = Time.frameCount;
            switch (playStep++)
            {
                case 0:
                    playReport = Object.FindFirstObjectByType<PatientReport>();
                    playRoute = Object.FindFirstObjectByType<NavigationRoute>();
                    playMap = playRoute.Map;
                    NavigationEditor.Preview(playRoute);
                    playRoute.ClearDestination();
                    Check(playReport.CandidateCount > 0 && playReport.TryReport(out _), "Start 생성 이후 후보와 상황 보고");
                    Check(!playReport.TryReport(out _), "Play 중복 보고 거절");
                    oldPoint = playReport.Patient;
                    oldPoint.Lane.enabled = false;
                    Check(!playReport.IsActive && !playRoute.HasDestination, "선택된 위치 비활성 시 즉시 보고 취소");
                    break;
                case 1:
                    Check(!playRoute.HasDestination && playRoute.Points.Count == 0, "취소 이후 예약된 재탐색 없음");
                    oldPoint.Lane.enabled = true;
                    Check(playReport.TryReport(out _) && playReport.Patient.Lane != oldPoint.Lane, "직전 위치 제외 후 다음 보고");
                    oldPoint = playReport.Patient;
                    Object.Destroy(oldPoint.Lane.gameObject);
                    break;
                case 2:
                    Check(!playReport.IsActive && !playRoute.HasDestination && !oldPoint.TryGetPose(out _, out _), "환자 위치 파괴 안전 처리");
                    Check(playMap.TryStartNewCity(411, out _), "새 도시 교체");
                    NavigationEditor.Preview(playRoute);
                    playRoute.ClearDestination();
                    Check(playReport.TryReport(out _) && playMap.ContainsLane(playReport.Patient.Lane), "새 도시 현장 선택");
                    oldPoint = playReport.Patient;
                    Check(playMap.TryStartNewCity(412, out _), "활성 보고 중 도시 교체");
                    Check(!playReport.IsActive && !playReport.Patient.Lane && !oldPoint.TryGetPose(out _, out _), "이전 도시 참조 정리");
                    break;
                case 3:
                    NavigationEditor.Preview(playRoute);
                    playRoute.ClearDestination();
                    Check(playReport.TryReport(out _), "재생성 후 보고");
                    playReport.enabled = false;
                    Check(!playReport.IsActive && !playRoute.HasDestination, "보고 컴포넌트 비활성 정리");
                    playReport.enabled = true;
                    Check(playReport.TryReport(out _), "재활성 보고");
                    playMap.gameObject.SetActive(false);
                    Check(!playReport.IsActive && !playRoute.HasDestination, "도시 비활성 보고 정리");
                    break;
                case 4:
                    playMap.gameObject.SetActive(true);
                    break;
                case 5:
                    Check(playMap.IsReady && playReport.TryReport(out _), "동일 도시 재활성 후 보고");
                    Canvas.ForceUpdateCanvases();
                    NavigationUI ui = Object.FindFirstObjectByType<NavigationUI>();
                    Mesh mesh = ui.canvasRenderer.GetMesh();
                    Check(mesh && mesh.vertexCount >= 8 && playRoute.Points.Count > 0, "보고 목적지의 기존 UI 경로 메시");
                    Check(!playError, "Play 검증 중 런타임 오류 없음");
                    Debug.Log("Patient report play validation passed: " + checks + " checks");
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

    // 조건 검사 이외의 Unity 오류도 기록한다
    private static void ObserveLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            playError = true;
    }

    // 테스트 구독을 정리하고 배치 또는 Play 모드를 종료한다
    private static void FinishPlay(bool success)
    {
        SessionState.SetBool(PlayKey, false);
        EditorApplication.update -= CheckPlay;
        Application.logMessageReceived -= ObserveLog;
        if (Application.isBatchMode)
            EditorApplication.Exit(success ? 0 : 1);
        else
            EditorApplication.ExitPlaymode();
    }
}
