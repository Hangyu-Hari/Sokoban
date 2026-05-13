using UnityEngine;
using SceneMgr = UnityEngine.SceneManagement.SceneManager;
using LoadMode = UnityEngine.SceneManagement.LoadSceneMode;
using UnitySceneManagement = UnityEngine.SceneManagement;

/// <summary>
/// 常驻场景管理：按名/下标加载、重开当前关、下一关（Build Settings 顺序），以及加载后主相机背景色。
/// </summary>
public sealed class GameSceneManager : MonoBehaviour
{
    static GameSceneManager _instance;

    public static GameSceneManager Instance => _instance;

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

    /// <summary>重新加载当前场景（同 buildIndex，用于重开本关）。</summary>
    public void RestartCurrentScene()
    {
        var active = UnitySceneManagement.SceneManager.GetActiveScene();
        if (!active.IsValid())
        {
            Debug.LogWarning("[GameSceneManager] 当前场景无效，无法重开。", this);
            return;
        }

        SceneMgr.LoadScene(active.buildIndex, LoadMode.Single);
        ApplyMainCameraBackground();
    }

    /// <summary>加载 Build Settings 中的下一关（当前 buildIndex + 1）。</summary>
    public void LoadNextScene()
    {
        var nextIndex = UnitySceneManagement.SceneManager.GetActiveScene().buildIndex + 1;
        if (nextIndex >= UnitySceneManagement.SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning("[GameSceneManager] 没有下一关：已是 Build Settings 里最后一个场景，或下标无效。", this);
            return;
        }

        SceneMgr.LoadScene(nextIndex, LoadMode.Single);
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
