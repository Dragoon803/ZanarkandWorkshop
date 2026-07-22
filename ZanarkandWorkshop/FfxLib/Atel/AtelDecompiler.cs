using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Atel;

internal static class AtelDecompiler
{
    private sealed record CallInfo(string Name, string[] Parameters, bool IsAccessorWrite = false);

    private static readonly Dictionary<ushort, CallInfo> Calls = new()
    {
        [0x0000] = C("Common.wait", "frames"), [0x005F] = C("Common.halt"), [0x00A9] = C("Common.GetRandomValue"),
        [0x6004] = C("Camera.camSetPolar", "p1", "p2", "p3"), [0x6010] = C("Camera.camMove", "frames"),
        [0x6016] = C("Camera.camResetMove"), [0x601A] = C("Camera.camWait"), [0x602E] = C("Camera.refMove", "frames"),
        [0x6034] = C("Camera.refResetMove"), [0x6038] = C("Camera.refWait"), [0x603A] = C("Camera.camSetRoll", "roll"),
        [0x603B] = C("Camera.camSetScrDpt", "depth"), [0x603F] = C("Camera.refSetBtl", "p1", "p2", "p3"),
        [0x6041] = C("Camera.refSetBtlPolar", "p1", "p2", "p3", "p4", "p5", "p6"),
        [0x6044] = C("Camera.camSetBtlPolar2", "p1", "p2", "p3", "p4", "p5", "p6"),
        [0x7000] = C("Battle.btlTerminateAction"), [0x7003] = C("Battle.btlDirTarget", "p1", "p2"),
        [0x7006] = C("Battle.btlDirBasic", "p1", "p2"), [0x7007] = C("Battle.startMotion", "motion"),
        [0x7008] = C("Battle.awaitMotion"), [0x700A] = C("Battle.setHeight", "heightType", "height"),
        [0x700B] = C("Battle.performCommand", "target", "command"), [0x700C] = C("Battle.btlMove", "p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8"),
        [0x700F] = C("Battle.readCharacterProperty", "character", "property"),
        [0x7010] = C("Battle.findMatchingCharacter", "group", "property", "unused", "selector"),
        [0x7016] = C("Battle.stopMotion", "motion"), [0x7018] = C("Battle.writeCharacterProperty", "character", "property", "value"),
        [0x7019] = C("Battle.usedCommand"), [0x701A] = C("Battle.readCommandProperty", "command", "property"),
        [0x701E] = C("Battle.countCharacterOverlap", "group", "character"), [0x7021] = C("Battle.dereferenceCharacter", "character"),
        [0x7026] = C("Battle.setWeakState", "state"), [0x7028] = C("Battle.scaleOwnSize", "x", "y", "z"),
        [0x702D] = C("Battle.resetMove"), [0x7034] = C("Battle.endBattle", "result"),
        [0x7037] = C("Battle.addCommand", "character", "command"), [0x7038] = C("Battle.removeCommand", "character", "command"),
        [0x7039] = C("Battle.terminateDeath"), [0x703B] = C("Battle.setCommandDisabled", "character", "command", "disabled"),
        [0x703F] = C("Battle.cameraRequest", "p1", "p2"), [0x705A] = C("Battle.forcePerformCommand", "target", "command"),
        [0x705D] = C("Battle.setBindEffect", "p1", "p2"), [0x706B] = C("Battle.setModelPartVisible", "character", "part", "visible"),
        [0x707B] = C("Battle.soundEffect", "character", "sound"), [0x7097] = C("Battle.runEncounterScriptB", "encScript"),
        [0x70AB] = new("Self.stat", ["property", "value"], true), [0x70B0] = C("Battle.targetDistanceFrames", "p1"),
        [0x70B1] = C("Battle.btlPrintSp", "p1"),
        [0x70B2] = new("Self.motion", ["property", "value"], true), [0x70E0] = C("Battle.isCounterattackAllowed")
    };

