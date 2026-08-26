using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FFXProjectEditor;

internal static class RecoverySource_Util
{
    public static async Task<bool> EnsureConfiguredAsync(Window owner)
    {
        string? savedPath = VanillaReference_Service.MasterPath;
        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            VanillaReference_Service.ValidationResult savedValidation = await Task.Run(() =>
                VanillaReference_Service.Validate(savedPath));
            if (savedValidation.CanConfigure)
            {
                bool savedAccepted = !savedValidation.RequiresAcceptance ||
                    VanillaReference_Service.IsAccepted(savedValidation) ||
                    await RecoveryVerification_Window.Show(owner, savedValidation, savedPath);
                if (savedAccepted)
                {
                    VanillaReference_Service.Configure(savedPath, savedValidation.RequiresAcceptance);
                    return true;
                }
                return false;
            }
        }

        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select your clean, unedited FFX Original Game Files folder",
                AllowMultiple = false
            });
        if (folders.Count == 0) return false;
        string? selectedPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
            throw new InvalidOperationException("No local folder was selected.");

        VanillaReference_Service.ValidationResult validation = await Task.Run(() =>
            VanillaReference_Service.Validate(selectedPath, true));
        if (!validation.CanConfigure)
            throw new InvalidOperationException(validation.Summary);
        bool accepted = !validation.RequiresAcceptance ||
            await RecoveryVerification_Window.Show(owner, validation, selectedPath);
        if (!accepted) return false;
        VanillaReference_Service.Configure(selectedPath, validation.RequiresAcceptance);
        return true;
    }
}
