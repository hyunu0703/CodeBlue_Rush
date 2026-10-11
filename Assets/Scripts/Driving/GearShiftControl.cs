using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class GearShiftControl : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text label;

    private Image background;
    private bool canUseTouch;
    private bool canShift;
    private bool isPressPending;
    private bool hasDisplayState;
    private bool displayedReverse;
    private bool displayedCanShift;

    private void Awake()
    {
        background = GetComponent<Image>();
    }

    private void OnDisable()
    {
        ResetInput();
        hasDisplayState = false;
    }

    public void Configure(TMP_Text label)
    {
        this.label = label;
        hasDisplayState = false;
    }

    public void SetTouchEnabled(bool value)
    {
        if (canUseTouch == value)
        {
            return;
        }

        canUseTouch = value;
        hasDisplayState = false;
        if (!value)
        {
            ResetInput();
        }
    }

    public void Display(bool isReverse, bool canShift)
    {
        this.canShift = canShift;
        if (background == null)
        {
            background = GetComponent<Image>();
        }

        if (label == null || background == null)
        {
            Debug.LogError("GearShiftControl requires an Image and its TMP label reference.", this);
            enabled = false;
            return;
        }

        if (hasDisplayState && displayedReverse == isReverse && displayedCanShift == canShift)
        {
            return;
        }

        hasDisplayState = true;
        displayedReverse = isReverse;
        displayedCanShift = canShift;
        string gearText = isReverse ? "D / <color=#FFD66B>R</color>" : "<color=#FFD66B>D</color> / R";
        label.text = AmbulanceDriveInput.IsMobileBuild ? gearText : gearText + "\n<size=16>[E]</size>";
        background.color = canShift ? new Color(0.08f, 0.12f, 0.18f, 0.95f) : new Color(0.18f, 0.2f, 0.23f, 0.85f);
        label.color = canShift ? Color.white : new Color(0.65f, 0.65f, 0.65f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isActiveAndEnabled && canUseTouch && canShift && Time.timeScale > 0f
            && eventData.button == PointerEventData.InputButton.Left)
        {
            isPressPending = true;
        }
    }

    public bool ConsumePress()
    {
        bool result = isPressPending;
        isPressPending = false;
        return result;
    }

    public void ResetInput()
    {
        isPressPending = false;
    }
}
