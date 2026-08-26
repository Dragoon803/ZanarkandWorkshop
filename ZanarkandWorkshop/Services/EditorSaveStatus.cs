using System;
using System.Globalization;

namespace FFXProjectEditor.Services;

/// <summary>Provides the standard success message for current and future editors.</summary>
public static class EditorSaveStatus
{
    public static string Success(string itemName) =>
        $"{itemName} saved successfully — {DateTime.Now.ToString("t", CultureInfo.CurrentCulture)}";
}
