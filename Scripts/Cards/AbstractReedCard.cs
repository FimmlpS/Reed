
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Reed.Scripts.Cards;

public abstract class AbstractReedCard : CustomCardModel
{
    public override string PortraitPath => $"res://Reed/images/cards/{replaceID(Id.Entry.ToLowerInvariant())}.png";

    public string replaceID(string path)
    {
        return path.Replace("reed-","");
    }

    public AbstractReedCard(int baseCost, CardType type, CardRarity rarity, TargetType target) 
    : base(baseCost,type,rarity,target,true,true)
    {
        
    }

    public AbstractReedCard(int baseCost, CardType type, CardRarity rarity, TargetType target, bool showInCardLibrary, bool autoAdd) 
    : base(baseCost,type,rarity,target,showInCardLibrary,autoAdd)
    {
        
    }
}