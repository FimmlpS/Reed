using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using Reed.Scripts.Cards.Attack;
using Reed.Scripts.Cards.Skill;
using Reed.Scripts.Pools;

namespace Reed.Scripts.Characters;

public class Reed : PlaceholderCharacterModel
{
    public override Color NameColor => new(0.42f,0.42f,1f);

    public override Color EnergyLabelOutlineColor => new(0.51f, 0f, 0.65f);

    public override CharacterGender Gender => CharacterGender.Feminine;

    public override int StartingHp => 64;

    // 人物模型tscn路径。要自定义见下。
    public override string CustomVisualPath => "res://Reed/scenes/ReedModel.tscn";
    // 卡牌拖尾路径。
    // public override string CustomTrailPath => "res://scenes/vfx/card_trail_ironclad.tscn";
    // 人物头像路径。
    public override string CustomIconTexturePath => "res://Reed/images/icon/character_icon_reed.png";
    // 人物头像2号。
    public override string CustomIconPath => "res://Reed/scenes/ReedIcon.tscn";
    // 能量表盘tscn路径。要自定义见下。
    public override string CustomEnergyCounterPath => "res://Reed/scenes/ReedEnergy.tscn";
    // 篝火休息动画。
    public override string CustomRestSiteAnimPath => "res://Reed/scenes/ReedRest.tscn";
    // 商店人物动画。
    public override string CustomMerchantAnimPath => "res://Reed/scenes/ReedMerchant.tscn";
    // 多人模式-手指。
    //public override string CustomArmPointingTexturePath => "res://Reed/images/char/default/reed_pointing.png";
    // 多人模式剪刀石头布-石头。
    //public override string CustomArmRockTexturePath => "res://Reed/images/char/default/reed_rock.png";
    // 多人模式剪刀石头布-布。
    //public override string CustomArmPaperTexturePath => "res://Reed/images/char/default/reed_paper.png";
    // 多人模式剪刀石头布-剪刀。
    //public override string CustomArmScissorsTexturePath => "res://Reed/images/char/default/reed_scissors.png";

    // 人物选择背景。
    public override string CustomCharacterSelectBg => "res://Reed/scenes/ReedSelectBg.tscn";
    // 人物选择图标。
    public override string CustomCharacterSelectIconPath => "res://Reed/images/icon/char_select_reed.png";
    // 人物选择图标-锁定状态。
    public override string CustomCharacterSelectLockedIconPath => "res://Reed/images/icon/char_select_reed_locked.png";
    // 人物选择过渡动画。
    // public override string CustomCharacterSelectTransitionPath => "res://materials/transitions/ironclad_transition_mat.tres";
    // 地图上的角色标记图标、表情轮盘上的角色头像
    // public override string CustomMapMarkerPath => null;
    // 攻击音效
    // public override string CustomAttackSfx => null;
    // 施法音效
    // public override string CustomCastSfx => null;
    // 死亡音效
    // public override string CustomDeathSfx => null;
    // 角色选择音效
    // public override string CharacterSelectSfx => null;
    // 过渡音效。这个不能删。
    public override string CharacterTransitionSfx => "event:/sfx/ui/wipe_ironclad";

    public override CardPoolModel CardPool => ModelDb.CardPool<ReedCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<ReedRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<ReedPotionPool>();

    public override IEnumerable<CardModel> StartingDeck => [
        ModelDb.Card<Strike>(),
        ModelDb.Card<Strike>(),
        ModelDb.Card<Strike>(),
        ModelDb.Card<Strike>(),
        //ModelDb.Card<Strike>(),
        ModelDb.Card<Defend>(),
        ModelDb.Card<Defend>(),
        ModelDb.Card<Defend>(),
        ModelDb.Card<Defend>(),
        //ModelDb.Card<Defend>(),
        //ModelDb.Card<Hunt>(),
        //ModelDb.Card<SharpAsTooth>()
    ];

    public override IReadOnlyList<RelicModel> StartingRelics => [
        //ModelDb.Relic<NaturalInclusion>(),
        //ModelDb.Relic<WoodEngrave>()
    ];

    public override List<string> GetArchitectAttackVfx()
    {
        return base.GetArchitectAttackVfx();
    }

    public override CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
    {
        return SetupAnimationState(
            controller,
            "Idle",
            "Die",false,
            null,false,
            "Attack",false,
            null,false,
            null,false
        );
    }
}