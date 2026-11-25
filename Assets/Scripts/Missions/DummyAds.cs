using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>게임 씬에서 더미 광고를 미리 로드하고, 광고 화면과 노출·닫힘 결과를 관리한다.</summary>
public sealed class DummyAds : MonoBehaviour
{
    /// <summary>광고 준비 슬롯과 Firebase 카운터가 사용하는 구분이다.</summary>
    internal enum Kind { Interstitial, Rewarded }

    /// <summary>워터폴 순서와 비딩 가격을 비교하는 가상 광고 공급자다.</summary>
    [Serializable]
    private sealed class Network
    {
        public string name;                      // 광고 화면과 로그에 표시할 네트워크 이름이다.
        [Min(0f)] public float eCPM;             // 노출 1,000회당 가상 단가다.
        [Range(0f, 1f)] public float fillRate;    // 요청을 채우는 확률이다.
    }

    [SerializeField] private Canvas gameCanvas;           // 게임과 같은 배율을 사용하는 광고 화면의 부모다.
    [SerializeField] private TMP_FontAsset font;          // 한글 광고 종류와 남은 시간을 표시한다.
    [SerializeField] private Network[] networks = {       // 위쪽 네트워크부터 워터폴로 요청한다.
        new Network { name = "Network A", eCPM = 10f, fillRate = .5f },
        new Network { name = "Network B", eCPM = 9f, fillRate = .6f },
        new Network { name = "Network C", eCPM = 7f, fillRate = .8f }
    };
    [SerializeField] private bool useBidding;             // 전체 응답 중 가장 높은 단가를 고른다.
    [SerializeField] private bool noFill;                 // 로드와 이미 준비된 광고의 표시를 강제로 막는다.
    [SerializeField, Min(0f)] private float askSeconds = .5f; // 공급자 한 곳에 요청하는 가상 시간이다.
    [SerializeField, Min(.01f)] private float interstitialSeconds = 3f; // 전면 광고를 닫기까지 기다릴 시간이다.
    [SerializeField, Min(.01f)] private float rewardedSeconds = 3f;     // 보상 지급에 필요한 광고 시간이다.
    private readonly Network[] ready = new Network[2];    // 종류별로 한 번 표시할 광고를 보관한다.
    private readonly Coroutine[] loading = new Coroutine[2]; // 종류별로 로드와 재시도를 하나만 유지한다.
    private readonly System.Random random = new System.Random(); // 광고 선택이 게임 난수를 소비하지 않게 한다.
    private GameObject overlay;                          // 다른 게임 UI의 입력을 막는 전체 화면 패널이다.
    private TMP_Text message;                            // 광고 이름과 남은 시간을 표시한다.
    private Button closeButton;                          // 닫기 가능 시점과 앱 활성 상태를 확인한다.
    private Action<bool> onClosed;                       // 현재 광고를 닫을 때 한 번 호출한다.
    private Coroutine playback;                          // 현재 광고의 카운트다운 루틴이다.
    private Kind playingKind;                            // 전면의 조기 닫기와 보상형의 완주를 구분한다.
    private float remaining;                             // 앱이 활성 상태였던 프레임만 차감하는 초다.
    private bool previousNoFill;                         // Inspector에서 변경한 상태를 UI에 전달한다.

    internal bool IsShowing => onClosed != null;
    internal event Action Changed;                       // 준비·표시 상태가 바뀌면 결과 버튼을 갱신한다.
    internal event Action<Kind> Shown;                    // 실제 광고 화면이 열렸을 때만 노출을 한 번 알린다.

    private void OnEnable()
    {
        if (gameCanvas == null || font == null)
        {
            Debug.LogError("DummyAds의 Game Canvas와 Font를 연결해주세요.", this);
            enabled = false;
            return;
        }
        if (overlay == null) CreateOverlay();
        previousNoFill = noFill;
        BeginLoad(Kind.Interstitial);
        BeginLoad(Kind.Rewarded);
    }

