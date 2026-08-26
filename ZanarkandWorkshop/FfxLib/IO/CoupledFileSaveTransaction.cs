using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.IO;

/// <summary>
/// Stages and installs two related files as one logical save. If installation
/// of either file fails, every file already installed is restored before the
/// error is returned to the caller.
/// </summary>
public static class CoupledFileSaveTransaction
{
    public static void Save(
        string firstPath,
        byte[] firstBytes,
        string secondPath,
        byte[] secondBytes) =>
        Save(firstPath, firstBytes, secondPath, secondBytes, beforeInstall: null);

    internal static void Save(
        string firstPath,
        byte[] firstBytes,
        string secondPath,
        byte[] secondBytes,
        Action<int>? beforeInstall)
    {
        MultiFileSaveTransaction.Save(
            new List<FileReplacement>
            {
                new(firstPath, firstBytes),
                new(secondPath, secondBytes)
            },
            beforeInstall);
    }
}
