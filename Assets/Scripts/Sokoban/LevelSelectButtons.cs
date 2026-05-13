using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 挂在关卡按钮的父物体上
/// 从按钮物体名中解析括号里的数字，例如 <c>Level(1)</c>、<c>level（2）</c> → 关卡号 1、2。
/// 每个按钮根下：<b>第 1 个子物体</b>上挂 <see cref="Text"/> 或 <see cref="TextMeshProUGUI"/> 显示关卡号，<b>第 2 个子物体</b>为锁（整物体显隐）。
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSelectButtons : MonoBehaviour
{
    [Tooltip("不拖则在运行时查找；也可手动拖场景管理器。")]
    [SerializeField] GameSceneManager sceneManager;

    [Tooltip("场景名格式，{0} 为关卡号。需与 Build Settings 里场景名一致，例如 \"Level 1\" 则填 \"Level {0}\"。")]
    [SerializeField] string sceneNameFormat = "Level {0}";

    static readonly Regex s_ParenNumber = new(@"[\(（](\d+)[\)）]", RegexOptions.Compiled);

    readonly List<WiredLevelButton> _wired = new();

    sealed class WiredLevelButton
    {
        public Button Button;
        public int LevelIndex;
        public GameObject TextGo;
        public GameObject LockGo;
    }

    void Awake()
    {
        if (sceneManager == null)
            sceneManager = FindFirstObjectByType<GameSceneManager>();

        _wired.Clear();

        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            var button = child.GetComponent<Button>();
            if (button == null)
                continue;

            if (!TryParseLevelIndexFromObjectName(child.name, out var levelIndex))
                continue;

            if (child.childCount < 2)
            {
                Debug.LogWarning(
                    $"[LevelSelectButtons] 按钮「{child.name}」至少需要 2 个子物体（第 1 个：文字，第 2 个：锁）。已跳过。",
                    child);
                continue;
            }

            var textRoot = child.GetChild(0);
            var lockRoot = child.GetChild(1);

            var sceneName = string.Format(sceneNameFormat, levelIndex);
            button.onClick.RemoveAllListeners();
            var captured = sceneName;
            button.onClick.AddListener(() => OnLevelButtonClicked(captured));

            _wired.Add(new WiredLevelButton
            {
                Button = button,
                LevelIndex = levelIndex,
                TextGo = textRoot.gameObject,
                LockGo = lockRoot.gameObject,
            });
        }

        RefreshLockVisuals();
    }

    void OnEnable()
    {
        RefreshLockVisuals();
    }

    void OnLevelButtonClicked(string sceneName)
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

    void RefreshLockVisuals()
    {
        var maxLevel = GameSceneManager.GetMaxUnlockedLevel();

        foreach (var e in _wired)
        {
            var unlocked = e.LevelIndex <= maxLevel;

            if (e.TextGo != null)
            {
                e.TextGo.SetActive(unlocked);
                if (unlocked)
                    ApplyLevelNumberText(e.TextGo.transform, e.LevelIndex);
            }

            if (e.LockGo != null)
                e.LockGo.SetActive(!unlocked);

            if (e.Button != null)
                e.Button.interactable = unlocked;
        }
    }

    /// <summary> 在第 1 个子物体上取 Text / TMP（不扫按钮父节点，也不向下递归）。 </summary>
    static void ApplyLevelNumberText(Transform textRoot, int levelIndex)
    {
        if (textRoot == null)
            return;

        var s = levelIndex.ToString();

        var ugui = textRoot.GetComponent<Text>();
        if (ugui != null)
            ugui.text = s;

        var tmp = textRoot.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = s;
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
}
