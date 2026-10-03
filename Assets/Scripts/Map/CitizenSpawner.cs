using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>현재 도시의 플레이어 주변 보도에서 시민 생성과 풀 수명을 관리한다</summary>
[DisallowMultipleComponent]
public sealed class CitizenSpawner : MonoBehaviour
{
    [SerializeField] private CityMap map;
    [SerializeField] private Transform player;
    [SerializeField] private Camera view;
    [SerializeField] private CitizenAI prefab;
    [SerializeField, Range(0, 64)] private int maxCitizens = 24;
    [SerializeField, Min(1f)] private float spawnNear = 16f;
    [SerializeField, Min(1f)] private float spawnFar = 38f;
    [SerializeField, Min(1f)] private float despawnDistance = 55f;
    [SerializeField, Min(0.1f)] private float interval = 0.6f;
    private readonly List<CitizenAI> active = new List<CitizenAI>();
    private readonly Stack<CitizenAI> pool = new Stack<CitizenAI>();
    private readonly Collider2D[] hits = new Collider2D[32];
    private SirenController siren;
    private CityMap subscribedMap;
    private CityMap layout;
    private Coroutine routine;
    private uint random;
    internal CityMap Map => map;
    public int ActiveCount => active.Count;
    public int PooledCount => pool.Count;

    // 기존 차량 생성기의 참조를 한 번 받아 시민에게 제공한다
    public void Configure(CityMap city, Transform source, Camera camera, SirenController alarm, CitizenAI citizenPrefab)
    {
        Unbind();
        ClearCitizens();
        map = city;
        player = source;
        view = camera;
        siren = alarm;
        prefab = citizenPrefab;
        layout = null;
        if (isActiveAndEnabled)
            Bind();
    }

    private void OnEnable()
    {
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
        ClearCitizens();
    }

    private void Bind()
    {
        Unbind();
        subscribedMap = map;
        if (subscribedMap)
            subscribedMap.StateChanged += HandleMap;
        HandleMap();
        if (Application.isPlaying)
            routine = StartCoroutine(Maintain());
    }

    private void Unbind()
    {
        if (subscribedMap)
            subscribedMap.StateChanged -= HandleMap;
        subscribedMap = null;
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
    }

    // 도시가 비활성화되거나 교체될 때 이전 시민을 즉시 정리한다
    private void HandleMap()
    {
        if (!map || !map.IsReady || layout != map)
        {
            ClearCitizens();
            layout = map && map.IsReady ? map : null;
            random = unchecked((uint)System.Guid.NewGuid().GetHashCode());
        }
    }

    private void ClearCitizens()
    {
        while (active.Count > 0)
            Release(active[active.Count - 1]);
    }

    // 콘텐츠와 분리된 생성기 난수를 사용한다
    internal int Next(int count)
    {
        random = unchecked(random * 1664525u + 1013904223u);
        return (int)((random >> 8) % (uint)count);
    }

    internal bool Roll(float chance)
    {
        return chance > 0f && (chance >= 1f || Next(100000) < chance * 100000f);
    }

    internal bool SirenNearby(Vector3 point)
    {
        return siren && siren.isActiveAndEnabled && siren.IsOn && (siren.transform.position - point).sqrMagnitude < 225f;
    }

    internal Vector3 NearWaitPoint(VehicleStopZone zone, Vector3 point)
    {
        Vector3 a = zone.PedestrianWaitPointA.position;
        Vector3 b = zone.PedestrianWaitPointB.position;
        return (a - point).sqrMagnitude <= (b - point).sqrMagnitude ? a : b;
    }

    internal Vector3 FarWaitPoint(VehicleStopZone zone, Vector3 point)
    {
        Vector3 a = zone.PedestrianWaitPointA.position;
        Vector3 b = zone.PedestrianWaitPointB.position;
        return (a - point).sqrMagnitude >= (b - point).sqrMagnitude ? a : b;
    }

    internal Vector3 NearCrosswalkEnd(VehicleStopZone zone, Vector3 point, bool near)
    {
        Vector3 a = zone.CrosswalkStart.position;
        Vector3 b = zone.CrosswalkEnd.position;
        bool chooseA = (a - point).sqrMagnitude <= (b - point).sqrMagnitude;
        return chooseA == near ? a : b;
    }

