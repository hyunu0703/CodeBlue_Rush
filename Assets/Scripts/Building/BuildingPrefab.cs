using UnityEngine;

/// <summary>건물의 배치 분류와 공유 바닥, 시작 구역의 충돌 연결부를 관리한다. 공용 원본은 오른쪽 기준이다.</summary>
[DisallowMultipleComponent]
public sealed class BuildingPrefab : MonoBehaviour
{
    // 배치 후보를 구분한다. 0은 양쪽 공용, 1은 왼쪽, 2는 오른쪽이다.
    [SerializeField, Range(0, 2)]
    [Tooltip("배치 방향: 0 = 양쪽, 1 = 왼쪽, 2 = 오른쪽")]
    private int index = 1;

    [SerializeField] private bool allowIntersection = true; // 외곽 일부라도 교차로 구역에 들어갈 수 있는지 지정한다.

    private SpriteRenderer ground; // 건물과 함께 풀로 반환하는 충돌 없는 바닥이다.
    private BoxCollider2D startBarrier; // 소방서 옆에 배치될 때만 인도 틈을 막으며 건물과 함께 풀에 반환한다.

    public bool AllowIntersection => allowIntersection;

    /// <summary>저장된 배치 분류를 반환한다. 0은 양쪽 공용, 1은 왼쪽, 2는 오른쪽이다.</summary>
    public int Index => index;

    // 시작 구역의 연결부를 맵 좌표로 설정하고 일반 건물로 재사용될 때는 충돌을 해제한다.
    public void ConfigureStartBarrier(Bounds? bounds, Transform mapRoot)
    {
        if (!bounds.HasValue)
        {
            if (startBarrier != null) startBarrier.enabled = false;
            return;
        }
        if (startBarrier == null)
        {
            var root = new GameObject("StartBoundary");
            root.layer = gameObject.layer;
            startBarrier = root.AddComponent<BoxCollider2D>();
        }
        startBarrier.transform.SetParent(mapRoot, false);
        startBarrier.transform.localPosition = bounds.Value.center;
        startBarrier.transform.localRotation = Quaternion.identity;
        startBarrier.transform.localScale = Vector3.one;
        startBarrier.offset = Vector2.zero;
        startBarrier.size = bounds.Value.size;
        startBarrier.enabled = true;
        startBarrier.transform.SetParent(transform, true);
    }

    // 맵 격자에 맞춘 인도 타일로 이미지와 콜라이더 외곽보다 최소 0.64유닛 넓은 바닥을 복원한다.
    public void ConfigureGround(Sprite tile, SpriteRenderer road, Bounds footprint, Transform mapRoot)
    {
        if (ground == null)
        {
            var root = new GameObject("BuildingGround");
            ground = root.AddComponent<SpriteRenderer>();
        }
        var cell = (Vector2)tile.bounds.size;
        var min = (Vector2)footprint.min - Vector2.one * 0.64f;
        var max = (Vector2)footprint.max + Vector2.one * 0.64f;
        min = new Vector2(Mathf.Floor(min.x / cell.x) * cell.x, Mathf.Floor(min.y / cell.y) * cell.y);
        max = new Vector2(Mathf.Ceil(max.x / cell.x) * cell.x, Mathf.Ceil(max.y / cell.y) * cell.y);

        // 겹치는 바닥도 동일한 타일 위상을 갖게 하고 건물의 회전·반전·배율을 바닥에 적용하지 않는다.
        ground.transform.SetParent(mapRoot, false);
        ground.transform.localPosition = (min + max) * 0.5f;
        ground.transform.localRotation = Quaternion.identity;
        ground.transform.localScale = Vector3.one;
        ground.sprite = tile;
        ground.drawMode = SpriteDrawMode.Tiled;
        ground.tileMode = SpriteTileMode.Continuous;
        ground.size = max - min;
        ground.sharedMaterial = road.sharedMaterial;
        ground.color = road.color;
        ground.sortingLayerID = road.sortingLayerID;
        ground.sortingOrder = road.sortingOrder - 1;
        ground.transform.SetParent(transform, true);
    }
}
