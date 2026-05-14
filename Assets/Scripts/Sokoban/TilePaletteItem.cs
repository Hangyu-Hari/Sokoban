using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// 挂在 TileUI 预制体上：绑定 Button、图标 Image、名称 TextMeshPro；由调色板在运行时 <see cref="Setup"/>。
/// </summary>
[DisallowMultipleComponent]
public sealed class TilePaletteItem : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] Image iconImage;
    [SerializeField] TextMeshProUGUI nameTMP;

    /// <summary> 用显式图标与文案初始化，并绑定点击（会先清掉原有 onClick）。 </summary>
    public void Setup(Sprite icon, string displayName, UnityAction onSelected)
    {
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        SetDisplayName(displayName);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onSelected != null)
                button.onClick.AddListener(onSelected);
        }
    }

    /// <summary> 从 <see cref="TileBase"/> 取精灵（仅支持内置 <see cref="Tile"/>；其它类型请用 <see cref="Setup(Sprite, string, UnityAction)"/> 传图）。 </summary>
    public void Setup(TileBase tile, string displayName, UnityAction onSelected)
    {
        Setup(TryGetSprite(tile), displayName, onSelected);
    }

    void SetDisplayName(string displayName)
    {
        if (nameTMP == null)
            return;

        nameTMP.text = displayName ?? string.Empty;
        nameTMP.gameObject.SetActive(!string.IsNullOrEmpty(displayName));
    }

    static Sprite TryGetSprite(TileBase tileBase)
    {
        if (tileBase == null)
            return null;
        if (tileBase is Tile tile)
            return tile.sprite;
        return null;
    }
}