    internal static readonly IReadOnlyDictionary<ushort, string> BattleProperties = new Dictionary<ushort, string>()
    {
        [0x0000]="HP", [0x0001]="MP", [0x0002]="MaxHP", [0x0003]="MaxMP", [0x0004]="IsAlive",
        [0x0005]="StatusPoison", [0x0006]="StatusPetrify", [0x0007]="StatusZombie", [0x0008]="WeakState",
        [0x0009]="STR", [0x000A]="DEF", [0x000B]="MAG", [0x000C]="MDF", [0x000D]="AGI",
        [0x000E]="LCK", [0x000F]="EVA", [0x0010]="ACC", [0x0011]="PoisonDamagePercent",
        [0x0012]="OverdriveMode", [0x0013]="OverdriveCurrent", [0x0014]="OverdriveMax", [0x0015]="IsOnFrontline",
        [0x001B]="WillDieToAttack", [0x001C]="Area", [0x001D]="Position", [0x001E]="BattleDistance",
        [0x001F]="EnemyGroup", [0x0020]="Armored", [0x0025]="StatusPowerBreak", [0x0026]="StatusMagicBreak",
        [0x0027]="StatusArmorBreak", [0x0028]="StatusMentalBreak", [0x0029]="StatusConfusion",
        [0x002A]="StatusBerserk", [0x002B]="StatusProvoke", [0x002C]="StatusThreaten", [0x002D]="StatusSleep",
        [0x002E]="StatusSilence", [0x002F]="StatusDarkness", [0x0030]="StatusShell", [0x0031]="StatusProtect",
        [0x0032]="StatusReflect", [0x0033]="StatusNulTide", [0x0034]="StatusNulBlaze", [0x0035]="StatusNulShock",
        [0x0036]="StatusNulFrost", [0x0037]="StatusRegen", [0x0038]="StatusHaste", [0x0039]="StatusSlow",
        [0x004F]="DeathAnimation", [0x0051]="GetsTurns", [0x0052]="Targetable", [0x0053]="VisibleOnCTB",
        [0x0059]="Host", [0x005B]="AnimationVariant", [0x0062]="AbsorbFire", [0x0063]="AbsorbIce",
        [0x0064]="AbsorbThunder", [0x0065]="AbsorbWater", [0x0066]="AbsorbHoly", [0x0067]="NullFire",
        [0x0068]="NullIce", [0x0069]="NullThunder", [0x006A]="NullWater", [0x006B]="NullHoly",
        [0x006C]="ResistFire", [0x006D]="ResistIce", [0x006E]="ResistThunder", [0x006F]="ResistWater",
        [0x0070]="ResistHoly", [0x0071]="WeakFire", [0x0072]="WeakIce", [0x0073]="WeakThunder",
        [0x0074]="WeakWater", [0x0075]="WeakHoly", [0x0079]="TimesStolenFrom", [0x0089]="ShowOverdriveBar",
        [0x008A]="Item1DropChance", [0x008B]="Item2DropChance", [0x008C]="GearDropChance", [0x008D]="StealChance",
        [0x0090]="StatusDistillPower", [0x0091]="StatusDistillMana", [0x0092]="StatusDistillSpeed",
        [0x0094]="StatusDistillAbility", [0x0097]="StatusEject", [0x0098]="StatusAutoLife", [0x0099]="StatusCurse",
        [0x009A]="StatusDefend", [0x009B]="StatusGuard", [0x009C]="StatusSentinel", [0x009D]="StatusDoom",
        [0x009F]="DoomCounterInitial", [0x00A0]="DoomCounterCurrent", [0x00A6]="LastDamageTakenHP",
        [0x00A7]="LastDamageTakenMP", [0x00A8]="LastDamageTakenCTB"
    };

    internal static readonly IReadOnlyDictionary<ushort, string> MotionProperties = new Dictionary<ushort, string>()
    {
        [0x0000]="motion_attack_start_dist", [0x0001]="motion_attack_offset",
        [0x0002]="motion_move_backjump_dist", [0x0003]="motion_run_speed",
        [0x0004]="motion_run_speed_return", [0x0005]="motion_run_speed_v0",
        [0x0006]="motion_run_speed_acc", [0x0007]="motion_weight",
        [0x0008]="motion_attack_height", [0x0009]="motion_width"
    };

