using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Goblin {
static class Program {
 [STAThread]static void Main(string[] args){
  if(args.Length>0&&args[0]=="--preview"){Application.EnableVisualStyles();using(MainForm f=new MainForm(false,true)){f.Show();Application.DoEvents();using(Bitmap b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png"));}}return;}
  if(args.Length>0&&args[0]=="--ready"){Environment.ExitCode=DriverReady.Probe(args.Length>1?args[1]:"auto");return;}
  if(args.Length>0&&args[0]=="--guard"){try{Watchdog.Run(args);}catch(Exception e){Files.Log(e.ToString());}return;}
  if(args.Length>0&&args[0]=="--probe"){using(IGpu g=new Nvidia(null))File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"probe.txt"),g.Name+"\r\nCore: "+Describe(g.Core())+"\r\nMemory: "+Describe(g.Memory())+"\r\nPower: "+Describe(g.Power()));return;}
  bool owner;using(Mutex mutex=new Mutex(true,"Local\\GoblinGovernor",out owner)){
   if(!owner)return;
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{Application.Run(new MainForm(args.Length>0&&args[0]=="--startup"));}catch(Exception e){Files.Log(e.ToString());MessageBox.Show(e.Message,"Goblin Governor");}
  }
 }
 static string Describe(Reading r){return r.Available?r.Value+" ["+r.Min+".."+r.Max+"]":r.Reason;}
}
public sealed class MainForm:Form {
 IGpu gpu;Watchdog guard; bool busy,closing,initStartup,cAvailable,mAvailable,pAvailable;Profile verified;
 bool manualApplied,cancelStartup; ComboBox lockMode; NumericUpDown minimumClock; Label startupInfo; Button confirmStartup;
 System.Collections.Generic.Dictionary<NumericUpDown,TrackBar> sliders=new System.Collections.Generic.Dictionary<NumericUpDown,TrackBar>();
 Label status,detail,gpuName,coreRead,memRead,powerRead,lockRead,telemetry;
 CheckBox clockLock,logTelemetry;NumericUpDown lockValue;
 System.Windows.Forms.Timer monitor=new System.Windows.Forms.Timer();
 bool sampling; bool lockRejected=false;
 CheckBox core,mem,power,startup;NumericUpDown coreValue,memValue,powerValue;Button apply,reset,refresh;
 System.Windows.Forms.Timer health=new System.Windows.Forms.Timer();
 Color bg=Color.FromArgb(11,24,46),panel=Color.FromArgb(23,44,71),green=Color.FromArgb(190,242,74);
 public MainForm(bool autorun,bool preview=false){
  Text="Goblin Governor";BackColor=bg;ForeColor=Color.White;Font=new Font("Segoe UI",16);ClientSize=new Size(930,790);MinimumSize=new Size(880,600);AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;StartPosition=FormStartPosition.CenterScreen;
  ClientSize=new Size(1120,950);MinimumSize=new Size(800,600);
  Panel scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=bg};Controls.Add(scroll);TableLayoutPanel root=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(24),ColumnCount=1,RowCount=16,BackColor=bg};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(root);
  int[] sizes={48,40,64,96,64,88,108,108,108,108,96,64,60,76,76,96};foreach(int n in sizes)root.RowStyles.Add(new RowStyle(SizeType.Absolute,n));AutoScroll=true;
  root.Controls.Add(Label("GOBLIN GOVERNOR",20,FontStyle.Bold));
  gpuName=Label("Checking NVIDIA controls…",14,FontStyle.Regular);root.Controls.Add(gpuName);
  status=Label("CHECKING",20,FontStyle.Bold);status.ForeColor=green;root.Controls.Add(status);
  detail=Label("No settings applied. Fixed 1800 MHz is the working default.",14,FontStyle.Regular);root.Controls.Add(detail);
  startup=new ReadableCheckBox{Text="START WITH WINDOWS — apply confirmed profile",Dock=DockStyle.Fill,Font=new Font("Segoe UI",15,FontStyle.Bold),ForeColor=Color.White};root.Controls.Add(startup);startupInfo=Label("Startup is off. No automatic profile selected.",13,FontStyle.Regular);root.Controls.Add(startupInfo);
  root.Controls.Add(Row("Core offset",-300,0,-100,"MHz",out core,out coreValue,out coreRead));core.Checked=false;
  root.Controls.Add(Row("Memory offset",-1000,0,-250,"MHz",out mem,out memValue,out memRead));
  root.Controls.Add(Row("Power limit",80,100,85,"%",out power,out powerValue,out powerRead));
  root.Controls.Add(Row("Core clock lock",300,2200,1800,"MHz",out clockLock,out lockValue,out lockRead));lockRead.Text="Driver support untested";
  clockLock.CheckedChanged+=delegate{if(clockLock.Checked)core.Checked=false;};core.CheckedChanged+=delegate{if(core.Checked)clockLock.Checked=false;};
  TableLayoutPanel modePanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Padding=new Padding(10),BackColor=panel};modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));
  lockMode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,Dock=DockStyle.Fill,Font=new Font("Segoe UI",14),BackColor=bg,ForeColor=Color.White};lockMode.Items.AddRange(new object[]{"FIXED LOCK (working default)","ADAPTIVE CAPPED (experimental)"});modePanel.Controls.Add(lockMode);modePanel.Controls.Add(Label("Adaptive minimum MHz",13,FontStyle.Regular));minimumClock=new NumericUpDown{Minimum=100,Maximum=300,Value=300,Increment=25,Dock=DockStyle.Fill,Font=new Font("Segoe UI",18,FontStyle.Bold),BackColor=bg,ForeColor=Color.White};modePanel.Controls.Add(minimumClock);root.Controls.Add(modePanel);
  lockMode.SelectedIndexChanged+=delegate{int maximum=lockMode.SelectedIndex==1?2200:1800;if(lockValue.Value>maximum)lockValue.Value=maximum;lockValue.Maximum=maximum;sliders[lockValue].Maximum=maximum;minimumClock.Enabled=!busy&&lockMode.SelectedIndex==1;lockRead.Text=lockMode.SelectedIndex==1?"Cap: choose 1800, 1900 or up to 2200 MHz":"Fixed: minimum = maximum";};lockMode.SelectedIndex=0;clockLock.Checked=true;
  telemetry=Label("Live telemetry unavailable until connected",14,FontStyle.Regular);root.Controls.Add(telemetry);
  logTelemetry=new ReadableCheckBox{Text="Record telemetry to CSV",Dock=DockStyle.Fill,ForeColor=Color.White,Font=new Font("Segoe UI",12)};root.Controls.Add(logTelemetry);
  FlowLayoutPanel buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};apply=Button("APPLY",green);reset=Button("RESET",Color.FromArgb(230,126,55));refresh=Button("REFRESH",Color.FromArgb(125,180,240));buttons.Controls.AddRange(new Control[]{apply,reset,refresh});root.Controls.Add(buttons);
  confirmStartup=Button("CONFIRM TESTED PROFILE",Color.FromArgb(125,180,240));confirmStartup.Width=480;root.Controls.Add(confirmStartup);
  root.Controls.Add(Label("Adaptive mode may reintroduce clock transitions. Voltage stays automatic.\nRESET disables startup. Exit restores owned settings.",12,FontStyle.Regular));
  monitor.Interval=1500;monitor.Tick+=async delegate{if(busy||sampling||gpu==null)return;sampling=true;try{string reading=await Task.Run(()=>((ITelemetry)gpu).Sample());string[] fields=reading.Split(',');telemetry.Text="Core "+fields[0]+" MHz    Memory "+fields[1]+" MHz    "+fields[2]+" C    "+fields[3]+" W    "+fields[4];if(logTelemetry.Checked){string path=Files.PathFor("telemetry.csv");if(!File.Exists(path))File.WriteAllText(path,"time,core_mhz,memory_mhz,temp_c,power_w,pstate\r\n");File.AppendAllText(path,DateTime.Now.ToString("o")+","+reading+"\r\n");}}catch(Exception e){telemetry.Text="Telemetry: "+e.Message;StartupPolicy.Block(StartupPolicy.Store,"Driver telemetry failed: "+e.Message);SyncStartup();Files.Log("Telemetry: "+e.Message);monitor.Stop();}finally{sampling=false;}};
  apply.Click+=async delegate{await ApplyNow();};reset.Click+=async delegate{cancelStartup=true;await ResetNow(true);};refresh.Click+=async delegate{await ReadNow();};
  startup.CheckedChanged+=async delegate{
   if(initStartup)return;bool enable=startup.Checked;
   if(busy&&enable){SyncStartup();return;}
   if(!enable){cancelStartup=true;StartupPolicy.Store.Delete("startup.enabled");}
   try{await Task.Run(()=>Startup.Set(enable));if(enable&&!startup.Checked)await Task.Run(()=>Startup.Set(false));}catch(Exception e){detail.Text=e.Message;}SyncStartup();
  };
  confirmStartup.Click+=delegate{
   if(busy||verified==null||!manualApplied)return;
   Profile selected=Selection();if(selected.Encode()!=verified.Encode()){detail.Text="Apply the edited profile before confirming it.";return;}
   if(MessageBox.Show("Have you manually tested this exact profile and want it available for automatic startup?\n\n"+verified.Description,"Confirm tested profile",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
   StartupPolicy.Confirm(StartupPolicy.Store,new ConfirmedProfile{Uuid=gpu.Uuid,Value=verified.Copy()});SyncStartup();detail.Text="Confirmed for startup. Use the Start with Windows switch to enable it.";
  };
  if(preview){gpuName.Text="NVIDIA GeForce RTX 5070 Ti Laptop GPU";status.Text="DEFAULT";coreRead.Text="Current: 0 MHz";memRead.Text="Current: 0 MHz";powerRead.Text="UNAVAILABLE";power.Enabled=powerValue.Enabled=false;startupInfo.Text="Confirmed startup profile: Fixed 1800 MHz. Startup is OFF.";StartPosition=FormStartPosition.Manual;Location=new Point(-10000,-10000);}
  else Shown+=async delegate{await Initialize(autorun);};
  FormClosing+=async delegate(object sender,FormClosingEventArgs e){if(closing)return;e.Cancel=true;if(busy||sampling){detail.Text="Wait for the current operation to finish.";return;}monitor.Stop();await ResetNow(false);if(File.Exists(Files.Journal)){detail.Text="Reset not verified. The recovery helper will retry after exit.";}closing=true;health.Stop();Close();};
 }
 Label Label(string text,int size,FontStyle style){return new Label{Text=text,Dock=DockStyle.Fill,Font=new Font("Segoe UI",size,style),ForeColor=ForeColor,TextAlign=ContentAlignment.MiddleLeft};}
 Button Button(string text,Color color){return new Button{Text=text,Width=210,Height=56,Margin=new Padding(0,5,14,5),BackColor=color,ForeColor=Color.FromArgb(15,20,25),FlatStyle=FlatStyle.Flat,Font=new Font("Segoe UI",16,FontStyle.Bold)};}
 Control Row(string title,int min,int max,int val,string unit,out CheckBox check,out NumericUpDown number,out Label reading){
  TableLayoutPanel r=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=panel,ColumnCount=4,Padding=new Padding(12),Margin=new Padding(0,4,0,4)};
  r.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));r.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,18));r.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,9));r.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,39));
  check=new ReadableCheckBox{Text=title,Dock=DockStyle.Fill,Font=new Font("Segoe UI",15,FontStyle.Bold),ForeColor=Color.White};
  number=new NumericUpDown{Minimum=min,Maximum=max,Value=val,Increment=unit=="%"?5:25,Dock=DockStyle.Fill,Font=new Font("Segoe UI",18,FontStyle.Bold),BackColor=bg,ForeColor=Color.White};
  reading=Label("Checking…",13,FontStyle.Regular);r.Controls.Add(check);r.Controls.Add(number);r.Controls.Add(Label(unit,13,FontStyle.Regular));r.Controls.Add(reading);
  r.RowCount=2;r.RowStyles.Add(new RowStyle(SizeType.Absolute,52));r.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
  NumericUpDown input=number;CheckBox toggle=check;TrackBar slider=new TrackBar{Minimum=min,Maximum=max,Value=val,TickStyle=TickStyle.None,Dock=DockStyle.Fill,AutoSize=false};r.Controls.Add(slider,0,1);r.SetColumnSpan(slider,4);sliders[input]=slider;slider.ValueChanged+=delegate{input.Value=Math.Min(input.Maximum,Math.Max(input.Minimum,slider.Value));};input.ValueChanged+=delegate{slider.Value=(int)input.Value;};input.EnabledChanged+=delegate{slider.Enabled=input.Enabled;};return r;
 }
 void EnableControls(bool yes){apply.Enabled=yes&&gpu!=null&&guard!=null&&guard.Healthy;reset.Enabled=true;refresh.Enabled=yes;startup.Enabled=true;core.Enabled=coreValue.Enabled=yes&&cAvailable;mem.Enabled=memValue.Enabled=yes&&mAvailable;power.Enabled=powerValue.Enabled=yes&&pAvailable;clockLock.Enabled=lockValue.Enabled=yes&&!lockRejected;lockMode.Enabled=yes;minimumClock.Enabled=yes&&lockMode.SelectedIndex==1;confirmStartup.Enabled=yes&&verified!=null&&manualApplied;}
 void Fill(Reading v,CheckBox c,NumericUpDown n,Label label,string unit){c.Enabled=v.Available;n.Enabled=v.Available;label.Text=v.Available?"Current: "+(unit=="W"?(v.Value/1000.0).ToString("0.0"):v.Value.ToString())+" "+unit:"UNAVAILABLE";if(!v.Available)c.Checked=false;label.ForeColor=v.Available?Color.LightGray:Color.FromArgb(230,126,55);}
 void ShowReadings(){Reading c=gpu.Core(),m=gpu.Memory(),p=gpu.Power();cAvailable=c.Available;mAvailable=m.Available;pAvailable=p.Available;gpuName.Text=gpu.Name;Fill(c,core,coreValue,coreRead,"MHz");Fill(m,mem,memValue,memRead,"MHz");Fill(p,power,powerValue,powerRead,"W");bool defaults=(!c.Available||c.Value==0)&&(!m.Available||m.Value==0)&&(!p.Available||p.Value==p.Max);status.Text=defaults?"DEFAULT":"EXTERNAL SETTINGS";if(File.Exists(Files.Journal)){int owned=Files.Mask();bool readable=((owned&1)==0||c.Available)&&((owned&2)==0||m.Available)&&((owned&4)==0||p.Available);bool matches=verified!=null&&(!verified.Core||c.Value==verified.CoreMHz)&&(!verified.Memory||m.Value==verified.MemoryMHz)&&(!verified.Power||p.Value==(int)Math.Round(p.Max*verified.PowerPercent/100.0));status.Text=!readable?"UNKNOWN":matches?"APPLIED":"CHANGED";}if(!c.Available&&!m.Available&&!p.Available)status.Text="UNAVAILABLE";detail.Text=defaults?"No governor changes. Core-only preset is ready.":"Last driver readback shown below. Refresh to check again.";}
 void SyncStartup(){initStartup=true;startup.Checked=Startup.Enabled;initStartup=false;string saved="No confirmed profile.";try{saved=ConfirmedProfile.Decode(StartupPolicy.Store.Read("verified-startup.txt")).Value.Description;}catch{}string pause=StartupPolicy.Store.Read("startup-paused.txt");startupInfo.Text=(pause!=null?"PAUSED after failure — manually apply and confirm again.\n":"")+"Startup "+(Startup.Enabled?"ON":"OFF")+" · "+saved;}
 Profile Selection(){return new Profile{Core=core.Checked,Memory=mem.Checked,Power=power.Checked,Lock=clockLock.Checked,Adaptive=clockLock.Checked&&lockMode.SelectedIndex==1,LockMin=(int)minimumClock.Value,LockMHz=(int)lockValue.Value,CoreMHz=(int)coreValue.Value,MemoryMHz=(int)memValue.Value,PowerPercent=(int)powerValue.Value};}
 void SelectProfile(Profile p){core.Checked=p.Core;mem.Checked=p.Memory;power.Checked=p.Power;clockLock.Checked=p.Lock;lockMode.SelectedIndex=p.Adaptive?1:0;minimumClock.Value=p.LockMin;lockValue.Value=p.LockMHz;coreValue.Value=p.CoreMHz;memValue.Value=p.MemoryMHz;powerValue.Value=p.PowerPercent;}
 bool Connect(string uuid){if(cancelStartup)return false;if(gpu!=null)return gpu.Uuid==uuid||uuid==null;gpu=DriverReady.Open(uuid);return true;}
 void ResetOwned(){if(File.Exists(Files.Journal)){ControlLogic.Reset(gpu,Files.Mask());File.Delete(Files.Journal);}}
 void ApplyTransaction(Profile p,bool automatic){
  if(guard==null||!guard.Healthy)throw new Exception("Recovery helper is unavailable.");p.Validate();ResetOwned();
  Reading c=gpu.Core(),m=gpu.Memory(),w=gpu.Power();if((p.Core&&c.Available&&c.Value!=0)||(p.Memory&&m.Available&&m.Value!=0)||(p.Power&&w.Available&&w.Value!=w.Max))throw new Exception("Another tuner changed the selected control. Reset it there first.");
  try{ControlLogic.Apply(gpu,p,mask=>guard.Arm(gpu.Uuid,mask));if(automatic&&(cancelStartup||!Startup.Enabled))throw new Exception("Startup cancelled.");Files.Write("profile.txt",p.Encode());Files.Log((automatic?"AUTO APPLIED ONCE ":"MANUAL REQUEST ACCEPTED ")+p.Encode());}
  catch(Exception original){StartupPolicy.Block(StartupPolicy.Store,original.Message);try{ResetOwned();}catch(Exception rollback){throw new Exception(original.Message+" Reset failed: "+rollback.Message);}throw;}
 }
 async Task Initialize(bool autorun){
  health.Stop();health.Dispose();health=new System.Windows.Forms.Timer();monitor.Stop();busy=true;cancelStartup=false;EnableControls(false);SyncStartup();status.Text="WAITING FOR NVIDIA";detail.Text="Waiting for the driver and GPU. Read-only probes; up to about 60 seconds.";
  string result=null;bool recovered=File.Exists(Files.Journal);bool interrupted=StartupPolicy.Store.Read("startup-inflight.txt")!=null;
  if(recovered||interrupted)StartupPolicy.Block(StartupPolicy.Store,"Previous session was interrupted. Automatic reapply paused.");
  try{
   StartupResult automatic=null;
   await Task.Run(()=>{
    if(autorun&&!recovered&&!interrupted){
     automatic=StartupPolicy.Run(StartupPolicy.Store,StartupPolicy.SignInId(),false,
      cp=>Connect(cp.Uuid),cp=>{guard=new Watchdog();ApplyTransaction(cp.Value,true);verified=cp.Value.Copy();},()=>System.Threading.Thread.Sleep(500),20);
     result=automatic.Message;
     // A refused/failed automatic run never attempts an alternate profile.
     if(!automatic.Applied&&gpu==null)return;
    }else{
     string uuid=null;if(recovered){string[] parts=File.ReadAllText(Files.Journal).Split('|');if(parts.Length!=3)throw new Exception("Invalid recovery record.");uuid=parts[1];}
     Exception last=null;for(int i=0;i<20&&!cancelStartup;i++){try{if(Connect(uuid))break;}catch(Exception e){last=e;}if(i<19)System.Threading.Thread.Sleep(500);}
     if(gpu==null)throw new Exception(cancelStartup?"Connection cancelled.":"NVIDIA unavailable after readiness wait: "+(last==null?"No response":last.Message));
     if(recovered)Files.Recover();
     if(interrupted)StartupPolicy.Store.Delete("startup-inflight.txt");
     result=recovered||interrupted?"RECOVERED: automatic reapply is paused. Manually apply and confirm.":"Ready. No settings applied at launch.";
    }
    if(gpu!=null&&guard==null)guard=new Watchdog();
   });
   if(cancelStartup&&gpu!=null){await Task.Run(()=>ResetOwned());verified=null;if(automatic!=null)automatic.Applied=false;result="Cancelled and reset. Startup is off.";}
   if(gpu!=null){ShowReadings();if(verified!=null)SelectProfile(verified);else try{SelectProfile(ConfirmedProfile.Decode(StartupPolicy.Store.Read("verified-startup.txt")).Value);}catch{}monitor.Start();}
   status.Text=automatic!=null?(automatic.Applied?"STARTUP REQUEST ACCEPTED":automatic.Message.StartsWith("STARTUP FAILED")?"STARTUP FAILED":"STARTUP NOT APPLIED"):recovered||interrupted?"RECOVERED":"DEFAULT";
   detail.Text=result;manualApplied=false;Files.Log(result);
   health.Interval=2000;health.Tick+=async delegate{if(!busy&&!sampling&&guard!=null&&!guard.Healthy){StartupPolicy.Block(StartupPolicy.Store,"Recovery helper stopped. Manually review before startup resumes.");await ResetNow(false);status.Text="HELPER STOPPED";detail.Text="Reset attempted. Restart Governor before applying again.";health.Stop();}};health.Start();
  }catch(Exception e){status.Text=autorun?"STARTUP FAILED":"UNAVAILABLE";detail.Text=e.Message;Files.Log(e.Message);if(autorun)StartupPolicy.Block(StartupPolicy.Store,e.Message);}
  finally{busy=false;EnableControls(true);SyncStartup();}
 }
 async Task ReadNow(){if(busy||sampling)return;if(gpu==null){await Initialize(false);return;}busy=true;EnableControls(false);try{ShowReadings();if(verified!=null&&verified.Lock){status.Text="LOCK REQUEST ACCEPTED";detail.Text=verified.Description+". Actual clocks are shown below.";}}catch(Exception e){status.Text="UNKNOWN";detail.Text=e.Message;}finally{busy=false;EnableControls(true);}}
 async Task ApplyNow(){
  if(busy||sampling||gpu==null)return;busy=true;cancelStartup=false;EnableControls(false);status.Text="APPLYING…";Profile p=Selection();
  try{await Task.Run(()=>ApplyTransaction(p,false));if(cancelStartup){await Task.Run(()=>ResetOwned());throw new Exception("Apply cancelled by Reset; owned settings restored.");}verified=p.Copy();manualApplied=true;ShowReadings();status.Text=p.Lock?"LOCK REQUEST ACCEPTED":"APPLIED";detail.Text=p.Description+" accepted. Test manually before confirming it for startup.";lockRead.Text=p.Lock?"Requested: "+(p.Adaptive?p.LockMin+"–":"")+p.LockMHz+" MHz":"No owned lock";}
  catch(Exception e){status.Text=File.Exists(Files.Journal)?"RESET FAILED":"NOT APPLIED";detail.Text=e.Message;Files.Log(e.Message);verified=null;manualApplied=false;}
  finally{busy=false;EnableControls(true);SyncStartup();}
 }
 async Task ResetNow(bool disableStartup){
  if(disableStartup){cancelStartup=true;StartupPolicy.Store.Delete("startup.enabled");StartupPolicy.Block(StartupPolicy.Store,"Reset requested. Confirm a tested profile to allow automatic reapply again.");try{await Task.Run(()=>Startup.Set(false));}catch(Exception e){Files.Log("Startup task removal: "+e.Message);}}
  SyncStartup();if(!busy&&sampling){busy=true;EnableControls(false);for(int i=0;i<100&&sampling;i++)await Task.Delay(50);busy=false;EnableControls(true);}if(busy||sampling){detail.Text="Startup cancelled. Wait for the current operation to finish, then press Reset again if needed.";return;}
  busy=true;EnableControls(false);status.Text="RESETTING…";
  try{if(gpu==null&&File.Exists(Files.Journal))throw new Exception("GPU unavailable. Reset is still pending; startup is paused.");if(gpu!=null)await Task.Run(()=>ResetOwned());verified=null;manualApplied=false;if(gpu!=null)ShowReadings();else status.Text="STARTUP OFF";detail.Text="Owned settings reset; startup "+(Startup.Enabled?"remains enabled.":"is off.");Files.Log("RESET completed for owned controls.");}
  catch(Exception e){status.Text="RESET FAILED";detail.Text=e.Message;StartupPolicy.Block(StartupPolicy.Store,e.Message);Files.Log(e.Message);}
  finally{busy=false;EnableControls(true);SyncStartup();}
 }
 protected override void Dispose(bool disposing){if(disposing){monitor.Stop();monitor.Dispose();health.Dispose();if(gpu!=null)gpu.Dispose();if(guard!=null)guard.Dispose();}base.Dispose(disposing);}
}
public sealed class ReadableCheckBox:CheckBox {
 public ReadableCheckBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AutoSize=false;Cursor=Cursors.Hand;}
 protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
 protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){
  e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  int side=Math.Max(28,(int)(Font.Height*1.25));side=Math.Min(side,Math.Max(12,Height-10));
  Rectangle box=new Rectangle(4,(Height-side)/2,side,side);
  Color lime=Color.FromArgb(190,242,74),muted=Color.FromArgb(173,190,211),orange=Color.FromArgb(230,126,55);
  using(SolidBrush b=new SolidBrush(Checked?(Enabled?lime:muted):Color.FromArgb(11,24,46)))e.Graphics.FillRectangle(b,box);
  using(Pen pen=new Pen(Enabled?(Checked?lime:orange):muted,3))e.Graphics.DrawRectangle(pen,box);
  if(Checked){using(Pen pen=new Pen(Color.FromArgb(11,24,46),Math.Max(3,side/8))){pen.StartCap=pen.EndCap=System.Drawing.Drawing2D.LineCap.Round;e.Graphics.DrawLines(pen,new Point[]{new Point(box.Left+side/5,box.Top+side/2),new Point(box.Left+side*2/5,box.Top+side*3/4),new Point(box.Left+side*4/5,box.Top+side/4)});}}
  Rectangle text=new Rectangle(box.Right+14,2,Math.Max(1,Width-box.Right-18),Height-4);
  TextRenderer.DrawText(e.Graphics,Text,Font,text,Enabled?Color.White:muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
  if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(1,1,Width-3,Height-3),lime,BackColor);
 }
}
}


