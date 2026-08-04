import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpGuideMapFormat extends GhidraScript {
    @Override
    public void run() throws Exception {
        DecompInterface d = new DecompInterface(); d.openProgram(currentProgram);
        for (String a : new String[]{"00921d60","0092b1a0","0092b2f0","0092b960","0092ba90"}) {
            Address address=currentProgram.getAddressFactory().getAddress(a);
            Function f=currentProgram.getFunctionManager().getFunctionContaining(address);
            println("\n===== "+a+" "+(f==null?"<none>":f.getName())+" =====");
            if(f!=null){ DecompileResults r=d.decompileFunction(f,180,monitor); println(r.getDecompiledFunction().getC()); }
        }
        d.dispose();
    }
}
