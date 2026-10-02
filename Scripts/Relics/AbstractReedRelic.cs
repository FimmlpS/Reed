using BaseLib.Abstracts;

namespace Reed.Scripts.Relics;

public abstract class AbstractReedRelic : CustomRelicModel
{
    // 小图标
    public override string PackedIconPath => $"res://Reed/images/relics/{replaceID(Id.Entry.ToLowerInvariant())}.png";
    // 轮廓图标
    protected override string PackedIconOutlinePath => $"res://Reed/images/relics/{replaceID(Id.Entry.ToLowerInvariant())}.png";
    // 大图标
    protected override string BigIconPath => $"res://Reed/images/relics/{replaceID(Id.Entry.ToLowerInvariant())}.png";

    public string replaceID(string path)
    {
        return path.Replace("reed-","");
    }
}