using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>기존 씬의 글꼴과 안전 영역을 사용해 로그인·최고 기록·재시도 입력을 연결한다.</summary>
public sealed class FirebaseAccountPanel : MonoBehaviour
{
    private FirebaseSession session;    // 씬 전환과 독립적으로 유지할 인증·동기화 서비스다.
    private TMP_Text status;            // 서버 확인 전의 기록과 통신 실패를 구별해 안내한다.
    private Button action;              // Start의 로그인 또는 Waiting의 동기화 재시도다.
    private Button logout;              // Waiting에서만 표시할 인증 해제 입력이다.
    private bool isStart;               // 로그인 화면과 기록 안내의 역할을 구분한다.
    private Func<bool> canInteract;     // 저장·출동·확인창 도중 이탈을 막는 기존 화면 조건이다.
    private RectTransform safeArea;     // Start에서는 별도 안전 영역을 화면 크기에 맞춰 적용한다.
    private Rect previousArea;          // 안전 영역이 바뀔 때만 앵커를 계산한다.
    private Vector2Int previousScreen;  // 해상도 변경을 감지한다.
    private CanvasGroup group;          // 기존 평가·브리핑 입력과 충돌할 때 패널을 숨긴다.

    // 직렬화 참조를 추가하지 않고 기존 컨트롤러가 가진 부모와 글꼴을 사용한다.
    public static FirebaseAccountPanel Create(RectTransform parent, TMP_Text fontSource, bool start, Func<bool> allowed)
    {
        var root = new GameObject("Firebase Account", typeof(RectTransform), typeof(CanvasGroup));
        root.SetActive(false);
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        var panel = root.AddComponent<FirebaseAccountPanel>();
        panel.session = FirebaseSession.Instance;
        panel.isStart = start;
        panel.canInteract = allowed;
        panel.group = root.GetComponent<CanvasGroup>();
        var bounds = (RectTransform)root.transform;
        if (start)
        {
            bounds.anchorMin = Vector2.zero;
            bounds.anchorMax = Vector2.one;
            bounds.offsetMin = bounds.offsetMax = Vector2.zero;
            panel.safeArea = bounds;
        }
        else SetBounds(bounds, new Vector2(0.04f, 0.025f), new Vector2(0.96f, 0.165f));

        var card = new GameObject("Account Card", typeof(RectTransform), typeof(Image));
        card.layer = root.layer;
        card.transform.SetParent(root.transform, false);
        SetBounds((RectTransform)card.transform, start ? new Vector2(0.08f, 0.32f) : Vector2.zero,
            start ? new Vector2(0.92f, 0.57f) : Vector2.one);
        card.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.16f, 0.94f);
        card.GetComponent<Image>().raycastTarget = false;
        panel.status = MakeText(card.transform, fontSource);
        panel.status.richText = false;
        SetBounds(panel.status.rectTransform, new Vector2(0.04f, 0.37f), new Vector2(0.96f, 0.96f));
        panel.action = MakeButton(card.transform, fontSource, start ? "게스트 로그인" : "동기화 재시도",
            new Vector2(0.04f, 0.04f), new Vector2(start ? 0.96f : 0.6f, 0.32f));
        if (!start) panel.logout = MakeButton(card.transform, fontSource, "로그아웃", new Vector2(0.64f, 0.04f), new Vector2(0.96f, 0.32f));
        root.SetActive(true);
        return panel;
    }

    // 새 UI 텍스트에도 프로젝트의 폰트·재질·가독성을 유지한다.
    private static TMP_Text MakeText(Transform parent, TMP_Text source)
    {
        var root = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        var text = root.GetComponent<TextMeshProUGUI>();
        text.font = source.font;
        text.fontSharedMaterial = source.fontSharedMaterial;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12;
        text.fontSizeMax = 18;
        text.raycastTarget = false;
        return text;
    }

    // 이벤트를 복제하지 않는 새 버튼에 기존 누름 효과를 적용한다.
    private static Button MakeButton(Transform parent, TMP_Text source, string label, Vector2 min, Vector2 max)
    {
        var root = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        SetBounds((RectTransform)root.transform, min, max);
        root.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.45f);
        var button = root.GetComponent<Button>();
        button.targetGraphic = root.GetComponent<Image>();
        var text = MakeText(root.transform, source);
        text.text = label;
        SetBounds(text.rectTransform, Vector2.zero, Vector2.one);
        ButtonPressEffect.Attach(button);
        return button;
    }

    // 비율 앵커를 사용해 세로 화면 폭 변경에도 카드 안의 상대 배치를 유지한다.
    private static void SetBounds(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void OnEnable()
    {
        session.Changed += Refresh;
        action.onClick.AddListener(Act);
        if (logout != null) logout.onClick.AddListener(Logout);
        Refresh();
    }

    private void OnDisable()
    {
        session.Changed -= Refresh;
        action.onClick.RemoveListener(Act);
        if (logout != null) logout.onClick.RemoveListener(Logout);
    }

    private void Update()
    {
        var allowed = canInteract == null || canInteract();
        group.alpha = !isStart && !allowed ? 0 : 1;
        group.blocksRaycasts = allowed;
        group.interactable = allowed && !ApplicationPause.IsPaused;
        if (safeArea == null) return;
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (Screen.width <= 0 || Screen.height <= 0 || (previousArea == Screen.safeArea && screen == previousScreen)) return;
        previousArea = Screen.safeArea;
        previousScreen = screen;
        SetBounds(safeArea, new Vector2(previousArea.xMin / Screen.width, previousArea.yMin / Screen.height),
            new Vector2(previousArea.xMax / Screen.width, previousArea.yMax / Screen.height));
    }

    // 서버 확인 전에는 점수 0 대신 미확인으로 표시한다.
    private void Refresh()
    {
        status.text = session.DisplayName + (isStart ? "" : " · 서버 최고: " + (session.BestScore.HasValue ? session.BestScore.Value + "점" : "미확인"))
            + "\n" + session.Status;
        action.interactable = !session.IsBusy && (!isStart || !session.IsLoggedIn);
        if (logout != null) logout.interactable = !SceneFadeTransition.IsTransitioning;
    }

    // 실패 재시도도 현재 화면의 진행 조건을 통과한 경우에만 실행한다.
    private void Act()
    {
        if (ApplicationPause.IsPaused || (canInteract != null && !canInteract())) return;
        if (isStart) session.Login();
        else if (GameProgressSession.IsLoaded) session.Submit(GameProgressSession.Current.CumulativeScore);
    }

    // 기존 화면이 이탈을 허용할 때에만 대기 요청과 인증을 정리한다.
    private void Logout()
    {
        if (ApplicationPause.IsPaused || SceneFadeTransition.IsTransitioning || (canInteract != null && !canInteract())) return;
        if (!session.Logout()) return;
        MissionDispatchSession.Clear();
        SceneFadeTransition.LoadScene("Start");
    }
}
