using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 挂在关卡按钮的父物体上
/// 从按钮物体名中解析括号里的数字，例如 <c>Level(1)</c>、<c>level（2）</c> → 关卡号 1、2。
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSelectButtons : MonoBehaviour
{
    [Tooltip("不拖则在运行时查找；也可手动拖场景管理器。")]
    [SerializeField] GameSceneManager sceneManager;

    [Tooltip("场景名格式，{0} 为关卡号。需与 Build Settings 里场景名一致，例如 \"Level 1\" 则填 \"Level {0}\"。")]
    [SerializeField] string sceneNameFormat = "Level {0}";

    static readonly Regex s_ParenNumber = new(@"[\(（](\d+)[\)）]", RegexOptions.Compiled);

    void Awake()
    {
        if (sceneManager == null)
            sceneManager = FindFirstObjectByType<GameSceneManager>();

        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            var button = child.GetComponent<Button>();
            if (button == null)
                continue;

            if (!TryParseLevelIndexFromObjectName(child.name, out var levelIndex))
                continue;

            var sceneName = string.Format(sceneNameFormat, levelIndex);
            button.onClick.RemoveAllListeners();
            var captured = sceneName;
            button.onClick.AddListener(() => LoadScene(captured));
        }
    }

    static bool TryParseLevelIndexFromObjectName(string objectName, out int levelIndex)
    {
        levelIndex = 0;
        if (string.IsNullOrEmpty(objectName))
            return false;

        var m = s_ParenNumber.Match(objectName);
        if (m.Success && int.TryParse(m.Groups[1].Value, out levelIndex))
            return true;

        return false;
    }

    void LoadScene(string sceneName)
    {
        if (sceneManager != null)
        {
            sceneManager.LoadScene(sceneName);
            return;
        }

        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        SceneManager.LoadScene(sceneName.Trim(), LoadSceneMode.Single);
    }
}
