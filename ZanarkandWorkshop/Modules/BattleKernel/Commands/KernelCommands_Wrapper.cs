using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Linq;
using static FFXProjectEditor.FfxLib.Ability.Ability_Command;

namespace FFXProjectEditor.Modules.BattleKernel.Commands
{
    internal partial class KernelCommands_Wrapper : ObservableObject
    {
        // UsageFlags contains presentation/camera bits that are not yet exposed by
        // the editor. Preserve the complete byte so saving an unrelated command
        // property cannot silently clear those unknown bits.
        private UsageFlags preservedUsageFlags;

        [ObservableProperty] public int index;
        [ObservableProperty] public short anim1Id;
        [ObservableProperty] public short anim2Id;
        [ObservableProperty] public byte iconId;
        [ObservableProperty] public byte casterAnimId;
        [ObservableProperty] public bool flagMenuMainMenu;
        [ObservableProperty] public bool flagMenuCategoryF4;
        [ObservableProperty] public bool flagMenuOpensSubMenu;
        [ObservableProperty] public byte subSubMenuCategorization;
        [ObservableProperty] public byte subMenuCategorization;
        [ObservableProperty] public Character_Enum characterUser;
        [ObservableProperty] public bool flagTargetEnabled;
        [ObservableProperty] public bool flagTargetEnemies;
        [ObservableProperty] public bool flagTargetMulti;
        [ObservableProperty] public bool flagTargetSelfOnly;
        [ObservableProperty] public bool flagTargetUnkF5;
        [ObservableProperty] public bool flagTargetEitherTeam;
        [ObservableProperty] public bool flagTargetDead;
        [ObservableProperty] public bool flagTargetUnkF8;
        [ObservableProperty] public bool flagUsageLongRange;
        [ObservableProperty] public bool flagUsageUnkF2;
        [ObservableProperty] public bool flagUsageUsesCursor;
        [ObservableProperty] public bool flagMisc1UseOutsideCombat;
        [ObservableProperty] public bool flagMisc1UseInCombat;
        [ObservableProperty] public bool flagMisc1DisplayMoveName;
        [ObservableProperty] public bool flagMisc1AffectedByDarkness;
        [ObservableProperty] public bool flagMisc1AffectedByReflect;
        [ObservableProperty] public HitCalcType flagMisc1HitCalcType;
        [ObservableProperty] public bool flagMisc2AbsorbDamage;
        [ObservableProperty] public bool flagMisc2StealItem;
        [ObservableProperty] public bool flagMisc2MenuUse;
        [ObservableProperty] public bool flagMisc2MenuRight;
        [ObservableProperty] public bool flagMisc2MenuLeft;
        [ObservableProperty] public bool flagMisc2DelayS;
        [ObservableProperty] public bool flagMisc2DelayL;
        [ObservableProperty] public bool flagMisc2RandomTargets;
        [ObservableProperty] public bool flagMisc3Piercing;
        [ObservableProperty] public bool flagMisc3AffectedBySilence;
        [ObservableProperty] public bool flagMisc3UseWeaponProps;
        [ObservableProperty] public bool flagMisc3TriggerCommand;
        [ObservableProperty] public bool flagMisc3CastAnimS;
        [ObservableProperty] public bool flagMisc3CastAnimL;
        [ObservableProperty] public bool flagMisc3DestroyCaster;
        [ObservableProperty] public bool flagMisc3MissToAlive;
        [ObservableProperty] public bool flagMisc4ChargeWarriorHealer;
        [ObservableProperty] public bool flagMisc4EmptyOverdrive;
        [ObservableProperty] public bool flagMisc4ShowSpellcastAura;
        [ObservableProperty] public bool flagMisc4RunOffScreen;
        [ObservableProperty] public bool flagMisc4CopycatEnabled;
        [ObservableProperty] public bool flagMisc4AnimationF6;
        [ObservableProperty] public bool flagMisc4AeonOverdrive;
        [ObservableProperty] public bool flagMisc4Bribe;
        [ObservableProperty] public bool flagDamagePhysical;
        [ObservableProperty] public bool flagDamageMagical;
        [ObservableProperty] public bool flagDamageCanCrit;
        [ObservableProperty] public bool flagDamageGearCritBonus;
        [ObservableProperty] public bool flagDamageHeals;
        [ObservableProperty] public bool flagDamageCleansesStatuses;
        [ObservableProperty] public bool flagDamageSupressBreakDamageLimit;
        [ObservableProperty] public bool flagDamageBreaksDamageLimit;
        [ObservableProperty] public bool stealGil;
        [ObservableProperty] public bool flagPreviewActive;
        [ObservableProperty] public bool flagPreviewHealMp;
        [ObservableProperty] public bool flagPreviewHealStatuses;
        [ObservableProperty] public bool flagPreviewIsMap;
        [ObservableProperty] public bool flagPreviewIsRenameCard;
        [ObservableProperty] public bool flagPreviewIsSphere;
        [ObservableProperty] public bool flagPreviewHealHp;
        [ObservableProperty] public bool flagPreviewIsRenameCard2;
        [ObservableProperty] public bool flagDamageTypeHp;
        [ObservableProperty] public bool flagDamageTypeMp;
        [ObservableProperty] public bool flagDamageTypeCtb;
        [ObservableProperty] public byte moveRank;
        [ObservableProperty] public byte costMp;
        [ObservableProperty] public byte costOverdrive;
        [ObservableProperty] public byte attackCritBonus;
        [ObservableProperty] public DamageFormula_Enum damageFormula;
        [ObservableProperty] public byte attackAccuracy;
        [ObservableProperty] public byte attackPower;
        [ObservableProperty] public byte hitCount;
        [ObservableProperty] public byte shatterChance;

