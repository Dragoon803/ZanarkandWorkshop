using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Atel;

public static class AtelOpcodeNames
{
    private static readonly Dictionary<byte, string> Names = new()
    {
        [0x00] = "NOP", [0x01] = "OR", [0x02] = "AND", [0x03] = "BIT_OR", [0x04] = "BIT_XOR",
        [0x05] = "BIT_AND", [0x06] = "EQ", [0x07] = "NE", [0x08] = "GT_UNSIGNED", [0x09] = "LT_UNSIGNED",
        [0x0A] = "GT", [0x0B] = "LT", [0x0C] = "GTE_UNSIGNED", [0x0D] = "LTE_UNSIGNED", [0x0E] = "GTE",
        [0x0F] = "LTE", [0x14] = "ADD", [0x15] = "SUB", [0x16] = "MUL", [0x17] = "DIV", [0x18] = "MOD",
        [0x19] = "NOT", [0x25] = "SET_RESULT", [0x26] = "GET_RESULT", [0x28] = "GET_TEST", [0x29] = "CASE",
        [0x2A] = "SET_TEST", [0x2B] = "DUP", [0x2C] = "SWITCH", [0x34] = "RETURN_SUBROUTINE",
        [0x3C] = "RETURN", [0x40] = "HALT", [0x54] = "DIRECT_RETURN", [0x9F] = "GET_VARIABLE",
        [0xA0] = "SET_VARIABLE", [0xA2] = "GET_ARRAY", [0xA3] = "SET_ARRAY", [0xAD] = "PUSH_INT32_REF",
        [0xAE] = "PUSH_INT16", [0xAF] = "PUSH_FLOAT_REF", [0xB0] = "JUMP", [0xB1] = "JUMP_TRUE",
        [0xB2] = "JUMP_FALSE", [0xB3] = "CALL_WORKER", [0xB5] = "CALL", [0xD5] = "TEST_JUMP",
        [0xD6] = "TEST_JUMP_TRUE", [0xD7] = "TEST_JUMP_FALSE", [0xD8] = "CALL_VOID", [0xF6] = "SYSTEM"
    };

    public static string GetName(byte opcode) => Names.TryGetValue(opcode, out string? name) ? name : $"OP_{opcode:X2}";
}
