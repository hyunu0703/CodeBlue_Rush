using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>상황 보고 API와 환자 위치를 Inspector에서 시험하고 기존 네비게이션 데모에 연결한다</summary>
[CustomEditor(typeof(PatientReport))]
public sealed class PatientReportEditor : Editor
{
    private string error;

    // 런타임 보고 데이터와 명시적인 시작 및 취소 버튼을 표시한다
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        PatientReport report = (PatientReport)target;
        EditorGUILayout.LabelField("Status", report.Status.ToString());
        EditorGUILayout.LabelField("Cached Candidates", report.CandidateCount.ToString());
        if (report.IsActive)
        {
            EditorGUILayout.HelpBox(report.Message, MessageType.Info);
            if (report.Patient.TryGetPose(out Vector3 position, out _))
                EditorGUILayout.Vector3Field("Patient Position", position);
        }
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Issue Report"))
                report.TryReport(out error);
            if (GUILayout.Button("Cancel Report"))
            {
                report.CancelReport();
                error = null;
            }
        }
        if (!string.IsNullOrEmpty(error))
            EditorGUILayout.HelpBox(error, MessageType.Warning);
    }

    // 저장된 네비게이션 데모를 복사하여 5단계 전용 연결 씬을 만든다
    [MenuItem("CodeBlue Rush/Patient Report/Create Demo Scene")]
    public static void CreateDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/NavigationDemo.unity");
        NavigationRoute route = Object.FindFirstObjectByType<NavigationRoute>();
        PatientReport report = route.gameObject.AddComponent<PatientReport>();
        report.Configure(route);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PatientReportDemo.unity");
        Selection.activeGameObject = report.gameObject;
    }
}
