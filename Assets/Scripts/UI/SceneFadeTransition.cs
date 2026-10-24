using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>씬을 넘겨 유지되는 화면 덮개로 페이드와 전환 중 UI 입력 차단을 담당한다.</summary>
public sealed class SceneFadeTransition : MonoBehaviour
{
    private static SceneFadeTransition instance; // 현재 실행 세션의 화면 전환 관리자다.
    private CanvasGroup cover;                  // 검은 화면의 투명도와 입력 차단을 제어한다.
    private bool isTransitioning;               // 중복 씬 로드를 차단한다.

    // Domain Reload를 끈 실행에서도 이전 세션의 참조를 제거한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        instance = null;
    }

    // 씬 전환을 처음 요청할 때만 투명한 화면 덮개를 준비한다.
    private static void Initialize()
    {
        if (instance != null) return;
        var root = new GameObject("Scene Fade", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasGroup), typeof(GraphicRaycaster));
        instance = root.AddComponent<SceneFadeTransition>();
        DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        instance.cover = root.GetComponent<CanvasGroup>();
        instance.cover.alpha = 0f;
        instance.cover.blocksRaycasts = false;
        var imageObject = new GameObject("Black Cover", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(root.transform, false);
        var rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        imageObject.GetComponent<Image>().color = Color.black;
    }

    // 로드 가능한 씬만 전환하고 진행 중인 추가 요청은 무시한다.
    public static void LoadScene(string sceneName)
    {
        if (instance == null) Initialize();
        if (instance.isTransitioning) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"씬 전환 실패: Build Settings에서 {sceneName} 씬을 확인해주세요.");
            return;
        }
        instance.isTransitioning = true;
        instance.cover.blocksRaycasts = true;
        instance.StartCoroutine(instance.Transition(sceneName));
    }

    // 완전히 어두워진 뒤 씬을 로드하고 새 씬의 초기화 다음 프레임부터 밝힌다.
    private IEnumerator Transition(string sceneName)
    {
        yield return FadeTo(1f, 0.6f);
        yield return SceneManager.LoadSceneAsync(sceneName);
        yield return null;
        yield return FadeTo(0f, 1.2f);
        cover.blocksRaycasts = false;
        isTransitioning = false;
    }

    // 시간 배율과 무관하게 지정한 시간(초) 동안 투명도를 부드럽게 변경한다.
    private IEnumerator FadeTo(float target, float duration)
    {
        float initial = cover.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            cover.alpha = Mathf.Lerp(initial, target, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }
        cover.alpha = target;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}

