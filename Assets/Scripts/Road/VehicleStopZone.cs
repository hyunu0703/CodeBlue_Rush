using UnityEngine;

/// <summary>기존 진입 차선의 정지선과 다음 단계에서 사용할 횡단 위치를 보관한다</summary>
[DisallowMultipleComponent]
public sealed class VehicleStopZone : MonoBehaviour
{
    [SerializeField] private TrafficSignalController signal;
    [SerializeField] private TrafficLane lane;
    [SerializeField] private float stopDistance = 4f;
    [SerializeField] private Transform stopLine;
    [UnityEngine.Serialization.FormerlySerializedAs("light")]
    [SerializeField] private SpriteRenderer signalLamp;
    [SerializeField] private Transform crosswalkStart;
    [SerializeField] private Transform crosswalkEnd;
    [SerializeField] private Transform pedestrianWaitPointA;
    [SerializeField] private Transform pedestrianWaitPointB;
    public TrafficSignalController Signal => signal;
    public TrafficLane Lane => lane;
    public float StopDistance => stopLine && lane ? Vector3.Dot(stopLine.position - lane.StartPoint, lane.StartDirection) : stopDistance;
    public bool IsGreen => isActiveAndEnabled && signal && signal.IsGreen(this);
    public Transform CrosswalkStart => crosswalkStart;
    public Transform CrosswalkEnd => crosswalkEnd;
    public Transform PedestrianWaitPointA => pedestrianWaitPointA;
    public Transform PedestrianWaitPointB => pedestrianWaitPointB;

    // 상태 변경 시에만 표시 색을 갱신한다
    internal void Show(bool green)
    {
        if (signalLamp)
            signalLamp.color = green ? Color.green : Color.red;
    }
}
