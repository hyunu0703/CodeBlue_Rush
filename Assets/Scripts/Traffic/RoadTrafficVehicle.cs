using UnityEngine;

/// <summary>시뮬레이터가 소유하는 차량 상태다. 거리, 크기, 속도는 맵 로컬 유닛과 초 기준이다.</summary>
public sealed class RoadTrafficVehicle
{
    // 식별자는 해당 시뮬레이션 세션에서만 유효하며 경로는 최신 네트워크에 속한다.
    public int Id { get; internal set; }
    public int PathId { get; internal set; }
    public float Distance { get; internal set; }
    public float Speed { get; internal set; }
    public float Length { get; internal set; }
    public float Width { get; internal set; }
    public float CruiseSpeed { get; internal set; }
    public Vector2 Position { get; internal set; }
    public Vector2 Forward { get; internal set; }
    public bool IsFinished { get; internal set; }
    public bool IsPhysicsControlled { get; internal set; }
    public bool IsChangingLane => TargetLaneId >= 0;
    public int TargetLaneId { get; internal set; } = -1;
    public int NextPathId { get; internal set; } = -1;
    // 예약은 뒷범퍼가 교차로 밖으로 나갈 때까지 유지한다. 이동 중인 차는 신호가 바뀌어도 빠져나간다.
    internal int ReservedMovement = -1;
    // 교차로 출구 이후 누적 주행 거리다. 짧은 출발 경로를 넘어가도 뒷범퍼가 빠져나갈 때까지 예약을 보존한다.
    internal float ExitClearanceRemaining = -1f;
    internal float StoppedSeconds;
    // 변경 궤적의 시작 호 길이와 계획 전진 거리다. 시간 대신 실제 거리로 보간하여 정차 중 횡이동을 막는다.
    internal float LaneChangeStartDistance;
    internal float LaneChangeDistance;
    internal float LaneChangeCooldown;
    internal float TargetSpeed;
    internal float ClearDistance;
    internal float FormationBoostRemaining; // 직선 대열 해소를 위한 임시 가속의 남은 시간(초)이다.
    internal float FormationBoostCooldown;  // 속도 보정의 반복 진동을 막는 재시도 대기 시간(초)이다.
}

/// <summary>플레이어/외부 차량의 한 물리 스텝 관측값이다. Forward는 단위 벡터, 크기는 전체 길이/폭이다.</summary>
public readonly struct RoadTrafficObstacle
{
    // 음수 식별자는 외부 오브젝트에 쓰고 AI 식별자는 시뮬레이터에서 부여한다.
    public int Id { get; }
    public Vector2 Position { get; }
    public Vector2 Velocity { get; }
    public Vector2 Forward { get; }
    public float Length { get; }
    public float Width { get; }

    /// <summary>호출자가 검증한 관측값을 복사한다. 네이티브 Unity 참조를 보관하지 않는다.</summary>
    public RoadTrafficObstacle(int id, Vector2 position, Vector2 velocity, Vector2 forward, float length, float width)
    {
        Id = id;
        Position = position;
        Velocity = velocity;
        Forward = forward;
        Length = length;
        Width = width;
    }
}
