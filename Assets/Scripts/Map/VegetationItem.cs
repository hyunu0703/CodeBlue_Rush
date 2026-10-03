using UnityEngine;

/// <summary>식생의 종류와 상단 왼쪽 광원을 유지하는 독립 Sprite 방향을 제공한다</summary>
[DisallowMultipleComponent]
public sealed class VegetationItem : MonoBehaviour
{
    public enum Kind { Tree, Bush, Hedge, Flower, FlowerBed, Planter }
    [SerializeField] private Kind kind;
    public Kind Category => kind;

    // 회전된 도로의 자식으로 생성되어도 승인된 광원 방향을 유지한다
    private void OnEnable()
    {
        transform.rotation = Quaternion.identity;
    }
}
