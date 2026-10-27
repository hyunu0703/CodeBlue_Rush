using UnityEngine;

/// <summary>건물의 좌우 배치 분류와 교차로 배치 허용 여부를 저장한다. 공용 원본은 오른쪽 기준이다.</summary>
[DisallowMultipleComponent]
public sealed class BuildingPrefab : MonoBehaviour
{
    // 배치 후보를 구분한다. 0은 양쪽 공용, 1은 왼쪽, 2는 오른쪽이다.
    [SerializeField, Range(0, 2)]
    [Tooltip("배치 방향: 0 = 양쪽, 1 = 왼쪽, 2 = 오른쪽")]
    private int index = 1;

    [SerializeField] private bool allowIntersection = true; // 외곽 일부라도 교차로 구역에 들어갈 수 있는지 지정한다.

    public bool AllowIntersection => allowIntersection;

    /// <summary>저장된 배치 분류를 반환한다. 0은 양쪽 공용, 1은 왼쪽, 2는 오른쪽이다.</summary>
    public int Index => index;
}
