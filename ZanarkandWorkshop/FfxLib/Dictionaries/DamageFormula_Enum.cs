namespace FFXProjectEditor.FfxLib.Dictionaries
{
    public enum DamageFormula_Enum : byte
    {
        NoDamage = 0,
        StrVsDef = 1,
        StrIgnoreDef = 2,
        MagVsMDef = 3,
        MagIgnoreMDef = 4,
        TargetCurrentHp = 5,
        MultiplesOf50 = 6,
        Healing = 7,
        TargetMaxHp = 8,
        MultiplesOf50R = 9,
        TargetMaxMp = 10,
        TargetTickSpeed = 11,
        TargetCurrentMp = 12,
        TargetTickCounter = 13,
        IgnoreDefenseNR = 14,
        SpecialMagic = 15,
        WielderMaxHp = 16,
        WielderHighHp = 17,
        WielderHighMp = 18,
        WielderLowHp = 19,
        SpecialMagicNR = 20,
        GilSpent = 21,
        TargetKillCount = 22,
        MultiplesOf9999 = 23,
    }
}
