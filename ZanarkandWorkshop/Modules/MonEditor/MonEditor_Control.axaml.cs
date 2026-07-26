using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.Atel;
using FFXProjectEditor.FfxLib.Dictionaries;
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
    private bool _restoringRejectedAiHex;
    private string? _rejectedAiHexDraft;
    private bool _updatingInlineMessage;
    private string? _aiMessageDetails;
    private string _aiMessageDetailsTitle = "Battle Script details";
    private Exception? _lastAiActionException;
    private string? _semanticRole;
    private readonly List<GroupOperandEditor> _groupOperandEditors = [];
    private static BattleScriptClipboard? _battleScriptClipboard;
	private int _selectedWorkerIndex = -1;
	private bool _updatingWorkerScope;
	private int _selectedFunctionIndex = -1;
	private bool _choosingWorkerJumpDestination;
	private int _jumpPickerWorkerIndex = -1;
	private int _jumpPickerJumpIndex = -1;
	private int _jumpPickerOriginalOffset = -1;
	private int? _jumpPickerCandidateOffset;
	private bool _jumpPickerAddsEntry;
	private bool _updatingFunctionScope;
	private ScrollViewer? _aiHexScrollViewer;
	private ScrollViewer? _aiChangeContextScrollViewer;
	private ScrollViewer? _aiExactChangeScrollViewer;
	private ScrollViewer? _aiJumpScrollViewer;
	private AtelInstruction? _activeJumpInstruction;
	private AiLogicSelectionOwner _logicSelectionOwner;
	private int _aiHexSelectionVersion;
	private static bool _hideStatementsPreference;
	private static bool _hideInstructionsPreference;
	private static bool _suppressDeleteStatementWarning;
	private static bool _suppressUnsafePasteWarning;
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
        public override string ToString() => DisplayOverride ?? $"[0x{Value:X4}]  {Name}";
    }

    private sealed record ActorReferenceKindChoice(string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record CommandEditorKindChoice(string Name, ushort Category)
    {
        public override string ToString() => Name;
    }

    private sealed record StatPropertyGroupChoice(string Name, ushort First, ushort Last)
    {
        public bool Contains(ushort property) => property >= First && property <= Last;
        public override string ToString() => Name;
    }

    private sealed record AiEditSnapshot(byte[] Bytes, byte[] ScriptCode, int WorkerIndex, int FunctionIndex);

    private sealed record BattleScriptClipboardInstruction(int SourceOffset, byte Opcode, byte[] Bytes,
        string OpcodeName, string Translation, ushort? Operand);

    private sealed record BattleScriptClipboardStatement(int SourceOffset, int ByteLength, byte[] Bytes,
        string Translation, IReadOnlyList<BattleScriptClipboardInstruction> Instructions);

    private sealed record BattleScriptClipboardBranch(int SourceInstructionOffset, ushort JumpIndex,
        int DestinationOffset, bool DestinationInsideRange);

    private sealed record BattleScriptClipboardFloat(int SourceInstructionOffset, ushort SourceIndex,
        int ValueBits);

    private sealed record BattleScriptClipboard(string SourceMonsterPath, int SourceWorkerIndex,
        int SourceFunctionIndex, int StartOffset, int EndOffset, byte[] Bytes,
        IReadOnlyList<BattleScriptClipboardStatement> Statements,
        IReadOnlyList<BattleScriptClipboardBranch> Branches,
        IReadOnlyList<BattleScriptClipboardFloat> Floats, bool FallsThrough)
    {
        public bool HasConditionalBranches => Branches.Count > 0;
        public int ByteLength => EndOffset - StartOffset;
        public int InstructionCount => Statements.Sum(statement => statement.Instructions.Count);
        public string SourceMonsterName => Path.GetFileNameWithoutExtension(SourceMonsterPath);
        public bool HasExternalBranches => Branches.Any(branch => !branch.DestinationInsideRange);
    }

    private sealed record GroupOperandEditor(int InstructionOffset, string Role, ComboBox? Options, TextBox? ValueText,
        ComboBox? ReferenceKind = null,
        ushort? FloatIndex = null);

	private sealed record WorkerScopeChoice(int Index, string Display);
	private sealed record FunctionScopeChoice(int Index, int Start, int End, string Display);
	private sealed record WorkerJumpChoice(int Index, int ScriptOffset, string Display);
	public sealed record AiEditorPreferences(bool HideGroupedLogic, bool HideDecodedInstructions,
		bool SuppressDeleteStatementWarning = false, bool SuppressUnsafePasteWarning = false);

    private static readonly ActorReferenceKindChoice CharacterReferenceKind = new("Character / battle slot");
    private static readonly ActorReferenceKindChoice MonsterReferenceKind = new("Specific monster type");
    private static readonly ActorReferenceKindChoice SelectorReferenceKind = new("Dynamic actor selector");
    private static readonly ActorReferenceKindChoice VariableReferenceKind = new("Worker variable");
    private static readonly ActorReferenceKindChoice[] ActorReferenceKinds =
        [CharacterReferenceKind, MonsterReferenceKind, SelectorReferenceKind, VariableReferenceKind];
    private static readonly CommandEditorKindChoice[] CommandEditorKinds =
    [
        new("Items", 0x0002),
        new("Player & Aeon Commands", 0x0003),
        new("Standard Monster Commands", 0x0004),
        new("Boss Commands", 0x0006)
    ];
    private static readonly StatPropertyGroupChoice[] StatPropertyGroups =
    [
        new("[0x0000–0x0039] Stats & statuses", 0x0000, 0x0039),
        new("[0x003A–0x0075] Abilities, presentation & elements", 0x003A, 0x0075),
        new("[0x0076–0x00AE] Behavior, items & battle state", 0x0076, 0x00AE),
        new("[0x00AF–0x00C7] Status resistances", 0x00AF, 0x00C7),
        new("[0x00C8–0x00D6] Status immunities", 0x00C8, 0x00D6),
        new("[0x00D7–0x0109] Runtime state & battle control", 0x00D7, 0x0109),
        new("[0x010A–0x012A] Commands, Aeons & advanced state", 0x010A, 0x012A),
        new("[0x012B–0x0149] Rewards & drops", 0x012B, 0x0149),
        new("[0x014A–0x0159] Advanced flags", 0x014A, 0x0159)
    ];

    private static readonly OperandChoice[] CharacterTargets = AtelDecompiler.BattleCharacters
        .Where(entry => entry.Key < 0xFFE6)
        .OrderBy(entry => entry.Key)
        .Select(entry => new OperandChoice(entry.Value, entry.Key))
        .ToArray();

    private static readonly OperandChoice[] SelectorTargets = AtelDecompiler.BattleCharacters
        .Where(entry => entry.Key >= 0xFFE6)
        .OrderBy(entry => entry.Key)
        .Select(entry => new OperandChoice(entry.Value, entry.Key))
        .ToArray();

    private static readonly OperandChoice[] MonsterTypeTargets = Monster_Dictionary.Instance
        .Where(entry => entry.Key >= 0 && entry.Key <= 0x0FFF)
        .OrderBy(entry => entry.Key)
        .Select(entry =>
        {
            ushort encoded = (ushort)(0x1000 | entry.Key);
            return new OperandChoice($"m{entry.Key} — {entry.Value}", encoded, 0xAE,
                $"[0x{encoded:X4}]  m{entry.Key} — {entry.Value}");
        })
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
        RefreshScriptClipboardDisplay();
        AiStatusText.PropertyChanged += AiStatusText_PropertyChanged;
        AiHexText.TextChanged += AiHexText_TextChanged;
		AiHexText.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, AiHexText_PointerPressed,
			Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
			handledEventsToo: true);
		AiHexText.AttachedToVisualTree += (_, _) => InitializeJumpDestinationOverlay();
		InitializeWorkerScopes();
		UpdateBattleScriptCommandVisibility();
		if (DataModel.AiDocument != null)
		{
			string recoveryNote = DataModel.AiDocument.RecoveredMissingCodeLength
				? "\n\nThe script header did not contain its code length. Zanarkand Workshop recovered that value from the file. Saving this monster will repair the header."
				: "";
			ShowInlineMessage("INFO", "●", "Battle Script loaded.", Brush.Parse("#70B7FF"), "#142A3A",
				"The monster's Battle Script was read successfully and is ready to inspect or edit.\n\n" +
				"Workers divide the monster's behavior into separate routines. Battle Logic shows those routines in readable groups, while Script Instructions shows the individual operations behind them. The hex view contains the complete original script data.\n\n" +
				"Changes made with the editor are checked before they are accepted. Direct hex edits must be applied with Apply Hex Changes so the readable views can be rebuilt. Nothing is written to the monster file until you press Save." +
				recoveryNote,
				"About the Battle Script editor");
		}
    }

	private void MonsterEditorTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
		UpdateBattleScriptCommandVisibility();

	private void UpdateBattleScriptCommandVisibility()
	{
		if (AiRevertButton == null || AiUndoButton == null || AiRedoButton == null ||
			MonsterEditorTabs == null) return;
		bool battleScriptSelected = MonsterEditorTabs.SelectedItem is TabItem tab &&
			string.Equals(tab.Header?.ToString(), "Battle Script", StringComparison.Ordinal);
		AiRevertButton.IsVisible = battleScriptSelected;
		AiUndoButton.IsVisible = battleScriptSelected;
		AiRedoButton.IsVisible = battleScriptSelected;
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
		ClearChangeHexHighlights();
		if (_choosingWorkerJumpDestination) EndWorkerJumpDestinationPicker();
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
		if (_choosingWorkerJumpDestination && choice.Index != _jumpPickerWorkerIndex) EndWorkerJumpDestinationPicker();
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
			AiChangeWorkerJumpButton.IsEnabled = false;
			AiAddWorkerJumpButton.IsEnabled = false;
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
					choices.Add(new(index, scriptOffset, $"[0x{index:X4}] j{index:X2} -> offset 0x{chunkOffset:X6}"));
				}
			}
			if (choices.Count == 0) choices.Add(new(-1, -1, "This worker has no jumps"));
			AiWorkerJumpOptions.IsEnabled = choices.Any(choice => choice.Index >= 0);
			AiWorkerJumpButton.IsEnabled = choices.Any(choice => choice.Index >= 0);
			AiChangeWorkerJumpButton.IsEnabled = choices.Any(choice => choice.Index >= 0);
			AiAddWorkerJumpButton.IsEnabled = worker?.JumpCount > 0;
		}
		AiWorkerJumpOptions.ItemsSource = choices;
		AiWorkerJumpOptions.SelectedIndex = 0;
	}

	private void RefreshNavigationAfterDocumentChange(int? preferredJumpIndex = null,
		int? preferredWorkerIndex = null, int? preferredFunctionIndex = null)
	{
		int workerIndex = preferredWorkerIndex ?? _selectedWorkerIndex;
		int functionIndex = preferredFunctionIndex ?? _selectedFunctionIndex;
		int jumpIndex = preferredJumpIndex ?? (AiWorkerJumpOptions.SelectedItem as WorkerJumpChoice)?.Index ?? -1;
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
				foreach ((int start, int functionIndex) in (worker?.FunctionOffsets ?? [])
					.Select((offset, index) => (offset, index)).OrderBy(item => item.offset).ThenBy(item => item.index))
				{
					int end = offsets.FirstOrDefault(offset => offset > start, workerEnd);
					string functionName = worker!.FunctionName(functionIndex);
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
		ClearChangeHexHighlights();
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
		AiReferenceTypeEditor.IsVisible = false;
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
		AiCopiedStatementText.IsVisible = !hidden && _battleScriptClipboard != null;
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
		// Messages is an inline notification banner. Keep the user's current
		// Script Editor or Find & Replace context intact.
	}

	private void ShowMessageError(string message)
	{
		ShowInlineMessage("ERROR", "✕", message, Brushes.Red, "#3A1719",
			FriendlyErrorDetails(message));
		FocusMessages();
	}

	private void ShowMessageError(string message, string details)
	{
		ShowInlineMessage("ERROR", "✕", message, Brushes.Red, "#3A1719",
			FriendlyErrorDetails(details));
		FocusMessages();
	}

	private void ShowMessageSuccess(string message, string? details = null) =>
		ShowInlineMessage("SUCCESS", "✓", message, Brushes.LimeGreen, "#15351F", details);

	private void ShowMessageWarning(string message, string? details = null) =>
		ShowInlineMessage("WARNING", "▲", message, Brush.Parse("#FFB35C"), "#3A2B14", details);

	private void ShowSelectionInfo(string message) =>
		ShowInlineMessage("INFO", "●", message, Brush.Parse("#70B7FF"), "#142A3A");

	private void ShowInlineMessage(string label, string icon, string message, IBrush foreground,
		string background, string? details = null, string? detailsTitle = null)
	{
		string shortMessage = FriendlyMessageSummary(label, message);
		string fullDetails = string.IsNullOrWhiteSpace(details)
			? FriendlyMessageDetails(label, message)
			: details;

		_updatingInlineMessage = true;
		AiValidationResultText.Text = label;
		AiValidationResultText.Foreground = foreground;
		AiMessageIcon.Text = icon;
		AiMessageIcon.Foreground = foreground;
		AiMessageBanner.Background = Brush.Parse(background);
		AiMessagesAttentionIndicator.BorderBrush =
			string.Equals(label, "ERROR", StringComparison.OrdinalIgnoreCase)
				? Brushes.Red
				: Brushes.Transparent;
		AiStatusText.Text = shortMessage;
		_aiMessageDetails = fullDetails;
		_aiMessageDetailsTitle = detailsTitle ?? $"{FriendlySeverityName(label)} details";
		AiMessageDetailsButton.IsVisible = true;
		_updatingInlineMessage = false;
	}

	private static string FriendlyMessageSummary(string label, string message)
	{
		string text = message.Trim();

		if (text.StartsWith("Selected Battle Logic statement", StringComparison.OrdinalIgnoreCase))
			return "Battle Logic group selected.";
		if (text.StartsWith("Selected script offset", StringComparison.OrdinalIgnoreCase))
			return "Script instruction selected.";
		if (text.StartsWith("Contiguous Battle Logic range", StringComparison.OrdinalIgnoreCase))
			return "Battle Logic range selected.";
		if (text.StartsWith("Showing Worker", StringComparison.OrdinalIgnoreCase) ||
			text.StartsWith("Showing all workers", StringComparison.OrdinalIgnoreCase))
			return "Battle Script view updated.";
		if (text.StartsWith("Worker jump destination", StringComparison.OrdinalIgnoreCase))
			return "Worker jump destination highlighted.";
		if (text.StartsWith("Jumped to", StringComparison.OrdinalIgnoreCase))
			return "Jump destination selected.";
		if (text.StartsWith("Found ", StringComparison.OrdinalIgnoreCase))
			return "Search results are ready.";
		if (text.StartsWith("Replaced ", StringComparison.OrdinalIgnoreCase))
			return "Replacement completed successfully.";
		if (text.StartsWith("Applied", StringComparison.OrdinalIgnoreCase) ||
			text.StartsWith("Changed", StringComparison.OrdinalIgnoreCase))
			return "Changes applied successfully.";
		if (text.StartsWith("Inserted", StringComparison.OrdinalIgnoreCase))
			return "Battle Logic inserted successfully.";
		if (text.StartsWith("Undid", StringComparison.OrdinalIgnoreCase))
			return "Last change undone.";
		if (text.StartsWith("Redid", StringComparison.OrdinalIgnoreCase))
			return "Last change restored.";
		if (text.StartsWith("Restored", StringComparison.OrdinalIgnoreCase))
			return "Battle Script restored.";
		if (text.StartsWith("Validated", StringComparison.OrdinalIgnoreCase))
			return "Battle Script validation passed.";
		if (text.StartsWith("Battle Script parsing failed", StringComparison.OrdinalIgnoreCase))
			return "The Battle Script could not be opened.";

		string firstSentence = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault()?.Trim() ?? text;
		int sentenceEnd = firstSentence.IndexOf(". ", StringComparison.Ordinal);
		if (sentenceEnd >= 0) firstSentence = firstSentence[..(sentenceEnd + 1)];
		if (firstSentence.Length <= 78) return firstSentence;

		return label.ToUpperInvariant() switch
		{
			"ERROR" => "That action could not be completed.",
			"WARNING" => "This action needs your attention.",
			"SUCCESS" => "The action completed successfully.",
			_ => "Battle Script information updated."
		};
	}

	private static string FriendlyMessageDetails(string label, string message)
	{
		string? explanation = label.ToUpperInvariant() switch
		{
			"ERROR" =>
				"The editor could not complete the requested action. It stopped the operation so the Battle Script would not be left in an uncertain state.",
			"WARNING" =>
				"The editor found something that may need your attention. Read the explanation below before deciding what to do next.",
			"SUCCESS" =>
				"The requested action completed successfully. The explanation below describes exactly what changed.",
			_ => null
		};

		string saveReminder = label.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
			? "\n\nChanges remain in the editor until you press Save."
			: "";
		return string.IsNullOrEmpty(explanation)
			? message.Trim()
			: $"{explanation}\n\n{message.Trim()}{saveReminder}";
	}

	private static string FriendlySeverityName(string label) => label.ToUpperInvariant() switch
	{
		"ERROR" => "Battle Script error",
		"WARNING" => "Battle Script warning",
		"SUCCESS" => "Battle Script success",
		_ => "Battle Script information"
	};

	private void AiStatusText_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
	{
		if (_updatingInlineMessage || e.Property != TextBlock.TextProperty) return;
		string message = AiStatusText.Text ?? "";
		if (string.IsNullOrWhiteSpace(message)) return;
		if (message.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
			ShowMessageError(message["ERROR".Length..].TrimStart(':', ' '));
		else if (message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
				 message.Contains("affect", StringComparison.OrdinalIgnoreCase))
			ShowInlineMessage("WARNING", "▲", message, Brush.Parse("#FFB35C"), "#3A2B14");
		else if (message.StartsWith("Applied", StringComparison.OrdinalIgnoreCase) ||
				 message.StartsWith("Inserted", StringComparison.OrdinalIgnoreCase) ||
				 message.StartsWith("Replaced", StringComparison.OrdinalIgnoreCase) ||
				 message.StartsWith("Changed", StringComparison.OrdinalIgnoreCase) ||
				 message.StartsWith("Undid", StringComparison.OrdinalIgnoreCase) ||
				 message.StartsWith("Redid", StringComparison.OrdinalIgnoreCase) ||
				 message.StartsWith("Restored", StringComparison.OrdinalIgnoreCase))
			ShowInlineMessage("SUCCESS", "✓", message, Brushes.LimeGreen, "#15351F");
		else
			ShowInlineMessage("INFO", "●", message, Brush.Parse("#70B7FF"), "#142A3A");
	}

	private async void Button_ShowMessageDetails(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_aiMessageDetails)) return;
		Window? owner = this.FindAncestorOfType<Window>();
		if (owner == null) return;
		await AiMessageDetailsWindow.Show(owner, _aiMessageDetailsTitle, _aiMessageDetails);
	}

	private static string FriendlyEditError(string technicalMessage)
	{
		const string fullControlNote = " To override this structural protection, enable Full Control mode.";
		if (technicalMessage.Contains("No matching or unused destination float", StringComparison.OrdinalIgnoreCase))
			return "This monster has no safe space for the copied numeric value. No changes were made." + fullControlNote;
		if (technicalMessage.Contains("bytes after the script are not empty padding", StringComparison.OrdinalIgnoreCase))
			return "The script cannot be expanded safely in this monster. No changes were made." + fullControlNote;
		if (technicalMessage.Contains("jump targets the middle", StringComparison.OrdinalIgnoreCase) ||
			technicalMessage.Contains("points inside the destination", StringComparison.OrdinalIgnoreCase))
			return "Other logic enters the middle of this selection. Choose a smaller range that excludes that jump destination. No changes were made." + fullControlNote;
		if (technicalMessage.Contains("jump table shares storage", StringComparison.OrdinalIgnoreCase))
			return "This worker’s branch data cannot be expanded safely. No changes were made." + fullControlNote;
		return "The edit could not be applied safely. No changes were made." + fullControlNote;
	}

	private static string FriendlyErrorDetails(string technicalMessage)
	{
		const string fullControlNote =
			"\n\nPlanned feature: Full Control mode will let advanced users override some of these structural protections.";
		string reason = technicalMessage.Trim();
		if (reason.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
			reason = reason["ERROR:".Length..].Trim();

		// Callers may already provide a complete, user-facing explanation.
		if (reason.Contains("left the Battle Script unchanged", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("No changes were made", StringComparison.OrdinalIgnoreCase))
			return reason;

		if (reason.Contains("function entry point", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThis statement is the first step of a function. Deleting it would leave that function without a valid starting point.\n\nNothing was deleted, and the Battle Script remains unchanged. Choose a statement that is not a function entry." + fullControlNote;
		if (reason.Contains("RETURN (3C)", StringComparison.OrdinalIgnoreCase) &&
			(reason.Contains("cannot be copied", StringComparison.OrdinalIgnoreCase) ||
			 reason.Contains("cannot be pasted", StringComparison.OrdinalIgnoreCase) ||
			 reason.Contains("cannot be replaced", StringComparison.OrdinalIgnoreCase) ||
			 reason.Contains("cannot add, remove, or move", StringComparison.OrdinalIgnoreCase)))
			return "Why it stopped:\nRETURN (3C) ends the current function, so the standard editor keeps it out of copy, paste, replacement, and manual hex operations.\n\nNothing was changed. Select only the editable instructions around RETURN. Full Control mode is planned for intentional function restructuring.";
		if (reason.Contains("jump destination", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("jump targets the middle", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("points inside the destination", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nAnother part of the script jumps to this statement or into the selected range. Removing it would leave that jump pointing to invalid logic.\n\nNothing was changed. Choose a smaller range that does not include the jump destination, or redirect the jump first." + fullControlNote;
		if (reason.Contains("contains RETURN", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThis statement ends the current function. Deleting it could make execution continue into unrelated monster behavior.\n\nNothing was deleted. Keep the RETURN in place or replace the surrounding logic without removing the function ending." + fullControlNote;
		if (reason.Contains("contains JUMP", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("terminates control flow", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThis statement controls where the script runs next. The editor cannot remove it without risking a broken execution path.\n\nNothing was deleted. Redirect or rebuild the related branch before trying again." + fullControlNote;
		if (reason.Contains("instruction boundary", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("inside an instruction", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("complete statement", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("complete Battle Logic", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe selected bytes begin or end partway through an instruction. Battle Script instructions must remain complete so the game can read them correctly.\n\nNothing was changed. Adjust the selection to include complete Battle Logic statements or use the structured editor.";
		if (reason.Contains("one contiguous insertion or deletion", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe hex edit changes the script in more than one separate place. The editor can safely resize only one continuous region at a time.\n\nNothing was applied. Make one insertion or deletion, apply it, and then make the next change.";
		if (reason.Contains("not empty padding", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe space immediately after this script already contains other data. Expanding the script would overwrite that data.\n\nNothing was inserted. Use a smaller replacement that fits in the existing space." + fullControlNote;
		if (reason.Contains("No matching or unused destination float", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe copied logic needs a stored numeric value, but this monster has no matching value or unused safe slot for it.\n\nNothing was pasted. Reuse an existing value or free an unused value slot before trying again." + fullControlNote;
		if (reason.Contains("jump table shares storage", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThis worker's branch list shares file space with another table. Expanding it could damage both structures.\n\nNothing was changed. Reuse an existing jump entry instead of adding another one." + fullControlNote;
		if (reason.Contains("jump table cannot contain any more entries", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("outside worker", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe selected worker has no valid branch slot for this jump.\n\nNothing was changed. Reuse an existing jump or choose a destination already represented in the worker's jump list." + fullControlNote;
		if (reason.Contains("header layout", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("did not preserve", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nAfter rebuilding the script, an internal structure no longer matched the layout required by the game. The editor rejected the result rather than save a potentially unreadable script.\n\nThe original Battle Script remains unchanged.";
		if (reason.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("search sequence", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe requested byte sequence does not appear in the current search area.\n\nNothing was replaced. Check the search bytes and selected worker, then try again.";
		if (reason.Contains("operand", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe operand is missing, outside the allowed range, or not written as a valid decimal or hexadecimal number.\n\nNothing was changed. Enter a value from 0 to 65535, such as 120 or 0x0078.";
		if (reason.Contains("Float value", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe shared value is not a usable number. It must be a normal finite value, such as 2.5, 4, or -0.25.\n\nNothing was changed. Correct the value and try again.";
		if (reason.Contains("same number of bytes", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe search and replacement values have different lengths. This operation replaces bytes in place and cannot resize the script.\n\nNothing was changed. Enter the same number of search and replacement bytes.";
		if (reason.Contains("no Battle Script", StringComparison.OrdinalIgnoreCase) ||
			reason.Contains("not loaded", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThe current monster does not have a readable Battle Script available for this action.\n\nOpen a monster with Battle Logic and try again.";
		if (reason.Contains("no Battle Script changes", StringComparison.OrdinalIgnoreCase))
			return "Why it stopped:\nThere is no earlier or later Battle Script edit available in this session.\n\nThe script remains unchanged.";

		return "Why it stopped:\nThe requested result did not pass the editor's safety checks, so it was rejected before the Battle Script could be changed.\n\nNothing was changed. Review the selected logic and try a smaller or structured edit." +
			fullControlNote + "\n\nTechnical reason:\n" + reason;
	}

	private void ShowWorkerSelectionEditor()
	{
		AiWorkerEditorPanel.IsVisible = true;
		AiEditorPanel.IsVisible = true;
		UpdateWorkerJumpActionVisibility();
		UpdateReturnOnlyManualInsertionVisibility();
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
		AiCopiedStatementText.IsVisible = !statementsHidden && _battleScriptClipboard != null;
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
			_suppressUnsafePasteWarning = preferences.SuppressUnsafePasteWarning;
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
				_suppressDeleteStatementWarning, _suppressUnsafePasteWarning);
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
		AiCopiedStatementText.IsVisible = !statementsHidden && _battleScriptClipboard != null;
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

    private void Button_ValidateAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
		FocusMessages();
        if (!_aiHexIsDirty)
        {
            AiStatusText.Text = "No unapplied hex changes.";
            Dispatcher.UIThread.Post(FocusMessages, DispatcherPriority.Loaded);
            return;
        }
        string rejectedHex = AiHexText.Text ?? DataModel.AiHex;
        (int Offset, int Length)? changedRange = GetPendingHexChangeRange();
        string? selectionKind =
            AiStatementList.SelectedItem is AtelStatement ? "Group" :
            _selectedInstruction != null ? "Instruction" : null;
        int? selectionOffset =
            AiStatementList.SelectedItem is AtelStatement validationStatement
                ? validationStatement.Offset
                : _selectedInstruction?.Offset;
        bool valid = RunAiAction(() => DataModel.ApplyAiHexTransactional(
            "manual Battle Script validation or hex edit", selectionKind, selectionOffset));
        if (valid)
        {
            _rejectedAiHexDraft = null;
            AiRestoreRejectedHexButton.IsVisible = false;
            ShowMessageSuccess("Hex changes applied.",
                "The manually edited hex was valid. Battle Logic and Script Instructions were rebuilt from the updated bytes so all three views now agree.\n\nThe monster file has not been changed yet. Press Save when you are ready to write these changes to disk.");
            if (changedRange.HasValue)
            {
                SelectAiHexRange(changedRange.Value.Offset, changedRange.Value.Length);
                ShowChangeHexHighlights(changedRange.Value.Offset, changedRange.Value.Length);
                HighlightAppliedLogicRange(changedRange.Value.Offset, changedRange.Value.Length);
            }
        }
        else
            RollBackRejectedAiHex(rejectedHex);
		// Header buttons participate in tab selection after Click is raised. Queue this
		// final focus change so validation always ends on Messages, even when validation
		// rebuilds the decoded views or opens the partial-statement repair dialog.
		Dispatcher.UIThread.Post(FocusMessages, DispatcherPriority.Loaded);
    }

	private void RollBackRejectedAiHex(string rejectedHex)
	{
		_rejectedAiHexDraft = rejectedHex;
		_restoringRejectedAiHex = true;
		try
		{
			AiHexText.Text = DataModel.AiHex;
		}
		finally
		{
			_restoringRejectedAiHex = false;
		}
		_aiHexIsDirty = false;
		AiApplyHexButton.IsEnabled = false;
		AiRestoreRejectedHexButton.IsVisible = true;
		AiStatementList.IsEnabled = true;
		AiInstructionList.IsEnabled = true;

		string reason = _lastAiActionException?.Message ?? "The edited data did not pass validation.";
		ShowInlineMessage("ERROR", "✕", "Manual change rejected and rolled back.",
			Brushes.Red, "#3A1719",
			FriendlyErrorDetails(reason) +
			"\n\nThe last valid Battle Script has been restored automatically. Use Restore Rejected Edit if you want to recover the attempted hex and correct it.",
			"Rejected Battle Script edit");
	}

	private void Button_RestoreRejectedAiHex(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(_rejectedAiHexDraft)) return;
		string draft = _rejectedAiHexDraft;
		_rejectedAiHexDraft = null;
		AiRestoreRejectedHexButton.IsVisible = false;
		_restoringRejectedAiHex = true;
		try
		{
			DataModel.AiHex = draft;
			AiHexText.Text = draft;
		}
		finally
		{
			_restoringRejectedAiHex = false;
		}
		ShowMessageWarning("Rejected edit restored for correction.",
			"The previously rejected hex is back in the editor but has not been applied. Correct the issue, then choose Apply Hex Changes again.");
	}

    private (int Offset, int Length)? GetPendingHexChangeRange()
    {
        if (DataModel.AiDocument == null) return null;
        try
        {
            byte[] original = DataModel.AiDocument.Bytes;
            byte[] edited = AtelScriptDocument.ParseHexEditorText(AiHexText.Text ?? "");
            int sharedLength = Math.Min(original.Length, edited.Length);
            int prefix = 0;
            while (prefix < sharedLength && original[prefix] == edited[prefix]) prefix++;

            int suffix = 0;
            while (suffix < sharedLength - prefix &&
                original[original.Length - 1 - suffix] == edited[edited.Length - 1 - suffix])
                suffix++;

            int changedLength = edited.Length - prefix - suffix;
            if (changedLength > 0)
                return (prefix, changedLength);

            // A deletion has no new bytes to select. Highlight the surviving byte
            // immediately at the deletion boundary (or the preceding final byte).
            if (original.Length != edited.Length && edited.Length > 0)
                return (Math.Min(prefix, edited.Length - 1), 1);

            return null;
        }
        catch
        {
            // The normal apply path reports malformed hex with its detailed error.
            return null;
        }
    }

    private void HighlightAppliedLogicRange(int chunkOffset, int byteLength)
    {
        if (DataModel.AiDocument == null || byteLength <= 0 ||
            AiStatementList.SelectedItems == null || AiInstructionList.SelectedItems == null)
            return;

        int codeStart = DataModel.AiDocument.ScriptCodeOffset;
        int codeEnd = codeStart + DataModel.AiDocument.ScriptCodeLength;
        int changedStart = Math.Max(chunkOffset, codeStart);
        int changedEnd = Math.Min(chunkOffset + byteLength, codeEnd);
        if (changedStart >= changedEnd) return;

        int scriptStart = changedStart - codeStart;
        int scriptEnd = changedEnd - codeStart;
        IEnumerable<AtelStatement> visibleStatements =
            AiStatementList.ItemsSource as IEnumerable<AtelStatement> ?? DataModel.AiDocument.Statements;
        IEnumerable<AtelInstruction> visibleInstructions =
            AiInstructionList.ItemsSource as IEnumerable<AtelInstruction> ?? DataModel.AiDocument.Instructions;
        AtelStatement[] displayedStatements = visibleStatements.ToArray();
        AtelInstruction[] displayedInstructions = visibleInstructions.ToArray();
        AtelStatement[] statements = displayedStatements
            .Where(statement => statement.Offset < scriptEnd &&
                statement.Offset + statement.ByteLength > scriptStart)
            .ToArray();
        AtelInstruction[] instructions = displayedInstructions
            .Where(instruction => instruction.Offset < scriptEnd &&
                instruction.Offset + instruction.Bytes.Length > scriptStart)
            .ToArray();
        foreach (AtelInstruction instruction in instructions)
            instruction.SetChangeSemanticToken(GetChangedOperandToken(instruction));
        foreach (AtelStatement statement in statements)
        {
            string? token = instructions
                .Where(instruction => instruction.Offset >= statement.Offset &&
                    instruction.Offset < statement.Offset + statement.ByteLength)
                .Select(GetChangedOperandToken)
                .FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate) &&
                    statement.Translation.Contains(candidate, StringComparison.Ordinal));
            statement.SetChangeTranslationToken(token);
        }

        _synchronizingStatementSelection = true;
        _synchronizingInstructionSelection = true;
        try
        {
            // Recreate the scoped rows after assigning the split token. This avoids
            // selected-item template recycling retaining the pre-change text runs.
            AiStatementList.ItemsSource = null;
            AiStatementList.ItemsSource = displayedStatements;
            AiInstructionList.ItemsSource = null;
            AiInstructionList.ItemsSource = displayedInstructions;

            AiStatementList.SelectedItems.Clear();
            foreach (AtelStatement statement in statements)
                AiStatementList.SelectedItems.Add(statement);

            AiInstructionList.SelectedItems.Clear();
            foreach (AtelInstruction instruction in instructions)
                AiInstructionList.SelectedItems.Add(instruction);
        }
        finally
        {
            _synchronizingStatementSelection = false;
            _synchronizingInstructionSelection = false;
        }

        if (statements.Length > 0) AiStatementList.ScrollIntoView(statements[0]);
        if (instructions.Length > 0) AiInstructionList.ScrollIntoView(instructions[0]);
    }

    private static string? GetChangedOperandToken(AtelInstruction instruction)
    {
        if (!instruction.HasOperand) return null;
        string bracketed = $"[0x{instruction.Operand:X4}]";
        return instruction.CompactSemanticDisplay.Contains(bracketed, StringComparison.Ordinal)
            ? bracketed
            : $"0x{instruction.Operand:X4}";
    }

    private void AiHexText_TextChanged(object? sender, TextChangedEventArgs e)
    {
		ClearJumpDestinationHighlight();
		ClearChangeHexHighlights();
        string validatedHex = DataModel.AiDocument?.ToHexEditorText() ?? "";
        bool dirty = !string.Equals(AiHexText.Text ?? "", validatedHex, StringComparison.Ordinal);
        AiApplyHexButton.IsEnabled = dirty;
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
        if (!_restoringRejectedAiHex && _rejectedAiHexDraft != null)
        {
            _rejectedAiHexDraft = null;
            AiRestoreRejectedHexButton.IsVisible = false;
        }
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
        AiReferenceTypeEditor.IsVisible = false;
        AiFloatEditor.IsVisible = false;
		AiGroupEditorPanel.Children.Clear();
		AiGroupEditorPanel.IsVisible = false;
		AiGroupApplyButton.IsVisible = false;
		AiWorkerEditorPanel.IsVisible = false;
		AiInstructionJumpButton.IsVisible = false;
		_activeJumpInstruction = null;
        AiStatusText.Text = "The Battle Script hex has unvalidated manual changes. Apply Hex Changes to rebuild Battle Logic and Script Instructions.";
    }

	private void AiHexText_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
	{
		ClearChangeHexHighlights();
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
            AiEditSnapshot before = CaptureAiEditSnapshot();
            DataModel.RecordAiUndoCheckpoint("Revert");
            DataModel.RestoreOriginalAiAndSave();
            RefreshAfterRevert(false);
            RefreshNavigationAfterDocumentChange(preferredWorkerIndex: before.WorkerIndex,
                preferredFunctionIndex: before.FunctionIndex);
            HighlightRestoredAiChange(before.Bytes, before.ScriptCode);
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
        byte[] beforeBytes = DataModel.AiDocument?.Bytes.ToArray() ?? [];
        byte[] beforeCode = GetCurrentScriptCodeBytes();
        int workerIndex = _selectedWorkerIndex;
        int functionIndex = _selectedFunctionIndex;
        try
        {
            _restoringAiHistory = true;
            DataModel.UndoLastAiChange();
            RefreshAfterRevert(false, preserveClipboard: true);
            RefreshNavigationAfterDocumentChange(preferredWorkerIndex: workerIndex,
                preferredFunctionIndex: functionIndex);
            HighlightRestoredAiChange(beforeBytes, beforeCode);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex) { AiStatusText.Text = "ERROR: " + ex.Message; }
        finally { _restoringAiHistory = false; }
    }

    private void Button_RedoAi(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        byte[] beforeBytes = DataModel.AiDocument?.Bytes.ToArray() ?? [];
        byte[] beforeCode = GetCurrentScriptCodeBytes();
        int workerIndex = _selectedWorkerIndex;
        int functionIndex = _selectedFunctionIndex;
        try
        {
            _restoringAiHistory = true;
            DataModel.RedoLastAiChange();
            RefreshAfterRevert(false, preserveClipboard: true);
            RefreshNavigationAfterDocumentChange(preferredWorkerIndex: workerIndex,
                preferredFunctionIndex: functionIndex);
            HighlightRestoredAiChange(beforeBytes, beforeCode);
        }
        catch (Exception ex) { AiStatusText.Text = "ERROR: " + ex.Message; return; }
        finally { _restoringAiHistory = false; }

        AiStatusText.Text = DataModel.AiStatus;
    }

    private byte[] GetCurrentScriptCodeBytes()
    {
        if (DataModel.AiDocument == null) return [];
        return DataModel.AiDocument.Bytes
            .Skip(DataModel.AiDocument.ScriptCodeOffset)
            .Take(DataModel.AiDocument.ScriptCodeLength)
            .ToArray();
    }

    private AiEditSnapshot CaptureAiEditSnapshot() =>
        new(DataModel.AiDocument?.Bytes.ToArray() ?? [], GetCurrentScriptCodeBytes(),
            _selectedWorkerIndex, _selectedFunctionIndex);

    private void CompleteAiEdit(AiEditSnapshot before, int? preferredJumpIndex = null,
        bool neonMintRawChange = false)
    {
        AiHexText.Text = DataModel.AiHex;
        AiSummaryText.Text = DataModel.AiSummary;
        _aiHexIsDirty = false;
        AiApplyHexButton.IsEnabled = false;
        AiInstructionList.IsEnabled = true;
        AiStatementList.IsEnabled = true;
        AiInstructionList.ItemsSource = DataModel.AiInstructions;
        AiStatementList.ItemsSource = DataModel.AiStatements;
        RefreshNavigationAfterDocumentChange(preferredJumpIndex,
            before.WorkerIndex, before.FunctionIndex);
        HighlightRestoredAiChange(before.Bytes, before.ScriptCode, neonMintRawChange);
    }

    private void HighlightRestoredAiChange(byte[] beforeBytes, byte[] beforeCode,
        bool neonMintRawChange = false)
    {
        if (DataModel.AiDocument == null) return;

        byte[] restoredBytes = DataModel.AiDocument.Bytes.ToArray();
        byte[] restoredCode = GetCurrentScriptCodeBytes();
        (int Offset, int Length)? rawRange = GetChangedByteRange(beforeBytes, restoredBytes);
        (int Offset, int Length)? codeRange = GetChangedByteRange(beforeCode, restoredCode);

        if (codeRange.HasValue)
        {
            int chunkOffset = DataModel.AiDocument.ScriptCodeOffset + codeRange.Value.Offset;
            SelectAiHexRange(chunkOffset, codeRange.Value.Length);
            ShowChangeHexHighlights(chunkOffset, codeRange.Value.Length);
            HighlightAppliedLogicRange(chunkOffset, codeRange.Value.Length);
        }
        else if (rawRange.HasValue)
        {
            ushort[] changedFloatIndices = GetFloatIndicesForRawRange(
                rawRange.Value.Offset, rawRange.Value.Length);
            bool isFloatChange = changedFloatIndices.Length > 0;
            SelectAiHexRange(rawRange.Value.Offset, rawRange.Value.Length,
                neonMintRawChange || isFloatChange);
            if (isFloatChange)
            {
                int preferredOffset = DataModel.LastUndoneScriptOffset ??
                    _selectedInstruction?.Offset ?? -1;
                HighlightFloatReferences(changedFloatIndices, preferredOffset);
            }
        }
        else
        {
            RestoreUndoSelection();
        }
    }

    private static (int Offset, int Length)? GetChangedByteRange(byte[] before, byte[] after)
        => AtelByteRange.FindChangedRange(before, after);

    private ushort[] GetFloatIndicesForRawRange(int rawOffset, int rawLength)
    {
        if (DataModel.AiDocument == null || rawLength <= 0) return [];
        int rawEnd = checked(rawOffset + rawLength);
        return DataModel.AiDocument.Workers
            .SelectMany(worker => worker.FloatConstantBits.Select((_, index) =>
                (Index: index, Offset: worker.FloatConstantOffset + index * 4)))
            .Where(item => item.Offset < rawEnd && item.Offset + 4 > rawOffset)
            .Select(item => checked((ushort)item.Index))
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
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

    public async System.Threading.Tasks.Task RestoreOriginalAsync(Window owner)
    {
        ClearValidationResult();
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

    private void RefreshAfterRevert(bool refreshWholeMonster, bool preserveClipboard = false)
    {
        if (refreshWholeMonster)
        {
            DataContext = null;
            DataContext = DataModel;
        }
        if (!preserveClipboard)
        {
            _battleScriptClipboard = null;
            AiCopiedStatementText.Text = "";
            AiCopiedStatementText.IsVisible = false;
        }
        else
        {
            AiCopiedStatementText.IsVisible = _battleScriptClipboard != null &&
                AiHideStatements.IsChecked != true;
        }
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
        AiReferenceTypeEditor.IsVisible = false;
        AiFloatEditor.IsVisible = false;
        AiGroupEditorPanel.IsVisible = false;
        AiGroupApplyButton.IsVisible = false;
    }

    private void AiInstruction_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingInstructionSelection) return;
		ClearChangeHexHighlights();
		AtelInstruction[] selected = AiInstructionList.SelectedItems?.OfType<AtelInstruction>()
			.OrderBy(item => item.Offset).ToArray() ?? [];
		if (selected.Length == 0) return;
		if (selected.Length == 1)
		{
			ActivateInstructionEditor(selected[0]);
			return;
		}
		SelectCompleteStatementRangeForInstructions(selected[0].Offset,
			selected[^1].Offset + selected[^1].Bytes.Length);
	}

	private void AiInstruction_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
	{
		AtelInstruction? instruction = (e.Source as Control)?.GetVisualAncestors()
			.OfType<ListBoxItem>().FirstOrDefault()?.DataContext as AtelInstruction;
		if (instruction == null) return;
		ClearChangeHexHighlights();
		// SelectionChanged normally activated this row before Tapped fires. Only
		// activate here when no selection event handled the tap.
		if (AiInstructionList.SelectedItems?.Count <= 1 &&
			(_logicSelectionOwner != AiLogicSelectionOwner.Instruction ||
			 !ReferenceEquals(_selectedInstruction, instruction)))
			ActivateInstructionEditor(instruction);
	}

	private void ActivateInstructionEditor(AtelInstruction instruction)
	{
		FocusSelectionEditor();
		// Ordinary instruction editing should not retain the worker navigation row.
		// Destination-picking mode keeps only its dedicated Apply/Cancel row visible.
		AiWorkerEditorPanel.IsVisible = _choosingWorkerJumpDestination;
		_logicSelectionOwner = AiLogicSelectionOwner.Instruction;
		UpdateWorkerJumpActionVisibility();
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
			ShowSelectionInfo($"Selected script offset 0x{_selectedInstruction.Offset:X4}; highlighted Battle Script offset 0x{chunkOffset:X}.");
			PreviewWorkerJumpDestination(_selectedInstruction.Offset, _selectedInstruction.OpcodeName);
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
        if (_synchronizingStatementSelection || DataModel.AiDocument == null ||
			AiInstructionList.SelectedItems == null) return;
		ClearChangeHexHighlights();
		AtelStatement[] selected = GetContiguousSelectedStatements();
		if (selected.Length == 0) return;
		if (selected.Length == 1)
			ActivateStatementEditor(selected[0]);
		else
			ShowStatementRangeSelection(selected);
    }

    private void AiStatement_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
		ClearChangeHexHighlights();
        if (_logicSelectionOwner != AiLogicSelectionOwner.Statement &&
			AiStatementList.SelectedItems?.Count <= 1 &&
			AiStatementList.SelectedItem is AtelStatement statement)
            ActivateStatementEditor(statement);
    }

	private AtelStatement[] GetContiguousSelectedStatements()
	{
		AtelStatement[] displayed = (AiStatementList.ItemsSource as IEnumerable<AtelStatement>)?.ToArray() ?? [];
		HashSet<AtelStatement> selected = AiStatementList.SelectedItems?.OfType<AtelStatement>().ToHashSet() ?? [];
		int[] indices = displayed.Select((item, index) => (item, index))
			.Where(pair => selected.Contains(pair.item)).Select(pair => pair.index).ToArray();
		if (indices.Length == 0) return [];
		AtelStatement[] range = displayed[indices.Min()..(indices.Max() + 1)];
		if (range.Length != selected.Count)
		{
			_synchronizingStatementSelection = true;
			try
			{
				AiStatementList.SelectedItems?.Clear();
				foreach (AtelStatement statement in range)
					AiStatementList.SelectedItems?.Add(statement);
			}
			finally { _synchronizingStatementSelection = false; }
		}
		return range;
	}

	private void SelectCompleteStatementRangeForInstructions(int start, int end)
	{
		AtelStatement[] displayed = (AiStatementList.ItemsSource as IEnumerable<AtelStatement>)?.ToArray() ?? [];
		AtelStatement[] statements = displayed
			.Where(item => item.Offset < end && item.Offset + item.ByteLength > start).ToArray();
		if (statements.Length == 0) return;
		_synchronizingStatementSelection = true;
		try
		{
			AiStatementList.SelectedItems?.Clear();
			foreach (AtelStatement statement in statements)
				AiStatementList.SelectedItems?.Add(statement);
		}
		finally { _synchronizingStatementSelection = false; }
		ShowStatementRangeSelection(statements);
	}

	private void ShowStatementRangeSelection(AtelStatement[] statements)
	{
		if (DataModel.AiDocument == null || AiInstructionList.SelectedItems == null ||
			statements.Length == 0) return;
		AtelInstruction[] instructions = statements.SelectMany(item => item.Instructions).ToArray();
		_synchronizingInstructionSelection = true;
		try
		{
			AiInstructionList.SelectedItems.Clear();
			foreach (AtelInstruction instruction in instructions)
				AiInstructionList.SelectedItems.Add(instruction);
		}
		finally { _synchronizingInstructionSelection = false; }

		_selectedInstruction = null;
		AiManualOperandEditor.IsVisible = false;
		AiGroupEditorPanel.IsVisible = false;
		AiGroupApplyButton.IsVisible = false;
		int start = statements[0].Offset;
		int end = statements[^1].Offset + statements[^1].ByteLength;
		SelectAiHexRange(DataModel.AiDocument.ScriptCodeOffset + start, end - start);
		AiSelectedInstructionText.Text =
			$"Range • script 0x{start:X4}–0x{end:X4} • {statements.Length} Battle Logic statement(s) • " +
			$"{instructions.Length} instruction(s) • {end - start} bytes";
		ShowSelectionInfo("Contiguous Battle Logic range selected. Click Copy to place the complete range on the Script Clipboard.");
	}

    private async void Button_CopyStatement(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (DataModel.AiDocument == null)
        {
            AiStatusText.Text = "No Battle Script is loaded.";
            return;
        }
        AtelStatement[] statements = GetContiguousSelectedStatements();
        if (statements.Length == 0)
        {
            AiStatusText.Text = "Select one or more contiguous Battle Logic statements to copy.";
            return;
        }
        int start = statements[0].Offset;
        int end = statements[^1].Offset + statements[^1].ByteLength;
        AtelInstruction[] rangeInstructions = statements.SelectMany(item => item.Instructions).ToArray();
        bool omitsProtectedReturn = false;
        AtelInstruction? protectedReturn =
            rangeInstructions.FirstOrDefault(instruction => instruction.Opcode == 0x3C);
        if (protectedReturn != null)
        {
            bool canCopyEditablePrefix = statements.Length == 1 &&
                ReferenceEquals(protectedReturn, rangeInstructions[^1]) &&
                protectedReturn.Offset > start;
            if (!canCopyEditablePrefix)
            {
                ShowMessageError("RETURN cannot be copied.",
                    "RETURN (3C) ends a function and is protected from being placed on the Script Clipboard. This selection cannot be reduced to one continuous editable range. Select a group with editable instructions before its final RETURN.");
                return;
            }
            omitsProtectedReturn = true;
            end = protectedReturn.Offset;
            rangeInstructions = rangeInstructions
                .Where(instruction => instruction.Offset < end).ToArray();
        }
        int workerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(statements[0].Offset);
        bool crossesWorkerBoundary = statements.Any(item =>
            DataModel.AiDocument.GetWorkerIndexForCodeOffset(item.Offset) != workerIndex);
        AtelWorker worker = DataModel.AiDocument.Workers.First(item => item.Index == workerIndex);
        int functionIndex = GetWorkerFunctionIndex(worker, statements[0].Offset);
        bool crossesFunctionBoundary =
            statements.Any(item => GetWorkerFunctionIndex(worker, item.Offset) != functionIndex);
        AtelInstruction[] rangeBranches = rangeInstructions
            .Where(instruction => instruction.Opcode is 0xB0 or 0xB1 or 0xB2 or 0xD5 or 0xD6 or 0xD7)
            .ToArray();
        bool isolatedBranch = statements.Length == 1 &&
            rangeInstructions.Any(instruction => instruction.Opcode is 0xB0 or 0xB1 or 0xB2);
        ushort[] invalidJumpIndices = rangeBranches
            .Where(instruction =>
            {
                int ownerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(instruction.Offset);
                AtelWorker owner = DataModel.AiDocument.Workers.First(item => item.Index == ownerIndex);
                return instruction.Operand >= owner.JumpCount;
            })
            .Select(instruction => instruction.Operand)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if ((isolatedBranch || crossesFunctionBoundary || crossesWorkerBoundary ||
             invalidJumpIndices.Length > 0) &&
            !_suppressUnsafePasteWarning)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var risks = new List<string>();
            if (isolatedBranch)
                risks.Add("Control flow: this statement redirects execution and its related destination logic may not be included.");
            if (crossesFunctionBoundary)
                risks.Add("Function boundary: this range includes logic from more than one function.");
            if (crossesWorkerBoundary)
                risks.Add("Worker boundary: this range includes logic owned by more than one worker. " +
                    "Worker-specific references will require review in the destination.");
            if (invalidJumpIndices.Length > 0)
                risks.Add($"Missing source jump points: {string.Join(", ", invalidJumpIndices.Select(value => $"j{value:X2}"))}");
            UnsafePasteConfirmationResult result = await AiUnsafePasteConfirmationWindow.Show(owner,
                string.Join(Environment.NewLine, risks), "Copy Anyway");
            if (!result.Confirmed)
            {
                ShowMessageWarning("Copy cancelled. The Script Clipboard was not changed.");
                return;
            }
            if (result.DoNotShowAgain)
            {
                _suppressUnsafePasteWarning = true;
                SaveLogicVisibilityPreferences();
            }
        }
        byte[] rangeBytes = DataModel.AiDocument.Bytes
            .Skip(DataModel.AiDocument.ScriptCodeOffset + start).Take(end - start).ToArray();
        BattleScriptClipboardStatement[] copiedStatements = omitsProtectedReturn
            ?
            [
                new BattleScriptClipboardStatement(start, end - start, rangeBytes,
                    "Editable instructions before protected RETURN",
                    rangeInstructions.Select(instruction => new BattleScriptClipboardInstruction(
                        instruction.Offset, instruction.Opcode, instruction.Bytes.ToArray(),
                        instruction.OpcodeName, instruction.Translation,
                        instruction.HasOperand ? instruction.Operand : null)).ToArray())
            ]
            : statements.Select(statement =>
                new BattleScriptClipboardStatement(statement.Offset, statement.ByteLength,
                    DataModel.AiDocument.GetStatementBytes(statement.Offset), statement.Translation,
                    statement.Instructions.Select(instruction => new BattleScriptClipboardInstruction(
                        instruction.Offset, instruction.Opcode, instruction.Bytes.ToArray(),
                        instruction.OpcodeName, instruction.Translation,
                        instruction.HasOperand ? instruction.Operand : null)).ToArray()))
                .ToArray();
        BattleScriptClipboardBranch[] branches = rangeBranches
            .Select(instruction =>
            {
                int ownerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(instruction.Offset);
                AtelWorker owner = DataModel.AiDocument.Workers.First(item => item.Index == ownerIndex);
                int destinationOffset = instruction.Operand < owner.JumpCount
                    ? owner.JumpOffsets[instruction.Operand]
                    : -1;
                return new BattleScriptClipboardBranch(instruction.Offset,
                    instruction.Operand, destinationOffset,
                    destinationOffset >= start && destinationOffset < end);
            })
            .ToArray();
        BattleScriptClipboardFloat[] floats = rangeInstructions
            .Where(instruction => instruction.Opcode == 0xAF &&
                DataModel.AiDocument.TryGetFloatConstant(instruction.Operand, out _))
            .Select(instruction =>
            {
                DataModel.AiDocument.TryGetFloatConstant(instruction.Operand, out float value);
                return new BattleScriptClipboardFloat(instruction.Offset, instruction.Operand,
                    BitConverter.SingleToInt32Bits(value));
            })
            .ToArray();
        _battleScriptClipboard = new BattleScriptClipboard(DataModel.MonsterPath, workerIndex,
            crossesWorkerBoundary ? -1 : functionIndex, start, end, rangeBytes, copiedStatements, branches, floats,
            rangeInstructions[^1].Opcode is not (0x34 or 0x3C or 0x40 or 0x54 or 0xB0 or 0xB1 or 0xB2));
        RefreshScriptClipboardDisplay();
        AiStatusText.Text = omitsProtectedReturn
            ? $"Copied {rangeInstructions.Length} editable instruction(s) before protected RETURN ({end - start} bytes)."
            : statements.Length == 1
            ? $"Copied complete statement at script offset 0x{start:X4} ({end - start} bytes)."
            : $"Copied {statements.Length} contiguous Battle Logic statements ({rangeInstructions.Length} instructions, {end - start} bytes).";
    }

    private static int GetWorkerFunctionIndex(AtelWorker worker, int scriptOffset) =>
        worker.FunctionOffsets.Select((offset, index) => (offset, index))
            .Where(item => item.offset <= scriptOffset)
            .OrderByDescending(item => item.offset)
            .Select(item => item.index)
            .FirstOrDefault(-1);

    private async void Button_InsertStatementAfter(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (_battleScriptClipboard == null)
        {
            ShowMessageError("Nothing has been copied yet. Select one or more Battle Logic rows, then click Copy.");
            return;
        }
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement destination)
        {
            ShowMessageError("Choose where to insert the copied logic. Select a Battle Logic row, then click Insert After.");
            return;
        }
        bool insertsAfterTerminator =
            destination.Instructions[^1].Opcode is 0x34 or 0x3C or 0x40 or 0x54 or 0xB0;
        string? insertionWarning = !_battleScriptClipboard.FallsThrough || insertsAfterTerminator
            ? insertsAfterTerminator
                ? "The selected row terminates or redirects execution. The inserted logic will be unreachable unless control flow is manually changed."
                : "The copied logic changes where execution continues. Logic after the inserted section may become unreachable."
            : null;
        if (!await ConfirmUnresolvedClipboardReferences(destination, insertionWarning)) return;
        try
        {
            int insertionOffset = destination.Offset + destination.ByteLength;
            AiEditSnapshot before = CaptureAiEditSnapshot();
            string unit = _battleScriptClipboard.Statements.Count == 1 ? "statement" : "range";
            DataModel.RecordAiUndoCheckpoint($"insert Battle Logic {unit} after", "Group", insertionOffset);
            int destinationWorkerIndex =
                DataModel.AiDocument.GetWorkerIndexForCodeOffset(destination.Offset);
            InsertCopiedLogic(insertionOffset, destinationWorkerIndex);
            CompleteAiEdit(before);
            ShowMessageSuccess("The copied logic was inserted successfully.", DataModel.AiStatus);
        }
        catch (Exception ex)
        {
            ShowMessageError(FriendlyEditError(ex.Message), ex.Message);
        }
    }

    private async void Button_InsertStatementBefore(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (_battleScriptClipboard == null)
        {
            ShowMessageError("Nothing has been copied yet. Select one or more Battle Logic rows, then click Copy.");
            return;
        }
        if (DataModel.AiDocument == null || AiStatementList.SelectedItem is not AtelStatement destination)
        {
            ShowMessageError("Choose where to insert the copied logic. Select a Battle Logic row, then click Insert Before.");
            return;
        }
        string? insertionWarning = _battleScriptClipboard.FallsThrough
            ? null
            : "The copied logic changes where execution continues. Logic after the inserted section may become unreachable.";
        if (!await ConfirmUnresolvedClipboardReferences(destination, insertionWarning)) return;
        try
        {
            int insertionOffset = destination.Offset;
            AiEditSnapshot before = CaptureAiEditSnapshot();
            string unit = _battleScriptClipboard.Statements.Count == 1 ? "statement" : "range";
            DataModel.RecordAiUndoCheckpoint($"insert Battle Logic {unit} before", "Group", insertionOffset);
            int destinationWorkerIndex =
                DataModel.AiDocument.GetWorkerIndexForCodeOffset(destination.Offset);
            InsertCopiedLogic(insertionOffset, destinationWorkerIndex,
                preserveFunctionEntryAtInsertion: true);
            CompleteAiEdit(before);
            ShowMessageSuccess("The copied logic was inserted successfully.", DataModel.AiStatus);
        }
        catch (Exception ex)
        {
            ShowMessageError(FriendlyEditError(ex.Message), ex.Message);
        }
    }

    private async void Button_PasteStatement(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearValidationResult();
        if (_battleScriptClipboard == null)
        {
			ShowMessageError("Nothing has been copied yet. Select one or more Battle Logic rows, then click Copy.");
            return;
        }
        if (DataModel.AiDocument == null)
        {
			ShowMessageError("No Battle Script is loaded. Open a monster with Battle Logic before pasting.");
            return;
        }
        AtelStatement[] destinations = GetContiguousSelectedStatements();
        if (destinations.Length == 0)
        {
			ShowMessageError("Choose what to replace. Select one or more continuous Battle Logic rows, then click Paste.");
            return;
        }
        AtelStatement destination = destinations[0];
        if (!CanReplaceSelectionAt(destinations)) return;
        AtelInstruction destinationLastInstruction = destinations[^1].Instructions[^1];
        bool destinationFallsThrough =
            destinationLastInstruction.Opcode is not (0x34 or 0x3C or 0x40 or 0x54 or 0xB0 or 0xB1 or 0xB2);
        string? controlFlowWarning = destinationFallsThrough == _battleScriptClipboard.FallsThrough
            ? null
            : "The copied logic and selected destination end differently. This replacement will change where execution continues.";
        int destinationStart = destinations[0].Offset;
        int destinationEndPreview = destinations[^1].Offset + destinations[^1].ByteLength;
        bool containsProtectedEntries = DataModel.AiDocument.Workers.Any(worker =>
            worker.FunctionOffsets.Any(offset => offset > destinationStart && offset < destinationEndPreview) ||
            worker.JumpOffsets.Any(offset => offset > destinationStart && offset < destinationEndPreview));
        if (containsProtectedEntries)
            controlFlowWarning = string.Join(" ", new[]
            {
                controlFlowWarning,
                "Function or jump entries point inside the selected destination. They will be remapped to corresponding instruction positions inside the replacement."
            }.Where(text => !string.IsNullOrEmpty(text)));
        if (!await ConfirmUnresolvedClipboardReferences(destination, controlFlowWarning)) return;
        try
        {
            int destinationOffset = destinations[0].Offset;
            int destinationEnd = destinations[^1].Offset + destinations[^1].ByteLength;
            AiEditSnapshot before = CaptureAiEditSnapshot();
            DataModel.RecordAiUndoCheckpoint("replace Battle Logic selection", "Group", destinationOffset);
            AtelRangeBranch[] internalBranches = GetClipboardInternalBranches();
            DataModel.ReplaceStatementRange(destinationOffset, destinationEnd,
                _battleScriptClipboard.Bytes, _battleScriptClipboard.StartOffset,
                DataModel.AiDocument.GetWorkerIndexForCodeOffset(destinationOffset), internalBranches,
                GetClipboardFloatReferences(), preserveUnresolvedFloatIndices: true,
                allowUnsafeDestinationEntries: containsProtectedEntries,
                preserveUnresolvedBranchIndices: true);
            CompleteAiEdit(before);
            ShowMessageSuccess("The selected Battle Logic was replaced successfully.", DataModel.AiStatus);
        }
        catch (Exception ex)
        {
			ShowMessageError(FriendlyEditError(ex.Message), ex.Message);
        }
    }

    private bool CanReplaceSelectionAt(AtelStatement[] destinations)
    {
        if (_battleScriptClipboard == null || DataModel.AiDocument == null ||
            destinations.Length == 0) return false;
        if (_battleScriptClipboard.Statements.SelectMany(statement => statement.Instructions)
            .Any(instruction => instruction.Opcode == 0x3C))
        {
            ShowMessageError("RETURN cannot be pasted.",
                "The Script Clipboard contains protected RETURN (3C) data. Copy the source again so the clipboard contains only editable instructions.");
            return false;
        }
        if (destinations.SelectMany(statement => statement.Instructions)
            .Any(instruction => instruction.Opcode == 0x3C))
        {
            ShowMessageError("RETURN cannot be replaced.",
                "The selected Battle Logic contains protected RETURN (3C). Delete or copy the editable instructions around RETURN instead; the RETURN instruction itself must remain in place.");
            return false;
        }
        int workerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(destinations[0].Offset);
        return true;
    }

    private async System.Threading.Tasks.Task<bool> ConfirmUnresolvedClipboardReferences(
        AtelStatement destination, string? controlFlowWarning = null)
    {
        if (_battleScriptClipboard == null || DataModel.AiDocument == null) return true;

        int destinationWorkerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(destination.Offset);
        AtelWorker destinationWorker = DataModel.AiDocument.Workers
            .First(item => item.Index == destinationWorkerIndex);
        int destinationFunctionIndex = GetWorkerFunctionIndex(destinationWorker, destination.Offset);
        bool originalFunction = IsClipboardFromCurrentMonster() &&
            destinationWorkerIndex == _battleScriptClipboard.SourceWorkerIndex &&
            destinationFunctionIndex == _battleScriptClipboard.SourceFunctionIndex;

        ushort[] missingJumps = originalFunction
            ? []
            : _battleScriptClipboard.Branches
                .Where(branch => !branch.DestinationInsideRange &&
                                 branch.JumpIndex >= destinationWorker.JumpCount)
                .Select(branch => branch.JumpIndex)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

        ushort[] missingVariables = _battleScriptClipboard.Statements
            .SelectMany(statement => statement.Instructions)
            .Where(instruction => instruction.Operand.HasValue &&
                                  instruction.Opcode is 0x9F or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 &&
                                  instruction.Operand.Value >= destinationWorker.VariableCount)
            .Select(instruction => instruction.Operand!.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        // External branches retain their source jump-table operand. Even when the destination
        // has an entry at that index, moving between functions or monsters requires review.
        ushort[] externalJumpsNeedingReview = originalFunction
            ? []
            : _battleScriptClipboard.Branches
                .Where(branch => !branch.DestinationInsideRange)
                .Select(branch => branch.JumpIndex)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

        bool internalBranchesNeedPortableRemap = !originalFunction &&
            _battleScriptClipboard.Branches.Any(branch => branch.DestinationInsideRange);
        float[] unresolvedFloats = GetUnresolvedClipboardFloatValues();
        if (externalJumpsNeedingReview.Length == 0 && missingVariables.Length == 0 &&
            !internalBranchesNeedPortableRemap && unresolvedFloats.Length == 0 &&
            string.IsNullOrEmpty(controlFlowWarning)) return true;
        if (_suppressUnsafePasteWarning) return true;
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;

        var details = new List<string>();
        if (externalJumpsNeedingReview.Length > 0)
        {
            string label = missingJumps.Length == externalJumpsNeedingReview.Length
                ? "Missing jump points"
                : "External jump points to verify";
            details.Add($"{label}: {string.Join(", ", externalJumpsNeedingReview.Select(value => $"j{value:X2}"))}");
        }
        if (internalBranchesNeedPortableRemap)
            details.Add("Internal jump points: the editor will create destination jump entries when possible. " +
                "If the destination jump table cannot be expanded, the original jump operands will be preserved for manual correction.");
        if (missingVariables.Length > 0)
            details.Add($"Missing variables: {string.Join(", ", missingVariables.Select(value => $"variable[0x{value:X4}]"))}");
        if (unresolvedFloats.Length > 0)
            details.Add($"Unmapped numeric values (original indices will be preserved): " +
                $"{string.Join(", ", unresolvedFloats.Select(value => value.ToString(CultureInfo.InvariantCulture)))}");
        if (!string.IsNullOrEmpty(controlFlowWarning))
            details.Add($"Control flow: {controlFlowWarning}");
        details.Add($"Destination: w{destinationWorkerIndex:X2}:f{destinationFunctionIndex:X2}");

        UnsafePasteConfirmationResult result =
            await AiUnsafePasteConfirmationWindow.Show(owner, string.Join(Environment.NewLine, details));
        if (!result.Confirmed)
        {
            ShowMessageWarning("Paste cancelled. No changes were made.");
            return false;
        }
        if (result.DoNotShowAgain)
        {
            _suppressUnsafePasteWarning = true;
            SaveLogicVisibilityPreferences();
        }
        return true;
    }

    private float[] GetUnresolvedClipboardFloatValues()
    {
        if (_battleScriptClipboard == null || DataModel.AiDocument == null ||
            _battleScriptClipboard.Floats.Count == 0) return [];

        int floatCount = DataModel.AiDocument.Workers.Count == 0
            ? 0
            : DataModel.AiDocument.Workers.Min(worker => worker.FloatConstantBits.Count);
        AtelWorker? firstWorker = DataModel.AiDocument.Workers.FirstOrDefault();
        var usedIndices = DataModel.AiDocument.Instructions
            .Where(instruction => instruction.Opcode == 0xAF)
            .Select(instruction => instruction.Operand)
            .ToHashSet();
        var unresolved = new List<float>();

        foreach (BattleScriptClipboardFloat reference in _battleScriptClipboard.Floats
                     .GroupBy(item => item.ValueBits).Select(group => group.First()))
        {
            bool matching = firstWorker != null &&
                firstWorker.FloatConstantBits.Any(bits => bits == reference.ValueBits);
            if (matching) continue;
            int unused = Enumerable.Range(0, floatCount)
                .FirstOrDefault(index => !usedIndices.Contains((ushort)index), -1);
            if (unused >= 0)
            {
                usedIndices.Add((ushort)unused);
                continue;
            }
            unresolved.Add(BitConverter.Int32BitsToSingle(reference.ValueBits));
        }
        return unresolved.ToArray();
    }

    private void InsertCopiedLogic(int insertionOffset, int destinationWorkerIndex,
        bool preserveFunctionEntryAtInsertion = false)
    {
        if (_battleScriptClipboard == null) return;
        EnsureClipboardContainsNoReturn();
        AtelRangeBranch[] internalBranches = GetClipboardInternalBranches();
        DataModel.InsertStatementRange(insertionOffset, _battleScriptClipboard.Bytes,
            _battleScriptClipboard.StartOffset, destinationWorkerIndex,
            internalBranches, GetClipboardFloatReferences(), preserveFunctionEntryAtInsertion,
            preserveUnresolvedFloatIndices: true, preserveUnresolvedBranchIndices: true);
    }

    private static void EnsureClipboardContainsNoReturn()
    {
        if (_battleScriptClipboard?.Statements
            .SelectMany(statement => statement.Instructions)
            .Any(instruction => instruction.Opcode == 0x3C) == true)
            throw new InvalidOperationException(
                "The Script Clipboard contains protected RETURN (3C) data and cannot be pasted. Copy the source again so only editable instructions are included.");
    }

    private bool IsClipboardFromCurrentMonster() =>
        _battleScriptClipboard != null &&
        string.Equals(Path.GetFullPath(_battleScriptClipboard.SourceMonsterPath),
            Path.GetFullPath(DataModel.MonsterPath), StringComparison.OrdinalIgnoreCase);

    private void RefreshScriptClipboardDisplay()
    {
        if (_battleScriptClipboard == null)
        {
            AiCopiedStatementText.Text = "";
            AiCopiedStatementText.IsVisible = false;
            return;
        }
        string source = IsClipboardFromCurrentMonster()
            ? _battleScriptClipboard.SourceMonsterName
            : $"{_battleScriptClipboard.SourceMonsterName} → current monster";
        string sourceScope = _battleScriptClipboard.SourceFunctionIndex < 0
            ? "multiple workers"
            : $"w{_battleScriptClipboard.SourceWorkerIndex:X2}:f{_battleScriptClipboard.SourceFunctionIndex:X2}";
        AiCopiedStatementText.Text =
            $"{source} • {sourceScope} • " +
            $"{_battleScriptClipboard.Statements.Count} logic • " +
            $"{_battleScriptClipboard.InstructionCount} instruction(s) • " +
            $"{_battleScriptClipboard.ByteLength} bytes • " +
            $"{_battleScriptClipboard.Floats.Select(item => item.ValueBits).Distinct().Count()} float value(s) • " +
            (_battleScriptClipboard.HasExternalBranches
                ? "source-function only"
                : "portable");
        AiCopiedStatementText.IsVisible = AiHideStatements.IsChecked != true;
    }

    private AtelRangeBranch[] GetClipboardInternalBranches()
    {
        if (_battleScriptClipboard == null) return [];
        return _battleScriptClipboard.Branches
            .Where(branch => branch.DestinationInsideRange)
            .Select(branch => new AtelRangeBranch(
                branch.SourceInstructionOffset - _battleScriptClipboard.StartOffset,
                branch.DestinationOffset - _battleScriptClipboard.StartOffset))
            .ToArray();
    }

    private AtelRangeFloat[] GetClipboardFloatReferences()
    {
        if (_battleScriptClipboard == null) return [];
        return _battleScriptClipboard.Floats.Select(reference => new AtelRangeFloat(
            reference.SourceInstructionOffset - _battleScriptClipboard.StartOffset,
            reference.SourceIndex, reference.ValueBits)).ToArray();
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
            bool preservesReturn = statement.Instructions.Any(instruction => instruction.Opcode == 0x3C);
            bool removesJumpReference = statement.Instructions.Any(instruction =>
                instruction.Opcode is 0xB0 or 0xB1 or 0xB2 or 0xD5 or 0xD6 or 0xD7);
            string description = $"Statement at script offset 0x{statement.Offset:X4} ({statement.ByteLength} bytes)\n{statement.Display}";
            DeleteStatementConfirmationResult result = await AiDeleteStatementConfirmationWindow.Show(owner,
                (preservesReturn
                    ? "This group contains a protected RETURN (3C). Only the editable instructions before RETURN will be deleted; RETURN will remain in place. "
                    : "This will remove the complete selected statement. ") +
                (removesJumpReference
                    ? "The selected jump reference will be removed, but its jump-table destination will be retained unchanged. "
                    : "") +
                "Every later function and jump-table offset will be rebuilt. Function entries, incoming jump destinations, and protected terminators are still refused.\n\n" +
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
            AiEditSnapshot before = CaptureAiEditSnapshot();
            DataModel.RecordAiUndoCheckpoint("delete Battle Logic statement", "Group", deletedOffset);
            DataModel.DeleteStatement(deletedOffset);
            CompleteAiEdit(before);
            AiStatusText.Text = DataModel.AiStatus;
        }
		catch (Exception ex)
        {
			FocusMessages();
			ShowMessageError("The statement could not be deleted.", ex.Message);
        }
    }

	private void ActivateStatementEditor(AtelStatement statement)
    {
        if (DataModel.AiDocument == null || AiInstructionList.SelectedItems == null) return;
		FocusSelectionEditor();
		// Ordinary statement editing should not retain the worker navigation row.
		// Destination-picking mode keeps only its dedicated Apply/Cancel row visible.
		AiWorkerEditorPanel.IsVisible = _choosingWorkerJumpDestination;
		_logicSelectionOwner = AiLogicSelectionOwner.Statement;
		UpdateWorkerJumpActionVisibility();
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
            AiReferenceTypeEditor.IsVisible = false;
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
        ShowSelectionInfo($"Selected Battle Logic statement at script offset 0x{statement.Offset:X4}; highlighted {statement.ByteLength} byte(s) at Battle Script offset 0x{chunkOffset:X}.");
		PreviewWorkerJumpDestination(statement.Offset, "Battle Logic statement");
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
				Text = IsActorReferenceRole(role) ? "Actor reference:" : role + ":",
				VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
				Margin = new Avalonia.Thickness(0, 0, 0, 3)
			};
            ComboBox? options = null;
            ComboBox? referenceKind = null;
            TextBox? valueText = null;
            if (IsActorReferenceRole(role))
            {
                referenceKind = new ComboBox
                {
                    ItemsSource = ActorReferenceKinds,
                    Width = 190,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Margin = new Avalonia.Thickness(0, 0, 0, 4)
                };
                options = new ComboBox
                {
                    Width = 190,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };
                ActorReferenceKindChoice detected = DetectActorReferenceKind(instruction);
                referenceKind.SelectedItem = detected;
                PopulateActorReferenceOptions(options, detected, instruction);
                referenceKind.SelectionChanged += (_, _) =>
                {
                    if (referenceKind.SelectedItem is ActorReferenceKindChoice kind)
                        PopulateActorReferenceOptions(options, kind, instruction, selectFirst: true);
                };
            }
            else if (IsCommandRole(role))
            {
                referenceKind = new ComboBox
                {
                    ItemsSource = CommandEditorKinds,
                    Width = 190,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Margin = new Avalonia.Thickness(0, 0, 0, 4)
                };
                options = new ComboBox
                {
                    Width = 190,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };
                CommandEditorKindChoice detected = DetectCommandEditorKind(instruction);
                referenceKind.SelectedItem = detected;
                PopulateCommandOptions(options, detected, instruction);
                referenceKind.SelectionChanged += (_, _) =>
                {
                    if (referenceKind.SelectedItem is CommandEditorKindChoice kind)
                        PopulateCommandOptions(options, kind, instruction, selectFirst: true);
                };
            }
            else if (IsStatPropertyRole(role))
            {
                referenceKind = new ComboBox
                {
                    ItemsSource = StatPropertyGroups,
                    Width = 190,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Margin = new Avalonia.Thickness(0, 0, 0, 4)
                };
                options = new ComboBox
                {
                    Width = 190,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                };
                StatPropertyGroupChoice detected = DetectStatPropertyGroup(instruction);
                referenceKind.SelectedItem = detected;
                PopulateStatPropertyOptions(options, detected, instruction);
                referenceKind.SelectionChanged += (_, _) =>
                {
                    if (referenceKind.SelectedItem is StatPropertyGroupChoice group)
                        PopulateStatPropertyOptions(options, group, instruction, selectFirst: true);
                };
            }
            else if (choices.Length > 0)
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
			if (referenceKind != null) field.Children.Add(referenceKind);
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
            _groupOperandEditors.Add(new GroupOperandEditor(instruction.Offset, role, options, valueText, referenceKind));
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
            _groupOperandEditors.Add(new GroupOperandEditor(first.Offset, axisLabel, null, valueText, null, linked.Key));
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
            AiEditSnapshot before = CaptureAiEditSnapshot();
            DataModel.RecordAiUndoCheckpoint("apply Battle Logic changes", "Group", statementOffset);
            var replacements = new List<AtelInstructionReplacement>();
            var changedFloatIndices = new HashSet<ushort>();
            foreach (GroupOperandEditor editor in _groupOperandEditors)
            {
                AtelInstruction current = DataModel.AiDocument.Instructions.First(instruction => instruction.Offset == editor.InstructionOffset);
                if (editor.FloatIndex.HasValue && editor.ValueText != null)
                {
                    DataModel.ApplyFloatConstant(editor.InstructionOffset, editor.FloatIndex.Value, editor.ValueText.Text ?? "");
                    changedFloatIndices.Add(editor.FloatIndex.Value);
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
            CompleteAiEdit(before, neonMintRawChange: changedFloatIndices.Count > 0);
            if (changedFloatIndices.Count > 0)
                HighlightFloatReferences(changedFloatIndices, statementOffset);
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
        bool isActorReference = IsActorReferenceRole(role);
        bool isCommand = IsCommandRole(role);
        bool isStatProperty = IsStatPropertyRole(role);
        bool isCategorized = isActorReference || isCommand || isStatProperty;
        bool hasChoices = role != null && (choices.Length > 0 || isCategorized);
        AiMeaningOptions.SelectedItem = null;
        AiMeaningOptions.ItemsSource = null;
        AiMeaningLabel.Text = isActorReference ? "Actor reference:" : role == null ? "Value:" : role + ":";
        AiMeaningLabel.IsVisible = hasChoices;
        AiMeaningOptions.IsVisible = hasChoices;
        AiReferenceTypeEditor.IsVisible = isCategorized;
        AiReferenceTypeLabel.Text = isCommand ? "Command editor:" :
            isStatProperty ? "Property group:" : "Reference type:";
        if (isActorReference)
        {
            ActorReferenceKindChoice detected = DetectActorReferenceKind(instruction);
            AiReferenceTypeOptions.ItemsSource = ActorReferenceKinds;
            AiReferenceTypeOptions.SelectedItem = detected;
            PopulateActorReferenceOptions(AiMeaningOptions, detected, instruction);
        }
        else if (isCommand)
        {
            CommandEditorKindChoice detected = DetectCommandEditorKind(instruction);
            AiReferenceTypeOptions.ItemsSource = CommandEditorKinds;
            AiReferenceTypeOptions.SelectedItem = detected;
            PopulateCommandOptions(AiMeaningOptions, detected, instruction);
        }
        else if (isStatProperty)
        {
            StatPropertyGroupChoice detected = DetectStatPropertyGroup(instruction);
            AiReferenceTypeOptions.ItemsSource = StatPropertyGroups;
            AiReferenceTypeOptions.SelectedItem = detected;
            PopulateStatPropertyOptions(AiMeaningOptions, detected, instruction);
        }
        else
        {
            AiReferenceTypeOptions.SelectedItem = null;
            AiReferenceTypeOptions.ItemsSource = null;
            AiMeaningOptions.ItemsSource = hasChoices ? choices : null;
            AiMeaningOptions.SelectedItem = hasChoices
                ? (_semanticRole == "Comparison"
                ? choices.FirstOrDefault(x => x.Opcode == instruction.Opcode)
                : choices.FirstOrDefault(x => x.Value == instruction.Operand && x.Opcode == instruction.Opcode))
                : null;
        }
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
				string display = $"[0x{index:X4}]  j{index:X2} -> offset 0x{chunkOffset:X6}";
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
				return (role, GetVariableChoices(instruction, 0x9F));
            if (role == "Value" && call.Operand is 0x7018 or 0x70AB)
            {
                int propertyArgument = call.Operand == 0x7018 ? firstArgument + 1 : firstArgument;
                if (propertyArgument >= 0 && propertyArgument < all.Length)
                {
                    ushort property = all[propertyArgument].Operand;
                    if (AtelStatProperties.BooleanProperties.Contains(property))
                        return (role, [new("False", 0x0000, instruction.Opcode), new("True", 0x0001, instruction.Opcode)]);
                    if (AtelStatProperties.CommandProperties.Contains(property))
                        return ("Command", DataModel.AiCommandNames
                            .Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).OrderBy(x => x.Value).ToArray());
                    if (AtelStatProperties.EnumValues.TryGetValue(property, out IReadOnlyDictionary<ushort, string>? values))
                        return (role, values.OrderBy(x => x.Key)
                            .Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).ToArray());
                }
            }
            return role switch
            {
				"Target" or "Character" or "Group" => (role, GetTargetChoices(instruction)),
                "Stat property" => (role, AtelStatProperties.Names.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Property" => (role, AtelStatProperties.Names.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Command property" => (role, AtelDecompiler.CommandProperties.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Motion property" => (role, AtelDecompiler.MotionProperties.Select(x => new OperandChoice(x.Value, x.Key)).OrderBy(x => x.Value).ToArray()),
                "Selector" => (role, [new("Any/All", 0x0000), new("Highest", 0x0001), new("Lowest", 0x0002), new("Not", 0x0080)]),
                "Command" => (role, DataModel.AiCommandNames.Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).OrderBy(x => x.Value).ToArray()),
                "Disabled" => (role, [new("Enabled", 0x0000), new("Disabled", 0x0001)]),
                "Visible" => (role, [new("Hidden", 0x0000), new("Visible", 0x0001)]),
                "Weak state" => (role, AtelStatProperties.EnumValues[0x0008]
                    .OrderBy(x => x.Key).Select(x => new OperandChoice(x.Value, x.Key, instruction.Opcode)).ToArray()),
                "Battle result" => (role, [new("Defeat", 0x0001), new("Victory", 0x0002),
                    new("Player Escaped", 0x0003), new("Monster Escaped", 0x0004)]),
				"Command source" when instruction.Opcode == 0x9F => (role,
					GetVariableChoices(instruction, 0x9F)),
                _ => (role, [])
            };
        }
		if (instruction.Opcode is 0x9F or 0xA0 or 0xA1)
			return (instruction.Opcode == 0x9F ? "Variable read" : "Variable assignment",
				GetVariableChoices(instruction, instruction.Opcode));
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

	private OperandChoice[] GetVariableChoices(AtelInstruction instruction, byte opcode)
	{
		if (DataModel.AiDocument == null) return [];
		int workerIndex = DataModel.AiDocument.GetWorkerIndexForCodeOffset(instruction.Offset);
		AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == workerIndex);
		if (worker == null || worker.VariableCount <= 0) return [];
		return Enumerable.Range(0, worker.VariableCount)
			.Select(index => new OperandChoice("Variable", (ushort)index, opcode))
			.ToArray();
	}

	private OperandChoice[] GetTargetChoices(AtelInstruction instruction)
	{
		OperandChoice[] variables = GetVariableChoices(instruction, 0x9F);
		return CharacterTargets.Concat(MonsterTypeTargets).Concat(SelectorTargets).Concat(variables).ToArray();
	}

    private static bool IsActorReferenceRole(string? role) =>
        role is "Target" or "Character" or "Group";

    private static bool IsCommandRole(string? role) => role == "Command";

    private static bool IsStatPropertyRole(string? role) =>
        role is "Stat property" or "Property";

    private ActorReferenceKindChoice DetectActorReferenceKind(AtelInstruction instruction)
    {
        if (instruction.Opcode == 0x9F) return VariableReferenceKind;
        if ((instruction.Operand & 0xF000) == 0x1000) return MonsterReferenceKind;
        if (instruction.Operand >= 0xFFE6) return SelectorReferenceKind;
        return CharacterReferenceKind;
    }

    private OperandChoice[] GetActorReferenceChoices(ActorReferenceKindChoice kind, AtelInstruction instruction) =>
        ReferenceEquals(kind, MonsterReferenceKind) ? MonsterTypeTargets :
        ReferenceEquals(kind, SelectorReferenceKind) ? SelectorTargets :
        ReferenceEquals(kind, VariableReferenceKind) ? GetVariableChoices(instruction, 0x9F) :
        CharacterTargets;

    private void PopulateActorReferenceOptions(ComboBox options, ActorReferenceKindChoice kind,
        AtelInstruction instruction, bool selectFirst = false)
    {
        OperandChoice[] choices = GetActorReferenceChoices(kind, instruction);
        options.ItemsSource = choices;
        options.SelectedItem = selectFirst
            ? choices.FirstOrDefault()
            : choices.FirstOrDefault(choice =>
                choice.Value == instruction.Operand && choice.Opcode == instruction.Opcode);
    }

    private static CommandEditorKindChoice DetectCommandEditorKind(AtelInstruction instruction)
    {
        ushort category = (ushort)(instruction.Operand >> 12);
        return CommandEditorKinds.FirstOrDefault(kind => kind.Category == category) ?? CommandEditorKinds[0];
    }

    private OperandChoice[] GetCommandChoices(CommandEditorKindChoice kind, AtelInstruction instruction) =>
        DataModel.AiCommandNames
            .Where(entry => (entry.Key >> 12) == kind.Category)
            .OrderBy(entry => entry.Key)
            .Select(entry => new OperandChoice(entry.Value, entry.Key, instruction.Opcode))
            .ToArray();

    private void PopulateCommandOptions(ComboBox options, CommandEditorKindChoice kind,
        AtelInstruction instruction, bool selectFirst = false)
    {
        OperandChoice[] choices = GetCommandChoices(kind, instruction);
        options.ItemsSource = choices;
        options.SelectedItem = selectFirst
            ? choices.FirstOrDefault()
            : choices.FirstOrDefault(choice =>
                choice.Value == instruction.Operand && choice.Opcode == instruction.Opcode);
    }

    private static StatPropertyGroupChoice DetectStatPropertyGroup(AtelInstruction instruction) =>
        StatPropertyGroups.FirstOrDefault(group => group.Contains(instruction.Operand)) ?? StatPropertyGroups[^1];

    private static OperandChoice[] GetStatPropertyChoices(StatPropertyGroupChoice group, AtelInstruction instruction) =>
        AtelStatProperties.Names
            .Where(entry => group.Contains(entry.Key))
            .OrderBy(entry => entry.Key)
            .Select(entry => new OperandChoice(entry.Value, entry.Key, instruction.Opcode))
            .ToArray();

    private static void PopulateStatPropertyOptions(ComboBox options, StatPropertyGroupChoice group,
        AtelInstruction instruction, bool selectFirst = false)
    {
        OperandChoice[] choices = GetStatPropertyChoices(group, instruction);
        options.ItemsSource = choices;
        options.SelectedItem = selectFirst
            ? choices.FirstOrDefault()
            : choices.FirstOrDefault(choice =>
                choice.Value == instruction.Operand && choice.Opcode == instruction.Opcode);
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

    private void AiReferenceType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingMeaningOptions || _selectedInstruction == null) return;
        _updatingMeaningOptions = true;
        if (AiReferenceTypeOptions.SelectedItem is ActorReferenceKindChoice actorKind)
            PopulateActorReferenceOptions(AiMeaningOptions, actorKind, _selectedInstruction, selectFirst: true);
        else if (AiReferenceTypeOptions.SelectedItem is CommandEditorKindChoice commandKind)
            PopulateCommandOptions(AiMeaningOptions, commandKind, _selectedInstruction, selectFirst: true);
        else if (AiReferenceTypeOptions.SelectedItem is StatPropertyGroupChoice propertyGroup)
            PopulateStatPropertyOptions(AiMeaningOptions, propertyGroup, _selectedInstruction, selectFirst: true);
        if (AiMeaningOptions.SelectedItem is OperandChoice choice)
            AiOperandText.Text = $"0x{choice.Value:X4}";
        _updatingMeaningOptions = false;
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
            AiEditSnapshot before = CaptureAiEditSnapshot();
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
            CompleteAiEdit(before);
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
            ushort floatIndex = _selectedInstruction.Operand;
            AiEditSnapshot before = CaptureAiEditSnapshot();
            DataModel.RecordAiUndoCheckpoint("apply float value change", "Instruction", offset);
            DataModel.ApplyFloatConstant(offset, floatIndex, AiFloatValueText.Text ?? "");
            CompleteAiEdit(before, neonMintRawChange: true);
            HighlightFloatReferences([floatIndex], offset);
            AiStatusText.Text = DataModel.AiStatus;
        }
        catch (Exception ex)
        {
            AiStatusText.Text = "ERROR: " + ex.Message;
        }
    }

    private void HighlightFloatReferences(IReadOnlyCollection<ushort> floatIndices, int preferredOffset)
    {
        if (DataModel.AiDocument == null || AiStatementList.SelectedItems == null ||
            AiInstructionList.SelectedItems == null) return;

        AtelInstruction[] displayedInstructions =
            (AiInstructionList.ItemsSource as IEnumerable<AtelInstruction> ??
             DataModel.AiDocument.Instructions).ToArray();
        AtelStatement[] displayedStatements =
            (AiStatementList.ItemsSource as IEnumerable<AtelStatement> ??
             DataModel.AiDocument.Statements).ToArray();
        AtelInstruction[] references = displayedInstructions
            .Where(instruction => instruction.Opcode == 0xAF &&
                floatIndices.Contains(instruction.Operand)).ToArray();
        AtelStatement[] statements = displayedStatements
            .Where(statement => statement.Instructions.Any(instruction =>
                instruction.Opcode == 0xAF && floatIndices.Contains(instruction.Operand)))
            .ToArray();
        if (references.Length == 0) return;

        _synchronizingStatementSelection = true;
        _synchronizingInstructionSelection = true;
        try
        {
            AiStatementList.SelectedItems.Clear();
            foreach (AtelStatement statement in statements)
                AiStatementList.SelectedItems.Add(statement);
            AiInstructionList.SelectedItems.Clear();
            foreach (AtelInstruction reference in references)
                AiInstructionList.SelectedItems.Add(reference);
        }
        finally
        {
            _synchronizingStatementSelection = false;
            _synchronizingInstructionSelection = false;
        }

        AtelInstruction preferred = references.FirstOrDefault(item =>
            item.Offset == preferredOffset) ?? references[0];
        _selectedInstruction = preferred;
        if (statements.Length > 0) AiStatementList.ScrollIntoView(statements[0]);
        AiInstructionList.ScrollIntoView(preferred);
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
            AiEditSnapshot before = CaptureAiEditSnapshot();
            DataModel.RecordAiUndoCheckpoint("replace Battle Script bytes");
            DataModel.ReplaceAiHex();
            CompleteAiEdit(before);
            _lastSearch = "";
            _searchResultIndex = 0;
            AiStatusText.Text = $"Replaced {DataModel.AiSearchOffsets.Count} occurrence(s). Highlighting the complete changed range.";
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
        AiMessageIcon.Text = "";
        AiMessageIcon.Foreground = null;
        AiMessageBanner.Background = Brushes.Transparent;
        AiMessagesAttentionIndicator.BorderBrush = Brushes.Transparent;
        AiMessageDetailsButton.IsVisible = false;
        _aiMessageDetails = null;
    }

	private void InitializeJumpDestinationOverlay()
	{
		_aiHexScrollViewer = AiHexText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
		_aiChangeContextScrollViewer = AiChangeContextText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
		_aiExactChangeScrollViewer = AiExactChangeText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
		_aiJumpScrollViewer = AiJumpDestinationText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
		if (_aiHexScrollViewer == null || _aiChangeContextScrollViewer == null ||
			_aiExactChangeScrollViewer == null || _aiJumpScrollViewer == null)
		{
			Dispatcher.UIThread.Post(InitializeJumpDestinationOverlay, DispatcherPriority.Loaded);
			return;
		}
		_aiChangeContextScrollViewer.Offset = _aiHexScrollViewer.Offset;
		_aiExactChangeScrollViewer.Offset = _aiHexScrollViewer.Offset;
		_aiJumpScrollViewer.Offset = _aiHexScrollViewer.Offset;
		UpdateHexColumnHeaderOffset();
		_aiHexScrollViewer.ScrollChanged += (_, _) =>
		{
			if (_aiChangeContextScrollViewer != null && _aiHexScrollViewer != null)
				_aiChangeContextScrollViewer.Offset = _aiHexScrollViewer.Offset;
			if (_aiExactChangeScrollViewer != null && _aiHexScrollViewer != null)
				_aiExactChangeScrollViewer.Offset = _aiHexScrollViewer.Offset;
			if (_aiJumpScrollViewer != null && _aiHexScrollViewer != null)
				_aiJumpScrollViewer.Offset = _aiHexScrollViewer.Offset;
			UpdateHexColumnHeaderOffset();
		};
	}

	private void ShowChangeHexHighlights(int exactChunkOffset, int exactByteLength)
	{
		if (DataModel.AiDocument == null || exactByteLength <= 0) return;
		int codeStart = DataModel.AiDocument.ScriptCodeOffset;
		int scriptStart = Math.Max(0, exactChunkOffset - codeStart);
		int scriptEnd = Math.Min(DataModel.AiDocument.ScriptCodeLength,
			exactChunkOffset + exactByteLength - codeStart);
		if (scriptStart >= scriptEnd) return;

		AtelInstruction[] instructions = DataModel.AiDocument.Instructions
			.Where(item => item.Offset < scriptEnd && item.Offset + item.Bytes.Length > scriptStart)
			.ToArray();
		int contextStart;
		int contextEnd;
		if (instructions.Length == 1)
		{
			contextStart = instructions[0].Offset;
			contextEnd = instructions[0].Offset + instructions[0].Bytes.Length;
		}
		else
		{
			AtelStatement[] statements = DataModel.AiDocument.Statements
				.Where(item => item.Offset < scriptEnd && item.Offset + item.ByteLength > scriptStart)
				.ToArray();
			contextStart = statements.Length > 0 ? statements.Min(item => item.Offset) : scriptStart;
			contextEnd = statements.Length > 0 ? statements.Max(item => item.Offset + item.ByteLength) : scriptEnd;
		}

		string hex = AiHexText.Text ?? "";
		AiExactChangeText.Text = hex;
		SetOverlayHexSelection(AiExactChangeText, exactChunkOffset, exactByteLength);
		int contextChunkOffset = codeStart + contextStart;
		int contextByteLength = contextEnd - contextStart;
		int contextSelectionStart = HexCharacterIndex(contextChunkOffset);
		int contextSelectionEnd = HexCharacterIndex(contextChunkOffset + contextByteLength - 1) + 2;
		int selectionVersion = ++_aiHexSelectionVersion;
		AiHexText.SelectionBrush = Brush.Parse("#62F5A7");
		AiHexText.SelectionForegroundBrush = Brush.Parse("#101010");
		AiHexText.Focus();
		// The editable TextBox owns the contextual selection so the mint layer is
		// guaranteed to render. The transparent red overlay marks the exact bytes.
		Dispatcher.UIThread.Post(() => Dispatcher.UIThread.Post(() =>
		{
			if (_aiHexIsDirty || selectionVersion != _aiHexSelectionVersion ||
				contextSelectionStart < 0 || contextSelectionEnd > (AiHexText.Text?.Length ?? 0)) return;
			AiHexText.CaretIndex = contextSelectionStart;
			AiHexText.SelectionStart = contextSelectionStart;
			AiHexText.SelectionEnd = contextSelectionEnd;
			ScrollAiHexSelectionToTop(contextSelectionStart);
			if (_aiExactChangeScrollViewer != null && _aiHexScrollViewer != null)
				_aiExactChangeScrollViewer.Offset = _aiHexScrollViewer.Offset;
		}, DispatcherPriority.Background), DispatcherPriority.Background);
		if (_aiHexScrollViewer != null)
		{
			if (_aiExactChangeScrollViewer != null) _aiExactChangeScrollViewer.Offset = _aiHexScrollViewer.Offset;
		}
	}

	private static void SetOverlayHexSelection(TextBox overlay, int byteOffset, int byteLength)
	{
		int start = HexCharacterIndex(byteOffset);
		int end = HexCharacterIndex(byteOffset + byteLength - 1) + 2;
		if (start < 0 || end > (overlay.Text?.Length ?? 0)) return;
		overlay.SelectionStart = start;
		overlay.SelectionEnd = end;
	}

	private void ClearChangeHexHighlights()
	{
		AiChangeContextText.SelectionStart = 0;
		AiChangeContextText.SelectionEnd = 0;
		AiExactChangeText.SelectionStart = 0;
		AiExactChangeText.SelectionEnd = 0;
		ClearLogicChangeHighlights();
	}

	private void ClearLogicChangeHighlights()
	{
		if (DataModel.AiDocument == null) return;
		foreach (AtelStatement statement in DataModel.AiDocument.Statements)
			statement.SetChangeTranslationToken(null);
		foreach (AtelInstruction instruction in DataModel.AiDocument.Instructions)
			instruction.SetChangeSemanticToken(null);
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

	private void Button_BeginWorkerJumpDestination(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		ClearValidationResult();
		if (DataModel.AiDocument == null || _selectedWorkerIndex < 0 ||
			AiWorkerJumpOptions.SelectedItem is not WorkerJumpChoice jump || jump.Index < 0)
		{
			AiStatusText.Text = "ERROR: Select a worker jump-table entry first.";
			return;
		}

		_choosingWorkerJumpDestination = true;
		_jumpPickerWorkerIndex = _selectedWorkerIndex;
		_jumpPickerJumpIndex = jump.Index;
		_jumpPickerOriginalOffset = jump.ScriptOffset;
		_jumpPickerCandidateOffset = null;
		_jumpPickerAddsEntry = false;
		AiWorkerJumpOptions.IsEnabled = false;
		AiWorkerJumpButton.IsEnabled = false;
		AiChangeWorkerJumpButton.IsEnabled = false;
		AiAddWorkerJumpButton.IsEnabled = false;
		AiJumpDestinationPicker.IsVisible = true;
		AiApplyJumpDestinationButton.IsEnabled = false;
		ApplyJumpDestinationPickerVisibility();
		AiJumpDestinationPickerText.Text = $"Picking destination for w{_jumpPickerWorkerIndex:X2}:j{_jumpPickerJumpIndex:X2} — select Battle Logic or an instruction.";
		HighlightJumpDestinationScriptOffset(_jumpPickerOriginalOffset);
		AiStatusText.Text = $"Destination-picking mode: the current destination for w{_jumpPickerWorkerIndex:X2}:j{_jumpPickerJumpIndex:X2} is blue. Select a Battle Logic statement or Script Instruction, then Apply or Cancel.";
	}

	private void Button_BeginAddWorkerJump(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		ClearValidationResult();
		if (DataModel.AiDocument == null || _selectedWorkerIndex < 0)
		{
			AiStatusText.Text = "ERROR: Select a worker first.";
			return;
		}
		AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == _selectedWorkerIndex);
		if (worker == null || worker.JumpCount == 0)
		{
			AiStatusText.Text = "ERROR: Phase 2 can add entries only to workers that already have a jump table.";
			return;
		}

		_choosingWorkerJumpDestination = true;
		_jumpPickerAddsEntry = true;
		_jumpPickerWorkerIndex = worker.Index;
		_jumpPickerJumpIndex = worker.JumpCount;
		_jumpPickerOriginalOffset = -1;
		_jumpPickerCandidateOffset = null;
		AiWorkerJumpOptions.IsEnabled = false;
		AiWorkerJumpButton.IsEnabled = false;
		AiChangeWorkerJumpButton.IsEnabled = false;
		AiAddWorkerJumpButton.IsEnabled = false;
		AiApplyJumpDestinationButton.IsEnabled = false;
		ApplyJumpDestinationPickerVisibility();
		AiJumpDestinationPickerText.Text = $"Adding w{worker.Index:X2}:j{worker.JumpCount:X2} — select Battle Logic or an instruction.";
		ClearJumpDestinationHighlight();
		AiStatusText.Text = $"Add-jump mode: select a Battle Logic statement or Script Instruction in w{worker.Index:X2}, then Apply or Cancel.";
	}

	private void PreviewWorkerJumpDestination(int destinationOffset, string description)
	{
		if (!_choosingWorkerJumpDestination || DataModel.AiDocument == null) return;
		if (!WorkerOwnsScriptOffset(_jumpPickerWorkerIndex, destinationOffset))
		{
			_jumpPickerCandidateOffset = null;
			AiApplyJumpDestinationButton.IsEnabled = false;
			AiJumpDestinationPickerText.Text = $"Invalid target 0x{destinationOffset:X4}: select an entry owned by w{_jumpPickerWorkerIndex:X2}.";
			if (_jumpPickerOriginalOffset >= 0) HighlightJumpDestinationScriptOffset(_jumpPickerOriginalOffset);
			AiStatusText.Text = $"The selected destination belongs to another worker. The current destination remains blue; choose an entry in w{_jumpPickerWorkerIndex:X2}.";
			ApplyJumpDestinationPickerVisibility();
			return;
		}

		_jumpPickerCandidateOffset = destinationOffset;
		AiApplyJumpDestinationButton.IsEnabled = _jumpPickerAddsEntry || destinationOffset != _jumpPickerOriginalOffset;
		int chunkOffset = DataModel.AiDocument.ScriptCodeOffset + destinationOffset;
		AiJumpDestinationPickerText.Text = $"Proposed: {description} at script 0x{destinationOffset:X4} / Battle Script 0x{chunkOffset:X}.";
		// The normal selection remains red; keep the existing jump destination blue for comparison.
		if (_jumpPickerOriginalOffset >= 0) HighlightJumpDestinationScriptOffset(_jumpPickerOriginalOffset);
		AiStatusText.Text = _jumpPickerAddsEntry
			? $"Previewing 0x{destinationOffset:X4} as the destination for new jump w{_jumpPickerWorkerIndex:X2}:j{_jumpPickerJumpIndex:X2}. Click Apply to append it or Cancel."
			: $"Previewing 0x{destinationOffset:X4} as the new destination for w{_jumpPickerWorkerIndex:X2}:j{_jumpPickerJumpIndex:X2}. Click Apply to commit or Cancel to keep 0x{_jumpPickerOriginalOffset:X4}.";
		ApplyJumpDestinationPickerVisibility();
	}

	private void Button_ApplyWorkerJumpDestination(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (!_choosingWorkerJumpDestination || !_jumpPickerCandidateOffset.HasValue) return;
		int workerIndex = _jumpPickerWorkerIndex;
		int jumpIndex = _jumpPickerJumpIndex;
		int destinationOffset = _jumpPickerCandidateOffset.Value;
		bool addsEntry = _jumpPickerAddsEntry;

		try
		{
            AiEditSnapshot before = CaptureAiEditSnapshot();
			DataModel.RecordAiUndoCheckpoint(addsEntry ? "add worker jump" : "change worker jump destination", "Jump", destinationOffset);
			if (addsEntry)
				jumpIndex = DataModel.AddWorkerJump(workerIndex, destinationOffset);
			else
				DataModel.ChangeWorkerJumpDestination(workerIndex, jumpIndex, destinationOffset);
			EndWorkerJumpDestinationPicker();
            CompleteAiEdit(before, jumpIndex);
            HighlightSelectedWorkerJump();
			AiStatusText.Text = DataModel.AiStatus;
		}
		catch (Exception ex)
		{
			AiStatusText.Text = "ERROR: " + ex.Message;
		}
	}

	private void Button_CancelWorkerJumpDestination(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (!_choosingWorkerJumpDestination) return;
		int originalOffset = _jumpPickerOriginalOffset;
		AiLogicSelectionOwner selectionOwner = _logicSelectionOwner;
		AtelInstruction? selectedInstruction = _selectedInstruction;
		AtelStatement? selectedStatement = AiStatementList.SelectedItem as AtelStatement;
		EndWorkerJumpDestinationPicker();
		if (selectionOwner == AiLogicSelectionOwner.Instruction && selectedInstruction != null)
			ActivateInstructionEditor(selectedInstruction);
		else if (selectionOwner == AiLogicSelectionOwner.Statement && selectedStatement != null)
			ActivateStatementEditor(selectedStatement);
		if (originalOffset >= 0) HighlightJumpDestinationScriptOffset(originalOffset);
		AiStatusText.Text = "Jump destination change canceled; no bytes were changed.";
	}

	private void EndWorkerJumpDestinationPicker()
	{
		_choosingWorkerJumpDestination = false;
		_jumpPickerWorkerIndex = -1;
		_jumpPickerJumpIndex = -1;
		_jumpPickerOriginalOffset = -1;
		_jumpPickerCandidateOffset = null;
		_jumpPickerAddsEntry = false;
		AiJumpDestinationPicker.IsVisible = false;
		AiApplyJumpDestinationButton.IsEnabled = false;
		ApplyJumpDestinationPickerVisibility();
		bool hasJump = AiWorkerJumpOptions.ItemsSource is IEnumerable<WorkerJumpChoice> jumps && jumps.Any(choice => choice.Index >= 0);
		AiWorkerJumpOptions.IsEnabled = hasJump;
		AiWorkerJumpButton.IsEnabled = hasJump;
		AiChangeWorkerJumpButton.IsEnabled = hasJump;
		AiAddWorkerJumpButton.IsEnabled = hasJump;
	}

	private void ApplyJumpDestinationPickerVisibility()
	{
		AiWorkerJumpNavigationPanel.IsVisible = !_choosingWorkerJumpDestination;
		AiJumpDestinationPicker.IsVisible = _choosingWorkerJumpDestination;
		if (!_choosingWorkerJumpDestination) return;

		// Destination picking is a control-flow operation. Hide unrelated editors so
		// only the target lists and the explicit Apply/Cancel decision remain.
		AiMeaningEditorPanel.IsVisible = false;
		AiMeaningLabel.IsVisible = false;
		AiMeaningOptions.IsVisible = false;
		AiReferenceTypeEditor.IsVisible = false;
		AiManualOperandEditor.IsVisible = false;
		AiFloatEditor.IsVisible = false;
		AiGroupEditorPanel.IsVisible = false;
		AiGroupApplyButton.IsVisible = false;
		AiInstructionJumpButton.IsVisible = false;
	}

	private void UpdateWorkerJumpActionVisibility()
	{
		bool workerSelection = _selectedWorkerIndex >= 0 &&
			_logicSelectionOwner == AiLogicSelectionOwner.None && !_choosingWorkerJumpDestination;
		AiChangeWorkerJumpButton.IsVisible = workerSelection;
		AiAddWorkerJumpButton.IsVisible = workerSelection;
	}

	private void UpdateReturnOnlyManualInsertionVisibility()
	{
		bool returnOnly = false;
		if (!_choosingWorkerJumpDestination && DataModel.AiDocument != null && _selectedWorkerIndex >= 0 &&
			_selectedFunctionIndex >= 0 && AiFunctionOptions.SelectedItem is FunctionScopeChoice function)
		{
			AtelWorker? worker = DataModel.AiDocument.Workers.FirstOrDefault(item => item.Index == _selectedWorkerIndex);
			returnOnly = worker != null && _selectedFunctionIndex < worker.FunctionOffsets.Count &&
				function.End == function.Start + 1 &&
				DataModel.AiDocument.Instructions.FirstOrDefault(item => item.Offset == function.Start)?.Opcode == 0x3C;
		}
		AiReturnOnlyManualInsertPanel.IsVisible = returnOnly;
	}

	private async void Button_InsertManualCodeBeforeReturn(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		ClearValidationResult();
		if (DataModel.AiDocument == null || _selectedWorkerIndex < 0 || _selectedFunctionIndex < 0 ||
			TopLevel.GetTopLevel(this) is not Window owner)
		{
			AiStatusText.Text = "ERROR: Select a return-only worker function first.";
			return;
		}

		try
		{
			string hexText = AiReturnOnlyManualCodeText.Text ?? "";
			(byte[] bytes, string preview, int functionStart) = DataModel.PreviewManualCodeBeforeReturn(
				_selectedWorkerIndex, _selectedFunctionIndex, hexText);
			string source = $"Target: w{_selectedWorkerIndex:X2}:f{_selectedFunctionIndex:X2} at script 0x{functionStart:X4}\n" +
				$"Bytes: {string.Join(' ', bytes.Select(value => value.ToString("X2")))}\n\nDecoded instructions:\n{preview}\n\nExisting instruction after insertion:\n{functionStart + bytes.Length:X4}  3C        RETURN";
			bool confirmed = await AiRevertConfirmationWindow.Show(owner, "Insert Manual Code Before RETURN",
				"The validated instructions below will become the start of this function. The existing RETURN remains at the end, and later function and jump offsets will be rebuilt.",
				source, "Insert Before RETURN");
			if (!confirmed)
			{
				AiStatusText.Text = "Manual code insertion was canceled; no bytes were changed.";
				return;
			}

			int workerIndex = _selectedWorkerIndex;
			int functionIndex = _selectedFunctionIndex;
            AiEditSnapshot before = CaptureAiEditSnapshot();
			DataModel.RecordAiUndoCheckpoint("insert manual code before RETURN", "Function", functionStart);
			DataModel.InsertManualCodeBeforeReturn(workerIndex, functionIndex, hexText);
            CompleteAiEdit(before);
			AiStatusText.Text = DataModel.AiStatus;
		}
		catch (Exception ex)
		{
			ShowMessageError(ex.Message);
		}
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

    private void SelectAiHexRange(int byteOffset, int byteLength, bool neonMint = false)
    {
        if (_aiHexIsDirty || byteLength <= 0) return;
		AiHexText.SelectionBrush = Brush.Parse(neonMint ? "#62F5A7" : "#B94A48");
		AiHexText.SelectionForegroundBrush = neonMint ? Brush.Parse("#101010") : Brushes.White;
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
                    AiReferenceTypeEditor.IsVisible = false;
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

    private bool RunAiAction(Action action)
    {
        _lastAiActionException = null;
        try
        {
            AiEditSnapshot before = CaptureAiEditSnapshot();
            action();
            CompleteAiEdit(before);
            AiStatusText.Text = DataModel.AiStatus;
            return true;
        }
        catch (Exception ex)
        {
            _lastAiActionException = ex;
            ShowMessageError("The action could not be completed.", ex.Message);
            return false;
        }
    }
}
