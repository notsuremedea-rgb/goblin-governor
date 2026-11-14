using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Goblin {
public sealed class Reading {
 public bool Available; public int Value, Min, Max; public string Reason;
 public static Reading No(string why) { return new Reading { Reason=why }; }
}
public interface IGpu : IDisposable {
 string Name {get;} string Uuid {get;}
 Reading Core(); Reading Memory(); Reading Power();
 void SetCore(int value); void SetMemory(int value); void SetPower(int value);
}
public interface IClockLock { Reading ClockRange(); void LockCore(int mhz); void UnlockCore(); }
public interface IAdaptiveClockLock { Reading AdaptiveRange(); void LockAdaptive(int minimum,int maximum); }
public interface ITelemetry { string Sample(); }
public sealed class Nvidia : IGpu, IClockLock, IAdaptiveClockLock, ITelemetry {
 IntPtr device; bool opened;
 public string Name {get;private set;} public string Uuid {get;private set;}
 [StructLayout(LayoutKind.Sequential)] struct Offset {
  public uint version,type,pstate; public int value,min,max;
  public Offset(uint t) { version=0x1000018;type=t;pstate=0;value=min=max=0; }
 }
 public Nvidia(string uuid) {
  string library=System.IO.Path.Combine(Environment.SystemDirectory,"nvml.dll");
  if (LoadLibraryEx(library,IntPtr.Zero,0x800)==IntPtr.Zero) throw new Exception("NVIDIA management library is unavailable.");
  Check(nvmlInit_v2()); opened=true;
  try {
   if(string.IsNullOrEmpty(uuid)) {uint count;Check(nvmlDeviceGetCount_v2(out count));if(count!=1)throw new Exception("This build requires exactly one NVIDIA GPU.");Check(nvmlDeviceGetHandleByIndex_v2(0,out device));}
   else Check(nvmlDeviceGetHandleByUUID(uuid,out device));
   StringBuilder b=new StringBuilder(128);Check(nvmlDeviceGetUUID(device,b,128));Uuid=b.ToString();
   b.Clear();Check(nvmlDeviceGetName(device,b,128));Name=b.ToString();
  } catch {Dispose();throw;}
 }
 static void Check(int r) {if(r!=0)throw new Exception(Error(r));}
 static string Error(int r) {return r==3?"UNAVAILABLE: driver reports not supported":r==4?"Administrator permission required":Marshal.PtrToStringAnsi(nvmlErrorString(r))+" (NVML "+r+")";}
 Reading Read(uint type) {
  try {Offset o=new Offset(type);int r=nvmlDeviceGetClockOffsets(device,ref o);if(r!=0)return Reading.No(Error(r));return new Reading{Available=true,Value=o.value,Min=o.min,Max=o.max};}
  catch(EntryPointNotFoundException){return Reading.No("UNAVAILABLE: driver has no clock-offset API");}
 }
 public Reading Core(){return Read(0);} public Reading Memory(){return Read(2);}
 public Reading Power(){uint v,def,min,max;int r=nvmlDeviceGetPowerManagementLimit(device,out v);if(r!=0)return Reading.No(Error(r));r=nvmlDeviceGetPowerManagementDefaultLimit(device,out def);if(r!=0)return Reading.No(Error(r));r=nvmlDeviceGetPowerManagementLimitConstraints(device,out min,out max);if(r!=0 || def==0)return Reading.No("UNAVAILABLE: no usable power range");return new Reading{Available=true,Value=(int)v,Min=(int)min,Max=(int)def};}
 void Set(uint type,int value) {Offset o=new Offset(type);Check(nvmlDeviceGetClockOffsets(device,ref o));if(value>0||value<o.min||value>o.max)throw new Exception("Offset outside the driver's allowed underclock range.");o.value=value;Check(nvmlDeviceSetClockOffsets(device,ref o));}
 public void SetCore(int v){Set(0,v);} public void SetMemory(int v){Set(2,v);}
 public Reading ClockRange(){uint max;int r=nvmlDeviceGetMaxClockInfo(device,0,out max);return r==0?new Reading{Available=true,Min=300,Max=(int)Math.Min(max,1800u),Value=1200}:Reading.No(Error(r));}
 public void LockCore(int mhz){Reading range=ClockRange();if(!range.Available||mhz<range.Min||mhz>range.Max)throw new Exception("Core frequency outside the available diagnostic range.");try{int result=nvmlDeviceSetGpuLockedClocks(device,(uint)mhz,(uint)mhz);if(result==3)throw new NotSupportedException("UNAVAILABLE: driver rejects frequency locking on this GPU.");Check(result);}catch(EntryPointNotFoundException){throw new NotSupportedException("UNAVAILABLE: driver has no frequency-lock API.");}}
 public Reading AdaptiveRange(){uint max;int r=nvmlDeviceGetMaxClockInfo(device,0,out max);return r==0?new Reading{Available=true,Min=100,Max=(int)Math.Min(max,2200u)}:Reading.No(Error(r));}
 public void LockAdaptive(int minimum,int maximum){Reading r=AdaptiveRange();if(!r.Available||minimum<100||minimum>300||maximum<minimum||maximum>r.Max)throw new Exception("Adaptive range outside the permitted request limits.");try{int result=nvmlDeviceSetGpuLockedClocks(device,(uint)minimum,(uint)maximum);if(result==3)throw new NotSupportedException("UNAVAILABLE: driver rejects adaptive frequency locking.");Check(result);}catch(EntryPointNotFoundException){throw new NotSupportedException("UNAVAILABLE: driver has no frequency-lock API.");}}
 public void UnlockCore(){Check(nvmlDeviceResetGpuLockedClocks(device));}
 public string Sample(){uint c,m,t,p,s;int r=nvmlDeviceGetClockInfo(device,0,out c);if(r!=0)throw new Exception(Error(r));Check(nvmlDeviceGetClockInfo(device,2,out m));Check(nvmlDeviceGetTemperature(device,0,out t));int pr=nvmlDeviceGetPowerUsage(device,out p);Check(nvmlDeviceGetPerformanceState(device,out s));return c+","+m+","+t+","+(pr==0?(p/1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture):"N/A")+",P"+s;}
 public void SetPower(int v){Reading p=Power();if(!p.Available)throw new Exception(p.Reason);if(v<p.Min||v>p.Max)throw new Exception("Power limit outside the permitted reduction range.");Check(nvmlDeviceSetPowerManagementLimit(device,(uint)v));}
 public void Dispose(){if(opened){nvmlShutdown();opened=false;}}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string file,IntPtr h,uint flags);
 [DllImport("nvml.dll")] static extern int nvmlInit_v2();
 [DllImport("nvml.dll")] static extern int nvmlShutdown();
 [DllImport("nvml.dll")] static extern IntPtr nvmlErrorString(int r);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetCount_v2(out uint count);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index,out IntPtr d);
 [DllImport("nvml.dll",CharSet=CharSet.Ansi)] static extern int nvmlDeviceGetHandleByUUID(string uuid,out IntPtr d);
 [DllImport("nvml.dll",CharSet=CharSet.Ansi)] static extern int nvmlDeviceGetName(IntPtr d,StringBuilder name,uint length);
 [DllImport("nvml.dll",CharSet=CharSet.Ansi)] static extern int nvmlDeviceGetUUID(IntPtr d,StringBuilder name,uint length);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetClockOffsets(IntPtr d,ref Offset o);
 [DllImport("nvml.dll")] static extern int nvmlDeviceSetClockOffsets(IntPtr d,ref Offset o);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetPowerManagementLimit(IntPtr d,out uint v);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr d,out uint v);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr d,out uint min,out uint max);
 [DllImport("nvml.dll")] static extern int nvmlDeviceSetPowerManagementLimit(IntPtr d,uint value);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetMaxClockInfo(IntPtr d,uint type,out uint value);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetClockInfo(IntPtr d,uint type,out uint value);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr d,uint sensor,out uint value);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetPowerUsage(IntPtr d,out uint value);
 [DllImport("nvml.dll")] static extern int nvmlDeviceGetPerformanceState(IntPtr d,out uint value);
 [DllImport("nvml.dll")] static extern int nvmlDeviceSetGpuLockedClocks(IntPtr d,uint min,uint max);
 [DllImport("nvml.dll")] static extern int nvmlDeviceResetGpuLockedClocks(IntPtr d);
}
public sealed class Profile {
 public bool Core=true,Memory,Power,Lock,Adaptive; public int CoreMHz=-100,MemoryMHz=-250,PowerPercent=85,LockMHz=1800,LockMin=300;
 public string Encode(){return (Core?"1":"0")+","+CoreMHz+","+(Memory?"1":"0")+","+MemoryMHz+","+(Power?"1":"0")+","+PowerPercent+","+(Lock?"1":"0")+","+LockMHz+","+(Adaptive?"1":"0")+","+LockMin;}
 public Profile Copy(){return Decode(Encode());}
 public string Description {get{return Lock?(Adaptive?"Adaptive "+LockMin+"–"+LockMHz+" MHz (experimental)":"Fixed "+LockMHz+" MHz"):"Core "+(Core?CoreMHz.ToString():"off")+", memory "+(Memory?MemoryMHz.ToString():"off")+", power "+(Power?PowerPercent+"%":"off");}}
 static bool Flag(string s){if(s!="0"&&s!="1")throw new Exception("Invalid profile flag");return s=="1";}
 public static Profile Decode(string s){string[] p=s.Trim().Split(',');if(p.Length!=6&&p.Length!=8&&p.Length!=10)throw new Exception("Invalid saved profile");Profile v=new Profile{Core=Flag(p[0]),CoreMHz=int.Parse(p[1]),Memory=Flag(p[2]),MemoryMHz=int.Parse(p[3]),Power=Flag(p[4]),PowerPercent=int.Parse(p[5]),Lock=p.Length>=8&&Flag(p[6]),LockMHz=p.Length>=8?int.Parse(p[7]):1800,Adaptive=p.Length==10&&Flag(p[8]),LockMin=p.Length==10?int.Parse(p[9]):300};v.Validate();return v;}
 public void Validate(){if(CoreMHz>0||CoreMHz< -300||MemoryMHz>0||MemoryMHz< -1000||PowerPercent<80||PowerPercent>100||LockMin<100||LockMin>300||LockMHz<(Adaptive?LockMin:300)||LockMHz>(Adaptive?2200:1800))throw new Exception("Profile is outside the permitted request limits.");if(Adaptive&&!Lock)throw new Exception("Adaptive mode requires clock locking.");if(Core&&Lock)throw new Exception("Choose either a core offset or a frequency lock.");if(!Core&&!Memory&&!Power&&!Lock)throw new Exception("Select a control first.");}
}
public static class ControlLogic {
 public static void Apply(IGpu gpu,Profile p,Action<int> arm) {
  p.Validate();Reading c=gpu.Core(),m=gpu.Memory(),w=gpu.Power();
  if(p.Core&&(!c.Available||p.CoreMHz<c.Min||p.CoreMHz>c.Max))throw new Exception("Core offset unavailable or out of range.");
  if(p.Memory&&(!m.Available||p.MemoryMHz<m.Min||p.MemoryMHz>m.Max))throw new Exception("Memory offset unavailable or out of range.");
  int watts=0;if(p.Power){if(!w.Available)throw new Exception(w.Reason);watts=(int)Math.Round(w.Max*p.PowerPercent/100.0);if(watts<w.Min)throw new Exception("Power selection is below driver minimum.");}
  IClockLock locks=gpu as IClockLock;if(p.Lock&&!p.Adaptive&&(locks==null||!locks.ClockRange().Available||p.LockMHz>locks.ClockRange().Max))throw new Exception("Frequency-lock control unavailable.");
  IAdaptiveClockLock adaptive=gpu as IAdaptiveClockLock;if(p.Lock&&p.Adaptive&&(adaptive==null||!adaptive.AdaptiveRange().Available||p.LockMHz>adaptive.AdaptiveRange().Max))throw new Exception("Adaptive clock control unavailable or maximum above driver limit.");
  int mask=(p.Core?1:0)|(p.Memory?2:0)|(p.Power?4:0)|(p.Lock?8:0);
  arm(mask); // Persist ownership BEFORE the first driver write.
  if(p.Core)gpu.SetCore(p.CoreMHz);if(p.Memory)gpu.SetMemory(p.MemoryMHz);if(p.Power)gpu.SetPower(watts);
  if(p.Core)Verify(gpu.Core(),p.CoreMHz);if(p.Memory)Verify(gpu.Memory(),p.MemoryMHz);if(p.Power)Verify(gpu.Power(),watts);
  if(p.Lock){if(p.Adaptive)adaptive.LockAdaptive(p.LockMin,p.LockMHz);else locks.LockCore(p.LockMHz); }
 }
 public static void Reset(IGpu gpu,int mask){
  string errors="";
  if((mask&8)!=0)try{IClockLock locks=gpu as IClockLock;if(locks==null)throw new Exception("Frequency reset unavailable");locks.UnlockCore();}catch(Exception e){errors+="Clock lock: "+e.Message+"; ";}
  if((mask&1)!=0)try{if(!gpu.Core().Available||gpu.Core().Value!=0)gpu.SetCore(0);Verify(gpu.Core(),0);}catch(Exception e){errors+="Core: "+e.Message+"; ";}
  if((mask&2)!=0)try{if(!gpu.Memory().Available||gpu.Memory().Value!=0)gpu.SetMemory(0);Verify(gpu.Memory(),0);}catch(Exception e){errors+="Memory: "+e.Message+"; ";}
  if((mask&4)!=0)try{Reading w=gpu.Power();if(!w.Available)throw new Exception(w.Reason);if(w.Value!=w.Max)gpu.SetPower(w.Max);Verify(gpu.Power(),w.Max);}catch(Exception e){errors+="Power: "+e.Message+"; ";}
  if(errors!="")throw new Exception(errors);
 }
 static void Verify(Reading r,int value){if(!r.Available||r.Value!=value)throw new Exception("Driver readback did not confirm the requested value.");}
}
}

