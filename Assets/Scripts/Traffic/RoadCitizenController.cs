using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>차량 고정 스텝에 맞춰 주변 시민과 임시 사각형 외형을 관리하고 맵 교체와 비활성화 시 정리한다.</summary>
[DisallowMultipleComponent]
public sealed class RoadCitizenController : MonoBehaviour
{
    [SerializeField, Range(1, 128)] private int maximumCitizens = 48;          // 플레이어 주변의 최대 시민 수다.
    [SerializeField, Min(1f)] private float radiusInLaneWidths = 26f;          // 생성 반경이며 맵 로컬 차로 폭 단위다.
    [SerializeField, Min(0.1f)] private float walkSpeed = 0.9f;                // 기본 속도이며 맵 로컬 유닛/초다.
    [SerializeField, Range(0.1f, 0.5f)] private float sizeInLaneWidths = 0.3f; // 인도 폭 안에 들어가는 임시 사각형 한 변이다.
    // 활성 시민과 외형의 대응이며 외형의 수명은 이 컴포넌트가 소유한다.
    private readonly Dictionary<RoadCitizenSimulation.Citizen, SpriteRenderer> visuals =
        new Dictionary<RoadCitizenSimulation.Citizen, SpriteRenderer>();
    private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>(); // 주변 보충 때 재사용하는 사각형 외형이다.
    private RoadCitizenSimulation simulation;                                  // 현재 미션의 시민 상태다.
    private RoadLaneNetwork network;                                           // 후송 확장 감지용 스냅숏이다.
    private RoadTrafficSignals signals;                                        // 후송 확장에서 교체될 수 있는 신호 소유자다.
    private MissionMapGenerator generator;                                     // 활성 도로와 좌표계를 제공하는 현재 생성기다.
    private Func<int, bool> isRoadActive;                                      // 고정 스텝마다 delegate를 할당하지 않는 활성 조회다.
    private Transform visualRoot;                                              // 이 컴포넌트만 소유하는 런타임 외형 부모다.
    private Texture2D texture;                                                 // 모든 시민이 공유하는 1px 흰색 텍스처다.
    private Sprite sprite;                                                     // 색으로 구분하는 임시 사각형 스프라이트다.
    private System.Random random;                                              // 생성 지점과 색상 선택용 난수다.
    private int session = -1;                                                  // 새 미션과 같은 미션의 후송 확장을 구분한다.
    private float spawnClock;                                                  // 주변 보충을 매 프레임 반복하지 않도록 하는 게임 시간이다.
    public RoadCitizenSimulation Simulation => simulation;