    internal static readonly IReadOnlyDictionary<ushort, string> CommandProperties = new Dictionary<ushort, string>()
    {
        [0x0000]="damageFormula", [0x0001]="damageType", [0x0002]="affectHP",
        [0x0003]="affectMP", [0x0004]="affectCTB", [0x0005]="elementHoly",
        [0x0006]="elementWater", [0x0007]="elementThunder", [0x0008]="elementIce",
        [0x0009]="elementFire", [0x000A]="targetType"
    };

    internal static readonly IReadOnlyDictionary<ushort, string> BattleCharacters = new Dictionary<ushort, string>()
    {
        [0x0000]="Tidus", [0x0001]="Yuna", [0x0002]="Auron", [0x0003]="Kimahri",
        [0x0004]="Wakka", [0x0005]="Lulu", [0x0006]="Rikku", [0x0007]="Seymour",
        [0x0008]="Valefor", [0x0009]="Ifrit", [0x000A]="Ixion", [0x000B]="Shiva",
        [0x000C]="Bahamut", [0x000D]="Anima", [0x000E]="Yojimbo", [0x000F]="Cindy",
        [0x0010]="Sandy", [0x0011]="Mindy", [0x0012]="PC_DUMMY", [0x0013]="PC_DUMMY2",
        [0x0014]="Monster#00", [0x0015]="Monster#01", [0x0016]="Monster#02", [0x0017]="Monster#03",
        [0x0018]="Monster#04", [0x0019]="Monster#05", [0x001A]="Monster#06", [0x001B]="Monster#07",
        [0x00FF]="Actor:None",
        [0xFFE6]="CHR_OWN_TARGET0", [0xFFE7]="CHR_ALL_PLY3", [0xFFE8]="CHR_ALL_PLAYER2",
        [0xFFE9]="AllCharsAndAeons", [0xFFEA]="CHR_PARENT", [0xFFEB]="AllChrs?",
        [0xFFEC]="AllAeons", [0xFFED]="CHR_ALL_PLY2", [0xFFEE]="CHR_INPUT",
        [0xFFEF]="LastAttacker", [0xFFF0]="MatchingGroup", [0xFFF1]="AllMonsters",
        [0xFFF2]="FrontlineChars", [0xFFF3]="Self", [0xFFF4]="CharacterReserve#4",
        [0xFFF5]="CharacterReserve#3", [0xFFF6]="CharacterReserve#2", [0xFFF7]="CharacterReserve#1",
        [0xFFF8]="Character#3", [0xFFF9]="Character#2", [0xFFFA]="Character#1",
        [0xFFFB]="AllActors", [0xFFFC]="?TargetChrsImmediate", [0xFFFD]="TargetChrs",
        [0xFFFE]="ActiveChrs", [0xFFFF]="Actor:Null"
    };

