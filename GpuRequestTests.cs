using System;
using System.Collections.Generic;
using LegionPowerSwitch;
static class GpuRequestTests {
    static int passed;
    static void Assert(bool condition){if(!condition)throw new Exception("Assertion failed");}
    static void Test(string name,Action test){test();passed++;Console.WriteLine("PASS "+name);}
    static bool Throws(Action action){try{action();return false;}catch{return true;}}
    static void Main(){
        Test("Delayed MUX state does not roll back request",()=>{var writes=new List<int>();GpuRequest.Submit(1,0,0,1,false,x=>writes.Add(x),x=>writes.Add(x),x=>{});Assert(writes.Count==2&&writes[0]==0&&writes[1]==1);});
        Test("IGPU rejection during MUX change is deferred",()=>{bool logged=false;GpuRequest.Submit(1,0,0,1,false,x=>{},x=>{throw new Exception("Not available until reboot");},x=>logged=true);Assert(logged);});
        Test("Standalone IGPU rejection remains an error",()=>Assert(Throws(()=>GpuRequest.Submit(0,0,0,1,false,x=>{},x=>{throw new Exception();},x=>{}))));
        Test("MUX rejection does not continue to IGPU",()=>{bool ig=false;Assert(Throws(()=>GpuRequest.Submit(1,0,0,1,false,x=>{throw new Exception();},x=>ig=true,x=>{})));Assert(!ig);});
        Test("Opposite pending request is overwritten",()=>{int request=-1;GpuRequest.Submit(1,0,1,0,true,x=>request=x,x=>{},x=>{});Assert(request==1);});
        Test("Post-boot route mismatch never writes IGPU",()=>{bool written=false;Assert(Throws(()=>GpuRequest.Complete(0,1,()=>1,()=>0,x=>written=true)));Assert(!written);});
        Test("Post-boot IGPU completion verifies result",()=>{int ig=0;GpuRequest.Complete(0,1,()=>0,()=>ig,x=>ig=x);Assert(ig==1);});
        Test("Post-boot unchanged IGPU is not called success",()=>Assert(Throws(()=>GpuRequest.Complete(0,1,()=>0,()=>0,x=>{}))));
        Test("Verified target requires no new writes",()=>GpuRequest.Complete(1,0,()=>1,()=>0,x=>{throw new Exception();}));
        Test("NORM ECO NORM cancels before reboot",()=>Assert(GpuRequest.ReturnsToOriginal(1,0,1,0,true,true)));
        Test("ECO NORM ECO cancels before reboot",()=>Assert(GpuRequest.ReturnsToOriginal(0,1,0,1,true,true)));
        Test("Return after a reboot is not a cancellation",()=>Assert(!GpuRequest.ReturnsToOriginal(1,0,1,0,true,false)));
        Test("New target is not mistaken for cancellation",()=>Assert(!GpuRequest.ReturnsToOriginal(0,1,1,0,true,true)));
        Test("Cancel restores original firmware request",()=>{var writes=new List<int>();GpuRequest.Cancel(0,1,x=>writes.Add(x),x=>writes.Add(x));Assert(writes.Count==2&&writes[0]==0&&writes[1]==1);});
        Test("Rejected cancellation remains an error",()=>Assert(Throws(()=>GpuRequest.Cancel(1,0,x=>{},x=>{throw new Exception();}))));
        Test("Pending ECO keeps actual NVIDIA output orange",()=>{var state=GpuPresentation.Describe(0,1,"Travel",true,1,0,"PCI\\VEN_10DE");Assert(state.PowerHint==1&&state.Text.Contains("目标"));});
        Test("Verified Intel-only state is green",()=>Assert(GpuPresentation.Describe(0,1,"",false,0,1,"PCI\\VEN_8086").PowerHint==0));
        Test("Intel output alone does not prove DGPU is off",()=>Assert(GpuPresentation.Describe(0,0,"",false,0,0,"PCI\\VEN_8086").PowerHint==-1));
        Console.WriteLine(passed+" tests passed; no hardware accessed.");
    }
}
