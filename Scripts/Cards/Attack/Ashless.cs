using BaseLib.Utils;
using Reed.Scripts.Pools;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Helpers;

namespace Reed.Scripts.Cards.Attack;

[Pool(typeof(ReedCardPool))]
public class Ashless : AbstractReedCard
{
    /// <summary>这张牌此刻是否正被自动打出：<see cref="CalculatedDamageVar"/> 的乘数靠它决定加不加 ExtraDamage。</summary>
    private static readonly SpireField<CardModel, bool> _autoPlaying = new(() => false);

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new CalculationBaseVar(2),
        new ExtraDamageVar(2),          // 自动打出时每段额外加的伤害；升级不动它
        new CalculatedDamageVar(ValueProp.Move)
            .WithMultiplier((card, _) => _autoPlaying[card] ? 1m : 0m),
        new RepeatVar(4)
    ];

    public Ashless() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {

    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 自动打出：每段 2+2=4 点，段数不变；手动打出：每段 2 点。
        _autoPlaying[this] = cardPlay.IsAutoPlay;
        try
        {
            AttackCommand attack = DamageCmd.Attack(DynamicVars.CalculatedDamage)
            .FromCard(this, cardPlay)
            .OnlyPlayAnimOnce()
            .WithHitCount(DynamicVars.Repeat.IntValue)
            .BeforeDamage(delegate
            {
                NGroundFireVfx nGroundFireVfx = NGroundFireVfx.Create(cardPlay.Target);
                if (nGroundFireVfx == null)
                {
                    return Task.CompletedTask;
                }
                SfxCmd.Play("event:/sfx/characters/attack_fire");
                nGroundFireVfx.Scale = Vector2.One * 0.8f;
                NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(nGroundFireVfx);
                return Task.CompletedTask;
            });
            attack = cardPlay.IsAutoPlay
                ? attack.TargetingRandomOpponents(CombatState)
                : attack.Targeting(cardPlay.Target);
            await attack.Execute(choiceContext);
        }
        finally
        {
            _autoPlaying[this] = false;
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Repeat.UpgradeValueBy(2);
    }
}
