import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
public class DumpGuideDefaults extends GhidraScript {
 @Override public void run() throws Exception {DecompInterface d=new DecompInterface();d.openProgram(currentProgram);
 for(String a:new String[]{"00921640","009209e0","0091ce50","0091d3a0"}){Address ad=currentProgram.getAddressFactory().getAddress(a);Function f=currentProgram.getFunctionManager().getFunctionContaining(ad);println("\n===== "+a+" "+(f==null?"<none>":f.getName())+" =====");if(f!=null){DecompileResults r=d.decompileFunction(f,180,monitor);println(r.getDecompiledFunction().getC());}}d.dispose();}
}
