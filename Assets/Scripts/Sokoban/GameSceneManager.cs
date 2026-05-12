using UnityEngine;
using SceneMgr = UnityEngine.SceneManagement.SceneManager;
using LoadMode = UnityEngine.SceneManagement.LoadSceneMode;

/// <summary>
/// 按名字切换场景。使用前先 Build Settings 里加入对应场景。
/// </summary>
public sealed class GameSceneManager : MonoBehaviour
{
    /// <summary>加载名为 <paramref name="sceneName"/> 的场景（单场景模式，会卸载当前场景）。</summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[GameSceneManager] 场景名为空。", this);
            return;
        }

        SceneMgr.LoadScene(sceneName.Trim(), LoadMode.Single);
    }
}
