namespace FFXProjectEditor.Services;

public interface IProjectEditorSave
{
    bool HasPendingChanges { get; }
    void Save();
    void SaveToMaster(string masterPath, string metadataPath);
}
