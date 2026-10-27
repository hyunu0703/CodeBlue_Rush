using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>씬을 넘어 앱·포커스·탭 숨김을 함께 추적하고 진행 시간과 입력을 일시정지한다.</summary>
[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class ApplicationPause : MonoBehaviour
{
    private static ApplicationPause instance; // 실행 세션을 소유하는 유일한 일시정지 관리자다.
    private readonly List<BaseInputModule> blockedModules = new List<BaseInputModule>(); // 복귀 때만 되살릴 UI 입력 모듈이다.
    private bool hasFocus = true;             // Unity가 보고한 포커스 상태다.
    private bool applicationPaused;           // 플랫폼의 앱 일시정지 알림 상태다.
    private bool browserHidden;               // 브라우저 탭 숨김과 pagehide 상태다.
    private bool browserFocused = true;       // 브라우저 창 자체의 포커스 상태다.
    private float previousTimeScale;          // 최초 이탈 직전 값이며 중복 알림으로 덮어쓰지 않는다.
    private int resumeFrame = -1;             // 복귀 첫 프레임의 큰 실제 시간 간격을 제외한다.
    private Coroutine resumeRoutine;          // 모든 복귀 조건 충족 후 한 프레임을 비울 작업이다.
    private GameObject inputCover;            // 일시정지 중 화면 입력을 막는 투명 덮개다.

    public static bool IsPaused { get; private set; }
    public static float ActiveDeltaTime => IsPaused || (instance != null && instance.resumeFrame == Time.frameCount)
        ? 0f : Time.unscaledDeltaTime;
    public static event Action<bool> Changed;

    // Domain Reload가 꺼져 있어도 이전 실행의 정적 상태와 이벤트를 이어받지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        if (instance != null && IsPaused) Time.timeScale = instance.previousTimeScale;
        instance = null;
        IsPaused = false;
        Changed = null;
    }

    // 시작 씬과 직접 실행한 씬 모두에서 별도 인스펙터 설정 없이 같은 관리자를 준비한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (instance == null) new GameObject("Application Pause", typeof(RectTransform)).AddComponent<ApplicationPause>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
#if UNITY_EDITOR
        // 배치 검증에는 Game View 포커스가 없으므로 실제 OS 알림 대신 같은 판정 진입점을 검증한다.
        hasFocus = Application.isBatchMode || Application.isFocused;
#else
        hasFocus = Application.isFocused;
#endif
        CreateInputCover();
        SceneManager.sceneLoaded += HandleSceneLoaded;
#if UNITY_WEBGL && !UNITY_EDITOR
        CodeBluePauseStart(gameObject.name);
#endif
        Reconcile();
    }

    private void OnApplicationFocus(bool value)
    {
#if UNITY_EDITOR
        if (Application.isBatchMode) return;
#endif
        SetFocus(value);
    }

    private void OnApplicationPause(bool value)
    {
        SetApplicationPaused(value);
    }

    // 포커스와 앱 일시정지를 독립적으로 갱신해 알림 순서가 달라도 조기 재개하지 않는다.
    internal void SetFocus(bool value)
    {
        hasFocus = value;
        Reconcile();
    }

    // Android 등에서 포커스 알림 없이 앱 일시정지만 온 경우도 같은 경계에서 처리한다.
    internal void SetApplicationPaused(bool value)
    {
        applicationPaused = value;
        Reconcile();
    }

    // JavaScript가 엔진 프레임 밖에서도 즉시 전달하는 탭·창 상태다. IL2CPP 제거를 막는다.
    [UnityEngine.Scripting.Preserve]
    private void ReceiveBrowserState(string state)
    {
        if (state == null || state.Length != 2) return;
        browserHidden = state[0] == '1';
        browserFocused = state[1] == '1';
        Reconcile();
    }

    // 하나라도 이탈 상태면 멈추며 모든 신호가 복귀해야 한 프레임 뒤 재개한다.
    private void Reconcile()
    {
        if (!hasFocus || applicationPaused || browserHidden || !browserFocused)
        {
            if (resumeRoutine != null) StopCoroutine(resumeRoutine);
            resumeRoutine = null;
            if (IsPaused) return;
            previousTimeScale = Time.timeScale;
            IsPaused = true;
            Time.timeScale = 0f;
            inputCover.SetActive(true);
            BlockInput();
            Changed?.Invoke(true);
        }
        else if (IsPaused && resumeRoutine == null) resumeRoutine = StartCoroutine(Resume());
    }

    // 복귀 첫 프레임은 0배속으로 버려 백그라운드 시간의 물리·HP·실제 시간 타이머 반영을 막는다.
    private IEnumerator Resume()
    {
        yield return null;
        resumeRoutine = null;
        if (!hasFocus || applicationPaused || browserHidden || !browserFocused) yield break;
        resumeFrame = Time.frameCount;
        Time.timeScale = previousTimeScale;
        IsPaused = false;
        foreach (var module in blockedModules) if (module != null) module.enabled = true;
        blockedModules.Clear();
        inputCover.SetActive(false);
        Changed?.Invoke(false);
    }

    // 이미 요청한 비동기 로드가 끝나도 새 씬의 UI 입력은 복귀 전까지 열지 않는다.
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsPaused) BlockInput();
    }

    // UI 모듈의 포인터·제출 대기를 비우고 원래 활성 모듈만 복귀 대상으로 보관한다.
    private void BlockInput()
    {
        var events = EventSystem.current;
        if (events == null) return;
        events.SetSelectedGameObject(null);
        foreach (var module in events.GetComponents<BaseInputModule>())
        {
            if (!module.enabled) continue;
            blockedModules.Add(module);
            module.enabled = false;
        }
    }

    // 화면 내용은 유지하면서 최상위 투명 덮개로 일시정지 중 클릭을 차단한다.
    private void CreateInputCover()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        gameObject.AddComponent<GraphicRaycaster>();
        inputCover = new GameObject("Paused Input Cover", typeof(RectTransform), typeof(Image));
        inputCover.transform.SetParent(transform, false);
        var rect = (RectTransform)inputCover.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        inputCover.GetComponent<Image>().color = Color.clear;
        inputCover.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
#if UNITY_WEBGL && !UNITY_EDITOR
        CodeBluePauseStop();
#endif
        if (IsPaused) Time.timeScale = previousTimeScale;
        foreach (var module in blockedModules) if (module != null) module.enabled = true;
        blockedModules.Clear();
        IsPaused = false;
        Changed = null;
        instance = null;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // 페이지 이벤트를 현재 Unity 인스턴스의 관리자에게 연결한다.
    [DllImport("__Internal")] private static extern void CodeBluePauseStart(string owner);
    // 종료 시 이전 인스턴스로 브라우저 이벤트가 전달되지 않도록 연결을 해제한다.
    [DllImport("__Internal")] private static extern void CodeBluePauseStop();
#endif
}
