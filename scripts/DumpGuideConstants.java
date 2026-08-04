import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
public class DumpGuideConstants extends GhidraScript {
 @Override public void run() throws Exception {for(String a:new String[]{"00b92308","00b922a0","00b922e0","00b50198"}){Address x=currentProgram.getAddressFactory().getAddress(a);int bits=currentProgram.getMemory().getInt(x);println(a+" bits=0x"+Integer.toHexString(bits)+" float="+Float.intBitsToFloat(bits));}}
}
