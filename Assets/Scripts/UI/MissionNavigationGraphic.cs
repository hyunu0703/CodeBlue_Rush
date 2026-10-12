using UnityEngine;
using UnityEngine.UI;

/// <summary>카메라 방향으로 도로와 경로를 투영한다. 차량 표시는 패널 하단 중앙에 고정한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
[DefaultExecutionOrder(100)]
public sealed class MissionNavigationGraphic : MaskableGraphic
{
    // 패널 폭에 표시할 월드 거리와 화면 선 두께다. Canvas 기준 픽셀 단위다.
    [SerializeField, Min(1f)] private float visibleWorldWidth = 40f;
    [SerializeField, Min(1f)] private float roadWidth = 8f;
    [SerializeField, Min(1f)] private float routeWidth = 4f;
    [SerializeField] private Color roadColor = new Color(0.9f, 0.94f, 1f, 0.45f);
    // 안내 경로와 목적지 표시의 색상이다. #00D9AF를 불투명하게 사용한다.
    [SerializeField] private Color routeColor = new Color(0f, 217f / 255f, 175f / 255f, 1f);
    [SerializeField] private Color vehicleColor = new Color(0.18f, 0.86f, 0.42f, 1f);
    // 미션 관리자가 직접 주입한다. 씬 변경 시 패널과 함께 해제된다.
    private MissionMapGenerator map;
    private Transform vehicle;
    private Camera viewCamera;
    private bool isGuidanceVisible;
    // 같은 프레임 카메라 LateUpdate 뒤에 투영하기 위한 화면 기준점과 역회전이다.
    private Vector2 markerPosition;
    private Quaternion inverseViewRotation;
    private float pixelsPerUnit;

    /// <summary>지도, 차량, 카메라를 연결한다. 입력을 가로채지 않는 표시 전용 UI다.</summary>
    public void Configure(MissionMapGenerator map, Transform vehicle, Camera viewCamera)
    {
        this.map = map;
        this.vehicle = vehicle;
        this.viewCamera = viewCamera;
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>대기와 종료 단계에서는 안내를 숨긴다. 지도 데이터는 생성기가 소유한다.</summary>
    public void SetGuidanceVisible(bool value)
    {
        isGuidanceVisible = value;
        SetVerticesDirty();
    }

    /// <summary>카메라 추적 뒤에 메시를 갱신해 교차로의 지도 회전과 주행 화면 회전을 일치시킨다.</summary>
    private void LateUpdate()
    {
        if (isGuidanceVisible)
        {
            SetVerticesDirty();
        }
    }

    /// <summary>연결된 도로 가지, 남은 경로, 목적지, 고정 차량 표시를 순서대로 그린다.</summary>
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (!isGuidanceVisible || map == null || vehicle == null || viewCamera == null)
        {
            return;
        }
        // 마커 반지름보다 넉넉한 아래 여백을 두며 작은 패널에서도 원 전체가 내부에 머문다.
        var bounds = rectTransform.rect;
        var radius = Mathf.Min(10f, Mathf.Min(bounds.width, bounds.height) * 0.12f);
        markerPosition = new Vector2(bounds.center.x, bounds.yMin + Mathf.Max(radius + 3f, bounds.height * 0.2f));
        inverseViewRotation = Quaternion.Inverse(viewCamera.transform.rotation);
        pixelsPerUnit = bounds.width / Mathf.Max(1f, visibleWorldWidth);

        // 추가 분기를 포함해 픽업 51개와 후송 51개가 상한이다. 프레임마다 오브젝트 검색이나 컬렉션 복사는 하지 않는다.
        foreach (var road in map.ActiveRoads)
        {
            if (road == null || !road.gameObject.activeInHierarchy)
            {
                continue;
            }
            for (var direction = 0; direction < 4; direction++)
            {
                if (road.TryGetConnectionPosition((RoadDirection)direction, 0, out var localEnd))
                {
                    AddLine(mesh, Project(road.transform.position), Project(road.transform.TransformPoint(localEnd)),
                        roadWidth, roadColor);
                }
            }
        }
        DrawRemainingRoute(mesh);
        AddCircle(mesh, Project(map.DestinationPosition), 5f, routeColor);
        AddCircle(mesh, markerPosition, radius, vehicleColor);
        // 흰색 꺾쇠는 위쪽을 향하고 고정된다. 도로와 경로만 카메라 방향으로 회전한다.
        AddLine(mesh, markerPosition + new Vector2(-radius * 0.5f, -radius * 0.25f),
            markerPosition + Vector2.up * radius * 0.4f, 2f, Color.white);
        AddLine(mesh, markerPosition + Vector2.up * radius * 0.4f,
            markerPosition + new Vector2(radius * 0.5f, -radius * 0.25f), 2f, Color.white);
    }

