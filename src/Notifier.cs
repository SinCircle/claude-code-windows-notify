using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

public class SessionState {
    public long Window;
    public int WindowProcess;
    public long WindowProcessStart;
    public int ClaudeProcess;
    public long ClaudeProcessStart;
    public bool Attention;
    public bool Completed;
    public string SessionTitle;
    public string InitialPrompt;
    public string TitleTranscript;
    public long TitleTranscriptLength;
}
public static class Notifier {
    public const string AppId = "Local.ClaudeCode";
    public const string ActivatorId = "B0BCB73E-A94E-4817-BEAE-038195F6C9CD";
    static string Root = AppDomain.CurrentDomain.BaseDirectory;
    static JavaScriptSerializer Json = new JavaScriptSerializer();
    static string Data = Path.Combine(Root, "state");
    static string LogPath = Path.Combine(Root, "events.jsonl");
    static string Get(Dictionary<string,object> d, string key) { return d.ContainsKey(key) ? Convert.ToString(d[key]) : ""; }
    static string Preview(string value, int limit = 360) {
        if (String.IsNullOrWhiteSpace(value)) return "";
        // Notifications contain a plain-text excerpt, without Markdown scaffolding.
        value=Regex.Replace(value,@"\x1B\[[0-?]*[ -/]*[@-~]","");
        value=Regex.Replace(value,@"(?m)^\s*```[^\r\n]*","");
        value=Regex.Replace(value,@"!?\[([^\]]+)\]\([^\r\n)]*\)","$1");
        value=Regex.Replace(value,@"(?m)^\s*(?:#{1,6}\s+|>\s*|[-*+]\s+|\d+[.)]\s+)","");
        value=value.Replace("**","").Replace("__","").Replace("`","");
        value=Regex.Replace(value,@"\s+"," ").Trim();
        if(value.Length<=limit) return value;
        int end=limit-1;
        if(end>0 && Char.IsHighSurrogate(value[end-1])) end--;
        return value.Substring(0,end).TrimEnd()+"…";
    }
    static string CompletionBody(Dictionary<string,object> d) {
        // Stop supplies the final response directly. The transcript may not yet be flushed.
        string excerpt=Preview(Get(d,"last_assistant_message"));
        return excerpt!="" ? excerpt : "本轮已结束；此次事件没有附带回复正文，请返回终端查看。";
    }
    static string SessionTitle(Dictionary<string,object> d, SessionState s) {
        string transcript=Get(d,"transcript_path"), session=Get(d,"session_id");
        try {
            if(!String.IsNullOrEmpty(transcript) && File.Exists(transcript)) {
                long length=new FileInfo(transcript).Length;
                if(s.TitleTranscript!=transcript || s.TitleTranscriptLength!=length) {
                    string custom="", automatic="";
                    using(var file=new FileStream(transcript,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var reader=new StreamReader(file,Encoding.UTF8)) {
                        string line;
                        while((line=reader.ReadLine())!=null) {
                            bool titleRecord=line.Contains("\"custom-title\"") || line.Contains("\"ai-title\"");
                            bool firstPrompt=String.IsNullOrEmpty(s.InitialPrompt) && line.Contains("\"user\"");
                            if(!titleRecord && !firstPrompt)continue;
                            try {
                                var entry=Json.Deserialize<Dictionary<string,object>>(line);
                                string entrySession=Get(entry,"sessionId");
                                if(entrySession!="" && entrySession!=session)continue;
                                string type=Get(entry,"type");
                                if(type=="custom-title") custom=Get(entry,"customTitle");
                                else if(type=="ai-title") automatic=Get(entry,"aiTitle");
                                else if(firstPrompt && type=="user" && (!entry.ContainsKey("isMeta") || !Object.Equals(entry["isMeta"],true))) {
                                    var message=entry.ContainsKey("message") ? entry["message"] as Dictionary<string,object> : null;
                                    if(message!=null && message.ContainsKey("content")) {
                                        string prompt=message["content"] as string;
                                        if(prompt==null) {
                                            var blocks=message["content"] as System.Collections.IEnumerable;
                                            var parts=new List<string>();
                                            if(blocks!=null)foreach(var block in blocks) {
                                                var text=block as Dictionary<string,object>;
                                                if(text!=null && Get(text,"type")=="text")parts.Add(Get(text,"text"));
                                            }
                                            prompt=String.Join(" ",parts);
                                        }
                                        s.InitialPrompt=Preview(prompt,100);
                                    }
                                }
                            } catch { /* A concurrent writer can leave an incomplete final line. */ }
                        }
                    }
                    s.SessionTitle=Preview(!String.IsNullOrWhiteSpace(custom)?custom:automatic,100);
                    s.TitleTranscript=transcript;s.TitleTranscriptLength=length;
                }
            }
        } catch { /* Keep the last known title if the transcript is temporarily unavailable. */ }
        if(!String.IsNullOrEmpty(s.SessionTitle))return s.SessionTitle;
        if(String.IsNullOrEmpty(s.InitialPrompt))s.InitialPrompt=Preview(Get(d,"prompt"),100);
        if(!String.IsNullOrEmpty(s.InitialPrompt))return s.InitialPrompt;
        string cwd=Get(d,"cwd").TrimEnd('\\','/');
        string folder=cwd.Substring(Math.Max(cwd.LastIndexOf('\\'),cwd.LastIndexOf('/'))+1);
        return !String.IsNullOrWhiteSpace(folder)?Preview(folder,100):"Claude Code";
    }
    static string AttentionBody(Dictionary<string,object> d) {
        string tool=Get(d,"tool_name");
        var input=d.ContainsKey("tool_input") ? d["tool_input"] as Dictionary<string,object> : null;
        if(input!=null) {
            if(tool=="AskUserQuestion" && input.ContainsKey("questions")) {
                var questions=input["questions"] as System.Collections.IEnumerable;
                var parts=new List<string>();
                if(questions!=null) foreach(var item in questions) {
                    var q=item as Dictionary<string,object>; if(q==null)continue;
                    string question=Get(q,"question"); if(String.IsNullOrWhiteSpace(question))continue;
                    var options=new List<string>();
                    if(q.ContainsKey("options")) {
                        var items=q["options"] as System.Collections.IEnumerable;
                        if(items!=null) foreach(var option in items) {
                            var o=option as Dictionary<string,object>;
                            if(o!=null && !String.IsNullOrWhiteSpace(Get(o,"label"))) options.Add(Get(o,"label"));
                        }
                    }
                    parts.Add(question+(options.Count>0 ? "（"+String.Join(" / ",options)+"）" : ""));
                }
                if(parts.Count>0) return Preview(String.Join("；",parts));
            }
            if(tool=="ExitPlanMode") {
                string plan=Preview(Get(input,"plan"),330);
                if(plan!="") return "请确认计划："+plan;
            }
            var details=new List<string>();
            string description=Get(input,"description");
            if(!String.IsNullOrWhiteSpace(description)) details.Add(description);
            foreach(string field in new[]{"command","file_path","path","url","prompt"}) {
                string value=Get(input,field);
                if(!String.IsNullOrWhiteSpace(value)) {details.Add(value);break;}
            }
            if(details.Count>0) return Preview("待确认 "+tool+"："+String.Join("；",details));
        }
        string message=Preview(Get(d,"message"));
        if(message!="") return message;
        return tool!="" ? "等待你确认 "+tool+"，请返回终端处理。" : "等待你的确认或回复，请返回终端处理。";
    }
    public static void Log(string action, object detail) {
        try {
            using (var m = new Mutex(false, "Local\\ClaudeCodeNotifyLog")) {
                if (!m.WaitOne(3000)) return;
                try {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2000000) File.Delete(LogPath);
                    File.AppendAllText(LogPath, Json.Serialize(new { time=DateTimeOffset.Now.ToString("o"), action=action, detail=detail }) + Environment.NewLine, Encoding.UTF8);
                } finally { m.ReleaseMutex(); }
            }
        } catch {}
    }
    static string Key(string session) {
        using (var h=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(session))).Replace("-", "").ToLowerInvariant().Substring(0,32);
    }
    internal static ManualResetEvent Activated = new ManualResetEvent(false);
    [MTAThread] public static int Main(string[] args) {
        try {
            Directory.CreateDirectory(Data);
            if (args.Length > 0 && args[0] == "--install") { Install(); return 0; }
            if (args.Length > 0 && (args[0] == "--com-server" || args[0] == "-Embedding")) { RunActivator(); return 0; }
            if (args.Length > 1 && args[0] == "--inspect") {
                int p=int.Parse(args[1]);
                IntPtr w=Native.ConsoleOwner(p);
                File.WriteAllText(Path.Combine(Root,"inspect.json"), Json.Serialize(new {window=w.ToInt64(), foreground=Native.GetForegroundWindow().ToInt64(), process=Native.WindowPid(w), title=Native.Title(w)}));
                return 0;
            }
            if (args.Length > 1 && args[0] == "--test") {
                IntPtr w=new IntPtr(long.Parse(args[1]));
                var s=new SessionState(); SetWindow(s,w);
                File.WriteAllText(Path.Combine(Data, "test.json"), Json.Serialize(s));
                if (Native.GetForegroundWindow() == w) Log("suppressed_foreground", new {test=true,window=s.Window});
                else Send("test", s, "本轮已完成", "结果已准备好，点击返回终端查看。", "test");
                return 0;
            }
            string raw;
            using(var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8)) raw=input.ReadToEnd();
            if (String.IsNullOrWhiteSpace(raw)) return 0;
            Handle(Json.Deserialize<Dictionary<string,object>>(raw));
        } catch (Exception e) { Log("error",e.ToString()); }
        // Notification failures must never block or approve Claude operations.
        return 0;
    }
    static void SetWindow(SessionState s, IntPtr w) {
        if (w==IntPtr.Zero || !Native.IsWindow(w)) return;
        int p=Native.WindowPid(w);
        s.Window=w.ToInt64(); s.WindowProcess=p;
        s.WindowProcessStart=Process.GetProcessById(p).StartTime.ToUniversalTime().Ticks;
    }
    static bool ValidWindow(SessionState s) {
        try { return s.Window!=0 && Native.IsWindow(new IntPtr(s.Window)) && Native.WindowPid(new IntPtr(s.Window))==s.WindowProcess && Process.GetProcessById(s.WindowProcess).StartTime.ToUniversalTime().Ticks==s.WindowProcessStart; } catch { return false; }
    }
    static void Handle(Dictionary<string,object> d) {
        string session=Get(d,"session_id"), ev=Get(d,"hook_event_name");
        if(String.IsNullOrEmpty(session)) { Log("missing_session",ev); return; }
        string key=Key(session), path=Path.Combine(Data,key+".json");
        using (var mutex=new Mutex(false,"Local\\ClaudeCodeNotify-"+key)) {
            if (!mutex.WaitOne(5000)) { Log("lock_timeout",ev); return; }
            try {
                SessionState s=File.Exists(path) ? Json.Deserialize<SessionState>(File.ReadAllText(path)) : new SessionState();
                int claude=Native.FindClaudeAncestor();
                if(claude>0) { s.ClaudeProcess=claude; s.ClaudeProcessStart=Process.GetProcessById(claude).StartTime.ToUniversalTime().Ticks; SetWindow(s,Native.ConsoleOwner(claude)); }
                if (!ValidWindow(s)) {
                    Log("unresolved_terminal", new {ev=ev,claude=claude});
                    File.WriteAllText(path,Json.Serialize(s)); return;
                }
                string action="", title="", body="";
                if (ev=="SessionStart" || ev=="UserPromptSubmit") {s.Attention=false;s.Completed=false;SessionTitle(d,s);action="reset";}
                else if(ev=="PostToolUse" || ev=="PostToolUseFailure") {s.Attention=false;action="tool_finished";}
                else if(ev=="Stop") {
                    if(s.Completed) action="suppressed_duplicate";
                    else {s.Completed=true; title=SessionTitle(d,s);body=CompletionBody(d);}
                }
                else if(ev=="PermissionRequest" || ev=="PreToolUse" || ev=="Notification") {
                    if(s.Attention) action="suppressed_duplicate";
                    else {
                        s.Attention=true; title="需要操作-"+SessionTitle(d,s);
                        body=AttentionBody(d);
                    }
                }
                File.WriteAllText(path,Json.Serialize(s));
                if(title!="") {
                    if(Native.GetForegroundWindow()==new IntPtr(s.Window)) action="suppressed_foreground";
                    else { Send(key,s,title,body,ev); return; }
                }
                Log(action,new {ev=ev,session=key,window=s.Window});
            } finally {mutex.ReleaseMutex();}
        }
    }
    static void Send(string key,SessionState s,string title,string body,string ev) {
        EnsureActivator();
        // The short-lived request carries the excerpt; activation records and logs contain no message text.
        string token=Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(Data,token+".activation.json"),Json.Serialize(s));
        string request=Path.Combine(Data,token+".toast.json");
        File.WriteAllText(request,Json.Serialize(new { title=title,body=body,token=token,tag=key.Substring(0,Math.Min(16,key.Length)),window=s.Window }));
        var p=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"));
        p.Arguments="-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(Root,"ShowToast.ps1")+"\" -Request \""+request+"\"";
        p.UseShellExecute=false; p.CreateNoWindow=true; p.WindowStyle=ProcessWindowStyle.Hidden;
        using(var child=Process.Start(p)) {
            if(!child.WaitForExit(10000)) { child.Kill(); Log("toast_timeout",ev); }
            else Log(child.ExitCode==0?"toast_submitted":child.ExitCode==3?"suppressed_foreground":"toast_failed",new {ev=ev,session=key,window=s.Window,exitCode=child.ExitCode});
        }
        foreach(string file in Directory.GetFiles(Data,"*.activation.json")) if(File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-7)) File.Delete(file);
    }
    static void RunActivator() {
        using(var single=new Mutex(false,"Local\\ClaudeCodeToastActivator")) {
            if(!single.WaitOne(0))return;
            try {
                using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\ClaudeCodeToastReady"))
                using(var activity=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ClaudeCodeToastActivity")) {
                    ready.Reset();
                    var factory=new ToastClassFactory();
                    Guid clsid=new Guid(ActivatorId);uint cookie;
                    Marshal.ThrowExceptionForHR(Native.CoRegisterClassObject(ref clsid,factory,4,1,out cookie));
                    try {
                        Log("com_ready",Process.GetCurrentProcess().Id);ready.Set();
                        // One shared receiver, not one process per notification. Exit after four idle hours.
                        while(activity.WaitOne(TimeSpan.FromHours(4))) {}
                    } finally {
                        ready.Reset();Native.CoRevokeClassObject(cookie);GC.KeepAlive(factory);
                        Log("com_stopped",Process.GetCurrentProcess().Id);
                    }
                }
            } finally {single.ReleaseMutex();}
        }
    }
    static void EnsureActivator() {
        using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\ClaudeCodeToastReady")) {
            if(!ready.WaitOne(0)) {
                int server=Native.StartReceiver(Path.Combine(Root,"ClaudeNotify.exe"));
                Log("com_start_requested",server);
                if(!ready.WaitOne(4000)) {Log("com_start_timeout",server);return;}
            }
            try {using(var activity=EventWaitHandle.OpenExisting("Local\\ClaudeCodeToastActivity"))activity.Set();}catch{}
        }
    }
    public static void ActivateToken(string token) {
        Guid parsed;
        if(!Guid.TryParseExact(token,"N",out parsed)) return;
        string path=Path.Combine(Data,token+".activation.json");
        if(!File.Exists(path)) return;
        var s=Json.Deserialize<SessionState>(File.ReadAllText(path));
        // Re-resolve the console owner so moved Terminal tabs follow their current window.
        if(s.ClaudeProcess>0) {
            try {if(Process.GetProcessById(s.ClaudeProcess).StartTime.ToUniversalTime().Ticks==s.ClaudeProcessStart) {IntPtr current=Native.ConsoleOwner(s.ClaudeProcess); if(current!=IntPtr.Zero) SetWindow(s,current);}} catch {}
        }
        if(!ValidWindow(s)) {Log("activation_stale",token);return;}
        bool success=Native.Focus(new IntPtr(s.Window));
        Log("activation",new {window=s.Window,success=success});
    }
    static void Install() {
        string exe=Path.Combine(Root,"ClaudeNotify.exe");
        using(var k=Registry.CurrentUser.CreateSubKey("Software\\Classes\\AppUserModelId\\"+AppId)) {
            k.SetValue("DisplayName","Claude Code"); k.SetValue("ShowInSettings",1,RegistryValueKind.DWord);
            k.SetValue("CustomActivator","{"+ActivatorId+"}");
            k.SetValue("IconUri",Path.Combine(Root,"claude.png"));
            k.SetValue("IconBackgroundColor","0");
        }
        using(var k=Registry.CurrentUser.CreateSubKey("Software\\Classes\\CLSID\\{"+ActivatorId+"}\\LocalServer32")) {
            k.SetValue("","\""+exe+"\" --com-server");
        }
        string shortcutDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Claude Code Notifications");
        Directory.CreateDirectory(shortcutDirectory);
        string shortcut=Path.Combine(shortcutDirectory,"Claude Code.lnk");
        Shortcut.Create(shortcut,exe,AppId);
        Native.SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
        Log("installed",new {exe=exe,shortcut=shortcut});
    }
}
[ComVisible(true), Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INotificationActivationCallback {
    void Activate([MarshalAs(UnmanagedType.LPWStr)]string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)]string invokedArgs, IntPtr data, uint count);
}
[ComVisible(true), Guid(Notifier.ActivatorId), ClassInterface(ClassInterfaceType.None)]
public class ToastActivator : INotificationActivationCallback {
    public void Activate(string appUserModelId, string invokedArgs, IntPtr data, uint count) {
        try {
            if(appUserModelId != Notifier.AppId && appUserModelId != "Local.ClaudeCode.Notifications") return;
            Notifier.Log("toast_clicked", new { token=invokedArgs });
            Notifier.ActivateToken(invokedArgs);
        } catch(Exception e) { Notifier.Log("activation_error",e.ToString()); }
        finally {try {using(var activity=EventWaitHandle.OpenExisting("Local\\ClaudeCodeToastActivity"))activity.Set();}catch{}}
    }
}
[ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IToastClassFactory {
    [PreserveSig] int CreateInstance(IntPtr outer,ref Guid iid,out IntPtr instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)]bool locked);
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public class ToastClassFactory : IToastClassFactory {
    public int CreateInstance(IntPtr outer,ref Guid iid,out IntPtr instance) {
        instance=IntPtr.Zero;
        if(outer!=IntPtr.Zero)return unchecked((int)0x80040110);
        IntPtr unknown=IntPtr.Zero;
        try {
            unknown=Marshal.GetIUnknownForObject(new ToastActivator());
            return Marshal.QueryInterface(unknown,ref iid,out instance);
        }catch(Exception e){return Marshal.GetHRForException(e);}
        finally {if(unknown!=IntPtr.Zero)Marshal.Release(unknown);}
    }
    public int LockServer(bool locked){return 0;}
}
public static class Native {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo {
        public int cb; public string reserved,desktop,title;
        public uint x,y,xSize,ySize,xCountChars,yCountChars,fillAttribute,flags;
        public ushort showWindow,reservedSize;
        public IntPtr reservedData,stdInput,stdOutput,stdError;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {
        public IntPtr process,thread; public uint processId,threadId;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateProcessW(string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,[MarshalAs(UnmanagedType.Bool)]bool inheritHandles,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
    public static int StartReceiver(string exe) {
        var startup=new StartupInfo();startup.cb=Marshal.SizeOf(typeof(StartupInfo));
        startup.flags=1;startup.showWindow=0; // STARTF_USESHOWWINDOW, SW_HIDE.
        ProcessInfo process;
        // A long-lived receiver must never inherit Claude's hook pipes: an open
        // inherited writer prevents the caller from observing EOF after hook exit.
        if(!CreateProcessW(exe,new StringBuilder("\""+exe+"\" --com-server"),IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,Path.GetDirectoryName(exe),ref startup,out process))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {return (int)process.processId;}
        finally {CloseHandle(process.thread);CloseHandle(process.process);}
    }
    [DllImport("ole32.dll")] public static extern int CoRegisterClassObject(ref Guid clsid,[MarshalAs(UnmanagedType.Interface)] IToastClassFactory factory,uint context,uint flags,out uint cookie);
    [DllImport("ole32.dll")] public static extern int CoRevokeClassObject(uint cookie);
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
    [DllImport("kernel32.dll")] static extern bool AttachConsole(uint process);
    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder text,int length);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int length);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h,int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot,ref PE entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot,ref PE entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct PE {
        public uint size,usage,id;public UIntPtr heap;public uint module,threads,parent;public int priority;public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string name;
    }
    public static int WindowPid(IntPtr h) { uint p;GetWindowThreadProcessId(h,out p);return (int)p; }
    public static string Title(IntPtr h) {var b=new StringBuilder(512);GetWindowText(h,b,b.Capacity);return b.ToString();}
    public static int FindClaudeAncestor() {
        var entries=new Dictionary<uint,PE>();var h=CreateToolhelp32Snapshot(2,0);
        try {var e=new PE();e.size=(uint)Marshal.SizeOf(typeof(PE));if(Process32FirstW(h,ref e)) do {entries[e.id]=e;} while(Process32NextW(h,ref e));} finally {CloseHandle(h);}
        uint id=(uint)Process.GetCurrentProcess().Id;
        for(int i=0;i<30 && entries.ContainsKey(id);i++) {var e=entries[id];if(e.name.Equals("claude.exe",StringComparison.OrdinalIgnoreCase))return (int)id;if(e.parent==id)break;id=e.parent;}
        return 0;
    }
    public static IntPtr ConsoleOwner(int pid) {
        FreeConsole();
        if(!AttachConsole((uint)pid))return IntPtr.Zero;
        try {
            IntPtr w=GetAncestor(GetConsoleWindow(),3);
            var b=new StringBuilder(128);GetClassName(w,b,b.Capacity);
            return b.ToString()=="CASCADIA_HOSTING_WINDOW_CLASS" || b.ToString()=="CASCADIA_HOSTING_CLASS_WINDOW" || b.ToString()=="ConsoleWindowClass" ? w : IntPtr.Zero;
        } finally {FreeConsole();}
    }
    public static bool Focus(IntPtr w) {
        if(IsIconic(w))ShowWindowAsync(w,9);
        if(SetForegroundWindow(w))return true;
        uint p,foreground=GetWindowThreadProcessId(GetForegroundWindow(),out p),current=GetCurrentThreadId();
        bool attached=foreground!=current && AttachThreadInput(current,foreground,true);
        try {SetForegroundWindow(w);} finally {if(attached)AttachThreadInput(current,foreground,false);}
        // Window restoration is asynchronous; let its activation finish before logging.
        for(int i=0;i<10 && GetForegroundWindow()!=w;i++) Thread.Sleep(100);
        return GetForegroundWindow()==w;
    }
}
// A real Start Menu shortcut with PKEY_AppUserModel_ID is required for desktop toasts.
public static class Shortcut {
    [ComImport,Guid("00021401-0000-0000-C000-000000000046")] class ShellLink {}
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellLink {
        void GetPath(IntPtr a,int b,IntPtr c,int d);void GetIDList(out IntPtr a);void SetIDList(IntPtr a);void GetDescription(IntPtr a,int b);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string a);void GetWorkingDirectory(IntPtr a,int b);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string a);void GetArguments(IntPtr a,int b);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string a);void GetHotkey(out short a);void SetHotkey(short a);void GetShowCmd(out int a);void SetShowCmd(int a);void GetIconLocation(IntPtr a,int b,out int c);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string a,int b);void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string a,int b);void Resolve(IntPtr a,int b);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string a);
    }
    [StructLayout(LayoutKind.Sequential,Pack=4)] struct PK {public Guid fmtid;public uint pid;}
    [StructLayout(LayoutKind.Explicit,Size=24)] struct PV {[FieldOffset(0)]public ushort type;[FieldOffset(8)]public IntPtr value;}
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore {void GetCount(out uint count);void GetAt(uint i,out PK key);void GetValue(ref PK key,out PV value);void SetValue(ref PK key,ref PV value);void Commit();}
    public static void Create(string path,string exe,string appId) {
        var link=(IShellLink)new ShellLink();link.SetPath(exe);link.SetDescription("Claude Code desktop notifications");link.SetWorkingDirectory(Path.GetDirectoryName(exe));link.SetIconLocation(Path.Combine(Path.GetDirectoryName(exe),"claude.ico"),0);
        var key=new PK{fmtid=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),pid=5};var val=new PV{type=31,value=Marshal.StringToCoTaskMemUni(appId)};
        try {
            var store=(IPropertyStore)link;store.SetValue(ref key,ref val);
            var clsidKey=new PK{fmtid=key.fmtid,pid=26};
            var clsidValue=new PV{type=72,value=Marshal.AllocCoTaskMem(16)};
            try {Marshal.StructureToPtr(new Guid(Notifier.ActivatorId),clsidValue.value,false);store.SetValue(ref clsidKey,ref clsidValue);}finally{Marshal.FreeCoTaskMem(clsidValue.value);}
            store.Commit();((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path,true);
        } finally {Marshal.FreeCoTaskMem(val.value);Marshal.FinalReleaseComObject(link);}
    }
}
