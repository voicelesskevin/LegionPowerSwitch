using System;
using LegionPowerSwitch;
static class CompatibilityTests {
    static int passed;
    static void Test(string name,Func<bool> test){if(!test())throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static PanelConnection Panel(string id,uint tech,bool active=true){return new PanelConnection{Instance=id,Technology=tech,Active=active};}
    static void Main(){
        Test("Embedded DisplayPort is internal",()=>Compatibility.InternalTechnology(11));
        Test("LVDS and embedded UDI are internal",()=>Compatibility.InternalTechnology(6)&&Compatibility.InternalTechnology(13));
        Test("INTERNAL high-bit value is supported",()=>Compatibility.InternalTechnology(0x80000000));
        Test("HDMI DP VGA and unknown are external",()=>!Compatibility.InternalTechnology(5)&&!Compatibility.InternalTechnology(10)&&!Compatibility.InternalTechnology(0)&&!Compatibility.InternalTechnology(uint.MaxValue));
        Test("Monitor interface maps to WMI instance",()=>Compatibility.MonitorKey(@"\\?\DISPLAY#ABC123#5&abcd&0&UID1#{class}")==Compatibility.MonitorKey(@"DISPLAY\ABC123\5&abcd&0&UID1_0"));
        Test("Full instance distinguishes identical panel models",()=>Compatibility.MonitorKey(@"DISPLAY\ABC123\instance1_0")!=Compatibility.MonitorKey(@"DISPLAY\ABC123\instance2_0"));
        Test("Select internal, not primary HDMI",()=>Compatibility.InternalPanel(new[]{Panel(@"DISPLAY\EXT\1_0",5),Panel(@"DISPLAY\ABC\2_0",11)})==@"DISPLAY\ABC\2");
        Test("No internal panel returns unsupported",()=>Compatibility.InternalPanel(new[]{Panel(@"DISPLAY\EXT\1_0",5)})=="");
        Test("Ambiguous internal panels are rejected",()=>Compatibility.InternalPanel(new[]{Panel(@"DISPLAY\ABC\1_0",11),Panel(@"DISPLAY\ABC\2_0",11)})=="");
        Test("Inactive panel is ignored",()=>Compatibility.InternalPanel(new[]{Panel(@"DISPLAY\ABC\1_0",11,false)})=="");
        Test("Duplicate WMI instances are deduplicated",()=>Compatibility.InternalPanel(new[]{Panel(@"DISPLAY\ABC\1_0",11),Panel(@"DISPLAY\ABC\1_0",11)})==@"DISPLAY\ABC\1");
        Test("Malformed interface is not guessed",()=>Compatibility.MonitorKey(@"\\?\DISPLAY#short")=="");
        Test("Panel model survives MUX route instance change",()=>Compatibility.PanelModel(@"DISPLAY\ABC\igpu_0")==Compatibility.PanelModel(@"DISPLAY\ABC\dgpu_0"));
        Test("ECO selects 60 when offered",()=>Compatibility.EcoRefresh(new[]{60,120,240},240)==60);
        Test("ECO supports panels without 60Hz",()=>Compatibility.EcoRefresh(new[]{48,120},120)==48);
        Test("ECO never invents a refresh rate",()=>Compatibility.EcoRefresh(new[]{120,144},144)==120);
        Test("Empty refresh modes keep current",()=>Compatibility.EcoRefresh(new int[0],75)==75);
        Test("Non-Lenovo cannot enable Lenovo fan",()=>!Compatibility.FanCompatible("DELL",true,2));
        Test("Missing fan methods are rejected",()=>!Compatibility.FanCompatible("LENOVO",false,2));
        Test("Unknown fan state is rejected",()=>!Compatibility.FanCompatible("LENOVO",true,99));
        Test("Valid Lenovo fan state is accepted",()=>Compatibility.FanCompatible("LENOVO",true,2));
        Test("Non-Lenovo cannot enable GPU writes",()=>!Compatibility.GpuCompatible("ASUS",true,1,1,0,1));
        Test("Incomplete GPU support is rejected",()=>!Compatibility.GpuCompatible("LENOVO",false,1,1,0,1)&&!Compatibility.GpuCompatible("LENOVO",true,1,0,0,1));
        Test("Unknown GPU values are rejected",()=>!Compatibility.GpuCompatible("LENOVO",true,1,1,2,1)&&!Compatibility.GpuCompatible("LENOVO",true,1,1,0,9));
        Test("Matching GPU schema is accepted",()=>Compatibility.GpuCompatible("LENOVO",true,1,1,0,1));
        Test("New user GPU switching is opt-in",()=>!new Options().IncludeGpu);
        Test("Automatic switching defaults off",()=>!new Options().AutoPower);
        Test("Brightness values are bounded",()=>Compatibility.BrightnessValue(-1,40)==40&&Compatibility.BrightnessValue(999,75)==75&&Compatibility.BrightnessValue(64,40)==64);
        Test("Another machine's backup is rejected",()=>!Compatibility.SameMachine("machine1","machine2"));
        Test("Unidentified legacy backup is rejected",()=>!Compatibility.SameMachine(null,"machine2")&&!Compatibility.SameMachine("",""));
        Test("Own backup is accepted",()=>Compatibility.SameMachine("machine1","machine1"));
        Test("AMD output alone never proves discrete GPU off",()=>GpuPresentation.Describe(0,1,"",false,0,1,@"PCI\VEN_1002").PowerHint==-1);
        Console.WriteLine(passed+" compatibility tests passed; no hardware accessed.");
    }
}
