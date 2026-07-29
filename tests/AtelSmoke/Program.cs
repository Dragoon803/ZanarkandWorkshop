using FFXProjectEditor.FfxLib.Atel;

static void AssertChangedRange(byte[] before, byte[] after, (int Offset, int Length)? expected)
{
    if (AtelByteRange.FindChangedRange(before, after) != expected)
        throw new InvalidDataException($"Changed-range mismatch for [{string.Join(',', before)}] -> [{string.Join(',', after)}].");
}

AssertChangedRange([1, 2, 3], [1, 9, 3], (1, 1));
AssertChangedRange([1, 3], [1, 2, 3], (1, 1));
AssertChangedRange([1, 2, 3], [1, 3], (1, 1));
AssertChangedRange([1, 2], [1, 2], null);
AssertChangedRange([], [], null);

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: AtelSmoke <battle/mon directory>");
    return 2;
}

int parsed = 0;
int recoveredHeaders = 0;
int retargetedJumps = 0;
int addedJumps = 0;
int allocatedJumpTables = 0;
int addedVariables = 0;
int expandedReturnOnlyFunctions = 0;
int preservedGroupedReturns = 0;
int protectedReturns = 0;
int deletedJumpReferences = 0;
var failures = new List<string>();
foreach (string directory in Directory.GetDirectories(args[0], "_m*"))
{
    string id = Path.GetFileName(directory)[1..];
    string path = Path.Combine(directory, id + ".bin");
    if (!File.Exists(path)) continue;
    try
    {
        byte[] monster = File.ReadAllBytes(path);
        int aiOffset = BitConverter.ToInt32(monster, 0x04);
        int workerOffset = BitConverter.ToInt32(monster, 0x08);
        if (aiOffset <= 0 || workerOffset <= aiOffset) throw new InvalidDataException("Invalid monster AI chunk pointers.");
        byte[] ai = monster.AsSpan(aiOffset, workerOffset - aiOffset).ToArray();
        AtelScriptDocument document = AtelScriptDocument.Read(ai);
        if (id == "m141" && !document.Statements.Any(statement =>
            statement.Translation.Contains("MonsterType=m124 (Seymour (Macalania)) [0x107C]", StringComparison.Ordinal)))
            throw new InvalidDataException("Monster-type actor reference 0x107C was not decoded as m124.");
		byte[] normalizedAi = document.Bytes.ToArray();
		if (document.RecoveredMissingCodeLength) recoveredHeaders++;
		foreach (AtelWorker originalWorker in document.Workers.Where(worker => worker.VariableCount < ushort.MaxValue))
		{
			AtelScriptDocument expansion = AtelScriptDocument.Read(normalizedAi);
			AtelWorker candidate = expansion.Workers.First(worker => worker.Index == originalWorker.Index);
			int oldVariableCount = candidate.VariableCount;
			int newIndex = expansion.AddWorkerVariable(candidate.Index);
			if (newIndex != oldVariableCount || expansion.Bytes.Length != normalizedAi.Length)
				throw new InvalidDataException("Adding a variable changed the ATEL chunk length or returned the wrong index.");
			AtelWorker expandedWorker = expansion.Workers.First(worker => worker.Index == candidate.Index);
			if (expandedWorker.VariableCount != oldVariableCount + 1)
				throw new InvalidDataException("Adding a variable did not increment the selected worker's variable count.");
			if (expansion.Workers.Where(worker => worker.Index != candidate.Index)
				.Any(worker => worker.VariableCount != document.Workers.First(original => original.Index == worker.Index).VariableCount))
				throw new InvalidDataException("Adding a variable changed another worker's variable count.");
			AtelScriptDocument expandedRoundTrip = AtelScriptDocument.Read(expansion.Bytes);
			if (expandedRoundTrip.Workers.First(worker => worker.Index == candidate.Index).VariableCount != oldVariableCount + 1)
				throw new InvalidDataException("Added variable did not survive an ATEL round trip.");
			addedVariables++;
		}
		foreach (AtelWorker originalWorker in document.Workers.Where(worker => worker.JumpCount == 0))
		{
			AtelScriptDocument allocation = AtelScriptDocument.Read(normalizedAi);
			AtelWorker candidate = allocation.Workers.First(worker => worker.Index == originalWorker.Index);
			AtelInstruction? candidateDestination = allocation.Instructions.FirstOrDefault(instruction =>
				allocation.GetWorkerIndexForCodeOffset(instruction.Offset) == candidate.Index);
			if (candidateDestination == null) continue;

			int oldDeclaredLength = BitConverter.ToInt32(allocation.Bytes, 0x10);
			int newIndex = allocation.AddWorkerJump(candidate.Index, candidateDestination.Offset);
			if (newIndex != 0 || allocation.Bytes.Length != normalizedAi.Length + 4)
				throw new InvalidDataException("Allocating a first jump table did not append exactly one four-byte entry.");
			AtelWorker allocatedWorker = allocation.Workers.First(worker => worker.Index == candidate.Index);
			if (allocatedWorker.JumpCount != 1 || allocatedWorker.JumpTableOffset != normalizedAi.Length ||
				allocatedWorker.JumpOffsets[0] != candidateDestination.Offset)
				throw new InvalidDataException("Allocating a first jump table did not preserve j00 and its destination.");
			if (BitConverter.ToInt32(allocation.Bytes, 0x10) != oldDeclaredLength + 4)
				throw new InvalidDataException("Allocating a first jump table did not update the ATEL data length.");
			AtelScriptDocument allocationRoundTrip = AtelScriptDocument.Read(allocation.Bytes);
			AtelWorker roundTripWorker = allocationRoundTrip.Workers.First(worker => worker.Index == candidate.Index);
			if (roundTripWorker.JumpCount != 1 || roundTripWorker.JumpOffsets[0] != candidateDestination.Offset)
				throw new InvalidDataException("The first allocated jump table did not survive an ATEL round trip.");
			allocatedJumpTables++;
		}
		AtelWorker? jumpWorker = document.Workers.FirstOrDefault(worker => worker.JumpOffsets.Count > 0);
		if (jumpWorker != null)
		{
			int workerStart = jumpWorker.FunctionOffsets.Min();
			AtelInstruction destination = document.Instructions.First(instruction => instruction.Offset >= workerStart &&
				document.GetWorkerIndexForCodeOffset(instruction.Offset) == jumpWorker.Index);
			int entryOffset = jumpWorker.JumpTableOffset;
			document.SetWorkerJumpDestination(jumpWorker.Index, 0, destination.Offset);
			if (document.Bytes.Length != normalizedAi.Length)
				throw new InvalidDataException("Jump retargeting changed the ATEL chunk length.");
			int[] changedOffsets = document.Bytes.Select((value, index) => (value, index))
				.Where(item => item.value != normalizedAi[item.index]).Select(item => item.index).ToArray();
			if (changedOffsets.Any(offset => offset < entryOffset || offset >= entryOffset + 4))
				throw new InvalidDataException("Jump retargeting changed bytes outside the selected jump-table entry.");
			AtelScriptDocument reparsed = AtelScriptDocument.Read(document.Bytes);
			if (reparsed.Workers.First(worker => worker.Index == jumpWorker.Index).JumpOffsets[0] != destination.Offset)
				throw new InvalidDataException("Jump destination did not survive an ATEL round trip.");
			retargetedJumps++;
			document = AtelScriptDocument.Read(normalizedAi);

			foreach (AtelWorker originalWorker in document.Workers.Where(worker => worker.JumpCount > 0).ToArray())
			{
				AtelScriptDocument expansion = AtelScriptDocument.Read(normalizedAi);
				AtelWorker candidate = expansion.Workers.First(worker => worker.Index == originalWorker.Index);
				int candidateStart = candidate.FunctionOffsets.Min();
				AtelInstruction candidateDestination = expansion.Instructions.First(instruction =>
					instruction.Offset >= candidateStart && expansion.GetWorkerIndexForCodeOffset(instruction.Offset) == candidate.Index);
				int oldJumpCount = candidate.JumpCount;
				int[] oldJumpOffsets = candidate.JumpOffsets.ToArray();
				int newIndex = expansion.AddWorkerJump(candidate.Index, candidateDestination.Offset);
				if (newIndex != oldJumpCount || expansion.Bytes.Length != normalizedAi.Length + 4)
					throw new InvalidDataException("Adding a jump did not append exactly one four-byte table entry.");
				AtelWorker expandedWorker = expansion.Workers.First(worker => worker.Index == candidate.Index);
				if (expandedWorker.JumpCount != oldJumpCount + 1 ||
					!expandedWorker.JumpOffsets.Take(oldJumpCount).SequenceEqual(oldJumpOffsets) ||
					expandedWorker.JumpOffsets[newIndex] != candidateDestination.Offset)
					throw new InvalidDataException("Adding a jump changed existing entries or lost the appended destination.");
				AtelScriptDocument expandedRoundTrip = AtelScriptDocument.Read(expansion.Bytes);
				if (expandedRoundTrip.Workers.First(worker => worker.Index == candidate.Index).JumpOffsets[newIndex] != candidateDestination.Offset)
					throw new InvalidDataException("Added jump did not survive an ATEL round trip.");
				addedJumps++;
			}
			document = AtelScriptDocument.Read(normalizedAi);
		}
		foreach (AtelWorker originalWorker in document.Workers)
		{
			int workerEnd = document.Workers.Where(worker => worker.FunctionOffsets.Count > 0 &&
				worker.FunctionOffsets.Min() > originalWorker.FunctionOffsets.DefaultIfEmpty(int.MaxValue).Min())
				.Select(worker => worker.FunctionOffsets.Min()).DefaultIfEmpty(document.ScriptCodeLength).Min();
			int[] starts = originalWorker.FunctionOffsets.Distinct().OrderBy(offset => offset).ToArray();
			for (int function = 0; function < starts.Length; function++)
			{
				int start = starts[function];
				int end = function + 1 < starts.Length ? starts[function + 1] : workerEnd;
				if (end != start + 1 || normalizedAi[document.ScriptCodeOffset + start] != 0x3C) continue;
				AtelScriptDocument expansion = AtelScriptDocument.Read(normalizedAi);
				AtelWorker functionWorker = expansion.Workers.First(worker => worker.Index == originalWorker.Index);
				int[] originalOffsets = functionWorker.FunctionOffsets.ToArray();
				string[] originalNames = Enumerable.Range(0, functionWorker.FunctionCount)
					.Select(functionWorker.FunctionName).ToArray();
				expansion.InsertStatementBytes(start, [0x18], preserveFunctionEntryAtInsertion: true);
				AtelWorker expandedFunctionWorker = expansion.Workers.First(worker => worker.Index == originalWorker.Index);
				int[] expandedOffsets = expandedFunctionWorker.FunctionOffsets.ToArray();
				string[] expandedNames = Enumerable.Range(0, expandedFunctionWorker.FunctionCount)
					.Select(expandedFunctionWorker.FunctionName).ToArray();
				if (!expandedNames.SequenceEqual(originalNames))
					throw new InvalidDataException("Insertion before a return-only function changed function-index naming.");
				for (int index = 0; index < originalOffsets.Length; index++)
				{
					int expected = originalOffsets[index] > start ? originalOffsets[index] + 1 : originalOffsets[index];
					if (expandedOffsets[index] != expected)
						throw new InvalidDataException("Insertion before a return-only function did not preserve its entry point.");
				}
				if (expansion.Instructions.All(instruction => instruction.Offset != start || instruction.Opcode != 0x18) ||
					expansion.Instructions.All(instruction => instruction.Offset != start + 1 || instruction.Opcode != 0x3C))
					throw new InvalidDataException("Return-only function expansion did not place code before RETURN.");
				int removedPrefix = expansion.DeleteStatement(start);
				if (removedPrefix != 1 ||
					expansion.Instructions.All(instruction => instruction.Offset != start || instruction.Opcode != 0x3C))
					throw new InvalidDataException("Deleting a grouped RETURN prefix did not preserve RETURN.");
				preservedGroupedReturns++;
				try
				{
					expansion.DeleteInstructionRange(start, start + 1);
					throw new InvalidDataException("Direct RETURN deletion was unexpectedly allowed.");
				}
				catch (InvalidOperationException ex) when (
					ex.Message.Contains("RETURN", StringComparison.OrdinalIgnoreCase))
				{
					protectedReturns++;
				}
				expandedReturnOnlyFunctions++;
			}
		}
		document = AtelScriptDocument.Read(normalizedAi);
		AtelStatement? groupedReturn = document.Statements.FirstOrDefault(statement =>
			statement.Instructions.Count > 1 &&
			statement.Instructions[^1].Opcode == 0x3C);
		if (groupedReturn != null)
		{
			int editableLength = groupedReturn.Instructions[^1].Offset - groupedReturn.Offset;
			AtelScriptDocument deletion = AtelScriptDocument.Read(normalizedAi);
			int removed = deletion.DeleteStatement(groupedReturn.Offset);
			if (removed != editableLength ||
				deletion.ScriptCodeLength != document.ScriptCodeLength - editableLength ||
				deletion.Instructions.All(instruction =>
					instruction.Offset != groupedReturn.Offset || instruction.Opcode != 0x3C))
				throw new InvalidDataException("Grouped RETURN deletion did not preserve RETURN at the function entry.");
			preservedGroupedReturns++;

			AtelScriptDocument protectedDeletion = AtelScriptDocument.Read(normalizedAi);
			int returnOffset = groupedReturn.Instructions[^1].Offset;
			try
			{
				protectedDeletion.DeleteInstructionRange(returnOffset, returnOffset + 1);
				throw new InvalidDataException("Direct RETURN deletion was unexpectedly allowed.");
			}
			catch (InvalidOperationException ex) when (
				ex.Message.Contains("RETURN", StringComparison.OrdinalIgnoreCase))
			{
				protectedReturns++;
			}
		}

		AtelStatement? removableJumpReference = document.Statements.FirstOrDefault(statement =>
		{
			int end = statement.Offset + statement.ByteLength;
			bool hasConditionalJump = statement.Instructions.Any(instruction =>
				instruction.Opcode is 0xD5 or 0xD6 or 0xD7);
			bool hasProtectedTerminator = statement.Instructions.Any(instruction =>
				instruction.Opcode is 0x34 or 0x3C or 0x40 or 0x54);
			bool hasIncomingEntry = document.Workers.Any(worker =>
				worker.FunctionOffsets.Any(offset => offset >= statement.Offset && offset < end) ||
				worker.JumpOffsets.Any(offset => offset >= statement.Offset && offset < end));
			return hasConditionalJump && !hasProtectedTerminator && !hasIncomingEntry;
		});
		if (removableJumpReference != null)
		{
			int start = removableJumpReference.Offset;
			int end = start + removableJumpReference.ByteLength;
			AtelScriptDocument deletion = AtelScriptDocument.Read(normalizedAi);
			Dictionary<int, int[]> oldDestinations = deletion.Workers.ToDictionary(
				worker => worker.Index, worker => worker.JumpOffsets.ToArray());
			int removed = deletion.DeleteStatement(start);
			if (removed != removableJumpReference.ByteLength)
				throw new InvalidDataException("Jump-reference deletion removed an unexpected number of bytes.");
			foreach (AtelWorker worker in deletion.Workers)
			{
				int[] oldOffsets = oldDestinations[worker.Index];
				if (worker.JumpOffsets.Count != oldOffsets.Length)
					throw new InvalidDataException("Jump-reference deletion changed the jump-table entry count.");
				for (int index = 0; index < oldOffsets.Length; index++)
				{
					int expected = oldOffsets[index] >= end ? oldOffsets[index] - removed : oldOffsets[index];
					if (worker.JumpOffsets[index] != expected)
						throw new InvalidDataException("Jump-reference deletion changed a destination instead of rebasing it.");
				}
			}
			deletedJumpReferences++;
		}
        byte[] hexRoundTrip = AtelScriptDocument.ParseHexEditorText(document.ToHexEditorText());
        document.ReplaceBytes(hexRoundTrip);
        if (!normalizedAi.SequenceEqual(document.Bytes)) throw new InvalidDataException("Hex round trip changed normalized bytes.");
        parsed++;
    }
    catch (Exception ex)
    {
        failures.Add($"{path}: {ex.Message}");
    }
}

Console.WriteLine($"Parsed={parsed} RecoveredHeaders={recoveredHeaders} RetargetedJumps={retargetedJumps} AddedJumps={addedJumps} AllocatedJumpTables={allocatedJumpTables} AddedVariables={addedVariables} ExpandedReturnOnlyFunctions={expandedReturnOnlyFunctions} PreservedGroupedReturns={preservedGroupedReturns} ProtectedReturns={protectedReturns} DeletedJumpReferences={deletedJumpReferences} Failed={failures.Count}");
foreach (string failure in failures) Console.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;
