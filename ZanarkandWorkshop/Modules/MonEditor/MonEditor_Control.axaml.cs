using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.Atel;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace FFXProjectEditor;

public partial class MonEditor_Control : UserControl
{
    private readonly MonEditor_DataModel DataModel;
    private string _lastSearch = "";
    private int _searchResultIndex;
    private AtelInstruction? _selectedInstruction;
    private bool _synchronizingInstructionSelection;
    private bool _synchronizingStatementSelection;
    private bool _updatingMeaningOptions;
    private bool _aiHexIsDirty;
    private bool _restoringAiHistory;
    private Exception? _lastAiActionException;
    private string? _semanticRole;
    private readonly List<GroupOperandEditor> _groupOperandEditors = [];
    private byte[]? _copiedStatementBytes;
    private int _copiedStatementOffset = -1;
    private bool _copiedStatementFallsThrough;
    private bool _copiedStatementHasConditionalJump;
    private int _copiedStatementWorkerIndex = -1;
	private int _selectedWorkerIndex = -1;
	private bool _updatingWorkerScope;
	private int _selectedFunctionIndex = -1;
	private bool _updatingFunctionScope;
	private ScrollViewer? _aiHexScrollViewer;
	private ScrollViewer? _aiJumpScrollViewer;
	private AtelInstruction? _activeJumpInstruction;
	private AiLogicSelectionOwner _logicSelectionOwner;
	private int _aiHexSelectionVersion;
	private static bool _hideStatementsPreference;
	private static bool _hideInstructionsPreference;
	private static bool _suppressDeleteStatementWarning;
	private static bool _logicPreferencesLoaded;
	private static readonly string LogicPreferencesPath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"FFXProjectEditor", "ai-editor-preferences.json");

	private enum AiLogicSelectionOwner
	{
		None,
		Statement,
		Instruction
	}

    private sealed record OperandChoice(string Name, ushort Value, byte Opcode = 0xAE, string? DisplayOverride = null)
    {
        public override string ToString() => DisplayOverride ?? $"{Name}  [0x{Value:X4}]";
    }

    private sealed record GroupOperandEditor(int InstructionOffset, string Role, ComboBox? Options, TextBox? ValueText,
        ushort? FloatIndex = null);

	private sealed record WorkerScopeChoice(int Index, string Display);
	private sealed record FunctionScopeChoice(int Index, int Start, int End, string Display);
	private sealed record WorkerJumpChoice(int Index, int ScriptOffset, string Display);
	public sealed record AiEditorPreferences(bool HideGroupedLogic, bool HideDecodedInstructions,
		bool SuppressDeleteStatementWarning = false);

    private static readonly OperandChoice[] BattleTargets = AtelDecompiler.BattleCharacters
        .OrderBy(entry => entry.Key)
        .Select(entry => new OperandChoice(entry.Value, entry.Key))
        .ToArray();

    public MonEditor_Control(Monster_File monFile, string monsterPath, MonEditorSelector_DataModel selectorDM)
    {
        DataModel = new MonEditor_DataModel(monFile, monsterPath, selectorDM);
        DataContext = DataModel;
		LoadLogicVisibilityPreferences();
        InitializeComponent();
		AiHideStatements.IsChecked = _hideStatementsPreference;
		AiHideInstructions.IsChecked = _hideInstructionsPreference;
		ApplyStoredLogicVisibility();
        AiHexText.TextChanged += AiHexText_TextChanged;
		AiHexText.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, AiHexText_PointerPressed,
			Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
			handledEventsToo: true);
		AiHexText.AttachedToVisualTree += (_, _) => InitializeJumpDestinationOverlay();
		InitializeWorkerScopes();
    }

	private void InitializeWorkerScopes(int preferredWorkerIndex = -1)
	{
		_updatingWorkerScope = true;
		try
		{
			var choices = new List<WorkerScopeChoice>
			{
				new(-1, "All Workers — complete Battle Script")
			};
			if (DataModel.AiDocument != null)
				choices.AddRange(DataModel.AiDocument.Workers.Select(worker => new WorkerScopeChoice(worker.Index, worker.Display)));
			AiWorkerList.ItemsSource = choices;
			AiWorkerList.SelectedItem = choices.FirstOrDefault(choice => choice.Index == preferredWorkerIndex) ?? choices[0];
			_selectedWorkerIndex = (AiWorkerList.SelectedItem as WorkerScopeChoice)?.Index ?? -1;
		}
		finally
		{
			_updatingWorkerScope = false;
		}
		InitializeFunctionScopes();
		InitializeWorkerJumps();
		ApplyWorkerScope();
		ShowWorkerSelectionEditor();
	}

	private void AiWorker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_updatingWorkerScope || AiWorkerList.SelectedItem is not WorkerScopeChoice choice) return;
		FocusSelectionEditor();
		_selectedWorkerIndex = choice.Index;
		InitializeFunctionScopes();
		InitializeWorkerJumps();
		ApplyWorkerScope();
		ShowWorkerSelectionEditor();
	}

	private void AiWorker_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
	{
		if (_updatingWorkerScope || AiWorkerList.SelectedItem is not WorkerScopeChoice choice) return;
		FocusSelectionEditor();
		_selectedWorkerIndex = choice.Index;
		ApplyWorkerScope();
		ShowWorkerSelectionEditor();
	}

	private void AiWorkerJump_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		FocusSelectionEditor();
		ShowWorkerSelectionEditor();
		HighlightSelectedWorkerJump();
	}

	private void AiWorkerJump_DropDownClosed(object? sender, EventArgs e) =>
		HighlightSelectedWorkerJump();

	private void HighlightSelectedWorkerJump()
	{
		if (AiWorkerJumpOptions.SelectedItem is not WorkerJumpChoice choice || choice.ScriptOffset < 0) return;
		_activeJumpInstruction = null;
		HighlightJumpDestinationScriptOffset(choice.ScriptOffset);
		AiStatusText.Text = $"Worker jump destination highlighted at Battle Script offset 0x{DataModel.AiDocument!.ScriptCodeOffset + choice.ScriptOffset:X}.";
	}

	private void InitializeWorkerJumps()
	{
		var choices = new List<WorkerJumpChoice>();
		if (DataModel.AiDocument == null || _selectedWorkerIndex < 0)
		{
			choices.Add(new(-1, -1, "Select a worker"));
			AiWorkerJumpOptions.IsEnabled = false;
			AiWorkerJumpButton.IsEnabled = false;
		}
		else
		{
			AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == _selectedWorkerIndex);
			if (worker != null)
			{
				for (int index = 0; index < worker.JumpOffsets.Count; index++)
				{
					int scriptOffset = worker.JumpOffsets[index];
					int chunkOffset = DataModel.AiDocument.ScriptCodeOffset + scriptOffset;
					choices.Add(new(index, scriptOffset, $"j{index:X2} [0x{index:X4}] -> offset 0x{chunkOffset:X6}"));
				}
			}
			if (choices.Count == 0) choices.Add(new(-1, -1, "This worker has no jumps"));
			AiWorkerJumpOptions.IsEnabled = choices.Any(choice => choice.Index >= 0);
			AiWorkerJumpButton.IsEnabled = choices.Any(choice => choice.Index >= 0);
		}
		AiWorkerJumpOptions.ItemsSource = choices;
		AiWorkerJumpOptions.SelectedIndex = 0;
	}

	private void RefreshNavigationAfterDocumentChange()
	{
		int workerIndex = _selectedWorkerIndex;
		int functionIndex = _selectedFunctionIndex;
		int jumpIndex = (AiWorkerJumpOptions.SelectedItem as WorkerJumpChoice)?.Index ?? -1;
		InitializeWorkerScopes(workerIndex);
		if (functionIndex >= 0 && AiFunctionOptions.ItemsSource is IEnumerable<FunctionScopeChoice> functions)
		{
			FunctionScopeChoice? function = functions.FirstOrDefault(choice => choice.Index == functionIndex);
			if (function != null) AiFunctionOptions.SelectedItem = function;
		}
		if (jumpIndex >= 0 && AiWorkerJumpOptions.ItemsSource is IEnumerable<WorkerJumpChoice> jumps)
		{
			WorkerJumpChoice? jump = jumps.FirstOrDefault(choice => choice.Index == jumpIndex);
			if (jump != null) AiWorkerJumpOptions.SelectedItem = jump;
		}
	}

	private void InitializeFunctionScopes()
	{
		_updatingFunctionScope = true;
		try
		{
			var choices = new List<FunctionScopeChoice>();
			if (DataModel.AiDocument == null || _selectedWorkerIndex < 0)
			{
				choices.Add(new(-1, 0, DataModel.AiDocument?.ScriptCodeLength ?? 0, "Select a worker"));
				AiFunctionOptions.IsEnabled = false;
			}
			else
			{
				AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == _selectedWorkerIndex);
				(int workerStart, int workerEnd) = GetWorkerScriptRange(_selectedWorkerIndex);
				choices.Add(new(-1, workerStart, workerEnd, $"All functions in w{_selectedWorkerIndex:X2}"));
				int[] offsets = worker?.FunctionOffsets.Distinct().OrderBy(offset => offset).ToArray() ?? [];
				for (int index = 0; index < offsets.Length; index++)
				{
					int start = offsets[index];
					int end = index + 1 < offsets.Length ? offsets[index + 1] : workerEnd;
					int functionIndex = worker == null ? index : worker.FunctionOffsets.ToList().IndexOf(start);
					string functionName = worker?.FunctionName(functionIndex) ?? $"f{functionIndex:X2}";
					choices.Add(new(functionIndex, start, end, $"{functionName} — script offsets 0x{start:X4}–0x{end:X4}"));
				}
				AiFunctionOptions.IsEnabled = true;
			}
			AiFunctionOptions.ItemsSource = choices;
			AiFunctionOptions.SelectedIndex = 0;
			_selectedFunctionIndex = -1;
		}
		finally
		{
			_updatingFunctionScope = false;
		}
	}

	private void AiFunction_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_updatingFunctionScope || AiFunctionOptions.SelectedItem is not FunctionScopeChoice choice) return;
		FocusSelectionEditor();
		_selectedFunctionIndex = choice.Index;
		ApplyWorkerScope();
		ShowWorkerSelectionEditor();
	}

	private void ApplyWorkerScope()
	{
		if (DataModel.AiDocument == null) return;
		ClearJumpDestinationHighlight();
		ResetEditorSelectionForScopeChange();
		_synchronizingStatementSelection = true;
		_synchronizingInstructionSelection = true;
		try
		{
			AiStatementList.SelectedItem = null;
			AiInstructionList.SelectedItems?.Clear();
		}
		finally
		{
			_synchronizingStatementSelection = false;
			_synchronizingInstructionSelection = false;
		}

		if (_selectedWorkerIndex < 0)
		{
			AiStatementList.ItemsSource = DataModel.AiStatements;
			AiInstructionList.ItemsSource = DataModel.AiInstructions;
			AiStatusText.Text = "Showing all workers and the complete Battle Script.";
			return;
		}

		(int start, int end) = GetActiveScriptRange();
		AtelStatement[] statements = DataModel.AiDocument.Statements
			.Where(statement => statement.Offset >= start && statement.Offset < end)
			.ToArray();
		AtelInstruction[] instructions = DataModel.AiDocument.Instructions
			.Where(instruction => instruction.Offset >= start && instruction.Offset < end)
			.ToArray();
		AiStatementList.ItemsSource = statements;
		AiInstructionList.ItemsSource = instructions;
		int chunkStart = DataModel.AiDocument.ScriptCodeOffset + start;
		SelectAiHexRange(chunkStart, end - start);
		AiStatusText.Text = $"Showing Worker w{_selectedWorkerIndex:X2}: script offsets 0x{start:X4}–0x{end:X4}, Battle Script offsets 0x{chunkStart:X}–0x{DataModel.AiDocument.ScriptCodeOffset + end:X}; {statements.Length} statement(s), {instructions.Length} instruction(s).";
	}

	private void ResetEditorSelectionForScopeChange()
	{
		_aiHexSelectionVersion++;
		_logicSelectionOwner = AiLogicSelectionOwner.None;
		_selectedInstruction = null;
		_activeJumpInstruction = null;
		_semanticRole = null;
		_groupOperandEditors.Clear();
		AiSelectedInstructionText.Text = "";
		AiOperandText.Text = "";
		AiOperandText.IsEnabled = false;
		AiManualOperandEditor.IsVisible = false;
		AiMeaningLabel.IsVisible = false;
		AiMeaningOptions.IsVisible = false;
		AiInstructionJumpButton.IsVisible = false;
		AiFloatEditor.IsVisible = false;
		AiGroupEditorPanel.Children.Clear();
		AiGroupEditorPanel.IsVisible = false;
		AiGroupApplyButton.IsVisible = false;
		AiWorkerEditorPanel.IsVisible = false;
		int caret = Math.Clamp(AiHexText.CaretIndex, 0, AiHexText.Text?.Length ?? 0);
		AiHexText.SelectionStart = caret;
		AiHexText.SelectionEnd = caret;
		ClearJumpDestinationHighlight();
	}

	private void AiStatementVisibility_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		bool hidden = AiHideStatements.IsChecked == true;
		_hideStatementsPreference = hidden;
		SaveLogicVisibilityPreferences();
		AiStatementList.IsVisible = !hidden;
		AiStatementActions.IsVisible = !hidden;
		AiCopiedStatementText.IsVisible = false;
		if (hidden && _logicSelectionOwner == AiLogicSelectionOwner.Statement)
			ClearOwnedLogicSelection();
		UpdateLogicPanelLayout();
	}

	private void AiInstructionVisibility_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		bool hidden = AiHideInstructions.IsChecked == true;
		_hideInstructionsPreference = hidden;
		SaveLogicVisibilityPreferences();
		AiInstructionList.IsVisible = !hidden;
		if (hidden && _logicSelectionOwner == AiLogicSelectionOwner.Instruction)
			ClearOwnedLogicSelection();
		UpdateLogicPanelLayout();
	}

	private void AiUtilityTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (!ReferenceEquals(sender, AiUtilityTabs) || AiSelectionApplyActions is null) return;
		AiSelectionApplyActions.IsVisible = AiUtilityTabs.SelectedIndex == 0;
	}

	private void FocusSelectionEditor()
	{
		if (AiUtilityTabs != null && AiUtilityTabs.SelectedIndex != 0)
			AiUtilityTabs.SelectedIndex = 0;
	}

	private void FocusMessages()
	{
		if (AiUtilityTabs != null && AiUtilityTabs.SelectedIndex != 2)
			AiUtilityTabs.SelectedIndex = 2;
	}

	private void ShowMessageError(string message)
	{
		AiValidationResultText.Text = "ERROR";
		AiValidationResultText.Foreground = Brushes.Red;
		AiStatusText.Text = message;
		FocusMessages();
	}

	private void ShowWorkerSelectionEditor()
	{
		AiWorkerEditorPanel.IsVisible = true;
		AiEditorPanel.IsVisible = true;
		AiSelectedInstructionText.Text = _selectedWorkerIndex < 0
			? "All Workers • Complete Battle Script"
			: $"Worker w{_selectedWorkerIndex:X2}";
	}

	private void ApplyStoredLogicVisibility()
	{
		bool statementsHidden = AiHideStatements.IsChecked == true;
		bool instructionsHidden = AiHideInstructions.IsChecked == true;
		AiStatementList.IsVisible = !statementsHidden;
		AiStatementActions.IsVisible = !statementsHidden;
		AiCopiedStatementText.IsVisible = false;
		AiInstructionList.IsVisible = !instructionsHidden;
		UpdateLogicPanelLayout();
	}

	private static void LoadLogicVisibilityPreferences()
	{
		if (_logicPreferencesLoaded) return;
		_logicPreferencesLoaded = true;
		try
		{
			if (!File.Exists(LogicPreferencesPath)) return;
			AiEditorPreferences? preferences = JsonSerializer.Deserialize<AiEditorPreferences>(
				File.ReadAllText(LogicPreferencesPath));
			if (preferences == null) return;
			_hideStatementsPreference = preferences.HideGroupedLogic;
			_hideInstructionsPreference = preferences.HideDecodedInstructions;
			_suppressDeleteStatementWarning = preferences.SuppressDeleteStatementWarning;
		}
		catch
		{
			// Invalid or inaccessible preferences must never prevent the editor from opening.
		}
	}

	private static void SaveLogicVisibilityPreferences()
	{
		try
		{
			string? directory = Path.GetDirectoryName(LogicPreferencesPath);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			var preferences = new AiEditorPreferences(_hideStatementsPreference, _hideInstructionsPreference,
				_suppressDeleteStatementWarning);
			File.WriteAllText(LogicPreferencesPath, JsonSerializer.Serialize(preferences));
		}
		catch
		{
			// Preference persistence is optional and must not interrupt editing.
		}
	}

	private void UpdateLogicPanelLayout()
	{
		bool statementsHidden = AiHideStatements.IsChecked == true;
		bool instructionsHidden = AiHideInstructions.IsChecked == true;
		bool bothHidden = statementsHidden && instructionsHidden;
		AiStatementActions.IsVisible = !statementsHidden;
		AiCopiedStatementText.IsVisible = false;
		AiStatementPane.RowDefinitions[1].Height = statementsHidden ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
		AiInstructionPane.RowDefinitions[1].Height = instructionsHidden ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
		AiLogicViewsGrid.RowDefinitions[0].MinHeight = 0;
		AiLogicViewsGrid.RowDefinitions[2].MinHeight = 0;
		AiStatementPane.Margin = new Avalonia.Thickness(0);
		AiInstructionPane.Margin = new Avalonia.Thickness(0);

		if (!statementsHidden && !instructionsHidden)
		{
			Grid.SetRow(AiStatementPane, 0);
			Grid.SetRow(AiLogicSplitter, 1);
			Grid.SetRow(AiInstructionPane, 2);
			AiLogicSplitter.IsVisible = true;
			AiLogicViewsGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
			AiLogicViewsGrid.RowDefinitions[1].Height = new GridLength(6);
			AiLogicViewsGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
			AiLogicViewsGrid.RowDefinitions[0].MinHeight = 145;
			AiLogicViewsGrid.RowDefinitions[2].MinHeight = 145;
		}
		else if (statementsHidden && !instructionsHidden)
		{
			Grid.SetRow(AiStatementPane, 0);
			Grid.SetRow(AiInstructionPane, 2);
			AiInstructionPane.Margin = new Avalonia.Thickness(0, 3, 0, 0);
			AiLogicSplitter.IsVisible = false;
			AiLogicViewsGrid.RowDefinitions[0].Height = GridLength.Auto;
			AiLogicViewsGrid.RowDefinitions[1].Height = new GridLength(0);
			AiLogicViewsGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
			AiLogicViewsGrid.RowDefinitions[2].MinHeight = 145;
		}
		else if (!statementsHidden && instructionsHidden)
		{
			Grid.SetRow(AiInstructionPane, 0);
			Grid.SetRow(AiStatementPane, 2);
			AiStatementPane.Margin = new Avalonia.Thickness(0, 3, 0, 0);
			AiLogicSplitter.IsVisible = false;
			AiLogicViewsGrid.RowDefinitions[0].Height = GridLength.Auto;
			AiLogicViewsGrid.RowDefinitions[1].Height = new GridLength(0);
			AiLogicViewsGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
			AiLogicViewsGrid.RowDefinitions[2].MinHeight = 145;
		}
		else
		{
			Grid.SetRow(AiStatementPane, 0);
			Grid.SetRow(AiInstructionPane, 2);
			AiInstructionPane.Margin = new Avalonia.Thickness(0, 3, 0, 0);
			AiLogicSplitter.IsVisible = false;
			AiLogicViewsGrid.RowDefinitions[0].Height = GridLength.Auto;
			AiLogicViewsGrid.RowDefinitions[1].Height = new GridLength(0);
			AiLogicViewsGrid.RowDefinitions[2].Height = GridLength.Auto;
		}

		AiEditorPanel.IsVisible = !bothHidden || AiWorkerEditorPanel.IsVisible;
		if (bothHidden && _logicSelectionOwner != AiLogicSelectionOwner.None)
			ClearOwnedLogicSelection();
	}

	private void ClearOwnedLogicSelection()
	{
		_synchronizingStatementSelection = true;
		_synchronizingInstructionSelection = true;
		try
		{
			AiStatementList.SelectedItem = null;
			AiInstructionList.SelectedItems?.Clear();
		}
		finally
		{
			_synchronizingInstructionSelection = false;
			_synchronizingStatementSelection = false;
		}
		ResetEditorSelectionForScopeChange();
	}

	private (int Start, int End) GetActiveScriptRange()
	{
		if (_selectedFunctionIndex >= 0 && AiFunctionOptions.SelectedItem is FunctionScopeChoice function)
			return (function.Start, function.End);
		return GetWorkerScriptRange(_selectedWorkerIndex);
	}

	private bool WorkerOwnsScriptOffset(int workerIndex, int scriptOffset)
	{
		if (DataModel.AiDocument == null) return false;
		(int start, int end) = GetWorkerScriptRange(workerIndex);
		return scriptOffset >= start && scriptOffset < end;
	}

	private (int Start, int End) GetWorkerScriptRange(int workerIndex)
	{
		if (DataModel.AiDocument == null) return (0, 0);
		AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == workerIndex);
		if (worker == null || worker.FunctionOffsets.Count == 0) return (0, 0);
		int start = worker.FunctionOffsets.Min();
		int end = DataModel.AiDocument.Workers
			.Where(item => item.FunctionOffsets.Count > 0 && item.FunctionOffsets.Min() > start)
			.Select(item => item.FunctionOffsets.Min())
			.DefaultIfEmpty(DataModel.AiDocument.ScriptCodeLength)
			.Min();
		return (start, end);
	}

    private void Button_Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RunAiAction(DataModel.Save);

    private async void Button_ValidateAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
		FocusMessages();
        DataModel.RecordAiUndoCheckpoint("manual Battle Script validation or hex edit",
            AiStatementList.SelectedItem is AtelStatement ? "Group" : _selectedInstruction != null ? "Instruction" : null,
            AiStatementList.SelectedItem is AtelStatement validationStatement ? validationStatement.Offset : _selectedInstruction?.Offset);
        bool valid = RunAiAction(DataModel.ApplyAiHex, prefixError: false);
        AiValidationResultText.Text = valid ? "SUCCESS" : "ERROR";
        AiValidationResultText.Foreground = valid ? Brushes.LimeGreen : Brushes.Red;
        if (!valid && _lastAiActionException is ManualAiPartialStatementException issue &&
            TopLevel.GetTopLevel(this) is Window owner)
        {
            SelectDirtyAiHexRange(issue.StatementChunkOffset, issue.RemainingByteLength);
            PartialStatementRepairChoice choice = await AiPartialStatementRepairWindow.Show(owner, issue);
            if (choice == PartialStatementRepairChoice.RestoreStatement)
            {
                RunAiAction(DataModel.RestoreUnvalidatedAiHex);
                AiValidationResultText.Text = "RESTORED";
                AiValidationResultText.Foreground = Brushes.LimeGreen;
            }
            else if (choice == PartialStatementRepairChoice.DeleteStatement)
            {
                DataModel.RecordAiUndoCheckpoint("delete invalid Battle Logic statement during repair");
                bool deleted = RunAiAction(() => DataModel.DeleteStatement(issue.StatementOffset));
                AiValidationResultText.Text = deleted ? "SUCCESS" : "ERROR";
                AiValidationResultText.Foreground = deleted ? Brushes.LimeGreen : Brushes.Red;
            }
            else
            {
                AiStatusText.Text = issue.Message + " The manual edit remains unchanged.";
            }
        }
		// Header buttons participate in tab selection after Click is raised. Queue this
		// final focus change so validation always ends on Messages, even when validation
		// rebuilds the decoded views or opens the partial-statement repair dialog.
		Dispatcher.UIThread.Post(FocusMessages, DispatcherPriority.Loaded);
    }

    private void AiHexText_TextChanged(object? sender, TextChangedEventArgs e)
    {
		ClearJumpDestinationHighlight();
        string validatedHex = DataModel.AiDocument?.ToHexEditorText() ?? "";
        bool dirty = !string.Equals(AiHexText.Text ?? "", validatedHex, StringComparison.Ordinal);
        if (!dirty)
        {
            bool wasDirty = _aiHexIsDirty;
            _aiHexIsDirty = false;
            if (wasDirty)
            {
                AiStatementList.IsEnabled = true;
                AiInstructionList.IsEnabled = true;
            }
            return;
        }
        if (!_restoringAiHistory)
            DataModel.ClearAiRedoHistory();
        if (_aiHexIsDirty) return;

        _aiHexIsDirty = true;
        ClearValidationResult();
        _synchronizingStatementSelection = true;
        _synchronizingInstructionSelection = true;
        try
        {
            AiStatementList.SelectedItem = null;
            AiInstructionList.SelectedItems?.Clear();
        }
        finally
        {
            _synchronizingStatementSelection = false;
            _synchronizingInstructionSelection = false;
        }
        AiStatementList.IsEnabled = false;
        AiInstructionList.IsEnabled = false;
        _selectedInstruction = null;
        AiSelectedInstructionText.Text = "";
        AiOperandText.Text = "";
        AiOperandText.IsEnabled = false;
		AiManualOperandEditor.IsVisible = false;
        AiMeaningLabel.IsVisible = false;
        AiMeaningOptions.IsVisible = false;
        AiFloatEditor.IsVisible = false;
		AiGroupEditorPanel.Children.Clear();
		AiGroupEditorPanel.IsVisible = false;
		AiGroupApplyButton.IsVisible = false;
		AiWorkerEditorPanel.IsVisible = false;
		AiInstructionJumpButton.IsVisible = false;
		_activeJumpInstruction = null;
        AiStatusText.Text = "The Battle Script hex has unvalidated manual changes. Check Data Validity to rebuild Battle Logic and Script Instructions.";
    }

	private void AiHexText_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
	{
		FocusSelectionEditor();
		AiWorkerEditorPanel.IsVisible = false;
		AiEditorPanel.IsVisible = !(AiHideStatements.IsChecked == true && AiHideInstructions.IsChecked == true);
		ClearJumpDestinationHighlight();
		_activeJumpInstruction = null;
		AiStatusText.Text = "";
		AiSelectedInstructionText.Text = "";
	}

    private async void Button_RestoreOriginalAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        bool confirmed = await AiRevertConfirmationWindow.Show(owner, "Revert Battle Script",
            "This will discard all Battle Script changes made since this monster was opened and immediately save the restored Battle Script to the monster file. Stats, loot, text, audio, and other monster data will not be changed.",
            DataModel.MonsterPath + " (Battle Script captured when opened)", "Revert and Save",
            "Confirming will immediately write the restored Battle Script to the monster file.");
        if (!confirmed) { AiStatusText.Text = "Revert was cancelled."; return; }
        try
        {
            DataModel.RecordAiUndoCheckpoint("Revert");
            DataModel.RestoreOriginalAiAndSave();
            RefreshAfterRevert(false);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void Button_UndoAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        try
        {
            DataModel.UndoLastAiChange();
            RefreshAfterRevert(false);
            RestoreUndoSelection();
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex) { AiStatusText.Text = "ERROR: " + ex.Message; }
    }

    private void Button_RedoAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        try
        {
            _restoringAiHistory = true;
            DataModel.RedoLastAiChange();
            RefreshAfterRevert(false);
        }
        catch (Exception ex) { AiStatusText.Text = "ERROR: " + ex.Message; return; }
        finally { _restoringAiHistory = false; }

        RestoreUndoSelection();
        AiStatusText.Text = DataModel.AiStatus;
    }

    private void RestoreUndoSelection()
    {
        if (DataModel.AiDocument == null || !DataModel.LastUndoneScriptOffset.HasValue) return;
        int offset = DataModel.LastUndoneScriptOffset.Value;

        if (DataModel.LastUndoneSelectionKind == "Group")
        {
            AtelStatement? statement = DataModel.AiDocument.Statements.FirstOrDefault(item => item.Offset == offset)
                ?? DataModel.AiDocument.Statements.LastOrDefault(item => item.Offset <= offset);
            if (statement == null) return;
            AiStatementList.SelectedItem = statement;
            AiStatementList.ScrollIntoView(statement);
            ActivateStatementEditor(statement);
            SelectAiHexRange(DataModel.AiDocument.ScriptCodeOffset + statement.Offset, statement.ByteLength);
            return;
        }

        if (DataModel.LastUndoneSelectionKind == "Instruction")
        {
            AtelInstruction? instruction = DataModel.AiDocument.Instructions.FirstOrDefault(item => item.Offset == offset)
                ?? DataModel.AiDocument.Instructions.LastOrDefault(item => item.Offset <= offset);
            if (instruction == null) return;
            AiInstructionList.SelectedItem = instruction;
            AiInstructionList.ScrollIntoView(instruction);
            ActivateInstructionEditor(instruction);
            SelectAiHexRange(DataModel.AiDocument.ScriptCodeOffset + instruction.Offset, instruction.Bytes.Length);
        }
    }

    private async void Button_RevertVanillaMonster(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (!VanillaReference_Service.TryValidate(VanillaReference_Service.MasterPath, out _))
        {
            IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select your clean, unedited FFX Original Game Files folder", AllowMultiple = false
            });
            if (folders.Count == 0) { AiStatusText.Text = "Restore Original was cancelled."; return; }
            string? selectedPath = folders[0].TryGetLocalPath();
            try
            {
                if (string.IsNullOrWhiteSpace(selectedPath)) throw new InvalidOperationException("No local folder was selected.");
                VanillaReference_Service.Configure(selectedPath);
            }
            catch (Exception ex)
            {
                AiStatusText.Text = "ERROR: " + ex.Message;
                return;
            }
        }

        string? vanillaPath = VanillaReference_Service.ResolveProjectFile(DataModel.MonsterPath);
        if (vanillaPath == null)
        {
            AiStatusText.Text = "ERROR: The configured Original Game Files folder does not contain the matching monster file. " +
                "Use Recovery > Select Original Game Files to choose another clean folder.";
            return;
        }

        const string explanation = "This will immediately replace the current monster file with its original, unedited game file.\n\n" +
            "This includes the Battle Script and combat behavior, stats and attributes, elemental weaknesses/resistances/immunities/absorption, status resistances, AP and rewards, item drops and steals, commands and abilities, text, audio, and every other monster section.\n\n" +
            "All current modifications to this monster will be discarded and the restored monster will be written to disk.";
        bool confirmed = await AiRevertConfirmationWindow.Show(owner, "Restore Original Monster",
            explanation, vanillaPath, "Restore and Save",
            "Confirming will immediately write the original monster to the current project file.");
        if (!confirmed) { AiStatusText.Text = "Restore Original was cancelled."; return; }
        try
        {
            DataModel.RestoreOriginalMonsterAndSave(vanillaPath);
            RefreshAfterRevert(true);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex) { AiStatusText.Text = "ERROR: " + ex.Message; }
    }

    private void RefreshAfterRevert(bool refreshWholeMonster)
    {
        if (refreshWholeMonster)
        {
            DataContext = null;
            DataContext = DataModel;
        }
        _copiedStatementBytes = null;
        _copiedStatementOffset = -1;
        _copiedStatementFallsThrough = false;
        AiCopiedStatementText.Text = "";
        _selectedInstruction = null;
        AiHexText.Text = DataModel.AiHex;
        AiSummaryText.Text = DataModel.AiSummary;
        AiInstructionList.SelectedItems?.Clear();
        AiStatementList.SelectedItem = null;
        AiInstructionList.ItemsSource = null;
        AiInstructionList.ItemsSource = DataModel.AiInstructions;
        AiStatementList.ItemsSource = null;
        AiStatementList.ItemsSource = DataModel.AiStatements;
        AiSelectedInstructionText.Text = "";
        AiOperandText.Text = "";
        AiOperandText.IsEnabled = false;
		AiManualOperandEditor.IsVisible = false;
        AiMeaningLabel.IsVisible = false;
        AiMeaningOptions.IsVisible = false;
        AiFloatEditor.IsVisible = false;
        AiGroupEditorPanel.IsVisible = false;
        AiGroupApplyButton.IsVisible = false;
    }

    private void AiInstruction_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingInstructionSelection) return;
		AtelInstruction? instruction = e.AddedItems.OfType<AtelInstruction>().LastOrDefault()
			?? AiInstructionList.SelectedItems?.OfType<AtelInstruction>().LastOrDefault();
		if (instruction != null) ActivateInstructionEditor(instruction);
	}

	private void AiInstruction_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
	{
		AtelInstruction? instruction = (e.Source as Control)?.GetVisualAncestors()
			.OfType<ListBoxItem>().FirstOrDefault()?.DataContext as AtelInstruction;
		if (instruction == null) return;
		_synchronizingInstructionSelection = true;
		try
		{
			AiInstructionList.SelectedItems?.Clear();
			AiInstructionList.SelectedItems?.Add(instruction);
		}
		finally
		{
			_synchronizingInstructionSelection = false;
		}
		ActivateInstructionEditor(instruction);
	}

	private void ActivateInstructionEditor(AtelInstruction instruction)
	{
		FocusSelectionEditor();
		AiWorkerEditorPanel.IsVisible = false;
		_logicSelectionOwner = AiLogicSelectionOwner.Instruction;
		AiManualOperandEditor.IsVisible = instruction.HasOperand;
        ClearValidationResult();
        AiGroupEditorPanel.IsVisible = false;
        AiGroupApplyButton.IsVisible = false;
		_selectedInstruction = instruction;
        SelectStatementForInstruction(_selectedInstruction);
		SetInstructionSelectionSummary(_selectedInstruction);
        AiOperandText.Text = _selectedInstruction.HasOperand ? $"0x{_selectedInstruction.Operand:X4}" : "";
        AiOperandText.IsEnabled = _selectedInstruction.HasOperand;
        UpdateMeaningEditor(_selectedInstruction);
		_activeJumpInstruction = IsJumpInstruction(_selectedInstruction) ? _selectedInstruction : null;
		AiInstructionJumpButton.IsVisible = _activeJumpInstruction != null;
        if (DataModel.AiDocument != null)
        {
            int chunkOffset = DataModel.AiDocument.ScriptCodeOffset + _selectedInstruction.Offset;
            SelectAiHexRange(chunkOffset, _selectedInstruction.Bytes.Length);
			UpdateJumpDestinationHighlight([_selectedInstruction]);
            AiStatusText.Text = $"Selected script offset 0x{_selectedInstruction.Offset:X4}; highlighted Battle Script offset 0x{chunkOffset:X}.";
        }
    }

	private void SetInstructionSelectionSummary(AtelInstruction instruction)
	{
		int chunkOffset = (DataModel.AiDocument?.ScriptCodeOffset ?? 0) + instruction.Offset;
		AiSelectedInstructionText.Text =
			$"Instruction • Script 0x{instruction.Offset:X4} • Battle Script 0x{chunkOffset:X4} • {instruction.Bytes.Length} byte(s) • {instruction.OpcodeName}";
	}

	private void SetStatementSelectionSummary(AtelStatement statement)
	{
		int chunkOffset = (DataModel.AiDocument?.ScriptCodeOffset ?? 0) + statement.Offset;
		AiSelectedInstructionText.Text =
			$"Group • Script 0x{statement.Offset:X4} • Battle Script 0x{chunkOffset:X4} • {statement.ByteLength} bytes • {statement.Instructions.Count} instructions";
	}

    private void AiStatement_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingStatementSelection || AiStatementList.SelectedItem is not AtelStatement statement ||
            DataModel.AiDocument == null || AiInstructionList.SelectedItems == null) return;
        ActivateStatementEditor(statement);
    }

    private void AiStatement_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (AiStatementList.SelectedItem is AtelStatement statement)
            ActivateStatementEditor(statement);
    }

    private void Button_CopyStatement(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement statement)
        {
            AiStatusText.Text = "Select a Battle Logic statement to copy.";
            return;
        }
        if (statement.Instructions.Any(instruction => instruction.Opcode is 0xB0 or 0xB1 or 0xB2))
        {
            AiStatusText.Text = "This statement contains an unconditional or alternate jump. For safety, only conditional check jumps can be edited here.";
            return;
        }
        AtelInstruction[] conditionalJumps = statement.Instructions
            .Where(instruction => instruction.Opcode is 0xD5 or 0xD6 or 0xD7).ToArray();
        int workerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(statement.Offset);
        AtelWorker worker = DataModel.AiDocument.Workers.First(item => item.Index == workerIndex);
        if (conditionalJumps.Any(instruction => instruction.Operand >= worker.JumpCount))
        {
            AiStatusText.Text = "This conditional statement refers to a jump index outside its worker table and was not copied.";
            return;
        }
        _copiedStatementBytes = DataModel.AiDocument.GetStatementBytes(statement.Offset);
        _copiedStatementOffset = statement.Offset;
        _copiedStatementFallsThrough = statement.Instructions[^1].Opcode is not (0x34 or 0x3C or 0x40 or 0x54 or 0xB0);
        _copiedStatementHasConditionalJump = conditionalJumps.Length > 0;
        _copiedStatementWorkerIndex = workerIndex;
        AiCopiedStatementText.Text = $"Copied 0x{statement.Offset:X4} ({statement.ByteLength} bytes, w{workerIndex:X2})";
        AiStatusText.Text = _copiedStatementHasConditionalJump
            ? $"Copied conditional statement at script offset 0x{statement.Offset:X4} from worker w{workerIndex:X2}; its existing jump destination will be preserved."
            : $"Copied complete statement at script offset 0x{statement.Offset:X4} ({statement.ByteLength} bytes).";
    }

    private void Button_InsertStatementAfter(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (_copiedStatementBytes == null || _copiedStatementOffset < 0)
        {
            AiStatusText.Text = "Copy a Battle Logic statement first.";
            return;
        }
        if (!_copiedStatementFallsThrough)
        {
            AiStatusText.Text = "The copied statement ends or redirects control flow and cannot be inserted safely.";
            return;
        }
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement destination)
        {
            AiStatusText.Text = "Select the Battle Logic statement after which the copy should be inserted.";
            return;
        }
        if (!CanUseCopiedStatementAt(destination)) return;
        if (destination.Instructions[^1].Opcode is 0x34 or 0x3C or 0x40 or 0x54 or 0xB0)
        {
            AiStatusText.Text = "Cannot insert after a return, halt, or unconditional jump because the new statement would be unreachable.";
            return;
        }
        try
        {
            int insertionOffset = destination.Offset + destination.ByteLength;
            DataModel.RecordAiUndoCheckpoint("insert Battle Logic statement after", "Group", insertionOffset);
            AtelStatement inserted = DataModel.InsertStatement(insertionOffset, _copiedStatementBytes, _copiedStatementOffset);
            AiHexText.Text = DataModel.AiHex;
            AiSummaryText.Text = DataModel.AiSummary;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = DataModel.AiStatements;
			RefreshNavigationAfterDocumentChange();
            AiStatementList.SelectedItem = inserted;
            AiStatementList.ScrollIntoView(inserted);
            ActivateStatementEditor(inserted);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void Button_InsertStatementBefore(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (_copiedStatementBytes == null || _copiedStatementOffset < 0)
        {
            AiStatusText.Text = "Copy a Battle Logic statement first.";
            return;
        }
        if (!_copiedStatementFallsThrough)
        {
            AiStatusText.Text = "The copied statement ends or redirects control flow and cannot be inserted safely.";
            return;
        }
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement destination)
        {
            AiStatusText.Text = "Select the Battle Logic statement before which the copy should be inserted.";
            return;
        }
        if (!CanUseCopiedStatementAt(destination)) return;
        try
        {
            int insertionOffset = destination.Offset;
            DataModel.RecordAiUndoCheckpoint("insert Battle Logic statement before", "Group", insertionOffset);
            AtelStatement inserted = DataModel.InsertStatement(insertionOffset, _copiedStatementBytes, _copiedStatementOffset);
            AiHexText.Text = DataModel.AiHex;
            AiSummaryText.Text = DataModel.AiSummary;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = DataModel.AiStatements;
			RefreshNavigationAfterDocumentChange();
            AiStatementList.SelectedItem = inserted;
            AiStatementList.ScrollIntoView(inserted);
            ActivateStatementEditor(inserted);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void Button_PasteStatement(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (_copiedStatementBytes == null || _copiedStatementOffset < 0)
        {
			ShowMessageError("Paste requires a copied Battle Logic statement. Select a statement and click Copy first.");
            return;
        }
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement destination)
        {
			ShowMessageError("Paste requires a destination. Select the Battle Logic statement you want to replace.");
            return;
        }
        if (!CanUseCopiedStatementAt(destination)) return;
        try
        {
            int destinationOffset = destination.Offset;
            DataModel.RecordAiUndoCheckpoint("paste over Battle Logic statement", "Group", destinationOffset);
            AtelStatement edited = DataModel.ApplyStatementReplacement(destinationOffset, _copiedStatementBytes, _copiedStatementOffset);
            AiHexText.Text = DataModel.AiHex;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = DataModel.AiStatements;
			RefreshNavigationAfterDocumentChange();
            AiStatementList.SelectedItem = edited;
            AiStatementList.ScrollIntoView(edited);
            ActivateStatementEditor(edited);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
			ShowMessageError(ex.Message);
        }
    }

    private bool CanUseCopiedStatementAt(AtelStatement destination)
    {
        if (!_copiedStatementHasConditionalJump || DataModel.AiDocument == null) return true;
        int destinationWorker = DataModel.AiDocument.GetWorkerIndexForCodeOffset(destination.Offset);
        if (destinationWorker == _copiedStatementWorkerIndex) return true;
		ShowMessageError($"This copied conditional check belongs to worker w{_copiedStatementWorkerIndex:X2}. " +
			$"The selected destination is in worker w{destinationWorker:X2}; cross-worker jump remapping is not enabled yet.");
        return false;
    }

    private async void Button_DeleteStatement(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement statement)
        {
            AiStatusText.Text = "Select a Battle Logic statement to delete.";
			FocusMessages();
            return;
        }
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        bool confirmed = _suppressDeleteStatementWarning;
        if (!confirmed)
        {
            string description = $"Statement at script offset 0x{statement.Offset:X4} ({statement.ByteLength} bytes)\n{statement.Display}";
            DeleteStatementConfirmationResult result = await AiDeleteStatementConfirmationWindow.Show(owner,
                "This will remove the complete selected statement and rebuild every later function and jump-table offset. " +
                "Deleting a conditional check also removes its branch, so execution will continue directly into the next statement. " +
                "Statements that terminate a function, use unsupported jump forms, or serve as an entry point or jump destination are protected and will be refused.\n\n" +
                "Use Undo if you want to reverse this deletion.", description);
            confirmed = result.Confirmed;
            if (confirmed && result.DoNotShowAgain)
            {
                _suppressDeleteStatementWarning = true;
                SaveLogicVisibilityPreferences();
            }
        }
        if (!confirmed) { AiStatusText.Text = "Statement deletion was cancelled."; return; }

        try
        {
            int deletedOffset = statement.Offset;
            DataModel.RecordAiUndoCheckpoint("delete Battle Logic statement", "Group", deletedOffset);
            DataModel.DeleteStatement(deletedOffset);
            AiHexText.Text = DataModel.AiHex;
            AiSummaryText.Text = DataModel.AiSummary;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = DataModel.AiStatements;
			RefreshNavigationAfterDocumentChange();
            AtelStatement? next = DataModel.AiDocument.Statements.FirstOrDefault(item => item.Offset >= deletedOffset)
                ?? DataModel.AiDocument.Statements.LastOrDefault();
            if (next != null)
            {
                AiStatementList.SelectedItem = next;
                AiStatementList.ScrollIntoView(next);
                ActivateStatementEditor(next);
            }
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
			FocusMessages();
			AiValidationResultText.Text = "ERROR";
			AiValidationResultText.Foreground = Brushes.Red;
            AiStatusText.Text = ex.Message;
        }
    }

    private void ActivateStatementEditor(AtelStatement statement)
    {
        if (DataModel.AiDocument == null || AiInstructionList.SelectedItems == null) return;
		FocusSelectionEditor();
		AiWorkerEditorPanel.IsVisible = false;
		_logicSelectionOwner = AiLogicSelectionOwner.Statement;
		AiManualOperandEditor.IsVisible = false;
        ClearValidationResult();

        _synchronizingInstructionSelection = true;
        try
        {
            AiInstructionList.SelectedItems.Clear();
            foreach (AtelInstruction instruction in statement.Instructions)
                AiInstructionList.SelectedItems.Add(instruction);
            AiInstructionList.ScrollIntoView(statement.Instructions[0]);
            _selectedInstruction = null;
			SetStatementSelectionSummary(statement);
            AiOperandText.Text = "";
            AiOperandText.IsEnabled = false;
            AiMeaningLabel.IsVisible = false;
            AiMeaningOptions.IsVisible = false;
            AiFloatEditor.IsVisible = false;
            BuildGroupEditors(statement);
        }
        finally
        {
            _synchronizingInstructionSelection = false;
        }

        int chunkOffset = DataModel.AiDocument.ScriptCodeOffset + statement.Offset;
        SelectAiHexRange(chunkOffset, statement.ByteLength);
		UpdateJumpDestinationHighlight(statement.Instructions);
        AiStatusText.Text = $"Selected Battle Logic statement at script offset 0x{statement.Offset:X4}; highlighted {statement.ByteLength} byte(s) at Battle Script offset 0x{chunkOffset:X}.";
    }

    private void BuildGroupEditors(AtelStatement statement)
    {
        AiGroupEditorPanel.Children.Clear();
        _groupOperandEditors.Clear();
		_activeJumpInstruction = statement.Instructions.LastOrDefault(IsJumpInstruction);
		AiInstructionJumpButton.IsVisible = false;
        if (TryBuildScaleGroupEditor(statement))
        {
            AiGroupEditorPanel.IsVisible = true;
            AiGroupApplyButton.IsVisible = true;
            return;
        }
		var fieldRow = new WrapPanel
		{
			Orientation = Avalonia.Layout.Orientation.Horizontal,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
		};
        foreach (AtelInstruction instruction in statement.Instructions)
        {
			(string? role, OperandChoice[] choices) = GetSemanticChoices(instruction);
			if (role == null) continue;
			bool isJumpField = role == "Jump" && IsJumpInstruction(instruction);
			var field = new StackPanel
			{
				Orientation = Avalonia.Layout.Orientation.Vertical,
				Width = 190,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
				Margin = new Avalonia.Thickness(4, 0)
			};
			var label = new TextBlock
			{
				Text = role + ":",
				VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
				Margin = new Avalonia.Thickness(0, 0, 0, 3)
			};
            ComboBox? options = null;
            TextBox? valueText = null;
            if (choices.Length > 0)
            {
				options = new ComboBox
				{
					ItemsSource = choices,
					Width = 190,
					HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
				};
                options.SelectedItem = role == "Comparison"
                    ? choices.FirstOrDefault(choice => choice.Opcode == instruction.Opcode)
                    : choices.FirstOrDefault(choice => choice.Value == instruction.Operand && choice.Opcode == instruction.Opcode);
            }
            else
            {
				valueText = new TextBox
				{
					Text = $"0x{instruction.Operand:X4}", Width = 190,
					HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
					FontFamily = new FontFamily("Consolas")
				};
			}
			field.Children.Add(label);
			if (options != null) field.Children.Add(options);
			if (valueText != null) field.Children.Add(valueText);
			fieldRow.Children.Add(field);
			if (isJumpField)
			{
				var jumpButton = new Button
				{
					Content = "Go to Jump",
					Width = 105,
					Margin = new Avalonia.Thickness(4, 20, 4, 0),
					HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
				};
				jumpButton.Click += Button_JumpToDestination;
				fieldRow.Children.Add(jumpButton);
			}
            _groupOperandEditors.Add(new GroupOperandEditor(instruction.Offset, role, options, valueText));
        }
        if (_groupOperandEditors.Count == 0)
        {
            AiGroupEditorPanel.IsVisible = false;
            AiGroupApplyButton.IsVisible = false;
            return;
        }
		AiGroupEditorPanel.Children.Add(fieldRow);
        AiGroupEditorPanel.IsVisible = true;
        AiGroupApplyButton.IsVisible = true;
    }

    private bool TryBuildScaleGroupEditor(AtelStatement statement)
    {
        if (DataModel.AiDocument == null || statement.Instructions.Count != 4 ||
            statement.Instructions[^1].Opcode is not (0xB5 or 0xD8) || statement.Instructions[^1].Operand != 0x7028 ||
            statement.Instructions.Take(3).Any(instruction => instruction.Opcode != 0xAF))
            return false;

        string[] axes = ["X", "Y", "Z"];
		var row = new WrapPanel
		{
			Orientation = Avalonia.Layout.Orientation.Horizontal,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
		};
        AiGroupEditorPanel.Children.Add(row);
        foreach (IGrouping<ushort, (AtelInstruction Instruction, string Axis)> linked in statement.Instructions.Take(3)
                     .Select((instruction, index) => (Instruction: instruction, Axis: axes[index]))
                     .GroupBy(item => item.Instruction.Operand))
        {
            if (!DataModel.AiDocument.TryGetFloatConstant(linked.Key, out float value)) continue;
            string axisLabel = "Scale " + string.Join("/", linked.Select(item => item.Axis));
			var field = new StackPanel
			{
				Orientation = Avalonia.Layout.Orientation.Vertical,
				Width = 190,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
				Margin = new Avalonia.Thickness(4, 0)
			};
            var label = new TextBlock
            {
				Text = axisLabel + ":", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Avalonia.Thickness(0, 0, 0, 3)
            };
            var valueText = new TextBox
            {
				Text = value.ToString("0.0#####", CultureInfo.InvariantCulture), Width = 190,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                FontFamily = new FontFamily("Consolas")
            };
			field.Children.Add(label);
            field.Children.Add(valueText);
            row.Children.Add(field);
            AtelInstruction first = linked.First().Instruction;
            _groupOperandEditors.Add(new GroupOperandEditor(first.Offset, axisLabel, null, valueText, linked.Key));
        }
        if (_groupOperandEditors.Count == 1 && statement.Instructions.Take(3).Select(i => i.Operand).Distinct().Count() == 1)
        {
            var warning = new TextBlock
            {
                Text = "Shared value: changing it changes X, Y, and Z together.", Foreground = Brushes.Orange,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            AiGroupEditorPanel.Children.Add(warning);
        }
        return _groupOperandEditors.Count > 0;
    }

    private void Button_ApplyGroupChanges(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement selectedStatement) return;
        try
        {
            int statementOffset = selectedStatement.Offset;
            DataModel.RecordAiUndoCheckpoint("apply Battle Logic changes", "Group", statementOffset);
            var replacements = new List<AtelInstructionReplacement>();
            foreach (GroupOperandEditor editor in _groupOperandEditors)
            {
                AtelInstruction current = DataModel.AiDocument.Instructions.First(instruction => instruction.Offset == editor.InstructionOffset);
                if (editor.FloatIndex.HasValue && editor.ValueText != null)
                {
                    DataModel.ApplyFloatConstant(editor.InstructionOffset, editor.FloatIndex.Value, editor.ValueText.Text ?? "");
                    continue;
                }
                if (editor.Options?.SelectedItem is OperandChoice choice)
                {
                    if (editor.Role == "Comparison")
                        replacements.Add(new AtelInstructionReplacement(editor.InstructionOffset, choice.Opcode, 0));
                    else
                        replacements.Add(new AtelInstructionReplacement(editor.InstructionOffset, choice.Opcode, choice.Value));
                }
                else if (editor.ValueText != null)
                    replacements.Add(new AtelInstructionReplacement(editor.InstructionOffset, current.Opcode,
                        MonEditor_DataModel.ParseOperandText(editor.ValueText.Text ?? "")));
                else
                    continue;
            }
            if (replacements.Count > 0)
                DataModel.ApplyGroupedInstructions(replacements, statementOffset);
            AiHexText.Text = DataModel.AiHex;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = DataModel.AiStatements;
            AtelStatement? restored = DataModel.AiDocument.Statements.FirstOrDefault(item => item.Offset == statementOffset);
            if (restored != null)
            {
                AiStatementList.SelectedItem = restored;
                AiStatementList.ScrollIntoView(restored);
                BuildGroupEditors(restored);
            }
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void SelectStatementForInstruction(AtelInstruction instruction)
    {
        if (DataModel.AiDocument == null) return;
        AtelStatement? statement = DataModel.AiDocument.Statements.FirstOrDefault(s =>
            instruction.Offset >= s.Offset && instruction.Offset < s.Offset + s.ByteLength);
        if (statement == null) return;
        _synchronizingStatementSelection = true;
        try
        {
            AiStatementList.SelectedItem = statement;
            AiStatementList.ScrollIntoView(statement);
        }
        finally
        {
            _synchronizingStatementSelection = false;
        }
    }

    private void UpdateMeaningEditor(AtelInstruction instruction)
    {
        _updatingMeaningOptions = true;
        (string? role, OperandChoice[] choices) = GetSemanticChoices(instruction);
        _semanticRole = role;
        bool hasChoices = role != null && choices.Length > 0;
        AiMeaningOptions.SelectedItem = null;
        AiMeaningOptions.ItemsSource = null;
        AiMeaningLabel.Text = role == null ? "Value:" : role + ":";
        AiMeaningLabel.IsVisible = hasChoices;
        AiMeaningOptions.IsVisible = hasChoices;
        AiMeaningOptions.ItemsSource = hasChoices ? choices : null;
        AiMeaningOptions.SelectedItem = hasChoices
            ? (_semanticRole == "Comparison"
                ? choices.FirstOrDefault(x => x.Opcode == instruction.Opcode)
                : choices.FirstOrDefault(x => x.Value == instruction.Operand && x.Opcode == instruction.Opcode))
            : null;
        _updatingMeaningOptions = false;
        UpdateFloatEditor(instruction);
    }

    private void UpdateFloatEditor(AtelInstruction instruction)
    {
        float value = 0;
        bool isFloatReference = instruction.Opcode == 0xAF && DataModel.AiDocument != null &&
            DataModel.AiDocument.TryGetFloatConstant(instruction.Operand, out value);
        AiFloatEditor.IsVisible = isFloatReference;
        if (!isFloatReference) return;
        int references = DataModel.AiDocument!.GetFloatReferenceCount(instruction.Operand);
        string parameter = GetFloatParameterName(instruction);
        AiFloatInfoText.Text = $"{parameter} - shared float 0x{instruction.Operand:X4} ({references} reference{(references == 1 ? "" : "s")}):";
        AiFloatValueText.Text = value.ToString("0.0#####", CultureInfo.InvariantCulture);
        AiFloatWarningText.Text = references > 1
            ? $"Shared value: changing it changes all {references} linked parameters."
            : "Only this parameter uses this value.";
    }

    private string GetFloatParameterName(AtelInstruction instruction)
    {
        if (DataModel.AiDocument == null) return "Float parameter";
        AtelInstruction[] all = DataModel.AiDocument.Instructions.ToArray();
        int selected = Array.IndexOf(all, instruction);
        if (selected < 0) return "Float parameter";
        for (int callIndex = selected + 1; callIndex < all.Length && callIndex <= selected + 3; callIndex++)
        {
            AtelInstruction call = all[callIndex];
            if (call.Opcode is not (0xB5 or 0xD8)) continue;
            if (call.Operand == 0x7028)
            {
                int argument = selected - (callIndex - 3);
                return argument switch { 0 => "Scale X", 1 => "Scale Y", 2 => "Scale Z", _ => "Float parameter" };
            }
            break;
        }
        return "Float parameter";
    }

    private (string? Role, OperandChoice[] Choices) GetSemanticChoices(AtelInstruction instruction)
    {
        if (DataModel.AiDocument == null) return (null, []);
        if (instruction.Opcode is >= 0x06 and <= 0x0F)
            return ("Comparison", [new("Equal ==", 0x06, 0x06), new("Not equal !=", 0x07, 0x07),
                new("Greater than >", 0x08, 0x08), new("Less than <", 0x09, 0x09),
                new("Greater than >", 0x0A, 0x0A), new("Less than <", 0x0B, 0x0B),
                new("Greater/equal >=", 0x0C, 0x0C), new("Less/equal <=", 0x0D, 0x0D),
                new("Greater/equal >=", 0x0E, 0x0E), new("Less/equal <=", 0x0F, 0x0F)]);
        if (instruction.Opcode is 0xB0 or 0xB1 or 0xB2 or 0xD5 or 0xD6 or 0xD7)
        {
			int workerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(instruction.Offset);
			AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == workerIndex);
			if (worker == null) return ("Jump", []);
			return ("Jump", worker.JumpOffsets.Select((scriptOffset, index) =>
			{
				int chunkOffset = DataModel.AiDocument.ScriptCodeOffset + scriptOffset;
				string display = $"j{index:X2} [0x{index:X4}] -> offset 0x{chunkOffset:X6}";
				return new OperandChoice($"j{index:X2}", (ushort)index, instruction.Opcode, display);
			}).ToArray());
        }
        if (instruction.Opcode is not (0x9F or 0xA0 or 0xA1 or 0xAE or 0xAF)) return (null, []);
        AtelInstruction[] all = DataModel.AiDocument.Instructions.ToArray();
        int selected = Array.IndexOf(all, instruction);
        if (selected < 0) return (null, []);
        if (instruction.Opcode == 0xAE && selected > 0 && selected + 1 < all.Length &&
            all[selected - 1].Opcode == 0xB5 && all[selected - 1].Operand == 0x00A9 && all[selected + 1].Opcode == 0x18)
            return ("Random range", Enumerable.Range(1, 256)
                .Select(i => new OperandChoice($"0 to {i - 1}", (ushort)i, 0xAE)).ToArray());

        for (int callIndex = selected + 1; callIndex < all.Length && callIndex <= selected + 12; callIndex++)
        {
            AtelInstruction call = all[callIndex];
            if (call.Opcode is not (0xB5 or 0xD8)) continue;
            string[]? roles = call.Operand switch
            {
                0x700B => ["Target", "Command"],
                0x700F => ["Character", "Stat property"],
                0x7010 => ["Group", "Stat property", "Unused", "Selector"],
                0x7018 => ["Character", "Stat property", "Value"],
                0x701A => ["Command source", "Command property"],
                0x701E => ["Group", "Character"],
                0x7026 => ["Weak state"],
                0x7034 => ["Battle result"],
                0x7037 => ["Character", "Command"],
                0x7038 => ["Character", "Command"],
                0x703B => ["Character", "Command", "Disabled"],
                0x705A => ["Target", "Command"],
                0x706B => ["Character", "Model part", "Visible"],
                0x70AB => ["Stat property", "Value"],
                0x70B2 => ["Motion property", "Float reference"],
                _ => AtelDecompiler.GetCallParameters(call.Operand)?.Select(HumanizeParameter).ToArray()
            };
            if (roles == null) continue;
            int firstArgument = callIndex - roles.Length;
            if (selected < firstArgument || selected >= callIndex) continue;
            if (Enumerable.Range(firstArgument, roles.Length).Any(i => i < 0 || all[i].Opcode is not (0x9F or 0xAE or 0xAF))) continue;
            string role = roles[selected - firstArgument];
            if (instruction.Opcode == 0xAF && !role.Contains("reference", StringComparison.OrdinalIgnoreCase))
                role += " float reference";
            if (instruction.Opcode == 0x9F && role is not ("Target" or "Character" or "Group" or "Command source"))
                return (role, Enumerable.Range(0, 32).Select(i => new OperandChoice("Variable", (ushort)i, 0x9F)).ToArray());
            if (role == "Value" && call.Operand is 0x7018 or 0x70AB)
            {
                int propertyArgument = call.Operand == 0x7018 ? firstArgument + 1 : firstArgument;
                if (propertyArgument >= 0 && propertyArgument < all.Length)
                {
                    ushort property = all[propertyArgument].Operand;
                    if (AtelStatProperties.BooleanProperties.Contains(property))
                        return (role, [new("False", 0x0000, instruction.Opcode), new("True", 0x0001, instruction.Opcode)]);
                    if (AtelStatProperties.CommandProperties.Contains(property))
                        return (role, DataModel.AiCommandNames
                            .Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).OrderBy(x => x.Value).ToArray());
                    if (AtelStatProperties.EnumValues.TryGetValue(property, out IReadOnlyDictionary<ushort, string>? values))
                        return (role, values.Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).ToArray());
                }
            }
            return role switch
            {
                "Target" or "Character" or "Group" => (role, GetTargetChoices()),
                "Stat property" => (role, AtelStatProperties.Names.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Property" => (role, AtelStatProperties.Names.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Command property" => (role, AtelDecompiler.CommandProperties.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Motion property" => (role, AtelDecompiler.MotionProperties.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Selector" => (role, [new("Any/All", 0x0000), new("Highest", 0x0001), new("Lowest", 0x0002), new("Not", 0x0080)]),
                "Command" => (role, DataModel.AiCommandNames.Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).OrderBy(x => x.Value).ToArray()),
                "Disabled" => (role, [new("Enabled", 0x0000), new("Disabled", 0x0001)]),
                "Visible" => (role, [new("Hidden", 0x0000), new("Visible", 0x0001)]),
                "Weak state" => (role, AtelStatProperties.EnumValues[0x0008]
                    .Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).ToArray()),
                "Battle result" => (role, [new("Defeat", 0x0001), new("Victory", 0x0002),
                    new("Player Escaped", 0x0003), new("Monster Escaped", 0x0004)]),
                "Command source" when instruction.Opcode == 0x9F => (role,
                    Enumerable.Range(0, 32).Select(i => new OperandChoice("Variable", (ushort)i, 0x9F)).ToArray()),
                _ => (role, [])
            };
        }
        if (instruction.Opcode is 0x9F or 0xA0 or 0xA1)
            return (instruction.Opcode == 0x9F ? "Variable read" : "Variable assignment",
                Enumerable.Range(0, 32).Select(i => new OperandChoice("Variable", (ushort)i, instruction.Opcode)).ToArray());
        return (null, []);
    }

    private static string HumanizeParameter(string parameter)
    {
        string role = string.Concat(parameter.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
        if (string.IsNullOrEmpty(role)) return "Value";
        role = char.ToUpperInvariant(role[0]) + role[1..];
        return role switch
        {
            "Btl Chr" => "Character",
            "Character" => "Character",
            "Target" => "Target",
            "Group" => "Group",
            "Command" => "Command",
            "Selector" => "Selector",
            "Property" => "Property",
            _ => role
        };
    }

    private static OperandChoice[] GetTargetChoices()
    {
        OperandChoice[] variables = Enumerable.Range(0, 32)
            .Select(index => new OperandChoice("Variable", (ushort)index, 0x9F))
            .ToArray();
        return BattleTargets.Concat(variables).ToArray();
    }

    private void AiMeaning_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingMeaningOptions || AiMeaningOptions.SelectedItem is not OperandChoice choice) return;
        AiOperandText.Text = $"0x{choice.Value:X4}";
    }

    private void AiMeaning_DropDownOpened(object? sender, EventArgs e)
    {
        if (AiMeaningOptions.SelectedItem != null)
            AiMeaningOptions.ScrollIntoView(AiMeaningOptions.SelectedItem);
    }

	private void Button_ApplyMeaning(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ApplySingleInstructionEdit(false);

	private void Button_ApplyManualOperand(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ApplySingleInstructionEdit(true);

    private void ApplySingleInstructionEdit(bool manual)
    {
        ClearValidationResult();
        if (_selectedInstruction == null)
        {
            AiStatusText.Text = "ERROR: Select an instruction first.";
            return;
        }
        try
        {
            DataModel.RecordAiUndoCheckpoint(manual ? "apply manual operand change" : "apply dropdown change",
                "Instruction", _selectedInstruction.Offset);
            AtelInstruction edited;
            if (manual)
                edited = DataModel.ApplyInstructionOperand(_selectedInstruction.Offset, AiOperandText.Text ?? "");
            else if (AiMeaningOptions.SelectedItem is not OperandChoice selectedChoice)
                throw new InvalidOperationException("Select a value from the dropdown first.");
            else if (_semanticRole == "Comparison")
                edited = DataModel.ApplyStructuredOpcode(_selectedInstruction.Offset, selectedChoice.Opcode, selectedChoice.Name);
            else if (_semanticRole is "Target" or "Character" or "Group")
                edited = DataModel.ApplyStructuredOperand(_selectedInstruction.Offset, selectedChoice.Opcode, selectedChoice.Value,
                    selectedChoice.Opcode == 0x9F ? "variable target" : "literal target");
            else
                edited = DataModel.ApplyInstructionOperand(_selectedInstruction.Offset, $"0x{selectedChoice.Value:X4}");
            AiHexText.Text = DataModel.AiHex;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = DataModel.AiStatements;
            _selectedInstruction = edited;
			SetInstructionSelectionSummary(edited);
            AiOperandText.Text = $"0x{edited.Operand:X4}";
            UpdateMeaningEditor(edited);
            SelectAiHexRange(DataModel.AiDocument!.ScriptCodeOffset + edited.Offset, edited.Bytes.Length);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void Button_ApplyFloatValue(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (_selectedInstruction == null || _selectedInstruction.Opcode != 0xAF)
        {
            AiStatusText.Text = "ERROR: Select a PUSH_FLOAT_REF instruction first.";
            return;
        }
        try
        {
            int offset = _selectedInstruction.Offset;
            DataModel.RecordAiUndoCheckpoint("apply float value change", "Instruction", offset);
            AtelInstruction edited = DataModel.ApplyFloatConstant(offset, _selectedInstruction.Operand, AiFloatValueText.Text ?? "");
            AiHexText.Text = DataModel.AiHex;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = DataModel.AiStatements;
            _selectedInstruction = edited;
            AiInstructionList.SelectedItem = edited;
            AiInstructionList.ScrollIntoView(edited);
			SetInstructionSelectionSummary(edited);
            AiOperandText.Text = $"0x{edited.Operand:X4}";
            UpdateMeaningEditor(edited);
            SelectStatementForInstruction(edited);
            SelectAiHexRange(DataModel.AiDocument!.ScriptCodeOffset + edited.Offset, edited.Bytes.Length);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void Button_FindAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => FindAi(1);

    private void Button_FindAiPrevious(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => FindAi(-1);

    private void FindAi(int direction)
    {
		ClearJumpDestinationHighlight();
		_activeJumpInstruction = null;
        ClearValidationResult();
        if (_aiHexIsDirty)
        {
            AiStatusText.Text = "Validate the manually edited Battle Script hex before searching or highlighting Script Instructions.";
            return;
        }
        CommitSearchInputs();
        try
        {
            DataModel.FindAiHex();
            string normalizedSearch = DataModel.AiSearchHex.Trim();
			int[] scopedOffsets = GetScopedAiSearchOffsets();
			string scopeDescription = _selectedWorkerIndex < 0 ? "all workers" : $"Worker w{_selectedWorkerIndex:X2}";
			if (scopedOffsets.Length == 0)
			{
				AiStatusText.Text = $"Bytes {FormatSearchBytes(AiSearchText.Text ?? "")} could not be found in {scopeDescription}.";
				return;
			}
			string scopedSearch = $"{normalizedSearch}|worker={_selectedWorkerIndex}";
            if (!string.Equals(_lastSearch, scopedSearch, StringComparison.OrdinalIgnoreCase))
            {
                _lastSearch = scopedSearch;
                _searchResultIndex = direction < 0 ? scopedOffsets.Length - 1 : 0;
            }
            else
            {
                _searchResultIndex = (_searchResultIndex + direction + scopedOffsets.Length) % scopedOffsets.Length;
            }

            int byteOffset = scopedOffsets[_searchResultIndex];
            SelectAiHexRange(byteOffset, DataModel.AiSearchLength);
            HighlightDecodedInstructions(byteOffset, DataModel.AiSearchLength);
            AiStatusText.Text = $"Match {_searchResultIndex + 1} of {scopedOffsets.Length} in {scopeDescription} at Battle Script offset 0x{byteOffset:X}. Use the up/down buttons to move between matches.";
        }
        catch (Exception ex)
        {
            AiStatusText.Text = ex.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase)
                ? $"Bytes {FormatSearchBytes(AiSearchText.Text ?? "")} could not be found."
                : "ERROR: " + ex.Message;
        }
    }

	private int[] GetScopedAiSearchOffsets()
	{
		if (_selectedWorkerIndex < 0 || DataModel.AiDocument == null)
			return DataModel.AiSearchOffsets.ToArray();

		(int scriptStart, int scriptEnd) = GetWorkerScriptRange(_selectedWorkerIndex);
		int chunkStart = DataModel.AiDocument.ScriptCodeOffset + scriptStart;
		int chunkEnd = DataModel.AiDocument.ScriptCodeOffset + scriptEnd;
		return DataModel.AiSearchOffsets
			.Where(offset => offset >= chunkStart && offset + DataModel.AiSearchLength <= chunkEnd)
			.ToArray();
	}

    private static string FormatSearchBytes(string searchText) => string.Join(' ',
        AtelScriptDocument.ParseHexEditorText(searchText).Select(value => value.ToString("X2")));

    private void Button_ReplaceAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
		ClearJumpDestinationHighlight();
		_activeJumpInstruction = null;
        ClearValidationResult();
        CommitSearchInputs();
        try
        {
            DataModel.RecordAiUndoCheckpoint("replace Battle Script bytes");
            DataModel.ReplaceAiHex();
            AiHexText.Text = DataModel.AiHex;
            _aiHexIsDirty = false;
            AiInstructionList.IsEnabled = true;
            AiStatementList.IsEnabled = true;
			RefreshNavigationAfterDocumentChange();
            _lastSearch = "";
            _searchResultIndex = 0;
            int byteOffset = DataModel.AiSearchOffsets[0];
            SelectAiHexRange(byteOffset, DataModel.AiSearchLength);
            HighlightDecodedInstructions(byteOffset, DataModel.AiSearchLength);
            AiStatusText.Text = $"Replaced {DataModel.AiSearchOffsets.Count} occurrence(s). Highlighting the first replacement at Battle Script offset 0x{byteOffset:X}.";
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void CommitSearchInputs()
    {
        // Read controls explicitly so clicking a button works even before Avalonia commits a focused TextBox binding.
        DataModel.AiSearchHex = AiSearchText.Text ?? "";
        DataModel.AiReplacementHex = AiReplacementText.Text ?? "";
    }

    private void ClearValidationResult()
    {
        AiValidationResultText.Text = "";
        AiValidationResultText.Foreground = null;
    }

	private void InitializeJumpDestinationOverlay()
	{
		_aiHexScrollViewer = AiHexText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
		_aiJumpScrollViewer = AiJumpDestinationText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
		if (_aiHexScrollViewer == null || _aiJumpScrollViewer == null)
		{
			Dispatcher.UIThread.Post(InitializeJumpDestinationOverlay, DispatcherPriority.Loaded);
			return;
		}
		_aiJumpScrollViewer.Offset = _aiHexScrollViewer.Offset;
		UpdateHexColumnHeaderOffset();
		_aiHexScrollViewer.ScrollChanged += (_, _) =>
		{
			if (_aiJumpScrollViewer != null && _aiHexScrollViewer != null)
				_aiJumpScrollViewer.Offset = _aiHexScrollViewer.Offset;
			UpdateHexColumnHeaderOffset();
		};
	}

	private void UpdateHexColumnHeaderOffset()
	{
		if (_aiHexScrollViewer == null || AiHexColumnHeader == null) return;
		AiHexColumnHeader.RenderTransform = new TranslateTransform(-_aiHexScrollViewer.Offset.X, 0);
	}

	private void UpdateJumpDestinationHighlight(IEnumerable<AtelInstruction> instructions)
	{
		ClearLogicJumpDestinationHighlights();
		if (_aiHexIsDirty || DataModel.AiDocument == null)
		{
			ClearJumpDestinationHighlight();
			return;
		}

		AtelInstruction? jump = instructions.LastOrDefault(IsJumpInstruction);
		if (jump == null)
		{
			ClearJumpDestinationHighlight();
			return;
		}

		int workerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(jump.Offset);
		AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == workerIndex);
		if (worker == null || jump.Operand >= worker.JumpOffsets.Count)
		{
			ClearJumpDestinationHighlight();
			return;
		}

		int destinationScriptOffset = worker.JumpOffsets[jump.Operand];
		HighlightJumpDestinationScriptOffset(destinationScriptOffset);
	}

	private void HighlightJumpDestinationScriptOffset(int destinationScriptOffset)
	{
		ClearLogicJumpDestinationHighlights();
		if (_aiHexIsDirty || DataModel.AiDocument == null)
		{
			ClearJumpDestinationHighlight();
			return;
		}

		int destinationChunkOffset = DataModel.AiDocument.ScriptCodeOffset + destinationScriptOffset;
		AtelInstruction? destinationInstruction = DataModel.AiDocument.Instructions.FirstOrDefault(item => item.Offset == destinationScriptOffset);
		AtelStatement? destinationStatement = DataModel.AiDocument.Statements.FirstOrDefault(item =>
			destinationScriptOffset >= item.Offset && destinationScriptOffset < item.Offset + item.ByteLength);
		if (destinationInstruction != null) destinationInstruction.IsJumpDestination = true;
		if (destinationStatement != null) destinationStatement.IsJumpDestination = true;
		int selectionStart = HexCharacterIndex(destinationChunkOffset);
		string hex = AiHexText.Text ?? "";
		if (selectionStart < 0 || selectionStart + 2 > hex.Length)
		{
			ClearJumpDestinationHighlight();
			return;
		}

		AiJumpDestinationText.Text = hex;
		AiJumpDestinationText.SelectionStart = selectionStart;
		AiJumpDestinationText.SelectionEnd = selectionStart + 2;
		if (_aiJumpScrollViewer != null && _aiHexScrollViewer != null)
			_aiJumpScrollViewer.Offset = _aiHexScrollViewer.Offset;
	}

	private static bool IsJumpInstruction(AtelInstruction instruction) =>
		instruction.Opcode is 0xB0 or 0xB1 or 0xB2 or 0xD5 or 0xD6 or 0xD7;

	private int? GetJumpDestinationChunkOffset(AtelInstruction jump)
	{
		if (DataModel.AiDocument == null || !IsJumpInstruction(jump)) return null;
		int workerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(jump.Offset);
		AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == workerIndex);
		if (worker == null || jump.Operand >= worker.JumpOffsets.Count) return null;
		return DataModel.AiDocument.ScriptCodeOffset + worker.JumpOffsets[jump.Operand];
	}

	private void Button_JumpToDestination(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (_activeJumpInstruction == null || DataModel.AiDocument == null ||
			GetJumpDestinationChunkOffset(_activeJumpInstruction) is not int chunkOffset)
		{
			AiStatusText.Text = "The selected jump does not have a valid destination in this worker.";
			return;
		}

		int scriptOffset = chunkOffset - DataModel.AiDocument.ScriptCodeOffset;
		NavigateToScriptDestination(scriptOffset);
	}

	private void Button_GoToWorkerJump(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (AiWorkerJumpOptions.SelectedItem is not WorkerJumpChoice choice || choice.ScriptOffset < 0)
		{
			AiStatusText.Text = "Select a valid jump-table entry first.";
			return;
		}
		NavigateToScriptDestination(choice.ScriptOffset);
	}

	private void NavigateToScriptDestination(int scriptOffset)
	{
		if (DataModel.AiDocument == null) return;
		int chunkOffset = DataModel.AiDocument.ScriptCodeOffset + scriptOffset;
		if (_selectedFunctionIndex >= 0 && AiFunctionOptions.ItemsSource is IEnumerable<FunctionScopeChoice> functions)
		{
			FunctionScopeChoice? destinationFunction = functions.FirstOrDefault(function =>
				function.Index >= 0 && scriptOffset >= function.Start && scriptOffset < function.End);
			AiFunctionOptions.SelectedItem = destinationFunction ?? functions.FirstOrDefault(function => function.Index < 0);
		}

		AtelStatement? destinationStatement = DataModel.AiDocument.Statements.FirstOrDefault(statement =>
			scriptOffset >= statement.Offset && scriptOffset < statement.Offset + statement.ByteLength);
		if (destinationStatement != null)
		{
			_synchronizingStatementSelection = true;
			try
			{
				AiStatementList.SelectedItem = destinationStatement;
				AiStatementList.ScrollIntoView(destinationStatement);
			}
			finally
			{
				_synchronizingStatementSelection = false;
			}
			ActivateStatementEditor(destinationStatement);
			AiStatusText.Text = $"Jumped to Battle Logic statement at script offset 0x{destinationStatement.Offset:X4} (Battle Script offset 0x{chunkOffset:X}).";
			return;
		}

		ClearJumpDestinationHighlight();
		SelectAiHexRange(chunkOffset, 1);
		int characterIndex = HexCharacterIndex(chunkOffset);
		string text = AiHexText.Text ?? "";
		int line = characterIndex <= 0 ? 0 : text.Take(Math.Min(characterIndex, text.Length)).Count(character => character == '\n');
		AiHexText.ScrollToLine(line);
		AiStatusText.Text = $"Jumped to Battle Script offset 0x{chunkOffset:X}; no Battle Logic statement begins there.";
	}

	private void ClearJumpDestinationHighlight()
	{
		ClearLogicJumpDestinationHighlights();
		if (AiJumpDestinationText == null) return;
		AiJumpDestinationText.SelectionStart = 0;
		AiJumpDestinationText.SelectionEnd = 0;
	}

	private void ClearLogicJumpDestinationHighlights()
	{
		if (DataModel.AiDocument == null) return;
		foreach (AtelInstruction instruction in DataModel.AiDocument.Instructions)
			instruction.IsJumpDestination = false;
		foreach (AtelStatement statement in DataModel.AiDocument.Statements)
			statement.IsJumpDestination = false;
	}

    private void SelectAiHexRange(int byteOffset, int byteLength)
    {
        if (_aiHexIsDirty || byteLength <= 0) return;
		int selectionVersion = ++_aiHexSelectionVersion;
        int selectionStart = HexCharacterIndex(byteOffset);
        int selectionEnd = HexCharacterIndex(byteOffset + byteLength - 1) + 2;
        AiHexText.Focus();
        // Apply after the button click/focus transition completes; otherwise Avalonia can clear the first selection.
        Dispatcher.UIThread.Post(() => Dispatcher.UIThread.Post(() =>
        {
            if (_aiHexIsDirty || selectionVersion != _aiHexSelectionVersion || selectionStart < 0 || selectionEnd > (AiHexText.Text?.Length ?? 0)) return;
            AiHexText.CaretIndex = selectionStart;
            AiHexText.SelectionStart = selectionStart;
            AiHexText.SelectionEnd = selectionEnd;
			ScrollAiHexSelectionToTop(selectionStart);
        }, DispatcherPriority.Background), DispatcherPriority.Background);
    }

    private void SelectDirtyAiHexRange(int byteOffset, int byteLength)
    {
        if (byteLength <= 0) return;
        List<(int Start, int End)> positions = GetHexByteCharacterPositions(AiHexText.Text ?? "");
        if (byteOffset < 0 || byteOffset + byteLength > positions.Count) return;
        int selectionStart = positions[byteOffset].Start;
        int selectionEnd = positions[byteOffset + byteLength - 1].End;
        AiHexText.Focus();
        AiHexText.CaretIndex = selectionStart;
        AiHexText.SelectionStart = selectionStart;
        AiHexText.SelectionEnd = selectionEnd;
		ScrollAiHexSelectionToTop(selectionStart);
    }

	private void ScrollAiHexSelectionToTop(int selectionStart)
	{
		string text = AiHexText.Text ?? "";
		int safeStart = Math.Clamp(selectionStart, 0, text.Length);
		int line = text.Take(safeStart).Count(character => character == '\n');
		AiHexText.ScrollToLine(line);

		Dispatcher.UIThread.Post(() =>
		{
			if (_aiHexScrollViewer == null) return;
			int lineCount = Math.Max(1, text.Count(character => character == '\n') + 1);
			double lineHeight = _aiHexScrollViewer.Extent.Height / lineCount;
			double maximumOffset = Math.Max(0, _aiHexScrollViewer.Extent.Height - _aiHexScrollViewer.Viewport.Height);
			double targetOffset = Math.Clamp(line * lineHeight, 0, maximumOffset);
			_aiHexScrollViewer.Offset = new Avalonia.Vector(_aiHexScrollViewer.Offset.X, targetOffset);
		}, DispatcherPriority.Background);
	}

    private static List<(int Start, int End)> GetHexByteCharacterPositions(string text)
    {
        var result = new List<(int Start, int End)>();
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            int newline = text.IndexOf('\n', lineStart);
            int lineEnd = newline < 0 ? text.Length : newline;
            int colon = text.IndexOf(':', lineStart, lineEnd - lineStart);
            int cursor = colon >= 0 ? colon + 1 : lineStart;
            while (cursor < lineEnd)
            {
                while (cursor < lineEnd && char.IsWhiteSpace(text[cursor])) cursor++;
                if (cursor >= lineEnd) break;
                int tokenStart = cursor;
                while (cursor < lineEnd && !char.IsWhiteSpace(text[cursor])) cursor++;
                int cleanStart = tokenStart;
                if (cursor - tokenStart >= 2 && text[tokenStart] == '0' && (text[tokenStart + 1] is 'x' or 'X')) cleanStart += 2;
                int digits = cursor - cleanStart;
                for (int index = 0; index + 1 < digits; index += 2)
                    result.Add((cleanStart + index, cleanStart + index + 2));
            }
            if (newline < 0) break;
            lineStart = newline + 1;
        }
        return result;
    }

    private void HighlightDecodedInstructions(int chunkOffset, int byteLength)
    {
        if (DataModel.AiDocument == null || AiInstructionList.SelectedItems == null) return;
        int scriptStart = DataModel.AiDocument.ScriptCodeOffset;
        int rangeStart = chunkOffset - scriptStart;
        int rangeEnd = rangeStart + byteLength;
        AtelInstruction[] overlapping = DataModel.AiDocument.Instructions
            .Where(i => i.Offset < rangeEnd && i.Offset + i.Bytes.Length > rangeStart)
            .ToArray();

        _synchronizingInstructionSelection = true;
        try
        {
            AiInstructionList.SelectedItems.Clear();
            foreach (AtelInstruction instruction in overlapping)
                AiInstructionList.SelectedItems.Add(instruction);
            if (overlapping.Length > 0)
            {
				_logicSelectionOwner = AiLogicSelectionOwner.Instruction;
				AiManualOperandEditor.IsVisible = overlapping.Length == 1 && overlapping[0].HasOperand;
                _selectedInstruction = overlapping[0];
                SelectStatementForInstruction(overlapping[0]);
                AiSelectedInstructionText.Text = overlapping.Length == 1
                    ? $"Instruction • Script 0x{overlapping[0].Offset:X4} • Battle Script 0x{DataModel.AiDocument.ScriptCodeOffset + overlapping[0].Offset:X4} • {overlapping[0].Bytes.Length} byte(s) • {overlapping[0].OpcodeName}"
                    : $"{overlapping.Length} instructions selected (0x{overlapping[0].Offset:X4}–0x{overlapping[^1].Offset:X4})";
                AiOperandText.Text = overlapping.Length == 1 && overlapping[0].HasOperand ? $"0x{overlapping[0].Operand:X4}" : "";
                AiOperandText.IsEnabled = overlapping.Length == 1 && overlapping[0].HasOperand;
                if (overlapping.Length == 1)
                    UpdateMeaningEditor(overlapping[0]);
                else
                {
                    AiMeaningLabel.IsVisible = false;
                    AiMeaningOptions.IsVisible = false;
                }
                AiInstructionList.ScrollIntoView(overlapping[0]);
            }
        }
        finally
        {
            _synchronizingInstructionSelection = false;
        }
    }

    private static int HexCharacterIndex(int byteOffset)
    {
        const int bytesPerLine = 16;
        const int prefixLength = 8; // "000000: "
        const int fullLineLengthWithNewline = 56;
        int line = byteOffset / bytesPerLine;
        int column = byteOffset % bytesPerLine;
        return line * fullLineLengthWithNewline + prefixLength + column * 3;
    }

    private bool RunAiAction(Action action, bool prefixError = true)
    {
        _lastAiActionException = null;
        try
        {
            int? selectedOffset = _selectedInstruction?.Offset;
            action();
            AiHexText.Text = DataModel.AiHex;
            _aiHexIsDirty = false;
            AiInstructionList.IsEnabled = true;
            AiStatementList.IsEnabled = true;
            AiInstructionList.ItemsSource = null;
            AiInstructionList.ItemsSource = DataModel.AiInstructions;
            AiStatementList.ItemsSource = null;
            AiStatementList.ItemsSource = DataModel.AiStatements;
			RefreshNavigationAfterDocumentChange();
            if (selectedOffset.HasValue && DataModel.AiDocument != null)
            {
                AtelInstruction? restored = DataModel.AiDocument.Instructions.FirstOrDefault(i => i.Offset == selectedOffset.Value);
                if (restored != null)
                {
                    _selectedInstruction = restored;
                    AiInstructionList.SelectedItem = restored;
					SetInstructionSelectionSummary(restored);
                    AiOperandText.Text = restored.HasOperand ? $"0x{restored.Operand:X4}" : "";
                    AiOperandText.IsEnabled = restored.HasOperand;
                    UpdateMeaningEditor(restored);
                    AiInstructionList.ScrollIntoView(restored);
                }
            }
            AiStatusText.Text = DataModel.AiStatus;
            return true;
        }
        catch (Exception ex)
        {
            _lastAiActionException = ex;
            AiStatusText.Text = (prefixError ? "ERROR: " : "") + ex.Message;
            return false;
        }
    }
}
