using UnityEngine;

/// <summary>시작점에서 끝점으로 진행하는 차선 경로와 직접 연결 참조를 소유한다</summary>
[DisallowMultipleComponent]
public sealed class TrafficLane : MonoBehaviour
{
    [SerializeField] private Vector2[] points = { Vector2.zero, Vector2.up * 10f };
    [SerializeField] private TrafficLane leftLane;
    [SerializeField] private TrafficLane rightLane;

    private TrafficLane nextLane;
    private float[] distances;
    private Matrix4x4 pathMatrix;
    private bool valid;

    public TrafficLane NextLane => nextLane;
    public TrafficLane LeftLane => leftLane;
    public TrafficLane RightLane => rightLane;
    public int PointCount => points == null ? 0 : points.Length;
    public Vector3 StartPoint => GetWorldPoint(0);
    public Vector3 EndPoint => GetWorldPoint(PointCount - 1);
    public Vector3 StartDirection => PointCount < 2 ? Vector3.zero : (GetWorldPoint(1) - StartPoint).normalized;
    public Vector3 EndDirection => PointCount < 2 ? Vector3.zero : (EndPoint - GetWorldPoint(PointCount - 2)).normalized;
    public float Length { get { EnsurePath(); return valid ? distances[distances.Length - 1] : 0f; } }

    // 경로가 편집되면 거리 캐시를 무효화한다
    private void OnValidate()
    {
        distances = null;
    }

    // 인덱스의 차선 점을 월드 좌표로 반환한다
    public Vector3 GetWorldPoint(int index)
    {
        return points != null && index >= 0 && index < points.Length ? transform.TransformPoint(points[index]) : transform.position;
    }

    // 점과 Transform이 바뀐 경우에만 월드 거리 캐시를 다시 계산한다
    private void EnsurePath()
    {
        Matrix4x4 matrix = transform.localToWorldMatrix;
        if (distances != null && pathMatrix == matrix)
            return;

        pathMatrix = matrix;
        distances = new float[PointCount];
        valid = PointCount >= 2;
        for (int i = 1; i < PointCount; i++)
        {
            float length = Vector3.Distance(GetWorldPoint(i - 1), GetWorldPoint(i));
            if (float.IsNaN(length) || float.IsInfinity(length) || length < 0.001f)
                valid = false;
            distances[i] = distances[i - 1] + length;
        }
    }

    // 월드 이동 거리에서 위치와 진행 방향을 이진 검색으로 계산한다
    public bool TrySample(float distance, out Vector3 position, out Vector3 direction)
    {
        position = transform.position;
        direction = Vector3.zero;
        EnsurePath();
        if (!valid || float.IsNaN(distance) || float.IsInfinity(distance))
            return false;

        distance = Mathf.Clamp(distance, 0f, distances[distances.Length - 1]);
        int low = 1;
        int high = distances.Length - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (distances[mid] < distance)
                low = mid + 1;
            else
                high = mid;
        }

        Vector3 start = GetWorldPoint(low - 1);
        Vector3 end = GetWorldPoint(low);
        float t = (distance - distances[low - 1]) / (distances[low] - distances[low - 1]);
        position = Vector3.Lerp(start, end, t);
        direction = (end - start).normalized;
        return true;
    }

    // 경로와 같은 방향의 인접 차선 설정을 검사한다
    public bool Validate(out string error)
    {
        EnsurePath();
        error = null;
        if (!valid)
            error = "차선에는 유효한 서로 다른 점이 두 개 이상 필요합니다.";
        else if (!ValidateNeighbor(leftLane, true) || !ValidateNeighbor(rightLane, false))
            error = "인접 차선은 같은 부모 아래 같은 방향이어야 하며 좌우 참조와 위치가 일치해야 합니다.";
        return error == null;
    }

    // 좌우를 진행 방향 기준으로 검사하고 역방향 차선 연결을 차단한다
    private bool ValidateNeighbor(TrafficLane lane, bool left)
    {
        if (!lane)
            return true;
        if (lane == this || lane.transform.parent != transform.parent || lane == (left ? rightLane : leftLane))
            return false;
        if ((left ? lane.rightLane : lane.leftLane) != this || lane.Length <= 0f)
            return false;
        if (Vector3.Dot(StartDirection, lane.StartDirection) < 0.9f || Vector3.Dot(EndDirection, lane.EndDirection) < 0.9f)
            return false;

        Vector3 side = Vector3.Cross(Vector3.forward, StartDirection);
        float offset = Vector3.Dot(side, lane.StartPoint - StartPoint);
        return left ? offset > 0f : offset < 0f;
    }

    // 연결 지점만 다음 차선의 상태를 변경한다
    internal void SetNext(TrafficLane lane)
    {
        nextLane = lane;
    }

    // Scene에서 경로와 진행 화살표를 표시한다
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        for (int i = 1; i < PointCount; i++)
            Gizmos.DrawLine(GetWorldPoint(i - 1), GetWorldPoint(i));
        if (!TrySample(Length * 0.5f, out Vector3 position, out Vector3 direction))
            return;
        Vector3 side = Vector3.Cross(Vector3.forward, direction) * 0.25f;
        Gizmos.DrawLine(position, position - direction * 0.5f + side);
        Gizmos.DrawLine(position, position - direction * 0.5f - side);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(StartPoint, 0.12f);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(EndPoint, 0.12f);
        if (nextLane)
            Gizmos.DrawLine(EndPoint, nextLane.StartPoint);
    }
}
