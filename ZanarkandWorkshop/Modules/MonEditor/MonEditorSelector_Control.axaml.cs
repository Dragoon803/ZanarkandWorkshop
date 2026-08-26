using Avalonia.Controls;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using Avalonia.Input;
using static FFXProjectEditor.Modules.MonEditor.MonEditorSelector_DataModel;

namespace FFXProjectEditor;

public partial class MonEditorSelector_Control : UserControl, IProjectEditorSave, IProjectEditorHistory
{
    MonEditorSelector_DataModel DataModel;
	private MonsterListEntry? _lastSuccessfulSelection;
	private bool _restoringSelection;
	private MonEditor_Control? _subscribedEditor;
    public MonEditorSelector_Control()
    {
        DataModel = new MonEditorSelector_DataModel();
        this.DataContext = DataModel;
        InitializeComponent();
        AddHandler(KeyDownEvent, Selector_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public MonEditor_Control? ActiveMonsterEditor => ContentFrame.Content as MonEditor_Control;
    public bool HasPendingChanges => ActiveMonsterEditor?.IsDirty == true;
    public void Save()
    {
        if (ActiveMonsterEditor?.SaveChanges() != true)
            throw new System.InvalidOperationException("Select a modified monster before saving.");
        SaveStatusText.Text = EditorSaveStatus.Success("Monster");
    }
    public void SaveToMaster(string masterPath, string metadataPath)
    {
        if (ActiveMonsterEditor is not { } editor)
            throw new System.InvalidOperationException("Select a monster before saving from the Monster Editor.");
        editor.SaveToMaster(masterPath);
    }

    private async void ListBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
		if (_restoringSelection) return;
		if (MonsterList.SelectedItem is not MonsterListEntry selected) return;
		if (_lastSuccessfulSelection is not null && selected.Index != _lastSuccessfulSelection.Index &&
			ActiveMonsterEditor?.IsDirty == true && TopLevel.GetTopLevel(this) is Main_Window transitionOwner)
		{
			RestoreLastMonsterSelection();
			if (!await transitionOwner.ResolvePendingChangesAsync(
				"Selecting another monster will replace the current monster.")) return;
			_restoringSelection = true;
			try { MonsterList.SelectedItem = selected; }
			finally { _restoringSelection = false; }
		}
		string monsterPath = FFXProjectEditor.Services.Project_Service.Instance.GetPathMon(selected.Index);
		string? monsterFolder = System.IO.Path.GetDirectoryName(monsterPath);
		if (string.IsNullOrWhiteSpace(monsterFolder) || !System.IO.Directory.Exists(monsterFolder))
		{
			if (TopLevel.GetTopLevel(this) is Window missingFolderOwner)
			{
				await RecoveryNotice_Window.Show(missingFolderOwner, "Monster folder is missing",
					"This monster couldn’t be opened because a required folder is missing. Close this message to continue using the program.",
					monsterFolder ?? monsterPath, false);
			}
			RestoreLastMonsterSelection();
			return;
		}
		if (!System.IO.File.Exists(monsterPath))
		{
			if (TopLevel.GetTopLevel(this) is Window missingOwner)
			{
				await RecoveryNotice_Window.Show(missingOwner, "Monster file is missing",
					"This monster couldn’t be opened because a required file is missing. Close this message to continue using the program.",
					monsterPath, false);
			}
			RestoreLastMonsterSelection();
			return;
		}
		try
		{
			DataModel.LoadMonster(selected, ContentFrame);
			_lastSuccessfulSelection = selected;
			if (ActiveMonsterEditor is { } editor)
			{
				if (_subscribedEditor is not null)
				{
					_subscribedEditor.DirtyStateChanged -= ActiveEditor_DirtyStateChanged;
					_subscribedEditor.FooterStateChanged -= ActiveEditor_FooterStateChanged;
					_subscribedEditor.SectionRecoveryCompleted -= ActiveEditor_SectionRecoveryCompleted;
				}
				_subscribedEditor = editor;
				editor.DirtyStateChanged += ActiveEditor_DirtyStateChanged;
				editor.FooterStateChanged += ActiveEditor_FooterStateChanged;
				editor.SectionRecoveryCompleted += ActiveEditor_SectionRecoveryCompleted;
				UpdateSaveButtonState();
				UpdateFooterState();
			}
		}
		catch (System.Exception ex)
		{
			if (TopLevel.GetTopLevel(this) is Window owner)
			{
				await RecoveryNotice_Window.Show(owner, "Monster file could not be opened",
					"The selected monster file is missing, unreadable, or malformed. The application can continue running.\n\n" + ex.Message,
					monsterPath, false);
			}
			RestoreLastMonsterSelection();
		}
    }

	private void RestoreLastMonsterSelection()
	{
		_restoringSelection = true;
		try
		{
			MonsterList.SelectedItem = _lastSuccessfulSelection;
			if (_lastSuccessfulSelection is not null)
				MonsterList.ScrollIntoView(_lastSuccessfulSelection);
		}
		finally
		{
			_restoringSelection = false;
		}
	}

    private void Filter_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        DataModel.ApplyFilter();
    }

