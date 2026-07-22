using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils;
using System.Collections.Generic;

namespace FFXProjectEditor.Converters
{
    public class DamageFormula_Converter : BaseEnumStringConverter<DamageFormula_Enum>
    {
        public DamageFormula_Converter()
        {
            Options = new Dictionary<DamageFormula_Enum, string>
            {
                {DamageFormula_Enum.NoDamage, "[0] No Damage"},
                {DamageFormula_Enum.StrVsDef, "[1] Str Vs Def"},
                {DamageFormula_Enum.StrIgnoreDef, "[2] Str Ignore Def"},
                {DamageFormula_Enum.MagVsMDef, "[3] Mag Vs MDef"},
                {DamageFormula_Enum.MagIgnoreMDef, "[4] Mag Ignore MDef"},
                {DamageFormula_Enum.TargetCurrentHp, "[5] Target Current Hp / 60"},
                {DamageFormula_Enum.MultiplesOf50, "[6] Multiples Of 50"},
                {DamageFormula_Enum.Healing, "[7] Healing"},
                {DamageFormula_Enum.TargetMaxHp, "[8] Target Max Hp / 60"},
                {DamageFormula_Enum.MultiplesOf50R, "[9] Multiples Of 50 R"},
                {DamageFormula_Enum.TargetMaxMp, "[10] Target Max Mp / 60"},
                {DamageFormula_Enum.TargetTickSpeed, "[11] Target Max CTB / 60"},
                {DamageFormula_Enum.TargetCurrentMp, "[12] Target Current Mp / 60"},
                {DamageFormula_Enum.TargetTickCounter, "[13] Target Current CTB / 60"},
                {DamageFormula_Enum.IgnoreDefenseNR, "[14] Ignore Defense NR"},
                {DamageFormula_Enum.SpecialMagic, "[15] Special Magic"},
                {DamageFormula_Enum.WielderMaxHp, "[16] Wielder Max Hp"},
                {DamageFormula_Enum.WielderHighHp, "[17] Wielder High Hp"},
                {DamageFormula_Enum.WielderHighMp, "[18] Wielder High Mp"},
                {DamageFormula_Enum.WielderLowHp, "[19] Wielder Low Hp"},
                {DamageFormula_Enum.SpecialMagicNR, "[20] Special Magic NR"},
                {DamageFormula_Enum.GilSpent, "[21] Gil Spent"},
                {DamageFormula_Enum.TargetKillCount, "[22] Target Kill Count"},
                {DamageFormula_Enum.MultiplesOf9999, "[23] Multiples Of 9999"},
            };
        }
    }
}