    // 차량이 판단하기 전에 시민의 진입과 점유를 확정한다. 호출은 RoadTrafficController 한 곳에서 소유한다.
    internal void Step(MissionMapGenerator map, RoadLaneNetwork current, RoadTrafficSimulation traffic,
        Vector2 center, float seconds, IReadOnlyList<RoadTrafficObstacle> obstacles)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        if (generator != map || session != map.LaneNetworkSession || simulation == null)
        {
            Clear();
            generator = map;
            isRoadActive = map.IsRoadActive;
            session = map.LaneNetworkSession;
            random = new System.Random(map.CurrentSeed ^ 0x713a);
            CreateVisuals();
            simulation = new RoadCitizenSimulation(current, traffic.Signals, map.CurrentSeed);
            network = current;
            signals = traffic.Signals;
            spawnClock = 1f;
        }
        else if (!ReferenceEquals(network, current) || !ReferenceEquals(signals, traffic.Signals))
        {
            simulation.SetNetwork(current, traffic.Signals);
            network = current;
            signals = traffic.Signals;
        }
        var laneWidth = current.Roads[0].Layout.LaneWidth;
        var radius = laneWidth * radiusInLaneWidths;
        // 더 넓은 회수 반경으로 경계 부근 시민의 반복 생성과 소멸을 줄인다.
        var removeRadius = radius + laneWidth * 6f;
        for (var i = simulation.Citizens.Count - 1; i >= 0; i--)
        {
            var citizen = simulation.Citizens[i];
            var edge = simulation.Sidewalks.Edges[citizen.EdgeId];
            if (!map.IsRoadActive(edge.Road) || (citizen.Position - center).sqrMagnitude > removeRadius * removeRadius)
            {
                var visual = visuals[citizen];
                visual.gameObject.SetActive(false);
                pool.Push(visual);
                visuals.Remove(citizen);
                simulation.Remove(citizen);
            }
        }
        spawnClock += seconds;
        if (spawnClock >= 0.5f)
        {
            spawnClock = 0f;
            FillCitizens(center, radius, laneWidth);
        }
        simulation.Tick(seconds, traffic.Vehicles, obstacles, isRoadActive);
        for (var i = 0; i < simulation.Citizens.Count; i++)
        {
            var citizen = simulation.Citizens[i];
            visuals[citizen].transform.localPosition = citizen.Position;
        }
    }

    private void OnDisable()
    {
        Clear();
    }

    // 새 에셋 없이 모든 시민이 공유할 임시 사각형을 만든다.
    private void CreateVisuals()
    {
        var root = new GameObject("RuntimeCitizens");
        root.transform.SetParent(generator.LaneMapRoot, false);
        visualRoot = root.transform;
        texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f, 1f);
    }

    // 활성 인도에만 시민을 보충하고 같은 위치에 겹쳐 생성하지 않는다.
    private void FillCitizens(Vector2 center, float radius, float laneWidth)
    {
        var sidewalks = simulation.Sidewalks;
        for (var attempt = 0; attempt < maximumCitizens * 8 && simulation.Citizens.Count < maximumCitizens; attempt++)
        {
            var edgeId = random.Next(sidewalks.Edges.Count);
            var edge = sidewalks.Edges[edgeId];
            if (edge.IsCrossing || !generator.IsRoadActive(edge.Road))
            {
                continue;
            }
            var fraction = (float)random.NextDouble();
            var position = Vector2.Lerp(sidewalks.Points[edge.A], sidewalks.Points[edge.B], fraction);
            if ((position - center).sqrMagnitude > radius * radius)
            {
                continue;
            }
            var overlaps = false;
            for (var i = 0; i < simulation.Citizens.Count; i++)
            {
                var existing = simulation.Citizens[i];
                overlaps |= (existing.Position - position).sqrMagnitude < laneWidth * laneWidth * 0.25f;
            }
            if (overlaps)
            {
                continue;
            }
            // Spawn은 임의 방향을 고르므로 양 끝에서 동일한 위치가 되도록 초기 위치를 직접 투영한다.
            var citizen = simulation.Spawn(edgeId, 0f, walkSpeed * (0.8f + (float)random.NextDouble() * 0.4f));
            citizen.Distance = Vector2.Distance(sidewalks.Points[citizen.From], position);
            citizen.Position = position;
            var visual = AcquireVisual(laneWidth, edge.Road);
            visual.transform.localPosition = position;
            visuals.Add(citizen, visual);
        }
    }

    // 풀을 먼저 사용하며 각 사각형은 도로 위에 보이도록 원본 도로의 정렬 레이어를 따른다.
    private SpriteRenderer AcquireVisual(float laneWidth, int road)
    {
        SpriteRenderer visual;
        if (pool.Count > 0)
        {
            visual = pool.Pop();
        }
        else
        {
            var actor = new GameObject("Citizen", typeof(SpriteRenderer));
            actor.transform.SetParent(visualRoot, false);
            visual = actor.GetComponent<SpriteRenderer>();
            visual.sprite = sprite;
        }
        visual.transform.localScale = Vector3.one * (laneWidth * sizeInLaneWidths);
        visual.color = Color.HSVToRGB((float)random.NextDouble(), 0.65f, 1f);
        var roadRenderer = generator.ActiveRoads[road].GetComponent<SpriteRenderer>();
        visual.sortingLayerID = roadRenderer.sortingLayerID;
        visual.sortingOrder = roadRenderer.sortingOrder + 3;
        visual.gameObject.SetActive(true);
        return visual;
    }

    // 이 컴포넌트가 생성한 상태와 그래픽만 해제하고 영구 에셋은 유지한다.
    internal void Clear()
    {
        simulation?.ClearOccupancy();
        simulation = null;
        network = null;
        signals = null;
        session = -1;
        generator = null;
        isRoadActive = null;
        visuals.Clear();
        pool.Clear();
        if (visualRoot != null)
        {
            visualRoot.gameObject.SetActive(false);
            Destroy(visualRoot.gameObject);
        }
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
        visualRoot = null;
        sprite = null;
        texture = null;
    }
}
