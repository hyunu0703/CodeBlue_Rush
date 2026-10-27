using UnityEngine;

/// <summary>건물의 좌우 배치 분류를 저장한다. 0은 양쪽 공용이며 원본 직선 도로 기준 왼쪽은 1, 오른쪽은 2다.</summary>
[DisallowMultipleComponent]
public sealed class BuildingPrefab : MonoBehaviour
{
    // 향후 랜덤 생성 시 건물 후보를 구분하는 분류 값이다. 0은 양쪽 공용, 1은 왼쪽, 2는 오른쪽이다.
    [SerializeField, Range(0, 2)]
    [Tooltip("배치 방향: 0 = 양쪽, 1 = 왼쪽, 2 = 오른쪽")]
    private int index = 1;

    /// <summary>저장된 배치 분류를 반환한다. 0은 양쪽 공용, 1은 왼쪽, 2는 오른쪽이다.</summary>
    public int Index => index;
}
