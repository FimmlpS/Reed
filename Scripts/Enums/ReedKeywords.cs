using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Reed.Scripts.Enums;

public class ReedKeywords
{
    [CustomEnum("BURN")]
    [KeywordProperties(AutoKeywordPosition.None)]
    public static CardKeyword Burn;

    [CustomEnum("RESISTANCE")]
    [KeywordProperties(AutoKeywordPosition.None)]
    public static CardKeyword Resistance;

    [CustomEnum("BURNING")]
    [KeywordProperties(AutoKeywordPosition.None)]
    public static CardKeyword Burning;

    [CustomEnum("ATTACH")]
    [KeywordProperties(AutoKeywordPosition.None)]
    public static CardKeyword Attach;

    [CustomEnum("FIRE_FLOWER")]
    [KeywordProperties(AutoKeywordPosition.None)]
    public static CardKeyword FireFlower;

    [CustomEnum("UNYIELDING")]
    [KeywordProperties(AutoKeywordPosition.Before)]
    public static CardKeyword Unyielding;

    [CustomEnum("IGNITE")]
    [KeywordProperties(AutoKeywordPosition.None)]
    public static CardKeyword Ignite;
}