    public static IReadOnlyList<AtelStatement> Translate(IReadOnlyList<AtelInstruction> instructions,
        Func<ushort, string?>? commandNameResolver = null, Func<ushort, string?>? floatConstantResolver = null)
    {
        var stack = new Stack<string>();
        var statements = new List<AtelStatement>();
        int statementStart = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            AtelInstruction ins = instructions[index];
            try { ins.Translation = TranslateOne(ins, stack, commandNameResolver, floatConstantResolver); }
            catch { ins.Translation = "Translation unavailable (stack context is incomplete)"; }
            if (IsLineEnd(ins.Opcode))
            {
                string statement = ins.Translation;
                for (int part = statementStart; part < index; part++)
                    instructions[part].Translation = $"Part of: {statement}";
                statements.Add(new AtelStatement(instructions.Skip(statementStart).Take(index - statementStart + 1).ToArray(), statement));
                statementStart = index + 1;
                if (stack.Count > 0) stack.Clear();
            }
        }
        if (statementStart < instructions.Count)
        {
            AtelInstruction[] trailing = instructions.Skip(statementStart).ToArray();
            statements.Add(new AtelStatement(trailing, trailing[^1].Translation));
        }
        AnnotateOperands(instructions, commandNameResolver, floatConstantResolver);
        return statements;
    }

    private static void AnnotateOperands(IReadOnlyList<AtelInstruction> instructions, Func<ushort, string?>? commandNameResolver,
        Func<ushort, string?>? floatConstantResolver)
    {
        foreach (AtelInstruction instruction in instructions)
        {
            if (!instruction.HasOperand) continue;
            instruction.SemanticOperandDisplay = instruction.Opcode switch
            {
                0x9F or 0xA0 or 0xA1 => $"Variable [0x{instruction.Operand:X4}]",
                0xB0 or 0xB1 or 0xB2 or 0xD5 or 0xD6 or 0xD7 => $"j{instruction.Operand:X2} [0x{instruction.Operand:X4}]",
                0xB5 or 0xD8 when Calls.TryGetValue(instruction.Operand, out CallInfo? call) => $"{call.Name} [0x{instruction.Operand:X4}]",
                0xAF when floatConstantResolver?.Invoke(instruction.Operand) is string floatValue => floatValue,
                _ => instruction.OperandDisplay
            };
        }

        for (int callIndex = 0; callIndex < instructions.Count; callIndex++)
        {
            AtelInstruction callInstruction = instructions[callIndex];
            if (callInstruction.Opcode is not (0xB5 or 0xD8) || !Calls.TryGetValue(callInstruction.Operand, out CallInfo? call)) continue;
            int firstArgument = callIndex - call.Parameters.Length;
            if (firstArgument < 0) continue;
            for (int parameterIndex = 0; parameterIndex < call.Parameters.Length; parameterIndex++)
            {
                AtelInstruction argument = instructions[firstArgument + parameterIndex];
                if (!argument.HasOperand || argument.Opcode is not (0x9F or 0xAE or 0xAF)) continue;
                string parameter = call.Parameters[parameterIndex];
                string raw = FormatInt16(argument.Operand);
                if (argument.Opcode == 0x9F)
                {
                    string role = parameter switch
                    {
                        "target" => "Target",
                        "character" or "btlChr" => "Character",
                        "group" => "Group",
                        "property" => "Property",
                        "selector" => "Selector",
                        "command" => "Command",
                        _ => FormatParameterLabel(parameter)
                    };
                    argument.SemanticOperandDisplay = $"{role} variable [0x{argument.Operand:X4}]";
                    continue;
                }
                if (parameter is "character" or "target" or "group" or "btlChr")
                    argument.SemanticOperandDisplay = FormatBattleCharacter(raw);
                else if (parameter == "property")
                    argument.SemanticOperandDisplay = callInstruction.Operand switch
                    {
                        0x701A => FormatCommandProperty(raw),
                        0x7018 => FormatStatProperty(raw),
                        0x70AB => FormatStatProperty(raw),
                        0x70B2 => FormatMotionProperty(raw),
                        _ => FormatBattleProperty(raw)
                    };
                else if (parameter == "selector")
                    argument.SemanticOperandDisplay = FormatSelector(raw);
                else if (parameter == "command")
                    argument.SemanticOperandDisplay = FormatCommand(raw, commandNameResolver);
                else if (callInstruction.Operand == 0x7028 && parameter is "x" or "y" or "z")
                {
                    string value = argument.Opcode == 0xAF
                        ? floatConstantResolver?.Invoke(argument.Operand) ?? argument.OperandDisplay
                        : argument.OperandDisplay;
                    argument.SemanticOperandDisplay = $"Scale {parameter.ToUpperInvariant()}: {value}";
                }
                else
                {
                    string value = argument.Opcode == 0xAF
                        ? floatConstantResolver?.Invoke(argument.Operand) ?? argument.OperandDisplay
                        : raw;
                    argument.SemanticOperandDisplay = $"{FormatParameterLabel(parameter)}: {value}";
                }
            }

            if (callInstruction.Operand == 0x701A && firstArgument + 1 < callIndex &&
                instructions[firstArgument + 1].Operand == 0x0001 && callIndex + 2 < instructions.Count &&
                instructions[callIndex + 1].Opcode == 0xAE && instructions[callIndex + 2].Opcode is >= 0x01 and <= 0x18)
            {
                AtelInstruction comparedValue = instructions[callIndex + 1];
                comparedValue.SemanticOperandDisplay = FormatDamageType(comparedValue.Operand);
            }
        }

        for (int index = 1; index + 1 < instructions.Count; index++)
        {
            AtelInstruction value = instructions[index];
            if (value.Opcode == 0xAE && instructions[index - 1].Opcode == 0xB5 &&
                instructions[index - 1].Operand == 0x00A9 && instructions[index + 1].Opcode == 0x18)
                value.SemanticOperandDisplay = $"Random range 0 to {Math.Max(0, value.Operand - 1)} [0x{value.Operand:X4}]";
        }
    }

    private static string TranslateOne(AtelInstruction ins, Stack<string> stack, Func<ushort, string?>? commandNameResolver,
        Func<ushort, string?>? floatConstantResolver)
    {
        byte op = ins.Opcode;
        if (op == 0x00) return "No operation";
        if (op == 0xAE) { string value = FormatInt16(ins.Operand); stack.Push(value); return $"Value for the next action: {value}"; }
        if (op == 0xAD) { string value = $"integerConstant[0x{ins.Operand:X4}]"; stack.Push(value); return $"Push {value}"; }
        if (op == 0xAF) { string value = floatConstantResolver?.Invoke(ins.Operand) ?? $"floatConstant[0x{ins.Operand:X4}]"; stack.Push(value); return $"Push {value}"; }
        if (op == 0x9F) { string value = $"variable[0x{ins.Operand:X4}]"; stack.Push(value); return $"Read {value}"; }
        if (op >= 0x01 && op <= 0x18)
        {
            string right = Pop(stack), left = Pop(stack);
            if (left.Contains("property=damageType", StringComparison.Ordinal) && TryReadValue(right, out ushort damageType))
                right = FormatDamageType(damageType);
            string symbol = BinarySymbol(op);
            string value = $"({left} {symbol} {right})";
            stack.Push(value);
            return $"Calculate {value}";
        }
        if (op is 0x19 or 0x1A or 0x1C)
        {
            string value = Pop(stack);
            string result = op == 0x19 ? $"!({value})" : op == 0x1A ? $"-({value})" : $"~({value})";
            stack.Push(result);
            return $"Calculate {result}";
        }
        if (op == 0x26) { stack.Push("LastCallResult"); return "Push the previous call's result"; }
        if (op == 0x28) { stack.Push("test"); return "Push current test result"; }
        if (op == 0x29) { stack.Push("case"); return "Push current switch case"; }
        if (op == 0x2B) { string value = Pop(stack); stack.Push(value); stack.Push(value); return $"Duplicate {value}"; }
        if (op == 0xA2) { string index = Pop(stack); string value = $"variable[0x{ins.Operand:X4}][{index}]"; stack.Push(value); return $"Read {value}"; }
        if (op == 0xB5) { string call = FormatCall(ins.Operand, stack, commandNameResolver); stack.Push(call); return $"Call {call} and push its result"; }
        if (op == 0xD8)
        {
            string call = FormatCall(ins.Operand, stack, commandNameResolver);
            return call.StartsWith("Set ", StringComparison.Ordinal) ? call : $"Call {call}";
        }
        if (op is 0xA0 or 0xA1) return $"Set variable[0x{ins.Operand:X4}] = {Pop(stack)}";
        if (op is 0xA3 or 0xA4) { string value = Pop(stack), index = Pop(stack); return $"Set variable[0x{ins.Operand:X4}][{index}] = {value}"; }
        if (op == 0x25) return $"Use/return {Pop(stack)}";
        if (op == 0x2A) return $"Set test = {Pop(stack)}";
        if (op == 0x2C) return $"Switch on {Pop(stack)}";
        if (op == 0xB0) return $"Jump to j{ins.Operand:X2}";
        if (op is 0xB1 or 0xD6) return $"Check ({Pop(stack)}) else jump to j{ins.Operand:X2}";
        if (op is 0xB2 or 0xD7) return $"Check ({Pop(stack)}) else jump to j{ins.Operand:X2}";
        if (op == 0xD5) return $"Check ({Pop(stack)}) then jump to j{ins.Operand:X2}";
        if (op == 0x34) return "Return from subroutine";
        if (op == 0x3C) return "Return from this function";
        if (op == 0x40) return "Halt this worker";
        if (op == 0x54) return "Direct return";
        if (op >= 0x59 && op <= 0x5C) return $"Set temporary integer {op - 0x59} = {Pop(stack)}";
        if (op >= 0x5D && op <= 0x66) return $"Set temporary float {op - 0x5D} = {Pop(stack)}";
        if (op >= 0x67 && op <= 0x6A) { string v = $"temporaryInteger[{op - 0x67}]"; stack.Push(v); return $"Push {v}"; }
        if (op >= 0x6B && op <= 0x74) { string v = $"temporaryFloat[{op - 0x6B}]"; stack.Push(v); return $"Push {v}"; }
        return AtelOpcodeNames.GetName(op);
    }

    private static string FormatCall(ushort target, Stack<string> stack, Func<ushort, string?>? commandNameResolver)
    {
        if (!Calls.TryGetValue(target, out CallInfo? info)) return $"function[0x{target:X4}](parameters unknown)";
        string[] values = new string[info.Parameters.Length];
        for (int i = values.Length - 1; i >= 0; i--)
        {
            values[i] = Pop(stack);
            if (info.Parameters[i] is "character" or "target" or "group" or "btlChr")
                values[i] = FormatBattleCharacter(values[i]);
            if (target == 0x700B && i == 1) values[i] = FormatCommand(values[i], commandNameResolver);
            if (info.Parameters[i] == "property")
                values[i] = target switch
                {
                    0x701A => FormatCommandProperty(values[i]),
                    0x7018 => FormatStatProperty(values[i]),
                    0x70AB => FormatStatProperty(values[i]),
                    0x70B2 => FormatMotionProperty(values[i]),
                    _ => FormatBattleProperty(values[i])
                };
            if (info.Parameters[i] == "selector") values[i] = FormatSelector(values[i]);
            if (target == 0x7026 && info.Parameters[i] == "state")
                values[i] = FormatNamedValue(values[i], AtelStatProperties.EnumValues[0x0008]);
            if (target == 0x7034 && info.Parameters[i] == "result")
                values[i] = FormatNamedValue(values[i], new Dictionary<ushort, string>
                {
                    [0x0001] = "Defeat", [0x0002] = "Victory",
                    [0x0003] = "Player Escaped", [0x0004] = "Monster Escaped"
                });
            if (target == 0x706B && info.Parameters[i] == "visible")
                values[i] = FormatNamedValue(values[i], new Dictionary<ushort, string>
                {
                    [0x0000] = "Hidden", [0x0001] = "Visible"
                });
        }
        if (target == 0x7018 && values.Length == 3)
        {
            values[2] = FormatStatValue(values[1], values[2], commandNameResolver);
            return $"Set {values[0]}.{values[1]} = {values[2]}";
        }
        if (target == 0x70AB && values.Length == 2)
        {
            values[1] = FormatStatValue(values[0], values[1], commandNameResolver);
            return $"Set Self.{values[0]} = {values[1]}";
        }
        if (info.IsAccessorWrite && values.Length == 2 && target == 0x70B2)
            return $"Set Self.{values[0]} = {values[1]}";
        if (info.IsAccessorWrite && values.Length == 2)
            return $"Set {info.Name}[{values[0]}] = {values[1]}";
        return $"{info.Name} [0x{target:X4}]({string.Join(", ", info.Parameters.Zip(values, (name, value) => $"{name}={value}"))})";
    }

    private static string FormatCommand(string value, Func<ushort, string?>? resolver)
    {
        int marker = value.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || marker + 6 > value.Length ||
            !ushort.TryParse(value.AsSpan(marker + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out ushort gameIndex))
            return value;
        string? name = resolver?.Invoke(gameIndex);
        return string.IsNullOrWhiteSpace(name) ? value : $"[{name}] [0x{gameIndex:X4}]";
    }

    private static string FormatNamedValue(string value, IReadOnlyDictionary<ushort, string> names)
    {
        if (!TryReadValue(value, out ushort raw) || !names.TryGetValue(raw, out string? name)) return value;
        return $"{name} [0x{raw:X4}]";
    }

    private static string FormatStatValue(string propertyText, string value, Func<ushort, string?>? commandNameResolver)
    {
        if (!TryReadValue(propertyText, out ushort property) || !TryReadValue(value, out ushort raw)) return value;
        if (AtelStatProperties.BooleanProperties.Contains(property))
            return $"{(raw == 0 ? "False" : raw == 1 ? "True" : $"Boolean({raw})")} [0x{raw:X4}]";
        if (AtelStatProperties.CommandProperties.Contains(property))
            return FormatCommand(value, commandNameResolver);
        if (AtelStatProperties.EnumValues.TryGetValue(property, out IReadOnlyDictionary<ushort, string>? names))
            return FormatNamedValue(value, names);
        return value;
    }

    private static string FormatBattleProperty(string value)
    {
        if (!TryReadValue(value, out ushort property) || !BattleProperties.TryGetValue(property, out string? name)) return value;
        return $"{name} [0x{property:X4}]";
    }

    private static string FormatMotionProperty(string value)
    {
        if (!TryReadValue(value, out ushort property) || !MotionProperties.TryGetValue(property, out string? name)) return value;
        return $"{name} [0x{property:X2}]";
    }

    private static string FormatStatProperty(string value)
    {
        if (!TryReadValue(value, out ushort property) || !AtelStatProperties.Names.TryGetValue(property, out string? name)) return value;
        return $"{name} [0x{property:X4}]";
    }

    private static string FormatCommandProperty(string value)
    {
        if (!TryReadValue(value, out ushort property) || !CommandProperties.TryGetValue(property, out string? name)) return value;
        return $"{name} [0x{property:X2}]";
    }

    private static string FormatDamageType(ushort value)
    {
        string? name = value switch { 0x0000 => "Special", 0x0001 => "Physical", 0x0002 => "Magical", _ => null };
        return name == null ? FormatInt16(value) : $"{name} [0x{value:X2}]";
    }

    private static string FormatParameterLabel(string parameter)
    {
        if (string.IsNullOrEmpty(parameter)) return "Value";
        string spaced = string.Concat(parameter.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private static string FormatSelector(string value)
    {
        if (!TryReadValue(value, out ushort selector)) return value;
        string? name = selector switch { 0x0000 => "Any/All", 0x0001 => "Highest", 0x0002 => "Lowest", 0x0080 => "Not", _ => null };
        return name == null ? value : $"{name} [0x{selector:X2}]";
    }

    private static bool TryReadValue(string value, out ushort result)
    {
        result = 0;
        int marker = value.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 && marker + 6 <= value.Length &&
            ushort.TryParse(value.AsSpan(marker + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out result);
    }

    private static string FormatBattleCharacter(string value)
    {
        if (value.Contains("variable[", StringComparison.OrdinalIgnoreCase)) return value;
        if (!TryReadValue(value, out ushort character) || !BattleCharacters.TryGetValue(character, out string? name)) return value;
        return $"{name} [0x{character:X4}]";
    }

    private static CallInfo C(string name, params string[] parameters) => new(name, parameters);

    internal static string[]? GetCallParameters(ushort target) =>
        Calls.TryGetValue(target, out CallInfo? call) ? call.Parameters.ToArray() : null;
    private static string Pop(Stack<string> stack) => stack.Count > 0 ? stack.Pop() : "<?>";
    private static string FormatInt16(ushort value) => ((short)value) < 0 ? $"{(short)value} (0x{value:X4})" : $"{value} (0x{value:X4})";
    private static string BinarySymbol(byte opcode) => opcode switch
    {
        0x01 => "or", 0x02 => "and", 0x03 => "|", 0x04 => "^", 0x05 => "&", 0x06 => "==", 0x07 => "!=",
        0x08 or 0x0A => ">", 0x09 or 0x0B => "<", 0x0C or 0x0E => ">=", 0x0D or 0x0F => "<=",
        0x10 => "enable-bit", 0x11 => "disable-bit", 0x12 => "<<", 0x13 => ">>", 0x14 => "+", 0x15 => "-",
        0x16 => "*", 0x17 => "/", 0x18 => "%", _ => "?"
    };
    private static bool IsLineEnd(byte op) => op is 0x25 or 0x2A or 0x2C or 0x34 or 0x3C or 0x40 or 0x54 or 0x77 or 0x78 or 0x79 or
        0xA0 or 0xA1 or 0xA3 or 0xA4 or 0xB0 or 0xB1 or 0xB2 or 0xB3 or 0xD5 or 0xD6 or 0xD7 or 0xD8 or 0xF6 ||
        (op >= 0x36 && op <= 0x3F) || (op >= 0x45 && op <= 0x66);
}
