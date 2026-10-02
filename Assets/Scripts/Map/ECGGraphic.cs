using UnityEngine;
using UnityEngine.UI;

/// <summary>ECG 값만 읽어 색상과 진폭 및 선 강도를 표현하는 세로 화면용 파동 UI다</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ECGGraphic : MaskableGraphic
{
    [SerializeField] private PatientECG source;
    private PatientECG subscribedSource;

    // 데이터 참조만 연결하며 별도의 ECG 값이나 생존 타이머를 만들지 않는다
    public void Configure(PatientECG ecg)
    {
        Unsubscribe();
        source = ecg;
        if (isActiveAndEnabled)
            Subscribe();
        SetVerticesDirty();
    }

    // UI 활성화 시 값 변경 알림을 연결한다
    protected override void OnEnable()
    {
        base.OnEnable();
        Subscribe();
    }

    // UI 비활성화가 ECG 감소에 영향을 주지 않도록 구독만 해제한다
    protected override void OnDisable()
    {
        Unsubscribe();
        base.OnDisable();
    }

    // 살아 있는 환자 파동의 시각적 위상만 갱신한다
    private void Update()
    {
        if (source && source.HasPatient && source.Value > 0f)
            SetVerticesDirty();
    }

    // Flatline과 취소도 즉시 그리도록 데이터 변경을 구독한다
    private void Subscribe()
    {
        Unsubscribe();
        subscribedSource = source;
        if (subscribedSource)
            subscribedSource.Changed += SetVerticesDirty;
    }

    // 이전 데이터 객체의 UI 구독을 해제한다
    private void Unsubscribe()
    {
        if (subscribedSource)
            subscribedSource.Changed -= SetVerticesDirty;
        subscribedSource = null;
    }

    // 실제 ECG 비율로 파동 크기와 두께 및 밝기를 함께 결정한다
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (!source || !source.HasPatient)
            return;
        float strength = source.Value / 100f;
        Color tint = source.State == PatientECG.ECGState.Green ? new Color(0.2f, 1f, 0.45f) : source.State == PatientECG.ECGState.Yellow ? new Color(1f, 0.85f, 0.15f) : new Color(1f, 0.2f, 0.2f);
        tint.a = Mathf.Lerp(0.45f, 1f, strength);
        Rect rect = GetPixelAdjustedRect();
        float amplitude = rect.height * 0.4f * strength;
        float thickness = Mathf.Lerp(1.2f, 3f, strength);
        float phase = strength > 0f ? (float)(Time.timeAsDouble % 1d) : 0f;
        const int segments = 160;
        Vector2 previous = new Vector2(rect.xMin, rect.center.y + Pulse(phase) * amplitude);
        for (int i = 1; i <= segments; i++)
        {
            float fraction = i / (float)segments;
            Vector2 next = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, fraction), rect.center.y + Pulse(fraction * 3f + phase) * amplitude);
            Vector2 normal = new Vector2(-(next - previous).y, (next - previous).x).normalized * (thickness * 0.5f);
            int index = mesh.currentVertCount;
            mesh.AddVert(previous - normal, tint, Vector2.zero);
            mesh.AddVert(previous + normal, tint, Vector2.zero);
            mesh.AddVert(next + normal, tint, Vector2.zero);
            mesh.AddVert(next - normal, tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
            previous = next;
        }
    }

    // 짧은 P와 QRS 및 T 모양을 삼각 펄스로 표현한다
    private static float Pulse(float phase)
    {
        float x = Mathf.Repeat(phase, 1f);
        return Triangle(x, 0.16f, 0.07f) * 0.15f - Triangle(x, 0.35f, 0.035f) * 0.25f + Triangle(x, 0.4f, 0.035f) - Triangle(x, 0.45f, 0.035f) * 0.4f + Triangle(x, 0.68f, 0.1f) * 0.25f;
    }

    // 지정 중심의 한 파동 성분을 계산한다
    private static float Triangle(float x, float center, float width)
    {
        return Mathf.Max(0f, 1f - Mathf.Abs(x - center) / width);
    }
}