    // 횡단 선분 주변의 실제 동적 차량만 제한된 물리 조회로 검사한다
    internal bool CrosswalkClear(VehicleStopZone zone)
    {
        if (!zone || !zone.CrosswalkStart || !zone.CrosswalkEnd)
            return false;
        Vector2 a = zone.CrosswalkStart.position;
        Vector2 b = zone.CrosswalkEnd.position;
        Vector2 segment = b - a;
        float length = segment.sqrMagnitude;
        if (length < 0.01f)
            return false;
        int count = Physics2D.OverlapCircle((a + b) * 0.5f, segment.magnitude * 0.5f + 4f, new ContactFilter2D { useTriggers = true }, hits);
        if (count == hits.Length)
            return false;
        for (int i = 0; i < count; i++)
        {
            Rigidbody2D body = hits[i] ? hits[i].attachedRigidbody : null;
            if (!body || (body.GetComponent<VehicleAI>() == null && body.transform != player))
                continue;
            float t = Mathf.Clamp01(Vector2.Dot(body.position - a, segment) / length);
            if ((body.position - a - segment * t).sqrMagnitude < 16f)
                return false;
        }
        return true;
    }

    // 보도 중간의 화면 밖 유효 지점에만 생성한다
    public bool TrySpawn(SidewalkPath path, float fraction, out CitizenAI citizen)
    {
        citizen = null;
        if (!isActiveAndEnabled || !map || !prefab || !player || !view || !view.isActiveAndEnabled || active.Count >= maxCitizens || !map.ContainsSidewalkPath(path) || path.PointCount < 2 || !float.IsFinite(fraction) || fraction < 0f || fraction > 1f)
            return false;
        int segment = Next(path.PointCount - 1);
        Vector3 point = Vector3.Lerp(path.GetPoint(segment), path.GetPoint(segment + 1), fraction);
        float distance = Vector2.Distance(point, player.position);
        Vector3 viewport = view.WorldToViewportPoint(point);
        if (distance < spawnNear || distance > spawnFar || viewport.z <= 0f || (viewport.x > -0.2f && viewport.x < 1.2f && viewport.y > -0.2f && viewport.y < 1.2f))
            return false;
        for (int i = 0; i < active.Count; i++)
            if (active[i] && (active[i].transform.position - point).sqrMagnitude < 2.25f)
                return false;
        while (pool.Count > 0 && !citizen)
            citizen = pool.Pop();
        if (!citizen)
        {
            citizen = Instantiate(prefab, transform);
            citizen.gameObject.SetActive(false);
        }
        citizen.gameObject.SetActive(true);
        int direction = Next(2) == 0 ? 1 : -1;
        citizen.Place(this, path, point, segment, direction, Roll(0.12f));
        citizen.Slot = active.Count;
        active.Add(citizen);
        return true;
    }

    // 마지막 슬롯 교환으로 시민을 즉시 풀에 반환한다
    internal void Release(CitizenAI citizen)
    {
        if (ReferenceEquals(citizen, null))
            return;
        int slot = citizen.Slot;
        if (slot < 0 || slot >= active.Count || active[slot] != citizen)
            return;
        CitizenAI last = active[active.Count - 1];
        active[slot] = last;
        last.Slot = slot;
        active.RemoveAt(active.Count - 1);
        citizen.Clear();
        if (citizen)
        {
            citizen.gameObject.SetActive(false);
            if (pool.Count < maxCitizens) pool.Push(citizen);
            else Destroy(citizen.gameObject);
        }
    }

    // 간헐적으로 거리와 도시 소유권을 정리하고 시민을 보충한다
    private IEnumerator Maintain()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, interval));
            if (!map || !map.IsReady || !player || !prefab)
                continue;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                CitizenAI citizen = active[i];
                if (!citizen || !citizen.isActiveAndEnabled || !map.ContainsSidewalkPath(citizen.Path) || (!citizen.IsCrossing && (citizen.transform.position - player.position).sqrMagnitude > despawnDistance * despawnDistance) || (!citizen.IsCrossing && active.Count > maxCitizens))
                    Release(citizen);
            }
            for (int attempt = 0, added = 0; attempt < 24 && active.Count < maxCitizens && added < 3 && map.SidewalkPathCount > 0; attempt++)
                if (TrySpawn(map.GetSidewalkPath(Next(map.SidewalkPathCount)), 0.15f + Next(700) / 1000f, out _))
                    added++;
        }
    }
}
