using Avalonia.Controls;
using FFXProjectEditor.Modules.MonEditor;
using static FFXProjectEditor.Modules.MonEditor.MonEditorSelector_DataModel;

namespace FFXProjectEditor;

public partial class MonEditorSelector_Control : UserControl
{
    MonEditorSelector_DataModel DataModel;
	private MonsterListEntry? _lastSuccessfulSelection;
	private bool _restoringSelection;
    public MonEditorSelector_Control()
    {
        DataModel = new MonEditorSelector_DataModel();
        this.DataContext = DataModel;
        InitializeComponent();
    }

    private async void ListBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
		if (_restoringSelection) return;
		if (MonsterList.SelectedItem is not MonsterListEntry selected) return;
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
}
