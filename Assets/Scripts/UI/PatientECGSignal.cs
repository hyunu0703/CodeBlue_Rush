using UnityEngine;

/// <summary>환자 HP에 맞춰 박동 모양, 간격과 빈사 공백을 생성하는 표시 전용 신호다.</summary>
internal sealed class PatientECGSignal
{
    private readonly System.Random random = new System.Random(173); // 교통 및 미션 시드와 독립적인 박동 변동을 만든다.
    private float elapsed;                                          // 현재 박동이 시작된 이후의 시간(초)이다.
    private float nextBeat;                                         // 다음 박동까지의 시작 간격(초)이다.
    private float beatDuration;                                     // 평탄 구간을 제외한 파형 길이(초)다.
    private float height;                                           // 패널 높이에 대한 최고점~최저점 비율이다.
    private float smoothHealth = 1f;                                // 높이와 색의 급격한 변화를 완화하는 HP 비율이다.
    private int remainingBeats;                                     // 빈사 묶음 안에서 추가로 발생할 박동 수다.
    private bool wasCritical;                                       // 빈사 구간 진입 시 이전 묶음을 이어받지 않게 한다.
    private bool wasFlat;                                           // 평선 이후 HP가 복원되면 새 박동부터 시작한다.

    // 새 미션에서 이전 리듬과 빈사 공백을 제거한다.
    public void Reset()
    {
        elapsed = 0f;
        nextBeat = 0f;
        remainingBeats = 0;
        smoothHealth = 1f;
        wasCritical = false;
        wasFlat = false;
    }

    // 일정한 샘플 시간 간격으로 새 파형만 생성하며 HP 0에서는 즉시 평선을 반환한다.
    public float Step(float seconds, float health, out Color sampleColor)
    {
        health = Mathf.Clamp01(health);
        smoothHealth = Mathf.MoveTowards(smoothHealth, health, seconds * 3f);
        sampleColor = GetColor(health <= 0f ? 0f : smoothHealth);
        if (health <= 0f)
        {
            wasFlat = true;
            return 0f;
        }
        if (wasFlat)
        {
            nextBeat = elapsed = 0f;
            remainingBeats = 0;
            wasFlat = false;
        }
        if (elapsed >= nextBeat)
        {
            elapsed -= nextBeat;
            BeginBeat(health);
        }
        var value = EvaluateWave(elapsed / beatDuration) * height / 1.95f;
        elapsed += seconds;
        return value;
    }

    // 초록·노랑·빨강 기준점 사이의 색을 연속적으로 보간한다.
    internal static Color GetColor(float health)
    {
        var green = new Color32(53, 232, 121, 255);
        var yellow = new Color32(255, 216, 74, 255);
        var red = new Color32(255, 77, 77, 255);
        return health >= 0.4f
            ? Color.Lerp(yellow, green, Mathf.InverseLerp(0.4f, 0.7f, health))
            : Color.Lerp(red, yellow, Mathf.InverseLerp(0.15f, 0.4f, health));
    }

    // 박동마다만 높이와 간격을 선택하여 한 박동 안의 무작위 떨림을 방지한다.
    private void BeginBeat(float health)
    {
        var critical = health <= 0.15f;
        if (critical != wasCritical) remainingBeats = 0;
        wasCritical = critical;
        // 이동 속도 감소와 같은 비율로 파형 시간을 늘려 화면에 보이는 모양과 폭을 보존한다.
        beatDuration = (critical ? 0.36f : Mathf.Lerp(0.26f, 0.48f, Mathf.InverseLerp(0.15f, 0.7f, health))) * 1.25f;
        if (critical)
        {
            height = Mathf.Lerp(0.08f, 0.25f, health / 0.15f);
            if (remainingBeats == 0)
            {
                remainingBeats = health > 0.1f ? 2 : health > 0.05f ? random.Next(1, 3) : 1;
            }
            remainingBeats--;
            if (remainingBeats > 0)
            {
                nextBeat = 0.84f;
            }
            else
            {
                // 빈사 구간의 공백도 20% 늘리되 생존 신호가 최대 3초 안에 다시 나오게 한다.
                var minimum = health > 0.1f ? 1.2f : health > 0.05f ? 1.8f : 2.64f;
                var maximum = health > 0.1f ? 1.8f : health > 0.05f ? 2.64f : 3f;
                nextBeat = beatDuration + RandomRange(minimum, maximum);
            }
            return;
        }
        var bpm = health >= 0.7f ? Mathf.Lerp(90f, 75f, (health - 0.7f) / 0.3f)
            : health >= 0.4f ? Mathf.Lerp(120f, 90f, (health - 0.4f) / 0.3f)
            : Mathf.Lerp(150f, 120f, (health - 0.15f) / 0.25f);
        var variation = health >= 0.7f ? 0f : health >= 0.4f
            ? 0.1f * Mathf.SmoothStep(0f, 1f, (0.7f - health) / 0.05f)
            : Mathf.Lerp(0.1f, 0.25f, Mathf.SmoothStep(0f, 1f, (0.4f - health) / 0.05f));
        // 직전 1.4배 간격에서 20% 더 늘리고 파형 자체의 폭과 이동 속도는 유지한다.
        nextBeat = 60f / bpm * RandomRange(1f - variation, 1f + variation) * 1.68f;
        // 15% 진입 전에 25% 높이로 접근시켜 빈사 상태와의 높이 단절을 줄인다.
        var baseHeight = Mathf.Lerp(0.25f, 0.4f, Mathf.SmoothStep(0f, 1f, (smoothHealth - 0.15f) / 0.05f));
        baseHeight = Mathf.Lerp(baseHeight, 0.45f, Mathf.SmoothStep(0f, 1f, (smoothHealth - 0.35f) / 0.05f));
        height = baseHeight * RandomRange(1f - variation, 1f + variation);
    }

    // 프로젝트의 전역 난수 상태를 바꾸지 않고 표시용 변동을 선택한다.
    private float RandomRange(float minimum, float maximum)
    {
        return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
    }

    // 참고 이미지처럼 작은 선행 굴곡, 뾰족한 상승, 비슷한 깊이의 하강과 둥근 회복 봉우리를 연결한다.
    internal static float EvaluateWave(float phase)
    {
        if (phase < 0f || phase >= 1f) return 0f;
        if (phase < 0.1f) return Mathf.SmoothStep(0f, -0.22f, phase / 0.1f);
        if (phase < 0.18f) return Mathf.SmoothStep(-0.22f, 0.12f, (phase - 0.1f) / 0.08f);
        if (phase < 0.23f) return Mathf.Lerp(0.12f, -0.1f, (phase - 0.18f) / 0.05f);
        if (phase < 0.38f) return Mathf.Lerp(-0.1f, 1f, (phase - 0.23f) / 0.15f);
        if (phase < 0.52f) return Mathf.Lerp(1f, -0.95f, (phase - 0.38f) / 0.14f);
        if (phase < 0.65f) return Mathf.Lerp(-0.95f, 0f, (phase - 0.52f) / 0.13f);
        if (phase < 0.8f) return Mathf.SmoothStep(0f, 0.33f, (phase - 0.65f) / 0.15f);
        return Mathf.SmoothStep(0.33f, 0f, (phase - 0.8f) / 0.2f);
    }
}
