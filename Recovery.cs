using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Security.Principal;
using System.Security;

namespace Goblin {
public static class Files {
 public static readonly string Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"state");
 public static string PathFor(string name){Directory.CreateDirectory(Root);return Path.Combine(Root,name);}
 public static void Write(string name,string value){string path=PathFor(name),temp=path+".tmp";using(FileStream f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){byte[] b=System.Text.Encoding.UTF8.GetBytes(value);f.Write(b,0,b.Length);f.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
 public static void Log(string value){try{File.AppendAllText(PathFor("actions.log"),DateTime.Now.ToString("s")+" "+value+Environment.NewLine);}catch{}}
 public static string Journal {get{return PathFor("recovery.txt");}}
 public static int Mask(){if(!File.Exists(Journal))return 0;string[] a=File.ReadAllText(Journal).Split('|');if(a.Length!=3)throw new Exception("Invalid recovery record");int m=int.Parse(a[2]);if(m<1||m>15)throw new Exception("Invalid recovery controls");return m;}
 public static void Recover(){Recover(uuid=>new Nvidia(uuid));} public static void Recover(Func<string,IGpu> connect){if(!File.Exists(Journal))return;StartupPolicy.Block(StartupPolicy.Store,"Recovery was required. Manually apply and confirm before automatic startup resumes.");string[] a=File.ReadAllText(Journal).Split('|');int m=Mask();using(IGpu gpu=connect(a[1]))ControlLogic.Reset(gpu,m);File.Delete(Journal);Log("Recovery reset verified.");}
}
public static class SessionShutdown {
 public static bool Finish(bool operationInProgress,Action reset,Action<string> pause,int timeoutMs=2000){
  try{
   if(operationInProgress)throw new Exception("GPU operation was still in progress at Windows shutdown.");
   // Finish ownership cleanup before returning WM_ENDSESSION to Windows.
   // Native driver calls may stall; never hold shutdown indefinitely.
   var work=System.Threading.Tasks.Task.Run(reset);
   if(!work.Wait(timeoutMs))throw new Exception("Windows shutdown reset timed out.");
   Files.Log("Windows session ending: owned settings reset; startup preference preserved.");
   return true;
  }catch(Exception e){
   string why="Shutdown cleanup incomplete: "+e.GetBaseException().Message;
   pause(why);Files.Log(why);return false;
  }
 }
}
public sealed class Watchdog : IDisposable {
 Process child; readonly string token=Guid.NewGuid().ToString("N");
 public bool Healthy {get{return child!=null&&!child.HasExited;}}
 public Watchdog(){
  Process own=Process.GetCurrentProcess();
  child=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--guard "+own.Id+" "+own.StartTime.ToUniversalTime().Ticks+" "+token){UseShellExecute=false,CreateNoWindow=true});
  string ready=Files.PathFor(token+".ready");
  for(int i=0;i<100&&!File.Exists(ready);i++){if(child.HasExited)throw new Exception("Recovery helper could not start.");Thread.Sleep(50);}
  if(!File.Exists(ready))throw new Exception("Recovery helper did not become ready.");
  File.Delete(ready);
 }
 public void Arm(string uuid,int mask){if(!Healthy)throw new Exception("Recovery helper is not running. Apply blocked.");Files.Write("recovery.txt",token+"|"+uuid+"|"+mask);}
 public void Dispose(){if(child!=null)child.Dispose();}
 public static void Run(string[] args){Run(args,()=>Files.Recover());} public static void Run(string[] args,Action recover){
  string token=args[3];if(token.Length!=32) return;
  Process owner;
  try{owner=Process.GetProcessById(int.Parse(args[1]));if(owner.StartTime.ToUniversalTime().Ticks!=long.Parse(args[2]))return;}catch{return;}
  Files.Write(token+".ready","ready");
  owner.WaitForExit();
  if(!File.Exists(Files.Journal))return;
  string record=File.ReadAllText(Files.Journal);if(!record.StartsWith(token+"|",StringComparison.Ordinal))return;
  StartupPolicy.Block(StartupPolicy.Store,"The main app exited with owned settings. Recovery ran; automatic reapplication is paused.");
  for(int attempt=0;attempt<3;attempt++){
   try{recover();return;}catch(Exception e){Files.Log("Recovery attempt failed: "+e.Message);Thread.Sleep(1000);}
  }
  Files.Log("RESET NOT VERIFIED. Restart Windows before tuning again.");
 }
}
public static class Startup {
 static string TaskName {get{return "GoblinGovernor-"+WindowsIdentity.GetCurrent().User.Value;}}
 public static bool Enabled {get{return File.Exists(Files.PathFor("startup.enabled"));}}
 public static void Set(bool enabled){
  if(enabled){
   ConfirmedProfile.Decode(StartupPolicy.Store.Read("verified-startup.txt"));if(StartupPolicy.Store.Read("startup-paused.txt")!=null)throw new Exception("Startup is paused after failure. Manually apply and confirm first.");
   string exe=System.Reflection.Assembly.GetExecutingAssembly().Location;
   string sid=WindowsIdentity.GetCurrent().User.Value;
   string xml="<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"+sid+"</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers><Principals><Principal id=\"Author\"><UserId>"+sid+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings><Actions Context=\"Author\"><Exec><Command>"+SecurityElement.Escape(exe)+"</Command><Arguments>--startup</Arguments><WorkingDirectory>"+SecurityElement.Escape(AppDomain.CurrentDomain.BaseDirectory)+"</WorkingDirectory></Exec></Actions></Task>";
   string path=Files.PathFor("startup.xml");File.WriteAllText(path,xml,System.Text.Encoding.Unicode);
   Run("/Create /TN \""+TaskName+"\" /XML \""+path+"\" /F");Files.Write("startup.enabled","1");
  }else{
   string marker=Files.PathFor("startup.enabled");if(File.Exists(marker))File.Delete(marker);
   dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();dynamic folder=service.GetFolder("\\");
   try{folder.GetTask(TaskName);}catch(System.Runtime.InteropServices.COMException e){if((uint)e.ErrorCode==0x80070002u)return;throw;}
   folder.DeleteTask(TaskName,0);
  }
 }
 static void Run(string args){using(Process p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"schtasks.exe"),args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){string result=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new Exception("Startup task could not be changed: "+result);}}
}
}


