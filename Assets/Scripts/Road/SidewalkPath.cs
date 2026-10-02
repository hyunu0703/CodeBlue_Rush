using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>보도 점과 인접 도로 연결 및 기존 횡단보도 연결 데이터만 제공한다</summary>
public sealed class SidewalkPath : MonoBehaviour
{
    [SerializeField] private RoadChunk road;
    [SerializeField] private Vector2[] points = Array.Empty<Vector2>();
    [SerializeField] private VehicleStopZone crosswalk;
    [SerializeField] private SidewalkPath across;
    private readonly List<SidewalkPath> next = new List<SidewalkPath>();
    public RoadChunk Road => road;
    public int PointCount => points == null ? 0 : points.Length;
    public int NextCount => next.Count;
    public VehicleStopZone Crosswalk => crosswalk;
    public SidewalkPath Across => across;

    // 보행 위치를 배열 수정 없이 월드 좌표로 반환한다
    public Vector3 GetPoint(int index)
    {
        return points != null && index >= 0 && index < points.Length ? transform.TransformPoint(points[index]) : transform.position;
    }

    // 연결된 활성 보도만 반환한다
    public SidewalkPath GetNext(int index)
    {
        SidewalkPath path = index >= 0 && index < next.Count ? next[index] : null;
        return path && path.isActiveAndEnabled ? path : null;
    }

    // 도시 생성 시 동일 끝점의 보도를 한 번 연결한다
    internal void Link(SidewalkPath path)
    {
        if (path && path != this && !next.Contains(path))
            next.Add(path);
    }
}
