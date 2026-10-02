using System.Collections;
using UnityEngine;

/// <summary>13단계 데모에서 실제 미션과 픽업 및 차량 접촉을 준비한다</summary>
public sealed class CollisionDemo : MonoBehaviour
{
    [SerializeField] private CityMap map;
    [SerializeField] private NavigationRoute navigation;
    [SerializeField] private PatientReport report;
    [SerializeField] private PatientECG ecg;
    [SerializeField] private HospitalTransfer transfer;
    [SerializeField] private AmbulanceController ambulance;
    [SerializeField] private TrafficSpawner traffic;
    [SerializeField] private Camera view;
    private Coroutine run;
    private Rigidbody2D body;
    private KeyboardAmbulanceInput keyboard;
    private AmbulanceCollision collision;
    public bool IsRunning => run != null;
    public VehicleAI Target { get; private set; }
    public string Error { get; private set; }

    // 실제 플레이어의 입력과 물리 참조를 준비한다
    private void Awake()
    {
        body = ambulance ? ambulance.GetComponent<Rigidbody2D>() : null;
        keyboard = ambulance ? ambulance.GetComponent<KeyboardAmbulanceInput>() : null;
        collision = ambulance ? ambulance.GetComponent<AmbulanceCollision>() : null;
    }

    // 중복 시나리오 실행을 막고 실제 런타임 준비를 시작한다
    public void RunScenario(int scenario)
    {
        if (run != null || !body || !map || !map.IsReady || !report || !ecg || !transfer || !traffic || !view)
            return;
        Error = null;
        run = StartCoroutine(Scenario(scenario));
    }

    // 환자 픽업과 병원 도착은 기존 물리 영역과 미션 API로 수행한다
    private IEnumerator Scenario(int scenario)
    {
        yield return null;
        if (keyboard)
            keyboard.enabled = false;
        if (Target)
            traffic.Release(Target);
        Target = null;
        while (ambulance.IsControlLocked)
            yield return null;
        ambulance.SetInput(0f, 0f, 0f);
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;

        bool reuse = scenario == 1 && report.IsPatientOnBoard && ecg.Value > 0f && transfer.Status == HospitalTransfer.TransferStatus.Transporting && !report.HasFatalCollision;
        if (!reuse)
        {
            report.CancelReport();
            TrafficLane start = map.GetLane(0);
            start.TrySample(start.Length * 0.5f, out Vector3 position, out Vector3 direction);
            Park(position, direction);
            navigation.ClearDestination();
            if (!report.TryReport(out string error))
            {
                Finish(error);
                yield break;
            }
        }
        if (scenario >= 1 && scenario <= 3 && !reuse)
        {
            report.Patient.TryGetPose(out Vector3 position, out Vector3 direction);
            Park(position + direction * 2f, direction);
            float deadline = Time.time + 10f;
            while (!report.IsPatientOnBoard && Time.time < deadline)
                yield return null;
            if (!report.IsPatientOnBoard || transfer.Status != HospitalTransfer.TransferStatus.Transporting)
            {
                Finish("Patient pickup / hospital transfer failed");
                yield break;
            }
        }
        if (scenario == 3)
        {
            Park(transfer.Destination.HospitalPoint, transfer.Destination.Lane.StartDirection);
            float deadline = Time.time + 1f;
            while (transfer.Status != HospitalTransfer.TransferStatus.Arrived && Time.time < deadline)
                yield return null;
            if (transfer.Status != HospitalTransfer.TransferStatus.Arrived)
            {
                Finish("Hospital arrival failed");
                yield break;
            }
        }

        TrafficLane lane = null;
        for (int i = 0; i < map.LaneCount; i++)
        {
            TrafficLane candidate = map.GetLane(i);
            if (candidate.Length >= 12f && candidate.NextCount > 0 && Vector3.Dot(candidate.StartDirection, candidate.EndDirection) > 0.999f && !TrafficSignalController.ForLane(candidate))
            {
                lane = candidate;
                break;
            }
        }
        if (!lane)
        {
            Finish("Straight test lane unavailable");
            yield break;
        }
        float along = lane.Length * 0.5f;
        lane.TrySample(along, out Vector3 point, out Vector3 heading);
        Vector3 side = Vector3.Cross(heading, Vector3.forward).normalized;
        Park(point + side * 10f, -side);
        Vector3 cameraPosition = view.transform.position;
        float cameraSize = view.orthographicSize;
        view.transform.position = body.transform.position + Vector3.back * 10f;
        view.orthographicSize = 1f;
        bool spawned = traffic.TrySpawn(lane, along, out VehicleAI target);
        view.transform.position = cameraPosition;
        view.orthographicSize = cameraSize;
        if (!spawned)
        {
            Finish("Traffic prefab spawn failed");
            yield break;
        }
        Target = target;
        target.AllowLaneChanges = false;
        Park(point + side * 2.5f, -side);
        float speed = scenario == 2 || scenario == 3 || scenario == 4 ? 4f : 2.5f;
        body.linearVelocity = -side * speed;
        ambulance.SetInput(0f, 1f, 0f);
        float contactDeadline = Time.time + 3f;
        while (!ambulance.IsControlLocked && Time.time < contactDeadline)
            yield return null;
        if (!ambulance.IsControlLocked)
        {
            Finish("Rigidbody / Trigger contact failed");
            yield break;
        }
        while (ambulance.IsControlLocked || (Target && Target.IsStunned))
            yield return null;
        Finish(null);
    }

