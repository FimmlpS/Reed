using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.Cards.Flower;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;
using Reed.Scripts.Resistance;

namespace Reed.Scripts.Relics;

[Pool(typeof(ReedRelicPool))]
public class FlowerOfDragon : AbstractReedRelic
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    private bool _wasUsedThisTurn;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BurnVar(1)
    ];
    
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromCard<NormalFlower>(),
        HoverTipFactory.FromKeyword(ReedKeywords.Attach),
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    private bool WasUsedThisTurn
    {
        get
        {
            return _wasUsedThisTurn;
        }
        set
        {
            AssertMutable();
            _wasUsedThisTurn = value;
        }
    }

    public override async Task BeforeCombatStartLate()
    {
        foreach(Creature creature in Owner.Creature.CombatState?.Creatures ?? [])
        {
            if (creature.IsMonster)
            {
                await ReedAttachCmd.Attach(new ThrowingPlayerChoiceContext(), ModelDb.Card<NormalFlower>(), creature);
            }
        }
    }

    public override async Task AfterCreatureAddedToCombat(Creature creature)
    {
        if (creature.IsMonster)
        {
            await ReedAttachCmd.Attach(new ThrowingPlayerChoiceContext(), ModelDb.Card<NormalFlower>(), creature);
        }
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (WasUsedThisTurn)
        {
            return;
        }

        if (!target.IsMonster)
        {
            return;
        }

        if (dealer != Owner.Creature)
        {
            return;
        }

        if (!props.IsPoweredAttack())
        {
            return;
        }

        if (!target.IsAlive)
        {
            return;
        }

        WasUsedThisTurn = true;
        Status = RelicStatus.Normal;
        await ReedBurnCmd.Burn(DynamicVars.Burn().IntValue)
        .Targeting(target)
        .Execute(choiceContext);

        return;
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (!participants.Contains(Owner.Creature))
        {
            return Task.CompletedTask;
        }

        WasUsedThisTurn = false;
        Status = RelicStatus.Active;
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom _)
    {
        WasUsedThisTurn = false;
        Status = RelicStatus.Normal;
        return Task.CompletedTask;
    }
}