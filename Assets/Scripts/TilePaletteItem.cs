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

    TilePaletteBrush _boundBrush;

    /// <summary> 与调色板行对应的笔刷（用于选中态高亮）。 </summary>
    public TilePaletteBrush BoundBrush => _boundBrush;

    /// <summary> 用显式图标与文案初始化，并绑定点击（会先清掉原有 onClick）。 </summary>
    public void Setup(Sprite icon, string displayName, TilePaletteBrush brush, UnityAction onSelected)
    {
        _boundBrush = brush;

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

        SetEntrySelected(false);
    }

    /// <summary> 从 <see cref="TileBase"/> 取精灵（仅支持内置 <see cref="Tile"/>；其它类型请用带 <see cref="Sprite"/> 的重载）。 </summary>
    public void Setup(TileBase tile, string displayName, TilePaletteBrush brush, UnityAction onSelected)
    {
        Setup(TryGetSprite(tile), displayName, brush, onSelected);
    }

    /// <summary> 与层切换 / 工具按钮一致的「选中略灰」外观。 </summary>
    public void SetEntrySelected(bool selected)
    {
        if (button == null)
            return;

        var c = button.colors;
        c.colorMultiplier = 1f;
        c.fadeDuration = 0.08f;
        if (selected)
        {
            c.normalColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            c.highlightedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            c.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
            c.selectedColor = c.normalColor;
            c.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        }
        else
        {
            c.normalColor = Color.white;
            c.highlightedColor = new Color(0.95f, 0.95f, 0.95f, 1f);
            c.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            c.selectedColor = Color.white;
            c.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f);
        }

        button.colors = c;
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
