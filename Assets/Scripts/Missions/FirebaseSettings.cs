using UnityEngine;

/// <summary>클라이언트에서 사용할 Firebase 프로젝트의 공개 연결 설정을 보관한다.</summary>
[CreateAssetMenu(menuName = "CodeBlue/Firebase Settings")]
public sealed class FirebaseSettings : ScriptableObject
{
    [SerializeField] private string apiKey;      // Firebase 프로젝트 설정의 Web API Key다.
    [SerializeField] private string databaseUrl; // Realtime Database의 HTTPS 루트 주소다.

    public string ApiKey => apiKey == null ? string.Empty : apiKey.Trim();
    public string DatabaseUrl => databaseUrl == null ? string.Empty : databaseUrl.Trim().TrimEnd('/');
}
