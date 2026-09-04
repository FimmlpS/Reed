using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Reed.Scripts.Pools;

public class ReedCardPool : CustomCardPoolModel
{
    public override string Title => "Reed";

    public override string? BigEnergyIconPath => "res://Reed/images/icon/energybg_reed.png";

    public override string? TextEnergyIconPath => "res://Reed/images/icon/energy_icon_reed.png";

    public override Color DeckEntryCardColor => new(0.67f,0.56f,0.32f);

    public override Color ShaderColor => new(0.67f,0.56f,0.32f);

    public override bool IsColorless => false;

    public override Texture2D? CustomFrame(CustomCardModel card)
    {
        if (card.Type == CardType.Attack)
        {
            return PreloadManager.Cache.GetAsset<Texture2D>("res://Reed/images/bg/cardbg_attack.png");
        }
        else if(card.Type == CardType.Power)
        {
            return PreloadManager.Cache.GetAsset<Texture2D>("res://Reed/images/bg/cardbg_power.png");
        }
        return PreloadManager.Cache.GetAsset<Texture2D>("res://Reed/images/bg/cardbg_skill.png");
    }
}