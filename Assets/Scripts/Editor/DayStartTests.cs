#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>실제 GamePlay 시작 안내의 문구·입력·HP 경계와 시민 최초 배치의 보간 잔상을 검증한다.</summary>
public sealed class DayStartTests
{
    private GameObject root; // 개별 표시 및 시민 검증에서 생성한 임시 오브젝트다.

    // 현재 프로젝트의 리로드 정책을 유지해 런타임 초기화를 검증한다.
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode(!EditorSettings.enterPlayModeOptionsEnabled
            || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0);
    }

    // 사용자 저장 파일에 접근하지 않고 테스트가 주입한 진행 상태를 실행 종료와 함께 비운다.
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.Destroy(root);
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    // 부족 실적이 있을 때만 부제목을 표시하고 목표 달성 후에는 제목을 중앙에 둔다.
    [UnityTest]
    public IEnumerator Labels_ShowRequiredScoreOnlyWhenPositive()
    {
        root = new GameObject("Day overlay test", typeof(RectTransform), typeof(TextMeshProUGUI));
        var source = root.GetComponent<TMP_Text>();
        source.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("6119818e450cedf4fa78f902a374f431"));
        var display = new GameObject("Overlay", typeof(RectTransform));
        display.transform.SetParent(root.transform, false);
        var overlay = display.AddComponent<DayStartOverlay>();
        overlay.Prepare(source, new GameProgress());
        var title = display.transform.Find("Day Title").GetComponent<TMP_Text>();
        var subtitle = display.transform.Find("Remaining Score").GetComponent<TMP_Text>();
        Assert.That(title.text, Is.EqualTo("1일차"));
        Assert.That(subtitle.text, Is.EqualTo("(남은 실적 : 200)"));
        Assert.That(subtitle.fontSizeMax, Is.LessThan(title.fontSizeMax));
        Assert.That(subtitle.rectTransform.anchoredPosition.y, Is.LessThan(title.rectTransform.anchoredPosition.y));
        Assert.That(GameProgress.TryRestore(4, 200, 1, ProgressStage.Ready, out var reached), Is.True);
        overlay.Prepare(source, reached);
        Assert.That(title.text, Is.EqualTo("4일차"));
        Assert.That(subtitle.gameObject.activeSelf, Is.False);
        Assert.That(title.rectTransform.anchoredPosition.y, Is.Zero);
        overlay.Hide();
        Assert.That(display.activeSelf, Is.False);
        Assert.That(display.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
        yield return null;
    }

    // 실제 씬에서 안내 도중 HP·물리 잠금이 유지되고 재활성화 후 안내 종료 시 함께 복구된다.
    [UnityTest]
    public IEnumerator GamePlay_StartsMissionOnlyAfterIntroAndResumesAfterDisable()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(GameProgressSession).GetField("current", flags).SetValue(null, new GameProgress());
        typeof(GameProgressSession).GetField("isLoaded", flags).SetValue(null, true);
        Assert.That(MissionDispatchSession.TryDispatch(1), Is.True);
        SceneManager.LoadScene("GamePlay");
        yield return null;
        yield return null;
        var mission = Object.FindFirstObjectByType<MissionController>();
        var health = Object.FindFirstObjectByType<PatientHealth>();
        var body = health.GetComponent<Rigidbody2D>();
        var input = body.GetComponent<AmbulanceDriveInput>();
        var inputAllowed = typeof(AmbulanceDriveInput).GetField("isMissionDrivingAllowed", BindingFlags.Instance | BindingFlags.NonPublic);
        var overlay = Object.FindFirstObjectByType<DayStartOverlay>();
        Assert.That(overlay, Is.Not.Null);
        Assert.That(mission.IsMissionActive, Is.False);
        Assert.That(inputAllowed.GetValue(input), Is.False);
        Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints2D.FreezeAll));
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(health.HealthPercent, Is.EqualTo(100f));
        mission.enabled = false;
        Assert.That(overlay.gameObject.activeSelf, Is.False);
        yield return null;
        mission.enabled = true;
        Assert.That(overlay.gameObject.activeSelf, Is.True);
        var deadline = Time.realtimeSinceStartupAsDouble + 8d;
        while (!mission.IsMissionActive)
        {
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline));
            Assert.That(health.HealthPercent, Is.EqualTo(100f));
            yield return null;
        }
        Assert.That(overlay.gameObject.activeSelf, Is.False);
        Assert.That(body.constraints, Is.Not.EqualTo(RigidbodyConstraints2D.FreezeAll));
        Assert.That(inputAllowed.GetValue(input), Is.True);
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.That(health.HealthPercent, Is.LessThan(100f));
    }

    // 생성과 풀 재사용 시 먼 이전 위치가 다음 렌더 프레임의 Transform에 남지 않아야 한다.
    [UnityTest]
    public IEnumerator CitizenTeleport_DiscardsPreviousRenderedPosition()
    {
        root = new GameObject("Citizen teleport test", typeof(SpriteRenderer), typeof(BoxCollider2D),
            typeof(Rigidbody2D), typeof(RoadCitizenCollision));
        var citizen = root.GetComponent<RoadCitizenCollision>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var configure = typeof(RoadCitizenCollision).GetMethod("Configure", flags);
        var move = typeof(RoadCitizenCollision).GetMethod("MoveTo", flags);
        foreach (var position in new[] { new Vector2(80f, 40f), new Vector2(-70f, -30f) })
        {
            configure.Invoke(citizen, new object[] { new RoadCitizenSimulation.Citizen() });
            move.Invoke(citizen, new object[] { position, true });
            Assert.That((Vector2)root.transform.position, Is.EqualTo(position));
            Assert.That(citizen.Body.position, Is.EqualTo(position));
            Assert.That(citizen.Body.interpolation, Is.EqualTo(RigidbodyInterpolation2D.Interpolate));
            yield return null;
            Assert.That(Vector2.Distance(root.transform.position, position), Is.LessThan(0.01f));
            root.SetActive(false);
            root.SetActive(true);
        }
    }
}
#endif
