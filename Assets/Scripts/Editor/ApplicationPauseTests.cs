#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>실제 상태 판정 경로와 물리·HP로 중복 알림과 백그라운드 시간 제외를 검증한다.</summary>
public sealed class ApplicationPauseTests
{
    private ApplicationPause pause; // 현재 실행의 영속 관리자다.
    private Scene scene;            // 테스트 소유 물리 오브젝트의 씬이다.

    // 배치 Editor 포커스와 독립된 실제 상태 판정 진입점을 준비한다.
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
        pause = UnityEngine.Object.FindFirstObjectByType<ApplicationPause>();
        Assert.That(pause, Is.Not.Null);
    }

    // 일시정지 상태로 실패해도 다음 검증의 배속·정적 상태를 오염시키지 않는다.
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (pause != null)
        {
            Signal("SetFocus", true); Signal("SetApplicationPaused", false); Signal("ReceiveBrowserState", "01");
            yield return null; yield return null;
        }
        Time.timeScale = 1f;
        if (scene.IsValid()) yield return SceneManager.UnloadSceneAsync(scene);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    // 두 알림의 순서·중복과 중간 복귀가 원래 배속을 덮어쓰거나 조기 재개하지 않아야 한다.
    [UnityTest]
    public IEnumerator OverlappingFocusAndPause_ResumeOnlyAfterBothClear()
    {
        Time.timeScale = .4f;
        var transitions = 0;
        ApplicationPause.Changed += _ => transitions++;
        Signal("SetFocus", false); Signal("SetFocus", false); Signal("SetApplicationPaused", true);
        Assert.That(ApplicationPause.IsPaused, Is.True); Assert.That(Time.timeScale, Is.Zero);
        Signal("SetFocus", true); yield return null; yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.True);
        Signal("SetApplicationPaused", false);
        Assert.That(ApplicationPause.IsPaused, Is.True);
        yield return null; yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.False); Assert.That(Time.timeScale, Is.EqualTo(.4f));
        Assert.That(transitions, Is.EqualTo(2));
    }

    // Unity와 브라우저의 복귀가 서로 다른 순서로 와도 모든 이탈 신호를 확인한다.
    [UnityTest]
    public IEnumerator BrowserVisibilityAndWindowFocus_AreIndependentReasons()
    {
        var resumeDelta = -1f;
        ApplicationPause.Changed += paused => { if (!paused) resumeDelta = ApplicationPause.ActiveDeltaTime; };
        Signal("ReceiveBrowserState", "11"); Signal("SetApplicationPaused", true);
        Signal("SetFocus", true); Signal("SetApplicationPaused", false);
        yield return null; Assert.That(ApplicationPause.IsPaused, Is.True);
        Signal("ReceiveBrowserState", "00"); yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.True);
        Signal("ReceiveBrowserState", "01"); yield return null; yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.False); Assert.That(resumeDelta, Is.Zero);
    }

    // 이미 0배속이던 상태는 복귀해도 임의로 재개하지 않는다.
    [UnityTest]
    public IEnumerator PriorZeroTimeScale_IsPreserved()
    {
        Time.timeScale = 0f;
        Signal("SetApplicationPaused", true); Signal("SetApplicationPaused", true);
        Signal("SetApplicationPaused", false); yield return null; yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.False); Assert.That(Time.timeScale, Is.Zero);
    }

    // 예약된 복귀 도중 다시 이탈하면 이전 복귀 코루틴이 게임을 재개하지 않는다.
    [UnityTest]
    public IEnumerator LossDuringPendingResume_CancelsThatResume()
    {
        Signal("SetFocus", false); Signal("SetFocus", true); Signal("SetApplicationPaused", true);
        yield return null; yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.True); Assert.That(Time.timeScale, Is.Zero);
        Signal("SetApplicationPaused", false); yield return null; yield return null;
        Assert.That(ApplicationPause.IsPaused, Is.False); Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    // 픽업 전부터 180초를 공유하고 픽업 시 체력을 복원하거나 충격 피해를 미리 적용하지 않는다.
    [UnityTest]
    public IEnumerator PatientTimer_RunsBeforePickupAndStopsAfterMission()
    {
        scene = SceneManager.CreateScene("Patient timer " + Guid.NewGuid());
        var root = new GameObject("Patient timer");
        SceneManager.MoveGameObjectToScene(root, scene);
        var health = root.AddComponent<PatientHealth>();
        health.ResetPatient();
        SignalHealth(health, "Advance", 90f);
        Assert.That(health.HealthPercent, Is.EqualTo(100f));
        health.SetTimerRunning(true);
        SignalHealth(health, "Advance", 90f);
        Assert.That(health.HealthPercent, Is.EqualTo(50f).Within(.001f));
        SignalHealth(health, "ApplyVehicleImpact", 60f / 21.6f);
        Assert.That(health.HealthPercent, Is.EqualTo(50f).Within(.001f));
        health.SetTransporting(true);
        SignalHealth(health, "Advance", 45f);
        Assert.That(health.HealthPercent, Is.EqualTo(25f).Within(.001f));
        health.SetTransporting(false);
        health.SetTimerRunning(false);
        SignalHealth(health, "Advance", 90f);
        Assert.That(health.HealthPercent, Is.EqualTo(25f).Within(.001f));
        health.SetTimerRunning(true);
        SignalHealth(health, "Advance", 45f);
        Assert.That(health.HealthPercent, Is.Zero);
        health.ResetPatient();
        SignalHealth(health, "Advance", 90f);
        Assert.That(health.HealthPercent, Is.EqualTo(100f));
        yield break;
    }

    // 실제 자동 물리와 HP Update를 관찰하며 Editor 코루틴에서도 유효한 실제 시간 대기를 사용한다.
    [UnityTest]
    public IEnumerator Background_StopsAutomaticPhysicsAndPatientHealth()
    {
        scene = SceneManager.CreateScene("Pause physics " + Guid.NewGuid());
        var root = new GameObject("Paused body"); SceneManager.MoveGameObjectToScene(root, scene);
        var body = root.AddComponent<Rigidbody2D>(); body.gravityScale = 0f;
        root.AddComponent<BoxCollider2D>().size = Vector2.one * .1f;
        var initial = new Vector2(-500f, -500f);
        body.position = initial;
        var health = root.AddComponent<PatientHealth>(); health.SetTransporting(true); health.SetTimerRunning(true);
        yield return null;
        body.position = initial; body.WakeUp();
        body.linearVelocity = Vector2.right * 4f; body.angularVelocity = 40f;
        yield return new WaitForSecondsRealtime(.08f);
        Assert.That(body.position, Is.Not.EqualTo(initial), "정지 검사 전 자동 물리가 실제로 진행해야 한다.");
        Signal("SetFocus", false);
        var position = body.position; var rotation = body.rotation; var hp = health.HealthPercent;
        SignalHealth(health, "Advance", 100f); SignalHealth(health, "ApplyVehicleImpact", 4f);
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(body.position, Is.EqualTo(position)); Assert.That(body.rotation, Is.EqualTo(rotation));
        Assert.That(health.HealthPercent, Is.EqualTo(hp)); Assert.That(ApplicationPause.ActiveDeltaTime, Is.Zero);
        Signal("SetFocus", true); yield return null; yield return null;
        yield return new WaitForSecondsRealtime(.08f);
        Assert.That(body.position, Is.Not.EqualTo(position)); Assert.That(health.HealthPercent, Is.LessThan(hp));
    }

    // 실제 플랫폼 콜백이 사용하는 동일한 내부 판정 진입점으로 순서와 중복을 재현한다.
    private void Signal(string name, object value) => typeof(ApplicationPause)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pause, new[] { value });

    // 프레임 경계 이전에 도착한 피해도 일시정지 중에는 적용되지 않는지 확인한다.
    private static void SignalHealth(PatientHealth health, string name, float value) => typeof(PatientHealth)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(health, new object[] { value });
}

#endif
