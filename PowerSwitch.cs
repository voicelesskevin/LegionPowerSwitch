using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LegionPowerSwitch {
    static class GpuRequest {
        public static bool ReturnsToOriginal(int targetGs,int targetIg,int originalGs,int originalIg,bool hasPending,bool sameBoot){return hasPending && sameBoot && targetGs==originalGs && targetIg==originalIg;}
        public static void Cancel(int gs,int ig,Action<int> setGs,Action<int> setIg){setGs(gs);setIg(ig);}
        public static void Submit(int gs,int ig,int targetGs,int targetIg,bool replacePending,Action<int> setGs,Action<int> setIg,Action<string> log) {
            bool muxChanged=gs!=targetGs || replacePending;
            if(muxChanged)setGs(targetGs);
            if(ig!=targetIg) {
                try{setIg(targetIg);}catch(Exception ex){if(!muxChanged)throw;log("核显子模式留待重启后确认："+ex.Message);}
            }
            // MUX getters can report the running boot, not the pending boot target.
        }
        public static void Complete(int targetGs,int targetIg,Func<int> readGs,Func<int> readIg,Action<int> setIg) {
            if(readGs()!=targetGs)throw new Exception("重启后的显卡路由未达到请求目标，请在 Legion Zone 确认");
            if(readIg()!=targetIg)setIg(targetIg);
            if(readGs()!=targetGs || readIg()!=targetIg)throw new Exception("显卡子模式尚未确认，请在 Legion Zone 检查");
        }
    }
    public sealed class GpuDisplayState {
        public string Text;
        public int PowerHint;
    }
    static class GpuPresentation {
        public static GpuDisplayState Describe(int gs,int ig,string pending,bool sameBoot,int originalGs,int originalIg,string adapterId) {
            string current;int hint;
            if(sameBoot && pending!="")gs=originalGs;
            if((adapterId??"").IndexOf("VEN_10DE",StringComparison.OrdinalIgnoreCase)>=0){current="独显输出";hint=1;}
            else if((adapterId??"").IndexOf("VEN_8086",StringComparison.OrdinalIgnoreCase)>=0){current=gs==0&&ig==1?"核显模式":"核显输出（独显状态未确认）";hint=gs==0&&ig==1?0:-1;}
            else if((adapterId??"").IndexOf("VEN_1002",StringComparison.OrdinalIgnoreCase)>=0){current="AMD 输出（显卡状态未确认）";hint=-1;}
            else if(gs==1){current="独显直连（固件读回）";hint=1;}
            else if(ig==1){current="核显模式（固件读回）";hint=0;}
            else {current="混合模式";hint=-1;}
            return new GpuDisplayState{Text="当前 "+current+(pending==""?"":" · 目标 "+(pending=="Travel"?"核显":"独显")+(sameBoot?"（待重启验证）":"（重启后未确认）")),PowerHint=hint};
        }
    }
    public sealed class Options {
        public string MachineKey = "";
        public int TravelBrightness = 40;
        public int DeskBrightness = 75;
        public bool AutoPower = false;
        public bool IncludeGpu = false;
        public string LastMode = "";
        public string PendingGpu = "";
        public long PendingBoot = 0;
        public int PreviousGsync = -1;
        public int PreviousIgpu = -1;
    }
    public sealed class Snapshot {
        public string MachineKey;
        public string DisplayKey;
        public string Time;
        public string Scheme;
        public string AcOverlay;
        public string DcOverlay;
        public int Brightness;
        public int RefreshRate;
        public int Fan;
        public int Gsync;
        public int Igpu;
        public uint CpuMaxAc,CpuMaxDc,BoostAc,BoostDc;
    }
    public sealed class DisplayInfo {
        public string InstanceName;
        public string Device;
        public string Monitor;
        public string AdapterId;
        public int Width;
        public int Height;
        public int Refresh;
        public int[] Rates;
    }
    static class Native {
        [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] public static extern uint PowerGetUserConfiguredACPowerMode(out Guid guid);
        [DllImport("powrprof.dll")] public static extern uint PowerGetUserConfiguredDCPowerMode(out Guid guid);
        [DllImport("powrprof.dll")] public static extern uint PowerSetUserConfiguredACPowerMode(ref Guid guid);
        [DllImport("powrprof.dll")] public static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid guid);
        [DllImport("powrprof.dll")] public static extern uint PowerReadACValueIndex(IntPtr root,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
        [DllImport("powrprof.dll")] public static extern uint PowerReadDCValueIndex(IntPtr root,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr ptr);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE data, uint flags);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int index, ref DEVMODE mode);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE mode, IntPtr hwnd, uint flags, IntPtr param);
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DISPLAY_DEVICE {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceKey;
        }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DEVMODE {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string dmDeviceName;
            public ushort dmSpecVersion,dmDriverVersion,dmSize,dmDriverExtra;
            public uint dmFields;
            public int dmPositionX,dmPositionY;
            public uint dmDisplayOrientation,dmDisplayFixedOutput;
            public short dmColor,dmDuplex,dmYResolution,dmTTOption,dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel,dmPelsWidth,dmPelsHeight,dmDisplayFlags,dmDisplayFrequency;
            public uint dmICMMethod,dmICMIntent,dmMediaType,dmDitherType,dmReserved1,dmReserved2,dmPanningWidth,dmPanningHeight;
        }
    }
    static class Engine {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string TravelScheme = "15a41766-d965-4519-9ba3-08c15b38d101";
        public static readonly string DeskScheme = "15a41766-d965-4519-9ba3-08c15b38d102";
        public static readonly string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        static readonly Guid Efficient = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static Options Settings = ReadOptions();
        static HardwareProfile hardware;
        public static HardwareProfile Hardware {get{return hardware??(hardware=HardwareDetection.Detect());}set{hardware=value;}}
        public static string AppRoot {get{return AppDomain.CurrentDomain.BaseDirectory;}}
        static Options ReadOptions() {
            Options options;
            try{options=Load<Options>("settings.json")??new Options();}catch{options=new Options();}
            options.TravelBrightness=Compatibility.BrightnessValue(options.TravelBrightness,40);
            options.DeskBrightness=Compatibility.BrightnessValue(options.DeskBrightness,75);
            if(options.PendingGpu!="" && options.PendingGpu!="Travel" && options.PendingGpu!="Desk")options.PendingGpu="";
            return options;
        }
        public static bool OnAc { get { return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online; } }
        public static long BootStamp { get { return DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(Native.GetTickCount64())).Ticks; } }
        public static bool SameBoot(long stamp) { return Math.Abs(BootStamp - stamp) < TimeSpan.FromSeconds(60).Ticks; }
        public static void Save(string name, object data) {
            string path=Path.Combine(Root,name), temp=path+".tmp";
            File.WriteAllText(temp,Json.Serialize(data),new UTF8Encoding(false));
            if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
        public static T Load<T>(string name) where T:class {
            string path=Path.Combine(Root,name);
            if(!File.Exists(path)) return null;
            return Json.Deserialize<T>(File.ReadAllText(path));
        }
        public static void Log(string message) {
            File.AppendAllText(Path.Combine(Root,"actions.log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"  "+message+Environment.NewLine,Encoding.UTF8);
        }
        public static string Run(string file,string args) {
            using(var p=new Process()) {
                p.StartInfo=new ProcessStartInfo(file,args) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                p.Start();
                var output=p.StandardOutput.ReadToEndAsync(); var error=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(20000)) {p.Kill(); throw new Exception(file+" 操作超时");}
                string text=output.Result+error.Result;
                if(p.ExitCode!=0) throw new Exception(file+" ("+p.ExitCode+"): "+text.Trim());
                return text;
            }
        }
        public static string PowerCfg(string args) { return Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"powercfg.exe"),args); }
        static uint WmiCall(string cls,string method,string parameter,uint value,bool set) {
            using(var search=new ManagementObjectSearcher(@"root\WMI","SELECT * FROM "+cls)) {
                search.Options.Timeout=TimeSpan.FromSeconds(8);
                using(var results=search.Get()) foreach(ManagementObject obj in results) using(obj) {
                    using(var input=obj.GetMethodParameters(method)) {
                        if(set) input[parameter]=value;
                        using(var output=obj.InvokeMethod(method,input,null)) {
                            if(output==null){if(set)return 0;throw new Exception(method+" 未返回状态");}
                            var rv=output.Properties.Cast<PropertyData>().FirstOrDefault(p=>p.Name.Equals("ReturnValue",StringComparison.OrdinalIgnoreCase));
                            if(rv!=null && rv.Value is bool && !(bool)rv.Value) throw new Exception(method+" 被固件拒绝");
                            var data=output.Properties.Cast<PropertyData>().FirstOrDefault(p=>p.Name.Equals("Data",StringComparison.OrdinalIgnoreCase));
                            if(!set && data==null)throw new Exception(method+" 未返回 Data 字段："+string.Join(",",output.Properties.Cast<PropertyData>().Select(p=>p.Name)));
                            return data==null ? 0:Convert.ToUInt32(data.Value);
                        }
                    }
                }
            }
            throw new Exception("未找到联想硬件接口 "+cls);
        }
        public static int GetLenovo(string method) { return (int)WmiCall("LENOVO_GAMEZONE_DATA",method,null,0,false); }
        static void SetLenovo(string method,string param,int value) {
            if(method=="SetSmartFanMode" ? !Hardware.LenovoFan : !Hardware.LenovoGpu)throw new Exception("本机不支持该联想硬件接口");
            if(method=="SetSmartFanMode" && value!=1 && value!=2 && value!=3 && value!=255)throw new Exception("无效的联想性能模式");
            if(method=="SetGSyncStatus" && value!=0 && value!=1)throw new Exception("无效的显卡路由");
            if(method=="SetIGPUModeStatus" && (value<0 || value>2))throw new Exception("无效的核显子模式");
            WmiCall("LENOVO_GAMEZONE_DATA",method,param,(uint)value,true);
        }
        public static string Scheme() {
            IntPtr ptr; Check(Native.PowerGetActiveScheme(IntPtr.Zero,out ptr),"读取电源计划");
            try{return ((Guid)Marshal.PtrToStructure(ptr,typeof(Guid))).ToString();}finally{Native.LocalFree(ptr);}
        }
        static void Check(uint result,string action) { if(result!=0) throw new Exception(action+"失败，Windows 错误 "+result); }
        static string Overlay(bool ac) { Guid g; Check(ac?Native.PowerGetUserConfiguredACPowerMode(out g):Native.PowerGetUserConfiguredDCPowerMode(out g),"读取电源模式");return g.ToString(); }
        static void SetOverlay(bool ac,string id) {Guid g=new Guid(id);Check(ac?Native.PowerSetUserConfiguredACPowerMode(ref g):Native.PowerSetUserConfiguredDCPowerMode(ref g),"设置电源模式");}
        public static int Brightness() {
            string key=Compatibility.InternalPanel(HardwareDetection.Panels());
            if(key=="")throw new Exception("未能唯一识别内屏，已跳过亮度控制");
            using(var s=HardwareDetection.Search(@"root\WMI","SELECT * FROM WmiMonitorBrightness WHERE Active=True"))
            using(var items=s.Get()) foreach(ManagementObject o in items) using(o) {
                if(Compatibility.MonitorKey(Convert.ToString(o["InstanceName"]))==key)return Convert.ToInt32(o["CurrentBrightness"]);
            }
            throw new Exception("无法读取内屏亮度");
        }
        static void SetBrightness(int percent) {
            if(percent<10 || percent>100) throw new Exception("亮度应为 10 到 100");
            bool done=false;
            string key=Compatibility.InternalPanel(HardwareDetection.Panels());
            if(key=="")throw new Exception("未能唯一识别内屏，已跳过亮度控制");
            using(var s=HardwareDetection.Search(@"root\WMI","SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True"))
            using(var items=s.Get()) foreach(ManagementObject o in items) using(o) {
                if(Compatibility.MonitorKey(Convert.ToString(o["InstanceName"]))!=key)continue;
                using(var input=o.GetMethodParameters("WmiSetBrightness")) {input["Timeout"]=(uint)0;input["Brightness"]=(byte)percent;using(var result=o.InvokeMethod("WmiSetBrightness",input,null)) {if(result!=null){var rv=result.Properties.Cast<PropertyData>().FirstOrDefault(p=>p.Name.Equals("ReturnValue",StringComparison.OrdinalIgnoreCase));if(rv!=null && Convert.ToUInt32(rv.Value)!=0)throw new Exception("亮度设置被拒绝");}}}done=true;
            }
            if(!done)throw new Exception("未找到内屏亮度接口");
            if(Math.Abs(Brightness()-percent)>2)throw new Exception("内屏亮度读回校验失败");
        }
        static Native.DISPLAY_DEVICE NewDevice(){var d=new Native.DISPLAY_DEVICE();d.cb=Marshal.SizeOf(d);return d;}
        static Native.DEVMODE NewMode(){var d=new Native.DEVMODE();d.dmSize=(ushort)Marshal.SizeOf(d);return d;}
        public static DisplayInfo InternalDisplay() {
            string key=Compatibility.InternalPanel(HardwareDetection.Panels());
            if(key=="")throw new Exception("未能唯一识别内屏；不会调整外接屏");
            // Match the monitor instance, not its model or the primary-screen flag.
            for(uint i=0;i<32;i++) {
                var adapter=NewDevice();if(!Native.EnumDisplayDevices(null,i,ref adapter,0))break;
                if((adapter.StateFlags&1)==0)continue;
                for(uint j=0;j<16;j++) {
                    var monitor=NewDevice();if(!Native.EnumDisplayDevices(adapter.DeviceName,j,ref monitor,1))break;
                    if(Compatibility.MonitorKey(monitor.DeviceID)!=key)continue;
                    int attached=0;
                    for(uint m=0;m<16;m++){var peer=NewDevice();if(!Native.EnumDisplayDevices(adapter.DeviceName,m,ref peer,1))break;if((peer.StateFlags&1)!=0)attached++;}
                    if(attached>1)throw new Exception("内屏与其他屏幕处于复制输出，跳过刷新率调整");
                    var current=NewMode();if(!Native.EnumDisplaySettings(adapter.DeviceName,-1,ref current))throw new Exception("无法读取内屏模式");
                    var rates=new HashSet<int>();
                    for(int k=0;k<1000;k++){var mode=NewMode();if(!Native.EnumDisplaySettings(adapter.DeviceName,k,ref mode))break;if(mode.dmPelsWidth==current.dmPelsWidth && mode.dmPelsHeight==current.dmPelsHeight && mode.dmBitsPerPel==current.dmBitsPerPel)rates.Add((int)mode.dmDisplayFrequency);}
                    return new DisplayInfo{InstanceName=key,Device=adapter.DeviceName,Monitor=monitor.DeviceID,AdapterId=adapter.DeviceID,Width=(int)current.dmPelsWidth,Height=(int)current.dmPelsHeight,Refresh=(int)current.dmDisplayFrequency,Rates=rates.OrderBy(x=>x).ToArray()};
                }
            }
            throw new Exception("内屏与活动显示输出未匹配；外接屏不会被修改");
        }
        static void SetRefresh(int hz) {
            var info=InternalDisplay();
            if(!info.Rates.Contains(hz))throw new Exception("当前内屏不支持 "+hz+"Hz");
            if(info.Refresh==hz)return;
            var mode=NewMode();if(!Native.EnumDisplaySettings(info.Device,-1,ref mode))throw new Exception("读取内屏模式失败");mode.dmDisplayFrequency=(uint)hz;mode.dmFields=0x00400000;
            if(Native.ChangeDisplaySettingsEx(info.Device,ref mode,IntPtr.Zero,2,IntPtr.Zero)!=0)throw new Exception("显示驱动未通过刷新率预检");
            int result=Native.ChangeDisplaySettingsEx(info.Device,ref mode,IntPtr.Zero,1,IntPtr.Zero);
            if(result!=0)throw new Exception("刷新率切换失败，Windows 返回 "+result);
            if(InternalDisplay().Refresh!=hz)throw new Exception("刷新率读回与目标不一致");
        }
        static uint CpuValue(bool ac,bool boost){Guid scheme=new Guid(Scheme()),sub=new Guid("54533251-82be-4824-96c1-47b60b740d00"),setting=new Guid(boost?"be337238-0d82-4146-a960-4f3749d470c7":"bc5038f7-23e0-4960-96da-33abaf5935ec");uint value;Check(ac?Native.PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref sub,ref setting,out value):Native.PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref sub,ref setting,out value),"读取 CPU 策略");return value;}
        static int Optional(Func<int> read){try{return read();}catch{return -1;}}
        static uint OptionalCpu(bool ac,bool boost){try{return CpuValue(ac,boost);}catch{return uint.MaxValue;}}
        public static Snapshot Capture() {
            string key="";int refresh=-1;try{key=Compatibility.PanelModel(Compatibility.InternalPanel(HardwareDetection.Panels()));}catch{}try{var display=InternalDisplay();refresh=display.Refresh;}catch{}
            string ac=null,dc=null;try{if(Hardware.OverlayAc)ac=Overlay(true);}catch{}try{if(Hardware.OverlayDc)dc=Overlay(false);}catch{}
            return new Snapshot{MachineKey=Hardware.MachineKey,DisplayKey=key,Time=DateTime.Now.ToString("o"),Scheme=Scheme(),AcOverlay=ac,DcOverlay=dc,Brightness=Optional(Brightness),RefreshRate=refresh,Fan=Hardware.LenovoFan?Optional(()=>GetLenovo("GetSmartFanMode")):-1,Gsync=Hardware.LenovoGpu?Optional(()=>GetLenovo("GetGSyncStatus")):-1,Igpu=Hardware.LenovoGpu?Optional(()=>GetLenovo("GetIGPUModeStatus")):-1,CpuMaxAc=OptionalCpu(true,false),CpuMaxDc=OptionalCpu(false,false),BoostAc=OptionalCpu(true,true),BoostDc=OptionalCpu(false,true)};
        }
        public static Snapshot Original() {
            var original=Load<Snapshot>("original.json");
            if(original!=null && !Compatibility.SameMachine(original.MachineKey,Hardware.MachineKey))throw new Exception("恢复备份不属于本机或来自旧版，已停止使用。请先迁移备份，不能直接复制其他电脑的数据。");
            return original;
        }
        static bool Exists(string id){return PowerCfg("/list").IndexOf(id,StringComparison.OrdinalIgnoreCase)>=0;}
        static void WriteValue(string scheme,bool ac,string sub,string setting,int value){PowerCfg((ac?"/setacvalueindex ":"/setdcvalueindex ")+scheme+" "+sub+" "+setting+" "+value);}
        static void PrepareBackup() {
            if(Original()!=null)return;
            var original=Capture();
            Save("original.json",original);
            try{PowerCfg("/export \""+Path.Combine(Root,"original-power-plan.pow")+"\" "+original.Scheme);}catch(Exception ex){Log("原电源计划导出未完成，保留原计划标识："+ex.Message);}
        }
        public static void PreparePlans() {
            PrepareBackup();
            if(File.Exists(Path.Combine(Root,"plans-v1.ready")) && Exists(TravelScheme) && Exists(DeskScheme))return;
            foreach(string id in new[]{TravelScheme,DeskScheme})if(!Exists(id))PowerCfg("/duplicatescheme "+Balanced+" "+id);
            PowerCfg("/changename "+TravelScheme+" \"Legion Travel\"");PowerCfg("/changename "+DeskScheme+" \"Legion Desk\"");
            foreach(bool ac in new[]{true,false}) {
                foreach(string id in new[]{TravelScheme,DeskScheme}) {
                    bool low=id==TravelScheme || !ac;
                    WriteValue(id,ac,"SUB_PROCESSOR","PROCTHROTTLEMIN",5);
                    WriteValue(id,ac,"SUB_PROCESSOR","PROCTHROTTLEMAX",low?80:100);
                    WriteValue(id,ac,"SUB_PROCESSOR","PERFBOOSTMODE",low?0:2);
                    WriteValue(id,ac,"SUB_VIDEO","VIDEOIDLE",low?180:900);
                    WriteValue(id,ac,"SUB_SLEEP","STANDBYIDLE",low?600:1800);
                }
            }
            File.WriteAllText(Path.Combine(Root,"plans-v1.ready"),DateTime.Now.ToString("o"));
        }
        public static List<string> Apply(bool travel,bool gpu,bool record) {
            if(!travel && !OnAc)throw new Exception("NORM 模式需要接电源；电池供电时请使用 ECO。");
            var previous=Capture();
            try {
            var results=new List<string>();
            PrepareBackup();
            if(Hardware.LenovoFan)try {
                SetLenovo("SetSmartFanMode","Data",travel?1:2);Thread.Sleep(350);
                if(GetLenovo("GetSmartFanMode")!=(travel?1:2))throw new Exception("可能被联想软件覆盖");
                results.Add(travel?"联想安静模式":"联想均衡模式（白标）");
            }catch(Exception ex){results.Add("联想模式未完成："+ex.Message);}
            else results.Add("联想扩展不支持，保留厂商性能模式");
            bool plan=false,overlay=false;
            try {
                PreparePlans();PowerCfg("/setactive "+(travel?TravelScheme:DeskScheme));
                if(Scheme()!=(travel?TravelScheme:DeskScheme))throw new Exception("电源计划校验失败");
                plan=true;results.Add(travel?"省电策略 / CPU 上限 80% / 关闭睿频":"均衡策略 / CPU 上限 100% / 启用睿频");
            }catch(Exception ex){results.Add("独立电源计划未完成："+ex.Message);}
            if(Hardware.OverlayAc)try{SetOverlay(true,(travel?Efficient:Guid.Empty).ToString());overlay=true;results.Add(travel?"接电：最佳能效":"接电：均衡");}catch(Exception ex){results.Add("接电模式未完成："+ex.Message);}
            if(Hardware.OverlayDc)try{SetOverlay(false,Efficient.ToString());overlay=true;}catch(Exception ex){results.Add("电池模式未完成："+ex.Message);}
            if(!plan && !overlay)throw new Exception("Windows 电源策略不可用；未应用模式。"+string.Join("；",results));
            var original=Original();
            if(original==null)throw new Exception("缺少本机恢复备份，已停止进一步调整");
            try {
                var d=InternalDisplay();
                if(original.DisplayKey!=Compatibility.PanelModel(d.InstanceName))throw new Exception("内屏与首次备份不同，跳过调整");
                int hz=travel?Compatibility.EcoRefresh(d.Rates,d.Refresh):original.RefreshRate;
                if(hz<1)throw new Exception("首次刷新率未记录");
                SetRefresh(hz);results.Add("内屏 "+InternalDisplay().Refresh+"Hz");
            }catch(Exception ex){results.Add("刷新率已跳过："+ex.Message);}
            try {int target=travel?Settings.TravelBrightness:Settings.DeskBrightness;SetBrightness(target);results.Add("内屏亮度 "+Brightness()+"%");}catch(Exception ex){results.Add("亮度已跳过："+ex.Message);}
            if(gpu)try{results.Add(PrepareGpu(travel));}catch(Exception ex){results.Add("显卡未完成："+ex.Message);}
            if(record){Settings.LastMode=travel?"Travel":"Desk";Save("settings.json",Settings);}
            Log(string.Join(" | ",results));return results;
            } catch {
                var restoreErrors=Restore(previous);
                if(restoreErrors.Count>0)Log("切换失败后的回退异常："+string.Join(" | ",restoreErrors));
                throw;
            }
        }
        static string PrepareGpu(bool travel) {
            if(!Hardware.LenovoGpu)throw new Exception("本机未通过联想显卡接口兼容性检测");
            if(!HasSafePending())throw new Exception("显卡待处理记录无效，已停止写入");
            if(travel && Screen.AllScreens.Length>1)throw new Exception("检测到多屏，请先断开外接屏再切核显");
            if(GetLenovo("IsSupportGSync")<=0 || GetLenovo("IsSupportIGPUMode")<=0)throw new Exception("固件未报告完整的显卡切换支持");
            int gs=GetLenovo("GetGSyncStatus"),ig=GetLenovo("GetIGPUModeStatus"),targetGs=travel?0:1,targetIg=travel?1:0;
            if(GpuRequest.ReturnsToOriginal(targetGs,targetIg,Settings.PreviousGsync,Settings.PreviousIgpu,Settings.PendingGpu!="",SameBoot(Settings.PendingBoot))) {
                CancelGpu();return "已发送撤销显卡切换请求；保持本次启动的显卡模式，无需为此重启";
            }
            if(Settings.PendingGpu!="" && !SameBoot(Settings.PendingBoot)) {
                if(gs==targetGs && ig==targetIg){Settings.PendingGpu="";Save("settings.json",Settings);return travel?"核显模式":"独显直连";}
            }
            if(gs==targetGs && ig==targetIg && Settings.PendingGpu=="")return travel?"核显模式":"独显直连";
            if(Settings.PendingGpu=="" || !SameBoot(Settings.PendingBoot)){Settings.PreviousGsync=gs;Settings.PreviousIgpu=ig;}
            // Firmware requests a boot target; never reset a GPU or force-close its applications.
            try {
                GpuRequest.Submit(gs,ig,targetGs,targetIg,Settings.PendingGpu!="",value=>SetLenovo("SetGSyncStatus","Data",value),value=>SetLenovo("SetIGPUModeStatus","mode",value),Log);
            }catch {
                try{SetLenovo("SetGSyncStatus","Data",gs);SetLenovo("SetIGPUModeStatus","mode",ig);}catch(Exception ex){Log("显卡目标回退失败："+ex.Message);}
                throw;
            }
            Settings.MachineKey=Hardware.MachineKey;Settings.PendingGpu=travel?"Travel":"Desk";Settings.PendingBoot=BootStamp;Save("settings.json",Settings);
            return (travel?"核显":"独显直连")+"请求已发送；尚未验证，需手动重启并重新打开本软件确认";
        }
        public static string CompleteGpuAfterBoot() {
            if(Settings.PendingGpu=="" || SameBoot(Settings.PendingBoot))return "";
            if(!Hardware.LenovoGpu || !HasSafePending())return "本机不支持验证该待处理显卡目标；保留记录，未写入固件";
            bool travel=Settings.PendingGpu=="Travel";
            try {
                GpuRequest.Complete(travel?0:1,travel?1:0,()=>GetLenovo("GetGSyncStatus"),()=>GetLenovo("GetIGPUModeStatus"),value=>SetLenovo("SetIGPUModeStatus","mode",value));
                Settings.PendingGpu="";Save("settings.json",Settings);
                string message=(travel?"核显":"独显直连")+"固件设置已核对；实际显示与续航仍需测试";Log(message);return message;
            }catch(Exception ex){Log("显卡重启后验证未完成："+ex.Message);return "显卡验证未完成："+ex.Message;}
        }
        public static void CancelGpu() {
            if(Settings.PendingGpu=="")return;
            if(!Hardware.LenovoGpu || !HasSafePending())throw new Exception("无法安全撤销此显卡记录，请在厂商软件中确认");
            GpuRequest.Cancel(Settings.PreviousGsync,Settings.PreviousIgpu,value=>SetLenovo("SetGSyncStatus","Data",value),value=>SetLenovo("SetIGPUModeStatus","mode",value));
            Settings.PendingGpu="";Save("settings.json",Settings);Log("撤销显卡待重启目标");
        }
        static bool HasSafePending(){return Settings.PendingGpu=="" || (Compatibility.SameMachine(Settings.MachineKey,Hardware.MachineKey) && Settings.PreviousGsync>=0 && Settings.PreviousGsync<=1 && Settings.PreviousIgpu>=0 && Settings.PreviousIgpu<=2 && Original()!=null);}
        public static bool HasRedundantGpuRequest(){return Hardware.LenovoGpu && HasSafePending() && GpuRequest.ReturnsToOriginal(Settings.PendingGpu=="Travel"?0:1,Settings.PendingGpu=="Travel"?1:0,Settings.PreviousGsync,Settings.PreviousIgpu,Settings.PendingGpu!="",SameBoot(Settings.PendingBoot));}
        public static List<string> Restore(Snapshot s) {
            var errors=new List<string>();
            Guid scheme;
            if(s==null || !Compatibility.SameMachine(s.MachineKey,Hardware.MachineKey) || !Guid.TryParse(s.Scheme,out scheme)){errors.Add("恢复备份不属于本机或内容无效；未修改任何设置");return errors;}
            Action<string,Action> step=(name,action)=>{try{action();}catch(Exception ex){errors.Add(name+": "+ex.Message);}};
            if(Hardware.LenovoFan && s.Fan>0)step("联想模式",()=>SetLenovo("SetSmartFanMode","Data",s.Fan));
            step("电源计划",()=>PowerCfg("/setactive "+scheme));
            if(Hardware.OverlayAc && s.AcOverlay!=null)step("接电模式",()=>SetOverlay(true,s.AcOverlay));
            if(Hardware.OverlayDc && s.DcOverlay!=null)step("电池模式",()=>SetOverlay(false,s.DcOverlay));
            if(s.RefreshRate>0)step("刷新率",()=>{if(Compatibility.PanelModel(InternalDisplay().InstanceName)!=s.DisplayKey)throw new Exception("内屏型号已变更，未调整刷新率");SetRefresh(s.RefreshRate);});
            if(s.Brightness>=0)step("亮度",()=>{if(Compatibility.PanelModel(Compatibility.InternalPanel(HardwareDetection.Panels()))!=s.DisplayKey)throw new Exception("内屏型号已变更，未调整亮度");SetBrightness(s.Brightness);});
            return errors;
        }
        public static GpuDisplayState ReadGpuDisplay() {
            if(!Hardware.LenovoGpu)return new GpuDisplayState{Text="由系统或厂商软件管理",PowerHint=-1};
            int gs=GetLenovo("GetGSyncStatus"),ig=GetLenovo("GetIGPUModeStatus");
            string adapterId="";try{adapterId=InternalDisplay().AdapterId;}catch{}
            return GpuPresentation.Describe(gs,ig,Settings.PendingGpu,SameBoot(Settings.PendingBoot),Settings.PreviousGsync,Settings.PreviousIgpu,adapterId);
        }
        public static bool CanBrightness(){try{return Brightness()>=0;}catch{return false;}}
        public static bool CanPendingGpu(){try{return Hardware.LenovoGpu && Settings.PendingGpu!="" && HasSafePending();}catch{return false;}}
        public static string ZonePath(){foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}){string path=Path.Combine(root,"Lenovo","LegionZone","LZMain.exe");if(File.Exists(path))return path;}return null;}
        public static void Probe(){
            DisplayInfo display=null;string displayError="";
            try{display=InternalDisplay();}catch(Exception ex){displayError=ex.Message;}
            Save("diagnostic.json",new{Capabilities=Hardware,Snapshot=Capture(),Display=display,DisplayError=displayError,BrightnessSupported=CanBrightness(),Screens=Screen.AllScreens.Select(s=>new{Device=s.DeviceName,Width=s.Bounds.Width,Height=s.Bounds.Height}).ToArray()});
        }
        public static void Startup(bool enabled) {
            string task="LegionPowerSwitch-"+Environment.UserName;
            if(!enabled){try{Run("schtasks.exe","/Delete /TN \""+task+"\" /F");}catch{}return;}
            string exe=System.Security.SecurityElement.Escape(Application.ExecutablePath);
            string user=System.Security.SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string xml="<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"+user+"</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers><Principals><Principal id=\"Author\"><UserId>"+user+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings><Actions Context=\"Author\"><Exec><Command>"+exe+"</Command><Arguments>--tray</Arguments></Exec></Actions></Task>";
            string file=Path.Combine(Root,"startup-task.xml");File.WriteAllText(file,xml,Encoding.Unicode);Run("schtasks.exe","/Create /TN \""+task+"\" /XML \""+file+"\" /F");
        }
        public static bool StartupEnabled() {try{Run("schtasks.exe","/Query /TN \"LegionPowerSwitch-"+Environment.UserName+"\"");return true;}catch{return false;}}
        public static bool SelfTest() {
            var report=new Dictionary<string,object>();Snapshot before=null;
            try {
                before=Capture();report["Before"]=before;Save("self-test-original.json",before);
                if(!OnAc)throw new Exception("自检仅在接电状态执行");
                report["Travel"]=Apply(true,false,false);var travel=Capture();report["TravelState"]=travel;
                int ecoRate=before.RefreshRate;try{ecoRate=Compatibility.EcoRefresh(InternalDisplay().Rates,before.RefreshRate);}catch{}
                if(travel.Scheme!=TravelScheme || (Hardware.LenovoFan && travel.Fan!=1) || (before.RefreshRate>0 && travel.RefreshRate!=ecoRate) || (before.Brightness>=0 && Math.Abs(travel.Brightness-Settings.TravelBrightness)>2) || (Hardware.OverlayAc && travel.AcOverlay!=Efficient.ToString()) || travel.CpuMaxAc!=80 || travel.CpuMaxDc!=80 || travel.BoostAc!=0 || travel.BoostDc!=0)throw new Exception("外出模式读回校验失败");
                if(travel.Gsync!=before.Gsync || travel.Igpu!=before.Igpu)throw new Exception("显卡设置意外改变");
                report["Desk"]=Apply(false,false,false);var desk=Capture();report["DeskState"]=desk;
                if(desk.Scheme!=DeskScheme || (Hardware.LenovoFan && desk.Fan!=2) || desk.RefreshRate!=before.RefreshRate || (before.Brightness>=0 && Math.Abs(desk.Brightness-Settings.DeskBrightness)>2) || (Hardware.OverlayAc && desk.AcOverlay!=Guid.Empty.ToString()) || desk.CpuMaxAc!=100 || desk.CpuMaxDc!=80 || desk.BoostAc!=2 || desk.BoostDc!=0)throw new Exception("常驻模式读回校验失败");
                report["Passed"]=true;
            }catch(Exception ex){report["Passed"]=false;report["Error"]=ex.ToString();}
            finally {
                if(before!=null){report["RestoreErrors"]=Restore(before);try{var after=Capture();report["After"]=after;report["Restored"]=after.Scheme==before.Scheme && after.Fan==before.Fan && after.Brightness==before.Brightness && after.RefreshRate==before.RefreshRate && after.AcOverlay==before.AcOverlay && after.DcOverlay==before.DcOverlay && after.Gsync==before.Gsync && after.Igpu==before.Igpu;}catch(Exception ex){report["RestoreError"]=ex.Message;}}
                Save("self-test.json",report);
            }
            return report.ContainsKey("Passed") && (bool)report["Passed"] && report.ContainsKey("Restored") && (bool)report["Restored"];
        }
    }
    sealed class MainWindow:Form {
        static readonly Color Canvas=Color.FromArgb(20,22,26),Ink=Color.FromArgb(234,237,242),Muted=Color.FromArgb(159,167,180),Surface=Color.FromArgb(37,40,47);
        Label power,gpu,display,mode,result;
        Button travel,desk,cancel,restart;
        CheckBox include,auto,startup;
        TrackBar travelBrightness,deskBrightness;
        NotifyIcon tray;
        System.Windows.Forms.Timer timer;
        bool busy=false,quitting=false,lastAc;
        public MainWindow(bool render) {
            Text="LegionPowerSwitch";ClientSize=new Size(680,660);MinimumSize=new Size(696,699);StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Microsoft YaHei UI",10);BackColor=Canvas;ForeColor=Ink;Icon=Icon.ExtractAssociatedIcon(Path.Combine(Engine.Root,"LegionPowerSwitch.exe"));AutoScaleMode=AutoScaleMode.Dpi;
            var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};Controls.Add(viewport);
            var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(24),ColumnCount=1,RowCount=11};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));viewport.Controls.Add(layout);
            var headerImage=Image.FromFile(Path.Combine(Engine.Root,"app-icon.png"));Disposed+=(s,e)=>headerImage.Dispose();
            var header=new TableLayoutPanel{ColumnCount=2,RowCount=1,Dock=DockStyle.Fill};header.RowStyles.Add(new RowStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,46));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.Controls.Add(new PictureBox{Image=headerImage,SizeMode=PictureBoxSizeMode.Zoom,Width=32,Height=32,Margin=new Padding(0,7,12,7)},0,0);header.Controls.Add(Title("LegionPowerSwitch",18),1,0);AddRow(layout,header,46);
            power=Title("读取电源状态…",11);power.ForeColor=Muted;AddRow(layout,power,32);
            var commands=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            travel=Command("ECO",Color.FromArgb(43,119,237),Color.White);desk=Command("NORM",Color.FromArgb(242,244,248),Color.FromArgb(25,28,34));desk.Margin=new Padding(6,8,0,8);commands.Controls.Add(travel,0,0);commands.Controls.Add(desk,1,0);AddRow(layout,commands,82);
            mode=Title("",10);gpu=Title("",10);display=Title("",10);gpu.ForeColor=display.ForeColor=Muted;
            var status=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<3;i++)status.RowStyles.Add(new RowStyle(SizeType.Percent,100f/3));status.Controls.Add(mode);status.Controls.Add(gpu);status.Controls.Add(display);foreach(Control c in status.Controls)c.Dock=DockStyle.Fill;AddRow(layout,status,88);
            var brightness=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};brightness.RowStyles.Add(new RowStyle(SizeType.Percent,100));brightness.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));brightness.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            travelBrightness=BrightnessControl(brightness,0,"ECO 亮度",Engine.Settings.TravelBrightness);deskBrightness=BrightnessControl(brightness,1,"NORM 亮度",Engine.Settings.DeskBrightness);AddRow(layout,brightness,82);
            include=new CheckBox{Text=Engine.Hardware.LenovoGpu?"联想显卡：核显 / 独显直连（需重启验证）":"联想显卡切换：本机不支持",Checked=Engine.Hardware.LenovoGpu&&Engine.Settings.IncludeGpu,Dock=DockStyle.Fill,AutoSize=true,FlatStyle=FlatStyle.Flat,Enabled=Engine.Hardware.LenovoGpu};AddRow(layout,include,32);
            auto=new CheckBox{Text="插拔电源自动调整电源与内屏（不切显卡）",Checked=Engine.Settings.AutoPower,Dock=DockStyle.Fill,AutoSize=true,FlatStyle=FlatStyle.Flat};AddRow(layout,auto,32);
            startup=new CheckBox{Text="登录 Windows 后驻留托盘",Checked=!render&&Engine.StartupEnabled(),Dock=DockStyle.Fill,AutoSize=true,FlatStyle=FlatStyle.Flat};AddRow(layout,startup,32);
            result=Title("",9);result.ForeColor=Muted;result.AutoEllipsis=false;result.TextAlign=ContentAlignment.TopLeft;result.Margin=new Padding(3,12,3,12);AddRow(layout,result,102);
            result.TextChanged+=(s,e)=>FitResult();result.SizeChanged+=(s,e)=>FitResult();FitResult();
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
            cancel=Small("撤销显卡");restart=Small("重启");cancel.Enabled=restart.Enabled=false;var restore=Small("恢复原设置");var zone=Small(Engine.ZonePath()==null?"电源设置":"Legion Zone");var logs=Small("日志");
            actions.Controls.AddRange(new Control[]{cancel,restart,restore,zone,logs});AddRow(layout,actions,60);
            travel.Click+=async(s,e)=>await Apply(true,false);desk.Click+=async(s,e)=>await Apply(false,false);
            include.CheckedChanged+=(s,e)=>SaveUi();auto.CheckedChanged+=(s,e)=>SaveUi();
            startup.CheckedChanged+=(s,e)=>{try{Engine.Startup(startup.Checked);}catch(Exception ex){MessageBox.Show(ex.Message,Text);}};
            cancel.Click+=(s,e)=>{if(busy)return;try{Engine.CancelGpu();result.Text="已发送撤销显卡切换请求；当前模式以读回状态为准。";RefreshState();}catch(Exception ex){result.Text="撤销未完成："+ex.Message;}};
            restart.Click+=(s,e)=>{if(busy)return;if(MessageBox.Show("即将重新启动电脑。请先保存所有文档和工作。\n\n现在重启？",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)Process.Start("shutdown.exe","/r /t 0");};
            restore.Click+=async(s,e)=>{if(busy)return;busy=true;try{auto.Checked=false;var original=Engine.Original();if(original==null){result.Text="尚未修改设置。";return;}var errors=await Task.Run(()=>{Engine.CancelGpu();return Engine.Restore(original);});Engine.Settings.LastMode="";Engine.Save("settings.json",Engine.Settings);result.Text=errors.Count==0?"已恢复已记录的电源、性能、亮度和刷新率设置。":string.Join("\n",errors);RefreshState();}catch(Exception ex){result.Text=ex.Message;}finally{busy=false;}};
            zone.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(Engine.ZonePath()??"ms-settings:powersleep"){UseShellExecute=true});}catch(Exception ex){result.Text=ex.Message;}};
            logs.Click+=(s,e)=>{string f=Path.Combine(Engine.Root,"actions.log");if(File.Exists(f))Process.Start("notepad.exe","\""+f+"\"");};
            tray=new NotifyIcon{Icon=Icon,Text="LegionPowerSwitch",Visible=!render};var menu=new ContextMenuStrip{BackColor=Surface,ForeColor=Ink,Renderer=new ToolStripProfessionalRenderer(new DarkMenuColors())};
            menu.Items.Add("打开控制器",null,(s,e)=>ShowWindow());menu.Items.Add("ECO · 外出省电",null,async(s,e)=>await Apply(true,false));menu.Items.Add("NORM · 接电均衡",null,async(s,e)=>await Apply(false,false));menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出",null,(s,e)=>{if(busy)return;quitting=true;Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowWindow();
            FormClosing+=(s,e)=>{if(!quitting&&!render){e.Cancel=true;Hide();}else tray.Dispose();};
            // Poll status only; Lenovo hotkeys and Zone remain authoritative between explicit transitions.
            lastAc=Engine.OnAc;timer=new System.Windows.Forms.Timer{Interval=5000};timer.Tick+=async(s,e)=>{if(busy)return;bool ac=Engine.OnAc;if(ac!=lastAc){lastAc=ac;if(auto.Checked)await Apply(!ac,true);}RefreshState();};
            Shown+=async(s,e)=>{if(render)return;try{if(Engine.HasRedundantGpuRequest()){busy=true;try{await Task.Run(()=>Engine.CancelGpu());result.Text="已发送撤销重复显卡请求；无需为此重启。";}finally{busy=false;}}RefreshState();UpdateCapabilities();timer.Start();if(Engine.Settings.PendingGpu!=""&&!Engine.SameBoot(Engine.Settings.PendingBoot)){busy=true;try{result.Text=await Task.Run(()=>Engine.CompleteGpuAfterBoot());RefreshState();}finally{busy=false;}}else if(auto.Checked)await Apply(!Engine.OnAc,true);}catch(Exception ex){busy=false;result.Text="初始化未完成："+ex.Message;}};
            if(render){UpdatePowerStatus(true,0.8f);mode.Text=Engine.Hardware.LenovoFan?"电源计划：NORM · 联想均衡（白标）模式":"电源计划：NORM · Windows 通用模式";UpdateGpuStatus(Engine.Hardware.LenovoGpu?"当前 独显输出":"由系统或厂商软件管理",Engine.Hardware.LenovoGpu?1:-1);display.Text="内屏：2560 × 1600 · 240Hz · 亮度 75% · 2 个屏幕";result.Text="";}
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);int dark=1;try{if(Native.DwmSetWindowAttribute(Handle,20,ref dark,4)!=0)Native.DwmSetWindowAttribute(Handle,19,ref dark,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
        sealed class DarkMenuColors:ProfessionalColorTable {
            public override Color ToolStripDropDownBackground{get{return Surface;}}
            public override Color ImageMarginGradientBegin{get{return Surface;}}
            public override Color ImageMarginGradientMiddle{get{return Surface;}}
            public override Color ImageMarginGradientEnd{get{return Surface;}}
            public override Color MenuItemSelected{get{return Color.FromArgb(56,63,76);}}
            public override Color MenuItemBorder{get{return Color.FromArgb(73,83,99);}}
            public override Color MenuBorder{get{return Color.FromArgb(67,73,83);}}
            public override Color SeparatorDark{get{return Color.FromArgb(67,73,83);}}
            public override Color SeparatorLight{get{return Surface;}}
        }
        static Label Title(string text,float size){return new Label{Text=text,Dock=DockStyle.Fill,ForeColor=Ink,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Microsoft YaHei UI",size, size>=18?FontStyle.Bold:FontStyle.Regular),AutoEllipsis=true};}
        static void AddRow(TableLayoutPanel panel,Control control,int height){int row=panel.RowStyles.Count;panel.RowStyles.Add(new RowStyle(SizeType.Absolute,height));panel.Controls.Add(control,0,row);control.Dock=DockStyle.Fill;}
        void FitResult(){var panel=result.Parent as TableLayoutPanel;if(panel==null)return;int width=Math.Max(80,result.ClientSize.Width);var measured=TextRenderer.MeasureText(string.IsNullOrEmpty(result.Text)?" ":result.Text,result.Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix);int height=Math.Max(result.Font.Height*4,measured.Height+4)+result.Margin.Vertical;var row=panel.RowStyles[panel.GetRow(result)];if(Math.Abs(row.Height-height)>0.5f)row.Height=height;}
        sealed class ThemedButton:Button {
            public static readonly Color DisabledInk=Color.FromArgb(153,162,176);
            protected override void OnPaint(PaintEventArgs e){if(Enabled){base.OnPaint(e);return;}e.Graphics.Clear(Color.FromArgb(30,33,39));ControlPaint.DrawBorder(e.Graphics,ClientRectangle,Color.FromArgb(56,62,73),ButtonBorderStyle.Solid);TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,DisabledInk,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);}
            protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
        }
        static Button Command(string text,Color color,Color ink){var button=new ThemedButton{Text=text,Dock=DockStyle.Fill,Margin=new Padding(0,8,6,8),BackColor=color,ForeColor=ink,FlatStyle=FlatStyle.Flat,Font=new Font("Microsoft YaHei UI",17,FontStyle.Bold),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=ControlPaint.Light(color,0.08f);button.FlatAppearance.MouseDownBackColor=ControlPaint.Dark(color,0.08f);return button;}
        static Button Small(string text){var button=new ThemedButton{Text=text,AutoSize=true,Height=32,Margin=new Padding(0,4,8,4),Padding=new Padding(6,0,6,0),Font=new Font("Microsoft YaHei UI",9),BackColor=Surface,ForeColor=Ink,FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,Cursor=Cursors.Hand};button.FlatAppearance.BorderColor=Color.FromArgb(67,73,83);button.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,54,64);return button;}
        static TrackBar BrightnessControl(TableLayoutPanel parent,int column,string name,int value){var p=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,8,12,0)};var label=Title(name+"  "+value+"%",10);label.Dock=DockStyle.Top;label.Height=26;var slider=new TrackBar{Dock=DockStyle.Bottom,BackColor=Canvas,AutoSize=false,Minimum=10,Maximum=100,Value=value,TickStyle=TickStyle.None,SmallChange=5,LargeChange=10,Height=36};slider.ValueChanged+=(s,e)=>label.Text=name+"  "+slider.Value+"%";p.Controls.Add(slider);p.Controls.Add(label);parent.Controls.Add(p,column,0);return slider;}
        void SaveUi(){Engine.Settings.IncludeGpu=include.Checked;Engine.Settings.AutoPower=auto.Checked;Engine.Settings.TravelBrightness=travelBrightness.Value;Engine.Settings.DeskBrightness=deskBrightness.Value;Engine.Save("settings.json",Engine.Settings);}
        void ShowWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
        async Task Apply(bool low,bool automatic) {
            if(busy)return;
            if(low && !automatic && include.Checked && Screen.AllScreens.Length>1){MessageBox.Show("请先断开外接屏，再切换外出核显模式。\n\n也可以取消“同时准备核显 / 独显直连”，先只应用省电设置。",Text,MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            SaveUi();busy=true;travel.Enabled=false;desk.Enabled=false;cancel.Enabled=restart.Enabled=false;include.Enabled=auto.Enabled=startup.Enabled=travelBrightness.Enabled=deskBrightness.Enabled=false;result.Text="正在切换…";
            try{var lines=await Task.Run(()=>Engine.Apply(low,!automatic&&include.Checked,true));result.Text=string.Join("\n",lines);RefreshState();if(!Visible){tray.BalloonTipTitle=low?"ECO":"NORM";tray.BalloonTipText=string.Join("；",lines);tray.ShowBalloonTip(4000);}}
            catch(Exception ex){result.Text="切换未完成："+ex.Message;Engine.Log(ex.ToString());if(!Visible){tray.BalloonTipTitle="切换未完成";tray.BalloonTipText=ex.Message;tray.ShowBalloonTip(5000);}}
            finally{busy=false;travel.Enabled=true;desk.Enabled=true;auto.Enabled=startup.Enabled=true;UpdateCapabilities();RefreshState();}
        }
        void UpdateCapabilities(){include.Enabled=Engine.Hardware.LenovoGpu;bool brightness=Engine.CanBrightness();travelBrightness.Enabled=deskBrightness.Enabled=brightness;}
        static string FanText(int fan){switch(fan){case 1:return "安静";case 2:return "均衡（白标）";case 3:return "性能（红标）";case 255:return "自定义";default:return "扩展（"+fan+"）";}}
        void UpdatePowerStatus(bool onAc,float battery){power.Text=(onAc?"电源已连接":"电池供电")+(battery>=0&&battery<=1?" · 电池 "+Math.Round(battery*100)+"%":" · 电量不可读");power.ForeColor=onAc?Color.FromArgb(74,222,128):Color.FromArgb(251,146,60);}
        void UpdateGpuStatus(string text,int powerHint){gpu.Text="显卡："+text;gpu.ForeColor=powerHint==1?Color.FromArgb(251,146,60):powerHint==0?Color.FromArgb(74,222,128):Muted;}
        void RefreshState(){
            UpdatePowerStatus(Engine.OnAc,SystemInformation.PowerStatus.BatteryLifePercent);
            try{string scheme=Engine.Scheme();mode.Text="电源计划："+(scheme==Engine.TravelScheme?"ECO":scheme==Engine.DeskScheme?"NORM":"其他");}catch{mode.Text="电源计划：不可读";}
            if(Engine.Hardware.LenovoFan)try{mode.Text+=" · 联想"+FanText(Engine.GetLenovo("GetSmartFanMode"))+"模式";}catch{mode.Text+=" · 联想状态不可读";}
            else mode.Text+=" · Windows 通用模式";
            try{var graphics=Engine.ReadGpuDisplay();UpdateGpuStatus(graphics.Text,graphics.PowerHint);}catch{UpdateGpuStatus("状态不可读",-1);}
            try{var d=Engine.InternalDisplay();display.Text="内屏："+d.Width+" × "+d.Height+" · "+d.Refresh+"Hz";}catch{display.Text="内屏：未识别或无法独立调整";}
            int brightness=-1;try{brightness=Engine.Brightness();}catch{}
            if(brightness>=0)display.Text+=" · 亮度 "+brightness+"%";
            display.Text+=" · "+Screen.AllScreens.Length+" 个屏幕";
            cancel.Enabled=restart.Enabled=!busy && Engine.CanPendingGpu();
        }
    }
    static class Program {
        [STAThread] static int Main(string[] args) {
            try {
                Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Contains("--self-test-generic") || args.Contains("--render-generic")){var real=Engine.Hardware;Engine.Hardware=new HardwareProfile{Manufacturer="Generic Windows",Model="Simulation",MachineKey=real.MachineKey,OverlayAc=real.OverlayAc,OverlayDc=real.OverlayDc};}
                if(args.Contains("--self-test") || args.Contains("--self-test-generic"))return Engine.SelfTest()?0:1;
                if(args.Contains("--probe")){Engine.Probe();return 0;}
                if(args.Contains("--restore")){Engine.Settings.AutoPower=false;Engine.CancelGpu();var errors=Engine.Restore(Engine.Original());Engine.Save("settings.json",Engine.Settings);Engine.Save("restore-result.json",new{Restored=errors.Count==0,Errors=errors});return errors.Count==0?0:1;}
                if(args.Contains("--render") || args.Contains("--render-generic")){using(var form=new MainWindow(true)){form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(Engine.Root,"controller-preview.png"));}form.Close();}return 0;}
                bool created;using(var mutex=new Mutex(true,@"Local\LegionPowerSwitch-"+WindowsIdentity.GetCurrent().User.Value,out created)){if(!created){MessageBox.Show("控制器已在运行，可从系统托盘打开。","电源切换");return 0;}var main=new MainWindow(false);if(args.Contains("--tray")){main.Shown+=(s,e)=>main.Hide();}Application.Run(main);}
                return 0;
            }catch(Exception ex){try{Engine.Log(ex.ToString());}catch{}if(!args.Any(a=>a.StartsWith("--") && a!="--tray"))MessageBox.Show(ex.Message,"电源控制器",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
}