    // 테스트 시작 위치만 배치하고 이후 충돌은 실제 Rigidbody 이동에 맡긴다
    private void Park(Vector3 position, Vector3 direction)
    {
        float rotation = Vector2.SignedAngle(Vector2.up, direction);
        body.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        body.position = position;
        body.rotation = rotation;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        Physics2D.SyncTransforms();
    }

    // 완료 후 키보드 조작을 복구하고 실패 원인을 화면에 남긴다
    private void Finish(string error)
    {
        Error = error;
        ambulance.SetInput(0f, 0f, 0f);
        if (keyboard)
            keyboard.enabled = true;
        run = null;
    }

    // 전용 데모의 상황 선택과 실제 속도 및 미션 결과를 표시한다
    private void OnGUI()
    {
        if (!ambulance || !body || !report || !ecg || !transfer)
            return;
        GUILayout.BeginArea(new Rect(12f, 12f, 370f, 300f), GUI.skin.box);
        GUILayout.Label("Stage 13 - Collision / Knockback / Stun");
        GUILayout.Label($"Speed {body.linearVelocity.magnitude:F2} / 10   Locked: {ambulance.IsControlLocked}");
        if (collision)
            GUILayout.Label($"Impact speed {collision.LastCollisionSpeed:F2}   High speed >= 4");
        GUILayout.Label($"ECG {ecg.Value:F1}   Collisions {report.CollisionCount}   Fatal {report.HasFatalCollision}");
        GUILayout.Label("Transfer: " + transfer.Status);
        GUI.enabled = !IsRunning && !ambulance.IsControlLocked;
        if (GUILayout.Button("A. Before pickup - low speed")) RunScenario(0);
        if (GUILayout.Button("A. Before pickup - high speed")) RunScenario(4);
        if (GUILayout.Button("B. Transport - low speed (repeat for death)")) RunScenario(1);
        if (GUILayout.Button("C. New mission - high speed")) RunScenario(2);
        if (GUILayout.Button("D. Arrived - high speed")) RunScenario(3);
        GUI.enabled = true;
        GUILayout.Label(Error ?? (IsRunning ? "Preparing / colliding / recovering..." : "WASD / Arrow Keys: Drive"));
        GUILayout.EndArea();
    }

    // 중단 시 데모의 준비 루틴과 입력 비활성 상태를 정리한다
    private void OnDisable()
    {
        if (run != null)
            StopCoroutine(run);
        run = null;
        if (keyboard)
            keyboard.enabled = true;
    }
}
