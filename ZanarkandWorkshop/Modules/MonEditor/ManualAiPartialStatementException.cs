using System;

namespace FFXProjectEditor.Modules.MonEditor;

internal sealed class ManualAiPartialStatementException : InvalidOperationException
{
    internal int StatementOffset { get; }
    internal int StatementChunkOffset { get; }
    internal int RemainingByteLength { get; }
    internal string DeletedBytes { get; }
    internal string StatementTranslation { get; }

    internal ManualAiPartialStatementException(int statementOffset, int statementChunkOffset,
        int remainingByteLength, string deletedBytes, string statementTranslation)
        : base($"Partial grouped-statement deletion at 0x{statementOffset:X4}. Removed bytes {deletedBytes} leave an incomplete expression or control-flow operation.")
    {
        StatementOffset = statementOffset;
        StatementChunkOffset = statementChunkOffset;
        RemainingByteLength = remainingByteLength;
        DeletedBytes = deletedBytes;
        StatementTranslation = statementTranslation;
    }
}
