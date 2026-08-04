import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.Reference;

import java.util.LinkedHashSet;
import java.util.Set;

public class DumpMap1Loaders extends GhidraScript {
    private void inspect(byte[] needle, String label, DecompInterface decompiler) throws Exception {
        Memory memory = currentProgram.getMemory();
        Address cursor = memory.getMinAddress();
        Set<Function> functions = new LinkedHashSet<>();
        while (cursor != null) {
            Address hit = memory.findBytes(cursor, needle, null, true, monitor);
            if (hit == null) break;
            println("\n" + label + " bytes at " + hit);
            for (Reference reference : currentProgram.getReferenceManager().getReferencesTo(hit)) {
                Function function = currentProgram.getFunctionManager().getFunctionContaining(reference.getFromAddress());
                println("  xref " + reference.getFromAddress() + " function=" +
                    (function == null ? "<none>" : function.getName()));
                if (function != null) functions.add(function);
            }
            cursor = hit.next();
        }
        for (Function function : functions) {
            println("\n===== " + label + " LOADER " + function.getEntryPoint() + " " + function.getName() + " =====");
            DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
            println(result.getDecompiledFunction().getC());
        }
    }

    @Override
    public void run() throws Exception {
        println("Program: " + currentProgram.getName());
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        inspect(new byte[] {0x4d, 0x41, 0x50, 0x31}, "MAP1", decompiler);
        inspect(new byte[] {0x59, 0x4e, 0x44, 0x54}, "YNDT", decompiler);
        inspect(new byte[] {0x59, 0x4e, 0x50, 0x52}, "YNPR", decompiler);
        decompiler.dispose();
    }
}