    private async void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Main_Window owner &&
            !await owner.EnsureActiveProjectRegisteredAsync()) return;
        if (ActiveMonsterEditor?.SaveChanges() == true)
            SaveStatusText.Text = EditorSaveStatus.Success("Monster");
    }

	private void ActiveEditor_DirtyStateChanged(object? sender, System.EventArgs e)
	{
		UpdateSaveButtonState();
		if (TopLevel.GetTopLevel(this) is Main_Window owner)
			owner.RefreshSaveCommandState();
	}

	private void ActiveEditor_FooterStateChanged(object? sender, System.EventArgs e)
	{
		if (sender is MonEditor_Control editor)
			HistoryStatusText.Text = editor.HistoryStatus;
		UpdateFooterState();
	}

	private void ActiveEditor_SectionRecoveryCompleted(object? sender, System.EventArgs e)
	{
		if (_lastSuccessfulSelection is null) return;
		string message = (sender as MonEditor_Control)?.LastRecoveryMessage ??
			"Original monster section restored and saved.";
		DataModel.LoadMonster(_lastSuccessfulSelection, ContentFrame);
		if (ActiveMonsterEditor is { } editor)
		{
			_subscribedEditor = editor;
			editor.DirtyStateChanged += ActiveEditor_DirtyStateChanged;
			editor.FooterStateChanged += ActiveEditor_FooterStateChanged;
			editor.SectionRecoveryCompleted += ActiveEditor_SectionRecoveryCompleted;
		}
		SaveStatusText.Text = message;
		UpdateSaveButtonState();
		UpdateFooterState();
	}

	private void UpdateSaveButtonState()
	{
		SaveButton.IsEnabled = ActiveMonsterEditor?.IsDirty == true;
	}

	private void UpdateFooterState()
	{
		UndoButton.IsEnabled = ActiveMonsterEditor?.CanUndo == true;
		RedoButton.IsEnabled = ActiveMonsterEditor?.CanRedo == true;
		UndoAllButton.IsEnabled = ActiveMonsterEditor?.CanUndoAll == true;
		HistoryStatusText.Text = ActiveMonsterEditor?.HistoryStatus ?? "";
	}

	private void Undo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
		ActiveMonsterEditor?.Undo();

	private void Redo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
		ActiveMonsterEditor?.Redo();

	private void UndoAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
		ActiveMonsterEditor?.UndoAll();

    public bool CanUndo => ActiveMonsterEditor?.CanUndo == true;
    public bool CanRedo => ActiveMonsterEditor?.CanRedo == true;
    public bool CanUndoAll => ActiveMonsterEditor?.CanUndoAll == true;
    public void Undo() => ActiveMonsterEditor?.Undo();
    public void Redo() => ActiveMonsterEditor?.Redo();
    public void UndoAll() => ActiveMonsterEditor?.UndoAll();

	private void Selector_KeyDown(object? sender, KeyEventArgs e)
	{
		if (ActiveMonsterEditor?.IsBattleScriptTabActive == true &&
			e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
			TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
		{
			if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
			{ ActiveMonsterEditor?.RedoBattleScript(); e.Handled = true; }
			else if (e.Key == Key.Z)
			{ ActiveMonsterEditor?.UndoBattleScript(); e.Handled = true; }
			else if (e.Key == Key.Y)
			{ ActiveMonsterEditor?.RedoBattleScript(); e.Handled = true; }
		}
	}
    private async void SaveAs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ActiveMonsterEditor is not { } editor || TopLevel.GetTopLevel(this) is not Main_Window owner) return;
        await owner.SaveAsActiveProjectAsync((master, _) =>
        {
            editor.SaveToMaster(master);
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }
}
