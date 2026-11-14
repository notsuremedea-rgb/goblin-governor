using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace Goblin {
public interface IStateStore {string Read(string key);void Write(string key,string value);void Delete(string key);}
public sealed class DiskState:IStateStore {
 public string Read(string key){string p=Files.PathFor(key);return File.Exists(p)?File.ReadAllText(p):null;}
 public void Write(string key,string value){Files.Write(key,value);}
 public void Delete(string key){string p=Files.PathFor(key);if(File.Exists(p))File.Delete(p);}
}
public sealed class ConfirmedProfile {
 public string Uuid;public Profile Value;
 public string Encode(){Value.Validate();return "1|"+Uuid+"|"+Value.Encode()+"|manual-confirmed";}
 public static ConfirmedProfile Decode(string s){if(string.IsNullOrEmpty(s))throw new Exception("No manually confirmed startup profile. Apply and confirm one first.");string[] p=s.Trim().Split('|');if(p.Length!=4||p[0]!="1"||!p[1].StartsWith("GPU-",StringComparison.Ordinal)||p[3]!="manual-confirmed")throw new Exception("Invalid verified startup record. Automatic apply blocked.");return new ConfirmedProfile{Uuid=p[1],Value=Profile.Decode(p[2])};}
}
public sealed class StartupResult {public bool Applied;public string Message;}
public static class StartupPolicy {
 public static readonly IStateStore Store=new DiskState();
 public static void Block(IStateStore store,string why){store.Write("startup-paused.txt",why);}
 public static void Confirm(IStateStore store,ConfirmedProfile value){store.Write("verified-startup.txt",value.Encode());store.Delete("startup-paused.txt");store.Delete("startup-inflight.txt");}
 public static StartupResult Run(IStateStore store,string session,bool recoveryPending,Func<ConfirmedProfile,bool> ready,Action<ConfirmedProfile> apply,Action wait,int attempts){
  if(store.Read("startup.enabled")==null)return new StartupResult{Message="Start with Windows is off. No automatic apply."};
  if(recoveryPending||store.Read("startup-inflight.txt")!=null)Block(store,"An interrupted session needs manual review. Recovery takes priority; no automatic reapply.");
  string paused=store.Read("startup-paused.txt");if(paused!=null)return new StartupResult{Message="STARTUP PAUSED: "+paused};
  if(store.Read("startup-attempt.txt")==session)return new StartupResult{Message="Already attempted this sign-in. No repeated automatic apply."};
  try {
   ConfirmedProfile profile=ConfirmedProfile.Decode(store.Read("verified-startup.txt"));
   store.Write("startup-attempt.txt",session);store.Write("startup-inflight.txt",session);
   string error="NVIDIA driver or GPU did not become ready.";
   for(int i=0;i<attempts;i++){
    if(store.Read("startup.enabled")==null){store.Delete("startup-inflight.txt");return new StartupResult{Message="Startup disabled. Automatic apply cancelled."};}
    bool ok=false;try{ok=ready(profile);}catch(Exception e){error=e.Message;}
    if(ok){
     // Check cancellation and recovery again immediately before the ONLY write attempt.
     if(store.Read("startup.enabled")==null||store.Read("startup-paused.txt")!=null)throw new Exception("Startup cancelled or paused before apply.");
     apply(profile);store.Delete("startup-inflight.txt");return new StartupResult{Applied=true,Message="Startup applied once: "+profile.Value.Description};
    }
    if(i+1<attempts)wait();
   }
   throw new Exception("Driver/GPU readiness timed out: "+error);
  }catch(Exception e){Block(store,e.Message);store.Delete("startup-inflight.txt");return new StartupResult{Message="STARTUP FAILED: "+e.Message+" Manual confirmation is required before another automatic attempt."};}
 }
 public static string SignInId(){
  IntPtr buffer=Marshal.AllocHGlobal(128);
  try{int needed;using(WindowsIdentity identity=WindowsIdentity.GetCurrent()){if(!GetTokenInformation(identity.Token,10,buffer,128,out needed))throw new Exception("Cannot identify Windows sign-in. Automatic apply blocked.");return identity.User.Value+":"+Marshal.ReadInt64(buffer,8).ToString();}}finally{Marshal.FreeHGlobal(buffer);}
 }
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int tokenClass,IntPtr information,int length,out int returnLength);
}
public static class DriverReady {
 // A separate read-only process bounds a stalled native readiness call.
 public static IGpu Open(string uuid){
  string exe=System.Reflection.Assembly.GetExecutingAssembly().Location;
  using(Process p=Process.Start(new ProcessStartInfo(exe,"--ready "+(uuid??"auto")){UseShellExecute=false,CreateNoWindow=true})){
   if(!p.WaitForExit(2500)){try{p.Kill();}catch{}throw new Exception("NVIDIA readiness query did not respond.");}
   if(p.ExitCode!=0)throw new Exception("NVIDIA driver/GPU is unavailable (probe "+p.ExitCode+").");
  }
  return new Nvidia(uuid);
 }
 public static int Probe(string uuid){try{using(Nvidia g=new Nvidia(uuid=="auto"?null:uuid)){Reading r=g.ClockRange();if(!r.Available)return 2;g.Sample();return 0;}}catch{return 3;}}
}
}
