using UnityEngine;

/// <summary>미연결 분기 입구의 공사 펜스와 물리 차단을 관리하며 도로 풀과 함께 재사용한다.</summary>
[DisallowMultipleComponent]
public sealed class RoadConstructionBarrier : MonoBehaviour
{
    private GameObject barrierRoot;                      // 도로 활성 상태에 종속되는 표시와 충돌의 공통 부모다.
    private SpriteRenderer rail;                         // 차도와 양옆 우회 공간을 가로지르는 공사 펜스다.
    private BoxCollider2D body;                          // 건물 영역을 침범하지 않고 차도와 양쪽 인도까지만 막는다.
    private Texture2D texture;                           // 최초 사용 때만 만드는 주황색·흰색 사선 무늬다.
    private Sprite stripe;                               // 풀 반환 중 유지하고 도로 파괴 때 해제하는 런타임 스프라이트다.
    private Sprite footSprite;                           // 펜스 받침대는 무늬 텍스처의 어두운 테두리를 공유한다.
    private readonly Transform[] feet = new Transform[8]; // 도로 폭에 맞춰 간격을 재배치하는 고정 받침대다.

    // 교차로 경계에서 한 차로 폭만큼 물러나 회전 경로와 겹치지 않게 펜스를 배치한다.
    public void Configure(RoadPrefab road, RoadDirection entry)
    {
        if (barrierRoot == null) CreateVisual(road.GetComponent<SpriteRenderer>());
        var laneWidth = road.ImageSize.x * RoadLaneMath.LaneWidthRatio;
        var outward = RoadLaneMath.Outward(entry);
        var halfLength = (entry == RoadDirection.North || entry == RoadDirection.South
            ? road.ImageSize.y : road.ImageSize.x) * 0.5f;
        // 기존 도로 원본과 건물 배치가 사용하는 차선 84.6px, 한쪽 인도 64px 규격에 맞춘다.
        var width = road.GetLaneCount(entry) * laneWidth + road.ImageSize.x * (128f / 1254f);
        barrierRoot.transform.localPosition = outward * (halfLength - laneWidth);
        barrierRoot.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * (int)entry);
        var thickness = laneWidth * 0.35f;
        rail.size = new Vector2(width, thickness);
        body.size = rail.size;
        for (var index = 0; index < feet.Length; index++)
        {
            feet[index].localPosition = new Vector3(width * ((index + 0.5f) / feet.Length - 0.5f), 0f, 0f);
            feet[index].localScale = new Vector3(laneWidth * 0.2f, laneWidth * 0.8f, 1f);
        }
        barrierRoot.SetActive(true);
    }

    // 일반 직선으로 풀에서 재사용할 때 이전 분기의 표시와 충돌을 함께 해제한다.
    public void Hide()
    {
        if (barrierRoot != null) barrierRoot.SetActive(false);
    }

    // 외부 이미지나 추가 재질 없이 기존 도로의 렌더 방식을 사용하는 사선 공사 펜스를 만든다.
    private void CreateVisual(SpriteRenderer road)
    {
        texture = new Texture2D(64, 16, TextureFormat.RGBA32, false);
        texture.name = "ConstructionStripes";
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        var colors = new Color32[64 * 16];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 64; x++)
                colors[y * 64 + x] = y < 2 || y >= 14 ? new Color32(45, 45, 45, 255)
                    : ((x + y) / 12) % 2 == 0 ? new Color32(255, 130, 25, 255) : new Color32(245, 245, 230, 255);
        texture.SetPixels32(colors);
        texture.Apply(false, true);
        stripe = Sprite.Create(texture, new Rect(0f, 0f, 64f, 16f), Vector2.one * 0.5f,
            64f, 0, SpriteMeshType.FullRect);
        stripe.name = "ConstructionFence";
        footSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f, 1f, 0, SpriteMeshType.FullRect);
        barrierRoot = new GameObject("ConstructionBarrier");
        barrierRoot.layer = road.gameObject.layer;
        barrierRoot.transform.SetParent(transform, false);
        rail = barrierRoot.AddComponent<SpriteRenderer>();
        rail.sprite = stripe;
        rail.drawMode = SpriteDrawMode.Tiled;
        rail.sharedMaterial = road.sharedMaterial;
        rail.sortingLayerID = road.sortingLayerID;
        rail.sortingOrder = road.sortingOrder + 2;
        body = barrierRoot.AddComponent<BoxCollider2D>();
        for (var index = 0; index < feet.Length; index++)
        {
            var foot = new GameObject("FenceFoot");
            foot.transform.SetParent(barrierRoot.transform, false);
            var renderer = foot.AddComponent<SpriteRenderer>();
            renderer.sprite = footSprite;
            renderer.sharedMaterial = road.sharedMaterial;
            renderer.sortingLayerID = rail.sortingLayerID;
            renderer.sortingOrder = rail.sortingOrder - 1;
            feet[index] = foot.transform;
        }
    }

    private void OnDestroy()
    {
        ReleaseResource(stripe);
        ReleaseResource(footSprite);
        ReleaseResource(texture);
    }

    // 에디터 검증에서도 런타임 생성 리소스를 남기지 않고 해당 실행 모드에 맞춰 해제한다.
    private static void ReleaseResource(Object resource)
    {
        if (resource == null) return;
        if (Application.isPlaying) Destroy(resource);
        else DestroyImmediate(resource);
    }
}
