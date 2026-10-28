using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>출동 맵 위에 현재 일차와 부족 실적을 표시하고 씬 전환 종료 후 시작 안내를 페이드한다.</summary>
public sealed class DayStartOverlay : MonoBehaviour
{
    private CanvasGroup group;             // 배경과 두 문구의 투명도 및 뒤쪽 UI 입력 차단을 함께 제어한다.
    private TMP_Text title;                // 현재 일차를 중앙에 크게 표시한다.
    private TMP_Text subtitle;             // 부족 실적이 양수일 때만 제목 아래에 표시한다.
    private const float HoldSeconds = 2f;   // 씬 전환 종료 후 문구를 유지하는 시간(초)이다.
    private const float FadeSeconds = 0.6f; // 시작 안내 전체가 사라지는 시간(초)이다.

    // 기존 Canvas와 한글 글꼴을 재사용하고 목표를 이미 채운 경우에는 부제목 공간도 비운다.
    public void Prepare(TMP_Text source, GameProgress progress)
    {
        if (group == null)
        {
            group = gameObject.AddComponent<CanvasGroup>();
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var background = gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.65f);
            title = CreateLabel("Day Title", source, 64f, new Vector2(0f, 30f), 100f);
            subtitle = CreateLabel("Remaining Score", source, 26f, new Vector2(0f, -45f), 60f);
        }
        title.text = $"{progress.CurrentDay}일차";
        subtitle.text = $"(남은 실적 : {progress.RequiredScore})";
        subtitle.gameObject.SetActive(progress.RequiredScore > 0L);
        title.rectTransform.anchoredPosition = new Vector2(0f, progress.RequiredScore > 0L ? 30f : 0f);
        transform.SetAsLastSibling();
        group.alpha = 1f;
        group.blocksRaycasts = true;
        gameObject.SetActive(true);
    }

    // 씬 전환의 검은 덮개가 사라진 뒤부터 시간을 재고 앱 일시정지 동안 안내 시간을 보존한다.
    public IEnumerator Play()
    {
        while (SceneFadeTransition.IsTransitioning || ApplicationPause.IsPaused) yield return null;
        var elapsed = 0f;
        while (elapsed < HoldSeconds)
        {
            elapsed += ApplicationPause.ActiveDeltaTime;
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < FadeSeconds)
        {
            elapsed += ApplicationPause.ActiveDeltaTime;
            group.alpha = 1f - Mathf.SmoothStep(0f, 1f, elapsed / FadeSeconds);
            yield return null;
        }
        while (ApplicationPause.IsPaused) yield return null;
        Hide();
    }

    // 미션 비활성화나 정상 종료 시 입력을 가로채는 투명한 덮개가 남지 않게 한다.
    public void Hide()
    {
        if (group != null)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

    // 화면 폭에 맞춰 축소하되 제목과 부제목의 크기 차이와 한 줄 배치를 유지한다.
    private TMP_Text CreateLabel(string labelName, TMP_Text source, float size, Vector2 position, float height)
    {
        var root = new GameObject(labelName, typeof(RectTransform), typeof(TextMeshProUGUI));
        root.layer = gameObject.layer;
        root.transform.SetParent(transform, false);
        var text = root.GetComponent<TextMeshProUGUI>();
        text.font = source.font;
        text.fontSharedMaterial = source.fontSharedMaterial;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = size * 0.4f;
        text.fontSizeMax = size;
        text.fontSize = size;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.05f, 0.5f);
        rect.anchorMax = new Vector2(0.95f, 0.5f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = position;
        return text;
    }
}
