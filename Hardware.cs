using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace LegionPowerSwitch {
    public sealed class HardwareProfile {
        public string Manufacturer = "", Model = "", MachineKey = "";
        public bool LenovoFan, LenovoGpu, OverlayAc, OverlayDc;
        public string[] Notes = new string[0];
    }
    public sealed class PanelConnection {
        public string Instance;
        public bool Active;
        public uint Technology;
    }
    static class Compatibility {
        public static bool InternalTechnology(uint value) { return value==6 || value==11 || value==13 || value==0x80000000U; }
        public static string MonitorKey(string name) {
            string value=(name??"").Trim().ToUpperInvariant();
            if(value.StartsWith(@"\\?\")) {
                var parts=value.Substring(4).Split('#');
                if(parts.Length<3)return "";
                value=string.Join(@"\",parts.Take(3));
            } else {
                int last=value.LastIndexOf('_');
                int instance;
                if(last>=0 && int.TryParse(value.Substring(last+1),out instance))value=value.Substring(0,last);
            }
            return value;
        }
        public static string InternalPanel(IEnumerable<PanelConnection> panels) {
            var found=panels.Where(p=>p.Active && InternalTechnology(p.Technology)).Select(p=>MonitorKey(p.Instance)).Where(p=>p!="").Distinct().ToArray();
            return found.Length==1?found[0]:"";
        }
        public static string PanelModel(string instance){return string.Join(@"\",MonitorKey(instance).Split('\\').Take(2));}
        public static int EcoRefresh(int[] rates,int current) {
            if(rates==null)return current;
            if(rates.Contains(60))return 60;
            return rates.Where(r=>r>=48 && r<=current).DefaultIfEmpty(current).Min();
        }
        public static bool FanCompatible(string manufacturer,bool methods,int fan) {
            return string.Equals(manufacturer,"LENOVO",StringComparison.OrdinalIgnoreCase) && methods && (fan==1 || fan==2 || fan==3 || fan==255);
        }
        public static bool GpuCompatible(string manufacturer,bool methods,int syncSupport,int igpuSupport,int sync,int igpu) {
            return string.Equals(manufacturer,"LENOVO",StringComparison.OrdinalIgnoreCase) && methods && syncSupport>0 && igpuSupport>0 && (sync==0 || sync==1) && (igpu==0 || igpu==1 || igpu==2);
        }
        public static bool SameMachine(string snapshot,string current) { return !string.IsNullOrEmpty(snapshot) && snapshot==current; }
        public static int BrightnessValue(int value,int fallback) { return value>=10 && value<=100?value:fallback; }
    }
    static class HardwareDetection {
        public static ManagementObjectSearcher Search(string scope,string query) {
            var search=new ManagementObjectSearcher(scope,query);
            search.Options.Timeout=TimeSpan.FromSeconds(5);
            return search;
        }
        public static PanelConnection[] Panels() {
            var list=new List<PanelConnection>();
            using(var search=Search(@"root\WMI","SELECT Active,InstanceName,VideoOutputTechnology FROM WmiMonitorConnectionParams"))
            using(var items=search.Get())foreach(ManagementObject item in items)using(item)
                list.Add(new PanelConnection{Instance=(string)item["InstanceName"],Active=Convert.ToBoolean(item["Active"]),Technology=Convert.ToUInt32(item["VideoOutputTechnology"])});
            return list.ToArray();
        }
        static bool Method(ManagementClass cls,string name,string parameter) {
            var method=cls.Methods[name];
            return method!=null && (parameter==null || (method.InParameters!=null && method.InParameters.Properties.Cast<System.Management.PropertyData>().Any(p=>p.Name.Equals(parameter,StringComparison.OrdinalIgnoreCase))));
        }
        public static HardwareProfile Detect() {
            var profile=new HardwareProfile();var notes=new List<string>();
            string identity=Environment.MachineName;
            try {
                using(var search=Search(@"root\CIMV2","SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
                using(var items=search.Get())foreach(ManagementObject item in items)using(item){profile.Manufacturer=Convert.ToString(item["Manufacturer"]);profile.Model=Convert.ToString(item["Model"]);break;}
                using(var search=Search(@"root\CIMV2","SELECT UUID FROM Win32_ComputerSystemProduct"))
                using(var items=search.Get())foreach(ManagementObject item in items)using(item){identity+="|"+Convert.ToString(item["UUID"]);break;}
            }catch(Exception ex){notes.Add("设备识别："+ex.Message);}
            using(var hash=SHA256.Create())profile.MachineKey=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity+"|"+profile.Manufacturer+"|"+profile.Model))).Replace("-","");
            try{Guid mode;profile.OverlayAc=Native.PowerGetUserConfiguredACPowerMode(out mode)==0;}catch{}
            try{Guid mode;profile.OverlayDc=Native.PowerGetUserConfiguredDCPowerMode(out mode)==0;}catch{}
            if(string.Equals(profile.Manufacturer,"LENOVO",StringComparison.OrdinalIgnoreCase)) {
                try {
                    using(var cls=new ManagementClass(@"root\WMI:LENOVO_GAMEZONE_DATA")) {
                        cls.Get();
                        bool fan=Method(cls,"GetSmartFanMode",null)&&Method(cls,"SetSmartFanMode","Data");
                        profile.LenovoFan=Compatibility.FanCompatible(profile.Manufacturer,fan,fan?Engine.GetLenovo("GetSmartFanMode"):-1);
                        bool gpu=Method(cls,"GetGSyncStatus",null)&&Method(cls,"SetGSyncStatus","Data")&&Method(cls,"GetIGPUModeStatus",null)&&Method(cls,"SetIGPUModeStatus","mode")&&Method(cls,"IsSupportGSync",null)&&Method(cls,"IsSupportIGPUMode",null);
                        if(gpu)profile.LenovoGpu=Compatibility.GpuCompatible(profile.Manufacturer,true,Engine.GetLenovo("IsSupportGSync"),Engine.GetLenovo("IsSupportIGPUMode"),Engine.GetLenovo("GetGSyncStatus"),Engine.GetLenovo("GetIGPUModeStatus"));
                    }
                }catch(Exception ex){notes.Add("联想扩展不可用："+ex.Message);}
            }
            profile.Notes=notes.ToArray();return profile;
        }
    }
}
