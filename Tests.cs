using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
namespace Goblin {
class MemoryState:IStateStore {
 public Dictionary<string,string> Values=new Dictionary<string,string>();
 public string Read(string k){return Values.ContainsKey(k)?Values[k]:null;}
 public void Write(string k,string v){Values[k]=v;}public void Delete(string k){Values.Remove(k);}
}
class FakeGpu:IGpu,IClockLock,IAdaptiveClockLock {
 public int FixedCalls,AdaptiveCalls,UnlockCalls,Min,Max,C,M,W=85000;public bool Reject=false,ResetFailure,DiskReset;
 public string Name{get{return "Fake GPU";}}public string Uuid{get{return "GPU-fake";}}
 public Reading Core(){return new Reading{Available=true,Value=C,Min=-1000,Max=1000};}
 public Reading Memory(){return new Reading{Available=true,Value=M,Min=-2000,Max=6000};}
 public Reading Power(){return new Reading{Available=true,Value=W,Min=5000,Max=85000};}
 public void SetCore(int n){C=n;}public void SetMemory(int n){M=n;}public void SetPower(int n){W=n;}
 public Reading ClockRange(){return new Reading{Available=true,Min=300,Max=1800};}
 public Reading AdaptiveRange(){return new Reading{Available=true,Min=100,Max=2200};}
 public void LockCore(int n){FixedCalls++;Min=Max=n;if(Reject)throw new Exception("Rejected");}
 public void LockAdaptive(int min,int max){AdaptiveCalls++;Min=min;Max=max;if(Reject)throw new Exception("Rejected");}
 public void UnlockCore(){UnlockCalls++;if(ResetFailure)throw new Exception("Driver reset failure");if(DiskReset)Files.Write("fake-reset.txt","reset-only");}
 public void Dispose(){}
}
class Tests {
 static int count;
 static void Check(bool condition,string title){if(!condition)throw new Exception("FAIL: "+title);count++;Console.WriteLine("PASS: "+title);}
 static Profile Fixed(){return new Profile{Core=false,Lock=true,LockMHz=1800};}
 static MemoryState Enabled(){MemoryState s=new MemoryState();StartupPolicy.Confirm(s,new ConfirmedProfile{Uuid="GPU-fake",Value=Fixed()});s.Write("startup.enabled","1");return s;}
 [STAThread]static void Main(string[] args){
  if(args.Length>0&&args[0]=="--guard"){Watchdog.Run(args,()=>Files.Recover(uuid=>new FakeGpu{DiskReset=true}));return;}
  if(args.Length>0&&args[0]=="--owner-clean"){using(Watchdog w=new Watchdog()){w.Arm("GPU-fake",8);ControlLogic.Reset(new FakeGpu(),8);File.Delete(Files.Journal);}return;}
  if(args.Length>0&&args[0]=="--owner-test"){using(Watchdog w=new Watchdog()){w.Arm("GPU-fake",8);Environment.Exit(27);}return;}
  if(args.Length>0&&args[0]=="--preview-test"){Application.EnableVisualStyles();using(MainForm f=new MainForm(false,true)){f.Show();Application.DoEvents();using(Bitmap b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png"));}}return;}
  FakeGpu g=new FakeGpu();bool armed=false;ControlLogic.Apply(g,Fixed(),m=>{armed=m==8;Check(g.FixedCalls==0,"ownership written before fixed-lock call");});Check(armed&&g.FixedCalls==1&&g.AdaptiveCalls==0&&g.Min==1800&&g.Max==1800,"fixed lock stays exactly 1800 / 1800");
  foreach(int cap in new int[]{1800,1900,2200})foreach(int low in new int[]{100,300}){g=new FakeGpu();ControlLogic.Apply(g,new Profile{Core=false,Lock=true,Adaptive=true,LockMin=low,LockMHz=cap},m=>{});Check(g.AdaptiveCalls==1&&g.FixedCalls==0&&g.Min==low&&g.Max==cap,"adaptive distinct path "+low+" / "+cap);}
  bool rejected=false;try{new Profile{Core=false,Lock=true,Adaptive=true,LockMin=300,LockMHz=250}.Validate();}catch{rejected=true;}Check(rejected,"inverted range rejected");
  Check(!Profile.Decode("0,-100,0,-250,0,85,1,1800").Adaptive,"legacy fixed profile stays fixed");
  MemoryState s=Enabled();int probes=0,applies=0;StartupResult result=StartupPolicy.Run(s,"login-a",false,p=>++probes==3,p=>applies++,()=>{},5);Check(result.Applied&&applies==1&&probes==3,"wait for readiness then apply once");
  result=StartupPolicy.Run(s,"login-a",false,p=>true,p=>applies++,()=>{},5);Check(!result.Applied&&applies==1,"same sign-in does not reapply");
  result=StartupPolicy.Run(s,"login-b",false,p=>true,p=>applies++,()=>{},5);Check(result.Applied&&applies==2,"later clean sign-in can apply once");
  s=Enabled();probes=applies=0;result=StartupPolicy.Run(s,"login-a",false,p=>{probes++;return false;},p=>applies++,()=>{},4);Check(!result.Applied&&applies==0&&probes==4&&s.Read("startup-paused.txt")!=null,"unavailable driver has bounded wait and persistent failure");
  StartupPolicy.Run(s,"login-b",false,p=>true,p=>applies++,()=>{},4);Check(applies==0,"failure blocks later sign-ins until manual confirmation");
  s=Enabled();applies=0;result=StartupPolicy.Run(s,"a",true,p=>true,p=>applies++,()=>{},4);Check(!result.Applied&&applies==0,"pending recovery never reapplies");
  s=Enabled();s.Write("startup-inflight.txt","old");result=StartupPolicy.Run(s,"b",false,p=>true,p=>applies++,()=>{},4);Check(!result.Applied&&applies==0,"interrupted startup pauses future attempts");
  s=Enabled();s.Delete("verified-startup.txt");s.Write("profile.txt",Fixed().Encode());result=StartupPolicy.Run(s,"a",false,p=>true,p=>applies++,()=>{},4);Check(!result.Applied&&applies==0,"last-applied legacy file alone is not verification");
  s=Enabled();s.Write("verified-startup.txt","broken");result=StartupPolicy.Run(s,"a",false,p=>true,p=>applies++,()=>{},4);Check(!result.Applied&&applies==0,"corrupt verification blocks writes");
  s=Enabled();result=StartupPolicy.Run(s,"a",false,p=>{s.Delete("startup.enabled");return true;},p=>applies++,()=>{},4);Check(!result.Applied&&applies==0,"disable during readiness prevents apply");
  s=Enabled();applies=0;result=StartupPolicy.Run(s,"a",false,p=>true,p=>{applies++;throw new Exception("driver rejected");},()=>{},4);Check(!result.Applied&&applies==1,"rejected set is not retried in a loop");
  s=Enabled();s.Write("profile.txt",new Profile{Core=false,Lock=true,Adaptive=true,LockMHz=2200}.Encode());Profile used=null;StartupPolicy.Run(s,"a",false,p=>true,p=>used=p.Value,()=>{},4);Check(used!=null&&!used.Adaptive&&used.LockMHz==1800,"unconfirmed experiment cannot replace confirmed fixed startup");
  s=Enabled();StartupPolicy.Block(s,"fault");StartupPolicy.Confirm(s,new ConfirmedProfile{Uuid="GPU-fake",Value=Fixed()});Check(s.Read("startup-paused.txt")==null,"explicit manual confirmation clears pause");
  g=new FakeGpu();ControlLogic.Reset(g,8);Check(g.UnlockCalls==1&&g.FixedCalls==0&&g.AdaptiveCalls==0,"recovery only resets; never reapplies");
  // Exercise the real persisted recovery and real two-process watchdog with a fake GPU.
  Directory.CreateDirectory(Files.Root);foreach(string name in new string[]{"recovery.txt","startup-paused.txt","fake-reset.txt"}){string file=Files.PathFor(name);if(File.Exists(file))File.Delete(file);}
  Files.Write("recovery.txt","test|GPU-fake|8");g=new FakeGpu{ResetFailure=true};try{Files.Recover(uuid=>g);}catch{}Check(File.Exists(Files.Journal)&&File.Exists(Files.PathFor("startup-paused.txt")),"failed recovery preserves journal and startup pause");
  Files.Recover(uuid=>new FakeGpu());Check(!File.Exists(Files.Journal)&&File.Exists(Files.PathFor("startup-paused.txt")),"successful recovery does not erase failure pause");
  using(Process owner=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--owner-test"){UseShellExecute=false,CreateNoWindow=true})){Check(owner.WaitForExit(10000)&&owner.ExitCode==27,"simulated owner exits abruptly without GPU crash");}
  for(int i=0;i<100&&!File.Exists(Files.PathFor("fake-reset.txt"));i++)Thread.Sleep(50);
  Check(File.Exists(Files.PathFor("fake-reset.txt"))&&!File.Exists(Files.Journal),"real watchdog resets fake GPU after owner failure");
  Check(File.Exists(Files.PathFor("startup-paused.txt")),"watchdog leaves persistent no-reapply latch");
  File.Delete(Files.PathFor("startup-paused.txt"));File.Delete(Files.PathFor("fake-reset.txt"));using(Process owner=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--owner-clean"){UseShellExecute=false,CreateNoWindow=true})){Check(owner.WaitForExit(10000)&&owner.ExitCode==0,"clean shutdown resets normally");}Thread.Sleep(500);Check(!File.Exists(Files.Journal)&&!File.Exists(Files.PathFor("startup-paused.txt")),"clean shutdown does not create recovery/reapply loop");
  s=Enabled();g=new FakeGpu();Files.Write("recovery.txt","shutdown|GPU-fake|8");
  bool clean=SessionShutdown.Finish(false,()=>{ControlLogic.Reset(g,8);File.Delete(Files.Journal);},why=>StartupPolicy.Block(s,why));
  Check(clean&&g.UnlockCalls==1&&!File.Exists(Files.Journal)&&s.Read("startup-paused.txt")==null&&s.Read("startup.enabled")!=null,"confirmed Windows shutdown resets synchronously and preserves startup");
  applies=0;result=StartupPolicy.Run(s,"after-restart",false,p=>true,p=>applies++,()=>{},2);
  Check(result.Applied&&applies==1,"clean Windows restart permits confirmed profile exactly once");
  s=Enabled();g=new FakeGpu{ResetFailure=true};Files.Write("recovery.txt","shutdown|GPU-fake|8");
  clean=SessionShutdown.Finish(false,()=>{ControlLogic.Reset(g,8);File.Delete(Files.Journal);},why=>StartupPolicy.Block(s,why));
  Check(!clean&&File.Exists(Files.Journal)&&s.Read("startup-paused.txt")!=null,"shutdown reset failure retains recovery and pauses startup");
  s=Enabled();int resetCalls=0;clean=SessionShutdown.Finish(true,()=>resetCalls++,why=>StartupPolicy.Block(s,why));
  Check(!clean&&resetCalls==0&&s.Read("startup-paused.txt")!=null,"shutdown during GPU work never races a reset");
  s=Enabled();using(ManualResetEvent release=new ManualResetEvent(false)){var finished=new ManualResetEvent(false);clean=SessionShutdown.Finish(false,()=>{release.WaitOne();finished.Set();},why=>StartupPolicy.Block(s,why),30);Check(!clean&&s.Read("startup-paused.txt")!=null,"stalled driver cleanup is bounded and pauses startup");release.Set();finished.WaitOne(1000);finished.Dispose();}
  File.Delete(Files.Journal);
  // Exercise Windows message routing on a preview form, never a real GPU.
  using(MainForm f=new MainForm(false,true)){
   var method=typeof(MainForm).GetMethod("WndProc",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   Files.Write("recovery.txt","cancelled|GPU-fake|8");
   object[] query={Message.Create(IntPtr.Zero,0x11,IntPtr.Zero,IntPtr.Zero)};method.Invoke(f,query);
   Check(((Message)query[0]).Result==new IntPtr(1)&&File.Exists(Files.Journal),"shutdown query accepts without clearing ownership");
   object[] cancel={Message.Create(IntPtr.Zero,0x16,IntPtr.Zero,IntPtr.Zero)};method.Invoke(f,cancel);
   Check(File.Exists(Files.Journal)&&!(bool)typeof(MainForm).GetField("closing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(f),"cancelled Windows shutdown keeps app and recovery ownership intact");
   g=new FakeGpu();typeof(MainForm).GetField("gpu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(f,g);
   Files.Write("startup.enabled","1");if(File.Exists(Files.PathFor("startup-paused.txt")))File.Delete(Files.PathFor("startup-paused.txt"));
   object[] end={Message.Create(IntPtr.Zero,0x16,new IntPtr(1),IntPtr.Zero)};method.Invoke(f,end);
   Check(!File.Exists(Files.Journal)&&g.UnlockCalls==1&&File.Exists(Files.PathFor("startup.enabled"))&&!File.Exists(Files.PathFor("startup-paused.txt")),"actual end-session message resets fake GPU without disabling startup or pausing");
  }
  Console.WriteLine("TOTAL "+count+" checks passed. No real GPU setter was called.");
 }
}
}

