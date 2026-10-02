using BaseLib.Abstracts;

namespace Reed.Scripts.Powers;

public abstract class AbstractReedPower : CustomPowerModel
{
    public override string? CustomPackedIconPath => $"res://Reed/images/powers/{replaceID(Id.Entry.ToLowerInvariant())}.png";
    public override string? CustomBigIconPath => $"res://Reed/images/powers/{replaceID(Id.Entry.ToLowerInvariant())}.png";

    public string replaceID(string path)
    {
        return path.Replace("reed-","");
    }
}