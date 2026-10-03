using UnityEngine;
using UnityEngine.UI;

/// <summary>읽기 전용 경로를 세로 화면의 간단한 경로선과 시작 및 목적지 표식으로 표시한다</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class NavigationUI : MaskableGraphic
{
    [SerializeField] private NavigationRoute navigation;
    [SerializeField, Min(1f)] private float lineWidth = 3f;
    [SerializeField] private Color startColor = Color.green;
    [SerializeField] private Color destinationColor = Color.red;

    // 경로 공급자를 연결하고 기존 구독을 교체한다
    public void Bind(NavigationRoute route)
    {
        if (navigation)
            navigation.RouteChanged -= Refresh;
        navigation = route;
        if (navigation && isActiveAndEnabled)
            navigation.RouteChanged += Refresh;
        raycastTarget = false;
        Refresh();
    }

    // 활성화 시 결과 변경만 구독한다
    protected override void OnEnable()
    {
        base.OnEnable();
        Bind(navigation);
    }

    // 비활성화 시 경로 이벤트 구독을 해제한다
    protected override void OnDisable()
    {
        if (navigation)
            navigation.RouteChanged -= Refresh;
        base.OnDisable();
    }

    // 경로가 변경된 경우에만 UI 메시를 갱신한다
    private void Refresh()
    {
        SetVerticesDirty();
    }

    // 도시 좌표의 비율을 유지하여 실제 경로와 양 끝 표식을 그린다
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (!navigation || navigation.Status != NavigationRoute.RouteStatus.Ready || navigation.Points.Count == 0 || !navigation.Map || !navigation.Map.IsReady)
            return;
        CityMap city = navigation.Map;
        Vector2 size = new Vector2(city.Width, city.Height) * city.CellSize;
        Vector2 center = (size - Vector2.one * city.CellSize) * 0.5f;
        Rect rect = GetPixelAdjustedRect();
        float scale = Mathf.Max(0f, Mathf.Min((rect.width - 16f) / size.x, (rect.height - 16f) / size.y));
        Vector2 first = Project(navigation.Points[0], center, scale, rect.center);
        Vector2 previous = first;
        for (int i = 1; i < navigation.Points.Count; i++)
        {
            Vector2 next = Project(navigation.Points[i], center, scale, rect.center);
            Vector2 delta = next - previous;
            Vector2 side = new Vector2(-delta.y, delta.x).normalized * lineWidth * 0.5f;
            AddQuad(mesh, previous - side, previous + side, next + side, next - side, color);
            previous = next;
        }
        AddMarker(mesh, first, startColor, 6f);
        AddMarker(mesh, previous, destinationColor, 4f);
    }

    // 월드 좌표를 도시 기준 UI 좌표로 변환한다
    private Vector2 Project(Vector3 point, Vector2 center, float scale, Vector2 offset)
    {
        return ((Vector2)navigation.Map.transform.InverseTransformPoint(point) - center) * scale + offset;
    }

    // 경로 양 끝을 구분하는 작은 사각 표식을 그린다
    private static void AddMarker(VertexHelper mesh, Vector2 center, Color tint, float radius)
    {
        AddQuad(mesh, center + new Vector2(-radius, -radius), center + new Vector2(-radius, radius), center + new Vector2(radius, radius), center + new Vector2(radius, -radius), tint);
    }

    // UI 메시의 사각형 하나를 추가한다
    private static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int index = mesh.currentVertCount;
        mesh.AddVert(a, tint, Vector2.zero);
        mesh.AddVert(b, tint, Vector2.zero);
        mesh.AddVert(c, tint, Vector2.zero);
        mesh.AddVert(d, tint, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index, index + 2, index + 3);
    }
}
