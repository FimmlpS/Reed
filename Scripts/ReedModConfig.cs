using BaseLib.Config;
using Godot;

namespace Reed.Scripts;

/// <summary>
/// Reed 的 mod 配置。继承 BaseLib 的 <see cref="SimpleModConfig"/> 后，游戏「模组设置 → 苇草Reed」里会
/// 自动生成一行可编辑的输入框；配置存在 <c>mod_configs/Reed.cfg</c>（文件名取命名空间根 Reed，
/// 前缀 <c>REED-</c> 用于匹配 settings_ui 里的本地化 key）。
///
/// <para>配置属性**必须是 static**（BaseLib 的限制）；不想暴露给玩家的属性用 [ConfigIgnore] 挡掉。</para>
/// </summary>
public class ReedModConfig : SimpleModConfig
{
    /// <summary>
    /// 快速开关「回忆牌堆」界面的按键，写 Godot 的键名（<c>R</c> / <c>M</c> / <c>F1</c> / <c>Space</c> …）。
    /// 默认 R。玩家可以在这里改成任意一个没被占用的键，避免和原版快捷键重复。
    /// </summary>
    [ConfigTextInput(TextInputPreset.Alphanumeric)]
    [ConfigHoverTip]
    public static string MemoryToggleKey { get; set; } = "R";

    /// <summary>配置里写的那个键；名字写错就静默退回默认的 R（绝不因为一个手滑把入口锁死）。</summary>
    public static Key MemoryToggleKeycode => ParseKey(MemoryToggleKey);

    /// <summary>按键的显示名（写进 tip 用，如 <c>R</c>）。</summary>
    public static string MemoryToggleKeyText => OS.GetKeycodeString(MemoryToggleKeycode);

    private static Key ParseKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Key.R;
        }
        Key key = OS.FindKeycodeFromString(name.Trim());
        return key == Key.None ? Key.R : key;
    }
}
