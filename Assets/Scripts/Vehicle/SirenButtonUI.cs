using UnityEngine;
using UnityEngine.UI;

/// <summary>모바일 버튼 클릭을 사이렌 토글에 한 번만 전달한다</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class SirenButtonUI : MonoBehaviour
{
    [SerializeField] private SirenController siren;
    [SerializeField] private Text label;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (!button) button = GetComponent<Button>();
        button.onClick.RemoveListener(Click);
        button.onClick.AddListener(Click);
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        if (button) button.onClick.RemoveListener(Click);
        if (siren) siren.Changed -= HandleChanged;
    }

    // Gameplay UI에서 구급차의 사이렌을 명시적으로 연결한다
    public void Bind(SirenController source)
    {
        if (siren) siren.Changed -= HandleChanged;
        siren = source;
        if (isActiveAndEnabled) Subscribe();
        Refresh();
    }

    private void Subscribe()
    {
        if (!siren) return;
        siren.Changed -= HandleChanged;
        siren.Changed += HandleChanged;
    }

    private void Click()
    {
        if (siren && siren.isActiveAndEnabled) siren.Toggle();
    }

    private void HandleChanged(bool on)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (label) label.text = siren && siren.IsOn ? "SIREN ON" : "SIREN OFF";
    }
}