    private void Update()
    {
        if (previousNoFill == noFill) return;
        previousNoFill = noFill;
        if (noFill) Array.Clear(ready, 0, ready.Length);
        BeginLoad(Kind.Interstitial);
        BeginLoad(Kind.Rewarded);
        Changed?.Invoke();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Array.Clear(loading, 0, loading.Length);
        Array.Clear(ready, 0, ready.Length);
        if (IsShowing) Finish(false, false);
        else Changed?.Invoke();
    }

    private void OnDestroy()
    {
        if (overlay != null) Destroy(overlay);
    }

    // No Fill과 이미 표시 중인 광고를 함께 확인해 실제로 열 수 있는 슬롯만 알린다.
    internal bool IsReady(Kind kind) => isActiveAndEnabled && !noFill && !IsShowing && ready[(int)kind] != null;

    // 정상적으로 화면을 연 뒤 노출을 알리고, 닫을 때에만 결과 콜백을 호출한다.
    internal bool TryShow(Kind kind, Action<bool> closed)
    {
        if (closed == null || !IsReady(kind) || ApplicationPause.IsPaused || overlay == null
            || !gameCanvas.isActiveAndEnabled) return false;
        var ad = ready[(int)kind];
        ready[(int)kind] = null;
        onClosed = closed;
        playingKind = kind;
        remaining = kind == Kind.Rewarded ? rewardedSeconds : interstitialSeconds;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        ApplicationPause.SetAdPlaying(true);
        overlay.SetActive(true);
        closeButton.gameObject.SetActive(kind == Kind.Rewarded);
        playback = StartCoroutine(Play(ad.name));
        Shown?.Invoke(kind);
        Changed?.Invoke();
        return true;
    }

    // 호출하는 결과 화면이 종료되면 광고를 해제하고 해당 화면의 콜백을 폐기한다.
    internal void Cancel()
    {
        if (IsShowing) Finish(false, true, false);
    }

    // 실패 재시도를 포함해 종류당 하나의 로드만 진행한다.
    private void BeginLoad(Kind kind)
    {
        var index = (int)kind;
        if (isActiveAndEnabled && ready[index] == null && loading[index] == null)
            loading[index] = StartCoroutine(Load(kind));
    }

    // 광고가 없는 동안 반복 요청하되 씬 비활성화 시 더 이상 실행하지 않는다.
    private IEnumerator Load(Kind kind)
    {
        var index = (int)kind;
        while (true)
        {
            var mode = useBidding ? "비딩" : "워터폴";
            Debug.Log($"{kind} 로드 요청 ({mode})", this);
            var winner = Pick(true, out _, out var rounds);
            yield return new WaitForSecondsRealtime(askSeconds * rounds);
            if (winner != null && !noFill)
            {
                ready[index] = winner;
                loading[index] = null;
                Debug.Log($"{kind} loaded: {winner.name}", this);
                Changed?.Invoke();
                yield break;
            }
            Debug.Log($"{kind} load failed (no fill)", this);
            yield return new WaitForSecondsRealtime(2f);
        }
    }

    // 워터폴은 첫 응답에서 멈추고 비딩은 같은 라운드에서 받은 단가를 비교한다.
    private Network Pick(bool log, out float price, out int rounds)
    {
        Network winner = null;
        price = 0f;
        rounds = 0;
        foreach (var network in networks)
        {
            if (network == null) continue;
            rounds = useBidding ? 1 : rounds + 1;
            var filled = !noFill && random.NextDouble() < network.fillRate;
            var offered = network.eCPM * (.5f + (float)random.NextDouble());
            if (log) Debug.Log($"    {network.name}: {(filled ? "$" + offered.ToString("F1") : "광고 없음")}", this);
            if (!filled) continue;
            if (winner == null || offered > price)
            {
                winner = network;
                price = offered;
            }
            if (!useBidding) break;
        }
        return winner;
    }

