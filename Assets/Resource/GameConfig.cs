using UnityEngine;

[CreateAssetMenu(fileName = "GameConfig", menuName = "Configs/GameConfig")]
public sealed class GameConfig : ScriptableObject
{
    [Header("API Configuration")]
    [SerializeField, Tooltip("Production 또는 Preview의 API origin")]
    private string _apiBaseUrl = "https://gamelab-week08-log-server.vercel.app";

    [SerializeField, Tooltip("Vercel의 W08LOG_UPLOAD_TOKEN")]
    private string _uploadToken;

    public string ApiBaseUrl => _apiBaseUrl;
    public string UploadToken => _uploadToken;
}