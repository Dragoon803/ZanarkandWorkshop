import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpGuideMapProjection extends GhidraScript {
    @Override public void run() throws Exception {
        DecompInterface d=new DecompInterface();d.openProgram(currentProgram);
        for(String a:new String[]{"0091d590","0091d5e0","0091d880","0091d930","0091da80","009202e0","009204b0","00928540"}){
            Address ad=currentProgram.getAddressFactory().getAddress(a);Function f=currentProgram.getFunctionManager().getFunctionContaining(ad);
            println("\n===== "+a+" "+(f==null?"<none>":f.getName())+" =====");
            if(f!=null){DecompileResults r=d.decompileFunction(f,180,monitor);println(r.getDecompiledFunction().getC());}
        }d.dispose();
    }
}
