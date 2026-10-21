using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;
using Reed.Scripts.Enums;
using Reed.Scripts.Memory;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Context;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class TimeFire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(5,ValueProp.Move),
        new DynamicVar("Increase",1)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Memory)
    ];

    public TimeFire() : base(0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
        
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        float scale = 0.8f;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
        .FromCard(this, cardPlay)
        .Targeting(cardPlay.Target)
        .BeforeDamage(delegate
            {
                NGroundFireVfx nGroundFireVfx = NGroundFireVfx.Create(cardPlay.Target);
                if (nGroundFireVfx == null)
                {
                    return Task.CompletedTask;
                }
                SfxCmd.Play("event:/sfx/characters/attack_fire");
                nGroundFireVfx.Scale = Vector2.One * scale;
                NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(nGroundFireVfx);
                scale += 0.1f;
                return Task.CompletedTask;
            })
        .Execute(choiceContext);

        foreach(int round in MemorySystem.Of(Owner).Rounds)
        {
            foreach(RememberedCard rCard in MemorySystem.Of(Owner).PeekRound(round))
            {
                CardModel card = rCard.Card;
                if (card.Type != CardType.Attack)
                {
                    continue;
                }
                decimal improveVal = DynamicVars["Increase"].BaseValue;
                //damage
                if (card.DynamicVars.ContainsKey("CalculatedDamage"))
                {
                    card.DynamicVars.CalculatedDamage.BaseValue += improveVal;
                }
                else if(card.DynamicVars.ContainsKey("Damage"))
                {
                    card.DynamicVars.Damage.BaseValue += improveVal;
                }
                else if(card.DynamicVars.ContainsKey("OstyDamage"))
                {
                    card.DynamicVars.OstyDamage.BaseValue += improveVal;
                }
            }
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars["Increase"].UpgradeValueBy(1);
    }
}