        [ObservableProperty] public bool flagElementFire;
        [ObservableProperty] public bool flagElementBlizzard;
        [ObservableProperty] public bool flagElementThunder;
        [ObservableProperty] public bool flagElementWater;
        [ObservableProperty] public bool flagElementHoly;
        [ObservableProperty] public StatusByteList statusChance;
        [ObservableProperty] public StatusDurationByteList statusDuration;
        [ObservableProperty] public bool flagStatusScan;
        [ObservableProperty] public bool flagStatusDistillPower;
        [ObservableProperty] public bool flagStatusDistillMana;
        [ObservableProperty] public bool flagStatusDistillSpeed;
        [ObservableProperty] public bool flagStatusDistillMove;
        [ObservableProperty] public bool flagStatusDistillAbility;
        [ObservableProperty] public bool flagStatusShield;
        [ObservableProperty] public bool flagStatusBoost;
        [ObservableProperty] public bool flagStatusEject;
        [ObservableProperty] public bool flagStatusAutoLife;
        [ObservableProperty] public bool flagStatusCurse;
        [ObservableProperty] public bool flagStatusDefend;
        [ObservableProperty] public bool flagStatusGuard;
        [ObservableProperty] public bool flagStatusSentinel;
        [ObservableProperty] public bool flagStatusDoom;

        [ObservableProperty] public bool flagStatBuffCheer;
        [ObservableProperty] public bool flagStatBuffAim;
        [ObservableProperty] public bool flagStatBuffFocus;
        [ObservableProperty] public bool flagStatBuffReflex;
        [ObservableProperty] public bool flagStatBuffLuck;
        [ObservableProperty] public bool flagStatBuffJinx;
        [ObservableProperty] public byte overdriveCategory;
        [ObservableProperty] public byte statBuffValue;

        [ObservableProperty] public bool flagSpecialBuffDoubleHp;
        [ObservableProperty] public bool flagSpecialBuffDoubleMp;
        [ObservableProperty] public bool flagSpecialBuffSpellspring;
        [ObservableProperty] public bool flagSpecialBuffDmg9999;
        [ObservableProperty] public bool flagSpecialBuffAlwaysCrit;
        [ObservableProperty] public bool flagSpecialBuffOverdrive150;
        [ObservableProperty] public bool flagSpecialBuffOverdrive200;

        [ObservableProperty] public ExtraCommandInfo extraInfo;

        [ObservableProperty] public byte[] nameScriptBytes;
        [ObservableProperty] public byte[] unusedText1ScriptBytes;
        [ObservableProperty] public byte[] descriptionScriptBytes;
        [ObservableProperty] public byte[] unusedText2ScriptBytes;
        // Text ids kept to keep the original data. Ideally this would be calculated.
        [ObservableProperty] public ushort nameScriptId;
        [ObservableProperty] public ushort unusedText1ScriptId;
        [ObservableProperty] public ushort descriptionScriptId;
        [ObservableProperty] public ushort unusedText2ScriptId;

        public string Name
        {
            get => FfxEncoding.DecodeEditableTextScript(NameScriptBytes, FfxEncoding.UsDecoder);
            set
            {
                byte[] encoded = FfxEncoding.EncodeTextScript(value ?? "", FfxEncoding.UsEncoder);
                if (NameScriptBytes.SequenceEqual(encoded))
                    return;

                NameScriptBytes = encoded;
                OnPropertyChanged();
            }
        }

        public string Description
        {
            get => FfxEncoding.DecodeEditableTextScript(DescriptionScriptBytes, FfxEncoding.UsDecoder);
            set
            {
                byte[] encoded = FfxEncoding.EncodeTextScript(value ?? "", FfxEncoding.UsEncoder);
                if (DescriptionScriptBytes.SequenceEqual(encoded))
                    return;

                DescriptionScriptBytes = encoded;
                OnPropertyChanged();
            }
        }

        public static KernelCommands_Wrapper Wrap(Ability_Command command)
        {
            KernelCommands_Wrapper wrapper = new()
            {
                preservedUsageFlags = command.UsageFlgs
            };

            PropertyUtil.CopyProperties(command, wrapper);

            return wrapper;
        }
        public Ability_Command Unwrap()
        {
            Ability_Command command = new()
            {
                UsageFlgs = preservedUsageFlags
            };

            PropertyUtil.CopyProperties(this, command);

            return command;
        }
    }
}
