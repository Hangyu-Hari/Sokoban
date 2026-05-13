using UnityEngine;
using SceneMgr = UnityEngine.SceneManagement.SceneManager;
using LoadMode = UnityEngine.SceneManagement.LoadSceneMode;
using UnitySceneManagement = UnityEngine.SceneManagement;

/// <summary>
/// 按名字切换场景
/// </summary>
public sealed class GameSceneManager : MonoBehaviour
{
    static GameSceneManager _instance;

    [Header("相机背景颜色")]
    [SerializeField] bool applyMainCameraBackgroundColor = true;
    [SerializeField] Color cameraBackgroundColor = new(0.19215687f, 0.3019608f, 0.4745098f, 0f);

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        UnitySceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        UnitySceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this)
            _instance = null;
    }

    void OnSceneLoaded(UnitySceneManagement.Scene scene, UnitySceneManagement.LoadSceneMode mode)
    {
        if (!isActiveAndEnabled)
            return;
        ApplyMainCameraBackground();
    }

    /// <summary>加载名为 <paramref name="sceneName"/> 的场景（单场景模式，会卸载当前场景）。</summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[GameSceneManager] 场景名为空。", this);
            return;
        }

        SceneMgr.LoadScene(sceneName.Trim(), LoadMode.Single);
        ApplyMainCameraBackground();
    }

    void ApplyMainCameraBackground()
    {
        if (!applyMainCameraBackgroundColor)
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = cameraBackgroundColor;
    }
}
