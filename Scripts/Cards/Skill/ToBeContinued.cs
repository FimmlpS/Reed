using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using Reed.Scripts.Pools;
using Reed.Scripts.Powers;

namespace Reed.Scripts.Cards.Skill;

[Pool(typeof(ReedCardPool))]
public class ToBeContinued : AbstractReedCard
{
    public const int BaseCombats = 2;

    public const int UpgradeBonusCombats = 2;

    private int _combatsSeen;

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DynamicVar("Combats", BaseCombats)
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Exhaust
    ];

    public ToBeContinued() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {

    }

    /// <summary>已经历的战斗数。持久化到存档：中途退出再回来，剩余场次不会重置。</summary>
    [SavedProperty]
    public int CombatsSeen
    {
        get => _combatsSeen;
        set
        {
            AssertMutable();
            _combatsSeen = Math.Max(0, value);
            RefreshCombatsLeft();
        }
    }

    private int MaxCombats => IsUpgraded ? BaseCombats + UpgradeBonusCombats : BaseCombats;

    private void RefreshCombatsLeft()
        => DynamicVars["Combats"].BaseValue = Math.Max(0, MaxCombats - _combatsSeen);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (CombatState?.RunState.CurrentRoom is CombatRoom room)
        {
            room.AddExtraReward(Owner, new CardReward(CardCreationOptions.ForRoom(Owner, room.RoomType), 3, Owner));
        }
        await PowerCmd.Apply<ContinuedPower>(choiceContext, Owner.Creature, 1m, Owner.Creature, this);
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (Pile == null || Pile.Type != PileType.Deck)
        {
            return;
        }
        CombatsSeen++;
        if (CombatsSeen >= MaxCombats && Pile?.Type == PileType.Deck)
        {
            await CardPileCmd.RemoveFromDeck(this);
        }
    }

    protected override void OnUpgrade()
    {
        decimal target = Math.Max(0, MaxCombats - _combatsSeen);
        DynamicVars["Combats"].UpgradeValueBy(target - DynamicVars["Combats"].BaseValue);
    }
}
