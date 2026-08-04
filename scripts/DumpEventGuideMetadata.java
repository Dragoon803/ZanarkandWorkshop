import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpEventGuideMetadata extends GhidraScript {
    @Override public void run() throws Exception {
        DecompInterface d=new DecompInterface();d.openProgram(currentProgram);
        for(String a:new String[]{"0086aa50","0086aa80","0086ab10","0091cb40","0091cb70","0091cae0","0091da30","0091d460","009200a0"}){
            Address ad=currentProgram.getAddressFactory().getAddress(a);Function f=currentProgram.getFunctionManager().getFunctionContaining(ad);
            println("\n===== "+a+" "+(f==null?"<none>":f.getName())+" =====");
            if(f!=null){DecompileResults r=d.decompileFunction(f,180,monitor);println(r.getDecompiledFunction().getC());}
        }d.dispose();
    }
}
