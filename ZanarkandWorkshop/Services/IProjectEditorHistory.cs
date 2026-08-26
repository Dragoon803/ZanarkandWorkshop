namespace FFXProjectEditor.Services;

/// <summary>Shared editing-history contract used by editor footers and global shortcuts.</summary>
public interface IProjectEditorHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool CanUndoAll { get; }
    void Undo();
    void Redo();
    void UndoAll();
}
