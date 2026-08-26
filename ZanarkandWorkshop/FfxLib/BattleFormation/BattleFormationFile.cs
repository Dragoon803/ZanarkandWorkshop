using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.BattleFormation;

public enum FormationPositionKind
{
    Party,
    PartySecondary,
    Aeon,
    Monster,
    MonsterSecondary
}
public sealed record FormationPosition(
    FormationPositionKind Kind,
    int Index,
    int FileOffset,
    float X,
    float Y,
    float Z,
    float W);

public sealed class BattleFormationFile
{
    public required byte[] OriginalBytes { get; init; }
    public required string SourcePath { get; init; }
    public required int EncounterOffset { get; init; }
    public required int PositionHeaderOffset { get; init; }
    public required ushort[] EnemyIds { get; init; }
    public required IReadOnlyList<FormationPosition> Positions { get; init; }
    public required byte PartyCount { get; init; }
    public required byte AeonCount { get; init; }
    public required byte MonsterCount { get; init; }
    public required bool CanResizeMonsterTables { get; init; }
}
