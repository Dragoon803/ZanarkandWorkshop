import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpFieldMapPipeline extends GhidraScript {
    @Override
    public void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        String[] addresses = {
            "00908000", // loadPhyreFieldMap
            "009083a0", // yiBattleMapReadNB
            "009085d0", // yiFieldMapRead
            "00908600", // yiFieldMapReadNB
            "009093b0"  // yiMapReadMaster
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
