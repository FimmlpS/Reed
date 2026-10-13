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

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class TimeFire : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CalculationBaseVar(4),
        new ExtraDamageVar(8),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card,target)=> {
            if(card.CombatState!=null){
                int playedRound = MemoryPlayRound.PlayedRound(card, card.CombatState);
                if(playedRound>0){
                    return Math.Abs(playedRound-card.CombatState.RoundNumber);
                }
            }
            return 0;
        })
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
        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
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
    }

    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(2);
        DynamicVars.ExtraDamage.UpgradeValueBy(4);
    }
}