    /// <summary>차량에 가장 가까운 도로 중심선부터 목적지까지 표시한다. 후진하거나 경로로 복귀해도 다시 계산한다.</summary>
    private void DrawRemainingRoute(VertexHelper mesh)
    {
        // 시드로 정해진 단일 연결 경로와 차량 중심의 월드 좌표다.
        var route = map.NavigationRoute;
        var position = (Vector2)vehicle.position;
        var nearestIndex = -1;
        var nearestPoint = Vector2.zero;
        var nearestDistance = float.PositiveInfinity;
        for (var index = 0; index < route.Count - 1; index++)
        {
            // 구역 안에서 회전하는 경로도 중심을 거쳐 연결되어 직각 모서리를 가로지르지 않는다.
            var delta = route[index + 1] - route[index];
            var fraction = delta.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(position - route[index], delta) / delta.sqrMagnitude) : 0f;
            var point = route[index] + delta * fraction;
            var distance = (position - point).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestIndex = index;
                nearestPoint = point;
                nearestDistance = distance;
            }
        }
        if (nearestIndex < 0)
        {
            return;
        }
        AddLine(mesh, Project(nearestPoint), Project(route[nearestIndex + 1]), routeWidth, routeColor);
        for (var index = nearestIndex + 1; index < route.Count - 1; index++)
        {
            AddLine(mesh, Project(route[index]), Project(route[index + 1]), routeWidth, routeColor);
        }
    }

    /// <summary>차량 상대 월드 위치를 카메라의 XY 방향으로 변환하고 고정 마커 위치에 더한다.</summary>
    private Vector2 Project(Vector3 worldPosition)
    {
        return markerPosition + (Vector2)(inverseViewRotation * (worldPosition - vehicle.position)) * pixelsPerUnit;
    }

    /// <summary>두 점 사이에 일정한 화면 두께의 사각형을 만든다. 부모 RectMask2D가 가장자리까지 잘라낸다.</summary>
    private static void AddLine(VertexHelper mesh, Vector2 start, Vector2 end, float width, Color tint)
    {
        // 선의 직각 방향과 첫 정점 번호다. 길이 0이면 정규화와 불필요한 정점을 생략한다.
        var delta = end - start;
        if (delta.sqrMagnitude < 0.000001f)
        {
            return;
        }
        var normal = new Vector2(-delta.y, delta.x).normalized * (width * 0.5f);
        var first = mesh.currentVertCount;
        mesh.AddVert(start - normal, tint, Vector2.zero);
        mesh.AddVert(start + normal, tint, Vector2.zero);
        mesh.AddVert(end + normal, tint, Vector2.zero);
        mesh.AddVert(end - normal, tint, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }

    /// <summary>차량 및 목적지 표시용 원을 24개의 삼각형으로 만든다. 별도 텍스처를 생성하지 않는다.</summary>
    private static void AddCircle(VertexHelper mesh, Vector2 center, float radius, Color tint)
    {
        // 첫 정점은 원 중심이며 둘레는 같은 반지름의 화면 좌표다.
        var first = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        for (var index = 0; index <= 24; index++)
        {
            var angle = index * Mathf.PI * 2f / 24f;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            if (index > 0)
            {
                mesh.AddTriangle(first, first + index, first + index + 1);
            }
        }
    }
}
