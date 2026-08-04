// Finds party.bin strings, their references, and decompiles the referring functions.
// @category ZanarkandWorkshop

import java.io.File;
import java.io.PrintWriter;
import java.util.HashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.DataType;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.Reference;

public class DumpPartyBin extends GhidraScript {
    @Override
    protected void run() throws Exception {
        File output = new File(
            "C:/Users/jeg80/Documents/Zanarkand Workshop/artifacts/party-analysis/ghidra-party-bin.txt");
        output.getParentFile().mkdirs();

        try (PrintWriter writer = new PrintWriter(output, "UTF-8")) {
            DecompInterface decompiler = new DecompInterface();
            decompiler.openProgram(currentProgram);
            Set<Address> dumpedFunctions = new HashSet<>();
            Listing listing = currentProgram.getListing();

            // Runtime helpers use module-relative offsets; FFX.exe's image base is 0x400000.
            Address[] kernelPointers = {
                toAddr("0112A92C"), // command.bin
                toAddr("0112A93C")  // party.bin candidate
            };
            for (Address partyPointer : kernelPointers) {
            writer.println("KERNEL POINTER " + partyPointer);
            for (Reference reference :
                currentProgram.getReferenceManager().getReferencesTo(partyPointer)) {
                Address from = reference.getFromAddress();
                writer.println("  POINTER REF " + from + " " + reference.getReferenceType());
                Function function = listing.getFunctionContaining(from);
                if (function == null || !dumpedFunctions.add(function.getEntryPoint()))
                    continue;

                writer.println();
                writer.println("FUNCTION " + function.getName() + " @ " +
                    function.getEntryPoint());
                DecompileResults results =
                    decompiler.decompileFunction(function, 60, monitor);
                if (results.decompileCompleted())
                    writer.println(results.getDecompiledFunction().getC());
                else
                    writer.println("DECOMPILE FAILED: " + results.getErrorMessage());
                writer.println();
            }
            }

            Address[] seedAddresses = {
                toAddr("00781D20"),
                toAddr("00780C10")
            };
            for (Address seed : seedAddresses) {
                Function function = listing.getFunctionContaining(seed);
                if (function == null || !dumpedFunctions.add(function.getEntryPoint()))
                    continue;
                writer.println("SEED FUNCTION " + function.getName() + " @ " +
                    function.getEntryPoint());
                DecompileResults results =
                    decompiler.decompileFunction(function, 60, monitor);
                if (results.decompileCompleted())
                    writer.println(results.getDecompiledFunction().getC());
                else
                    writer.println("DECOMPILE FAILED: " + results.getErrorMessage());
                writer.println();
            }

            byte[] needle = "party.bin".getBytes("US-ASCII");
            Memory memory = currentProgram.getMemory();
            for (MemoryBlock block : memory.getBlocks()) {
                if (!block.isInitialized()) continue;
                Address cursor = block.getStart();
                while (cursor != null && cursor.compareTo(block.getEnd()) <= 0) {
                    Address found = memory.findBytes(
                        cursor, block.getEnd(), needle, null, true, monitor);
                    if (found == null) break;

                    writer.println("STRING BYTES " + found + " = party.bin");
                for (Reference reference :
                    currentProgram.getReferenceManager().getReferencesTo(found)) {
                    Address from = reference.getFromAddress();
                    writer.println("  REF " + from + " " + reference.getReferenceType());
                    Function function = listing.getFunctionContaining(from);
                    if (function == null || !dumpedFunctions.add(function.getEntryPoint()))
                        continue;

                    writer.println();
                    writer.println("FUNCTION " + function.getName() + " @ " +
                        function.getEntryPoint());
                    DecompileResults results =
                        decompiler.decompileFunction(function, 60, monitor);
                    if (results.decompileCompleted()) {
                        writer.println(results.getDecompiledFunction().getC());
                    } else {
                        writer.println("DECOMPILE FAILED: " + results.getErrorMessage());
                        Instruction instruction =
                            listing.getInstructionAt(function.getEntryPoint());
                        while (instruction != null &&
                            function.getBody().contains(instruction.getAddress())) {
                            writer.println(instruction);
                            instruction = instruction.getNext();
                        }
                    }
                    writer.println();
                }
                    cursor = found.add(1);
                }
            }
            decompiler.dispose();
        }

        println("Wrote " + output.getAbsolutePath());
    }
}
