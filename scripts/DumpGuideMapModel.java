import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DumpGuideMapModel extends GhidraScript {
    @Override public void run() throws Exception {
        DecompInterface d=new DecompInterface(); d.openProgram(currentProgram);
        for(String a:new String[]{"00927be0","00927fa0","00928020","009281d0","009282a0","00928320","009283e0"}){
            Address ad=currentProgram.getAddressFactory().getAddress(a); Function f=currentProgram.getFunctionManager().getFunctionContaining(ad);
            println("\n===== "+a+" "+(f==null?"<none>":f.getName())+" =====");
            if(f!=null){DecompileResults r=d.decompileFunction(f,180,monitor);println(r.getDecompiledFunction().getC());}
        } d.dispose();
    }
}
