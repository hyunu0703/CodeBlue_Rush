using System;
using UnityEngine;

/// <summary>구급차 사이렌 상태와 선택적인 소리 재생을 소유한다</summary>
[DisallowMultipleComponent]
public sealed class SirenController : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    public bool IsOn { get; private set; }
    public event Action<bool> Changed;

    // 클릭 한 번에 상태를 한 번 전환한다
    public void Toggle()
    {
        Set(IsOn == false);
    }

    // 실제 변경 시에만 청취자와 선택적 음원을 갱신한다
    public void Set(bool on)
    {
        if (IsOn == on)
            return;
        IsOn = on;
        if (audioSource && audioSource.clip)
        {
            if (on) audioSource.Play();
            else audioSource.Stop();
        }
        Changed?.Invoke(on);
    }

    // 비활성 구급차는 주변 AI에 영향을 주지 않는다
    private void OnDisable()
    {
        Set(false);
    }
}
