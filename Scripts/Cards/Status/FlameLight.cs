using BaseLib.Patches.Hooks;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;
using Reed.Scripts.DynamicVars;
using Reed.Scripts.Enums;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Cards.Status;

[Pool(typeof(ReedCardPool))]
public class FlameLight : AbstractReedCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(4m, ValueProp.Unpowered | ValueProp.Move | ReedValueProp.FlameLight)
    ];

    protected override IEnumerable<IHoverTip> ExtraHoverTips => [
        HoverTipFactory.FromKeyword(ReedKeywords.Burn)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Unplayable
    ];

    public FlameLight() : base(-1, CardType.Status, CardRarity.Token, TargetType.None)
    {
        
    }

    protected override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)
    {
        // foreach(Creature target in GetPossibleTargets())
        // {
        //     if (target.IsAlive)
        //     {
        //         NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(NGroundFireVfx.Create(target));
        //         SfxCmd.Play("event:/sfx/characters/attack_fire");
        //         await CreatureCmd.Damage(choiceContext, target, base.DynamicVars.Damage, this, null);
        //     }

        // }
        foreach(Creature target in GetPossibleTargets())
        {
            if (target.IsAlive)
            {
                NCombatRoom.Instance?.CombatVfxContainer.AddChildSafely(NGroundFireVfx.Create(target));
            }
        }
        SfxCmd.Play("event:/sfx/characters/attack_fire");
        await CreatureCmd.Damage(choiceContext,GetPossibleTargets(),DynamicVars.Damage,Owner.Creature,this,null);
    }

    private IReadOnlyList<Creature> GetPossibleTargets()
    {
        if(CombatState == null)
        {
            return [];
        }
        return CombatState.GetOpponentsOf(Owner.Creature);
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card == this)
        {
            if(card.Pile?.Type!=PileType.Hand && card.Pile?.Type != PileType.Exhaust && card.Pile?.Type!=PileType.Play && card.Pile?.Type!=PileType.None)
            {
                await CardCmd.Exhaust(new ThrowingPlayerChoiceContext(),this,false,true);
            }
        }
    }

    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        if(card == this && Keywords.Contains(ReedKeywords.Unyielding))
        {
            if (PileType.Hand.GetPile(Owner).Cards.Count < MaxHandSizePatch.GetMaxHandSize(Owner, CardPile.MaxCardsInHand))
            {
                foreach(CardModel c in await this.CreateInHand<FlameLight>(1))
                {
                    CardCmd.ApplyKeyword(c,ReedKeywords.Unyielding);
                }
            }
        }
    }

    public override bool HasTurnEndInHandEffect => true;

    protected override IEnumerable<string> ExtraRunAssetPaths => NGroundFireVfx.AssetPaths;

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
    }
}