    // 게임 배속 대신 앱이 활성 상태였던 실제 시간만 카운트다운에 사용한다.
    private IEnumerator Play(string network)
    {
        var title = playingKind == Kind.Rewarded ? "보상형 광고" : "전면 광고";
        var displayed = -1;
        var lastSample = Time.realtimeSinceStartupAsDouble;
        while (remaining > 0f)
        {
            var seconds = Mathf.CeilToInt(remaining);
            if (seconds != displayed)
            {
                message.text = $"{title}\n{seconds}\n{network}";
                displayed = seconds;
            }
            yield return null;
            var now = Time.realtimeSinceStartupAsDouble;
            if (ApplicationPause.ActiveDeltaTime > 0f)
                remaining = Mathf.Max(0f, remaining - (float)(now - lastSample));
            lastSample = now;
        }
        message.text = $"{title}\n광고 끝\n{network}";
        closeButton.gameObject.SetActive(true);
        playback = null;
    }

    // 전면 광고는 타이머를 마쳐야 닫고, 보상형은 조기 종료도 허용한다.
    private void Close()
    {
        if (!IsShowing || ApplicationPause.IsPaused || (playingKind == Kind.Interstitial && remaining > 0f)) return;
        Finish(playingKind == Kind.Rewarded && remaining <= 0f, true);
    }

    // 콜백 전에 입력·음소거를 복구하고 재진입해도 이전 광고를 다시 닫지 않게 한다.
    private void Finish(bool earned, bool reload, bool notify = true)
    {
        if (!IsShowing) return;
        var closed = onClosed;
        onClosed = null;
        if (playback != null) StopCoroutine(playback);
        playback = null;
        if (overlay != null) overlay.SetActive(false);
        ApplicationPause.SetAdPlaying(false);
        if (reload) BeginLoad(playingKind);
        if (notify) closed(earned);
        Changed?.Invoke();
    }

    // 기존 화면 배율 아래에서 광고를 항상 결과 화면보다 위에 그린다.
    private void CreateOverlay()
    {
        overlay = new GameObject("Dummy Ad Overlay", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(Image));
        overlay.layer = gameCanvas.gameObject.layer;
        overlay.transform.SetParent(gameCanvas.transform, false);
        var canvas = overlay.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(.07f, .10f, .15f);
        message = CreateLabel("Ad text", overlay.transform);
        message.fontSize = 34f;
        message.rectTransform.anchorMin = new Vector2(.05f, .25f);
        message.rectTransform.anchorMax = new Vector2(.95f, .75f);
        message.rectTransform.offsetMin = message.rectTransform.offsetMax = Vector2.zero;

        var close = new GameObject("Close Ad", typeof(RectTransform), typeof(Image), typeof(Button));
        close.layer = overlay.layer;
        close.transform.SetParent(overlay.transform, false);
        var closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-16f, -16f);
        closeRect.sizeDelta = new Vector2(56f, 56f);
        close.GetComponent<Image>().color = new Color(.24f, .33f, .46f);
        closeButton = close.GetComponent<Button>();
        closeButton.targetGraphic = close.GetComponent<Image>();
        closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
        closeButton.onClick.AddListener(Close);
        var label = CreateLabel("Close label", close.transform);
        label.text = "X";
        label.fontSize = 30f;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        overlay.SetActive(false);
    }

    // 광고의 글자에는 레이캐스트를 주지 않아 패널과 닫기 버튼이 직접 입력을 받는다.
    private TMP_Text CreateLabel(string objectName, Transform parent)
    {
        var label = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        label.layer = overlay.layer;
        label.transform.SetParent(parent, false);
        var text = label.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    // 로드·표시 슬롯을 바꾸지 않고 현재 Inspector 설정의 채움률·단가·대기를 비교한다.
    [ContextMenu("100회 시뮬레이션")]
    private void Simulate()
    {
        var filled = 0;
        var roundsTotal = 0;
        var sum = 0f;
        for (var i = 0; i < 100; i++)
        {
            var ad = Pick(false, out var price, out var rounds);
            roundsTotal += rounds;
            if (ad == null) continue;
            filled++;
            sum += price;
        }
        var mode = useBidding ? "비딩" : "워터폴";
        Debug.Log($"{mode} 100회: 채움 {filled}%, 평균 eCPM ${(filled == 0 ? 0f : sum / filled):F1}, "
            + $"예상 수익 ${sum / 1000f:F3}, 평균 대기 {roundsTotal / 100f * askSeconds:F2}초", this);
    }
}
