
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Cards;

public abstract class AbstractReedCard : CustomCardModel
{
    public override string PortraitPath => $"res://Reed/images/cards/{replaceID(Id.Entry.ToLowerInvariant())}.png";

    public string replaceID(string path)
    {
        return path.Replace("reed-","");
    }

    /// <summary>
    /// 给火花牌的描述注入形态参数：描述里的 <c>{Alive:…|}{Dead:…|}</c> 靠它们取条件
    /// （见 <see cref="LivingForm"/>）。非火花卡不加，保持描述原样。
    /// <para>规范卡（canonical）没有 owner，取不到形态 → 两个条件都是 false，显示的即“普通形态”的文本
    /// （卡牌图鉴、预览里看到的就是这个）。<c>Owner</c> 在规范卡上会断言失败，所以必须先判 <c>IsMutable</c>。</para>
    /// </summary>
    protected override void AddExtraArgsToDescription(LocString description)
    {
        if (this is not IFireFlower)
        {
            return;
        }
        LivingForm.AddArgsTo(description, IsMutable ? Owner : null);
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