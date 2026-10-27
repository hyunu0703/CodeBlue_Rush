using UnityEngine;
using UnityEngine.UI;

/// <summary>최근 4초의 심전도를 왼쪽으로 흘려보내고 오래된 기록의 감광을 표시한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PatientECGGraphic : MaskableGraphic
{
    private const int SampleRate = 240;                                // 빠른 박동의 좁은 주봉도 보존하기 위한 초당 샘플 수다.
    private const float WindowSeconds = 4f;                            // 기존 3.2초 대비 이동 속도를 20% 낮추는 표시 시간 범위(초)다.
    private const int Capacity = (int)(SampleRate * WindowSeconds) + 1; // 양 끝점을 포함하는 고정 길이 순환 버퍼다.
    private readonly float[] samples = new float[Capacity];            // 이미 그려진 파형 높이를 보존한다.
    private readonly Color[] colors = new Color[Capacity];             // 이미 그려진 파형 색상을 보존한다.
    private readonly PatientECGSignal signal = new PatientECGSignal(); // 이 패널만 사용하는 박동 생성기다.
    private PatientHealth patient;                                     // 미션 관리자가 연결하는 HP 원본이다.
    private int first;                                                 // 순환 버퍼에서 가장 오래된 샘플 위치다.
    private float remainder;                                           // 프레임 사이에 남은 샘플 미만의 시간(초)이다.

    // 새 환자의 안정 파형으로 화면 이력을 채워 이전 미션의 흔적을 제거한다.
    public void ResetPatient(PatientHealth value)
    {
        patient = value;
        raycastTarget = false;
        first = 0;
        remainder = 0f;
        signal.Reset();
        for (var index = 0; index < Capacity; index++)
        {
            samples[index] = signal.Step(1f / SampleRate, 1f, out colors[index]);
        }
        SetVerticesDirty();
    }

    private void Update()
    {
        if (ApplicationPause.IsPaused || patient == null || Time.deltaTime <= 0f) return;
        remainder += Time.deltaTime;
        // 프레임률에 관계없이 같은 샘플 간격을 사용하고 힙 할당 없이 이력을 교체한다.
        while (remainder >= 1f / SampleRate)
        {
            samples[first] = signal.Step(1f / SampleRate, patient.NormalizedHealth, out colors[first]);
            first = (first + 1) % Capacity;
            remainder -= 1f / SampleRate;
        }
        SetVerticesDirty();
    }

    // 평균 접선으로 연결된 선 띠를 그려 주봉 연결부가 벌어지지 않게 한다.
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (patient == null) return;
        var bounds = rectTransform.rect;
        if (bounds.width <= 0f || bounds.height <= 0f) return;
        var scale = canvas != null ? Mathf.Max(0.001f, canvas.scaleFactor) : 1f;
        var halfWidth = Mathf.Clamp(bounds.height * scale * 0.01f, 1.5f, 3f) / scale * 0.5f;
        var width = bounds.width;
        var step = width / (Capacity - 1);
        var offset = remainder / WindowSeconds * width;
        for (var index = 0; index < Capacity; index++)
        {
            var previous = (first + Mathf.Max(0, index - 1)) % Capacity;
            var current = (first + index) % Capacity;
            var next = (first + Mathf.Min(Capacity - 1, index + 1)) % Capacity;
            var tangent = new Vector2(step * (index == 0 || index == Capacity - 1 ? 1f : 2f),
                (samples[next] - samples[previous]) * bounds.height).normalized;
            var normal = new Vector2(-tangent.y, tangent.x) * halfWidth;
            var point = new Vector2(bounds.xMin + index * step - offset,
                bounds.center.y + samples[current] * bounds.height);
            // 저장된 HP 색상은 유지하고 오래된 쪽의 RGB 밝기만 55%까지 낮춘다.
            var brightness = Mathf.Lerp(0.55f, 1f, (float)index / (Capacity - 1));
            var sampleColor = colors[current];
            sampleColor.r *= brightness;
            sampleColor.g *= brightness;
            sampleColor.b *= brightness;
            mesh.AddVert(point - normal, sampleColor, Vector2.zero);
            mesh.AddVert(point + normal, sampleColor, Vector2.zero);
            if (index == 0) continue;
            var vertex = index * 2;
            mesh.AddTriangle(vertex - 2, vertex - 1, vertex);
            mesh.AddTriangle(vertex, vertex - 1, vertex + 1);
        }
    }
}
