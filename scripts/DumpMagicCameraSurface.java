import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.Symbol;
import java.util.LinkedHashSet;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public class DumpMagicCameraSurface extends GhidraScript {
    private static final Pattern ENGINE_CALL =
        Pattern.compile("\\+ 0x([0-9a-fA-F]+)\\)\\)");

    public void run() throws Exception {
        println("Program: " + currentProgram.getName());
        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        LinkedHashSet<Function> roots = new LinkedHashSet<>();

        Function overlayExport = functionNamed("GetEffectOverlayTable");
        if (overlayExport != null) {
            InstructionIterator instructions = currentProgram.getListing()
                .getInstructions(overlayExport.getBody(), true);
            while (instructions.hasNext()) {
                Instruction ins = instructions.next();
                for (Reference ref : ins.getReferencesFrom()) {
                    Address to = ref.getToAddress();
                    if (currentProgram.getMemory().contains(to) &&
                            currentProgram.getFunctionManager().getFunctionAt(to) == null) {
                        dumpPossibleOverlay(to, roots);
                    }
                }
            }
        }

        println("\n===== OVERLAY CALLBACKS =====");
        for (Function f : roots) dumpFunction(dec, f);

        println("\n===== FUNCTIONS USING ENGINE API OFFSETS =====");
        FunctionIterator functions =
            currentProgram.getFunctionManager().getFunctions(true);
        while (functions.hasNext()) {
            Function f = functions.next();
            DecompileResults result = dec.decompileFunction(f, 30, monitor);
            if (!result.decompileCompleted()) continue;
            String c = result.getDecompiledFunction().getC();
            Matcher matcher = ENGINE_CALL.matcher(c);
            LinkedHashSet<String> offsets = new LinkedHashSet<>();
            while (matcher.find()) offsets.add("0x" + matcher.group(1).toLowerCase());
            if (!offsets.isEmpty()) {
                println(f.getEntryPoint() + " " + f.getName() + " engineCalls=" + offsets);
            }
        }
        dec.dispose();
    }

    private Function functionNamed(String name) {
        for (Symbol s : currentProgram.getSymbolTable().getSymbols(name)) {
            Function f = currentProgram.getFunctionManager().getFunctionAt(s.getAddress());
            if (f != null) return f;
        }
        return null;
    }

    private void dumpPossibleOverlay(Address base, LinkedHashSet<Function> roots) {
        Memory mem = currentProgram.getMemory();
        println("Possible overlay table at " + base);
        for (int i = 0; i < 16; i++) {
            try {
                long raw = Integer.toUnsignedLong(mem.getInt(base.add(i * 4L)));
                Address value = currentProgram.getAddressFactory()
                    .getDefaultAddressSpace().getAddress(raw);
                Function f = currentProgram.getFunctionManager().getFunctionAt(value);
                println(String.format("  +0x%02X -> %s%s", i * 4, value,
                    f == null ? "" : " " + f.getName()));
                if (f != null) roots.add(f);
            } catch (Exception ignored) {
                break;
            }
        }
    }

    private void dumpFunction(DecompInterface dec, Function f) {
        println("\n===== " + f.getEntryPoint() + " " + f.getName() + " =====");
        DecompileResults result = dec.decompileFunction(f, 60, monitor);
        if (result.decompileCompleted()) println(result.getDecompiledFunction().getC());
        else println("DECOMPILE FAILED: " + result.getErrorMessage());
    }
}
