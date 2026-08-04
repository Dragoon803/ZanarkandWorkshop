import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpGuideMapPipeline extends GhidraScript {
    @Override
    public void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        String[] addresses = {
            "00907f00", "00845000", "00866430", "0086b6d0", "0086b930",
            "00919cd0", "00919d00", "00919d30", "00919d70", "00919da0", "00919e20",
            "0092b390", "0092b5e0", "0092b6b0"
        };
        for (String addressText : addresses) {
            Address address = currentProgram.getAddressFactory().getAddress(addressText);
            Function function = currentProgram.getFunctionManager().getFunctionContaining(address);
            println("\n===== " + addressText + " " + (function == null ? "<none>" : function.getName()) + " =====");
            if (function != null) {
                DecompileResults result = decompiler.decompileFunction(function, 180, monitor);
                println(result.getDecompiledFunction().getC());
            }
        }
        decompiler.dispose();
    }
}
