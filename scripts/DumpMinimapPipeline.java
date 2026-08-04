import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpMinimapPipeline extends GhidraScript {
    @Override
    public void run() throws Exception {
        println("Program: " + currentProgram.getName());
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        String[] addresses = {
            "0063eae0", // graphicDrawUIAbmapElement
            "006414d0", // graphicGetMiniMapType
            "006425c0", // graphicMinimapCameraSetWorldMatrix
            "006427a0", // graphicMinimapSetEnable
            "006438d0", // graphicSetMiniMapType
            "0066dad0"  // setMinimapCameraWorldMatrix
        };
        for (String addressText : addresses) {
            Address address = currentProgram.getAddressFactory().getAddress(addressText);
            Function function = currentProgram.getFunctionManager().getFunctionContaining(address);
            if (function == null) {
                println("No function at " + addressText);
                continue;
            }
            println("\n===== " + addressText + " " + function.getName() + " =====");
            DecompileResults result = decompiler.decompileFunction(function, 90, monitor);
            println(result.getDecompiledFunction().getC());
        }
        decompiler.dispose();
    }
}
