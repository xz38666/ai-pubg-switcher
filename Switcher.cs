using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Serialization;
using System.Collections.Generic;
using Microsoft.Win32;

public class SavedValue {
    public string Hive, Path, Name, Kind, Text;
    public bool Exists;
}
public class Snapshot {
    public List<SavedValue> Values = new List<SavedValue>();
}
public class ProxyState {
    public int Flags;
    public string Url, Server, Bypass;
}
static class Native {
    [StructLayout(LayoutKind.Explicit)] public struct Value {
        [FieldOffset(0)] public int Number;
        [FieldOffset(0)] public IntPtr Text;
        [FieldOffset(0)] public long FileTime;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Option { public int Id; public Value Data; }
    [StructLayout(LayoutKind.Sequential)] public struct Options {
        public int Size; public IntPtr Connection; public int Count, Error; public IntPtr Items;
    }
    [DllImport("wininet.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool InternetSetOption(IntPtr h, int op, ref Options data, int size);
    [DllImport("wininet.dll", EntryPoint="InternetSetOptionW", SetLastError=true)]
    static extern bool Notify(IntPtr h, int op, IntPtr data, int size);
    [DllImport("wininet.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool InternetQueryOption(IntPtr h, int op, ref Options data, ref int size);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr p);
    public static ProxyState Query() {
        int step=Marshal.SizeOf(typeof(Option)); IntPtr mem=Marshal.AllocHGlobal(step*4);
        int[] ids={10,4,2,3};
        try {
            for(int i=0;i<4;i++) { Option o=new Option(); o.Id=ids[i]; Marshal.StructureToPtr(o,IntPtr.Add(mem,i*step),false); }
            Options list=new Options(); list.Size=Marshal.SizeOf(typeof(Options)); list.Count=4; list.Items=mem; int size=list.Size;
            if(!InternetQueryOption(IntPtr.Zero,75,ref list,ref size)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ProxyState s=new ProxyState();
            for(int i=0;i<4;i++) { Option o=(Option)Marshal.PtrToStructure(IntPtr.Add(mem,i*step),typeof(Option));
                if(i==0) s.Flags=o.Data.Number;
                else { string t=o.Data.Text==IntPtr.Zero?"":Marshal.PtrToStringUni(o.Data.Text); if(i==1)s.Url=t; if(i==2)s.Server=t;if(i==3)s.Bypass=t; if(o.Data.Text!=IntPtr.Zero)GlobalFree(o.Data.Text); }
            } return s;
        } finally { Marshal.FreeHGlobal(mem); }
    }
    public static void Set(bool ai) {
        int step=Marshal.SizeOf(typeof(Option)); IntPtr mem=Marshal.AllocHGlobal(step*2);
        IntPtr url=Marshal.StringToHGlobalUni(ai?Program.PacUrl:"");
        try {
            Option flags=new Option();flags.Id=1;flags.Data.Number=ai?5:1;
            Option pac=new Option();pac.Id=4;pac.Data.Text=url;
            Marshal.StructureToPtr(flags,mem,false);Marshal.StructureToPtr(pac,IntPtr.Add(mem,step),false);
            Options list=new Options();list.Size=Marshal.SizeOf(typeof(Options));list.Count=2;list.Items=mem;
            if(!InternetSetOption(IntPtr.Zero,75,ref list,list.Size))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }finally {Marshal.FreeHGlobal(url);Marshal.FreeHGlobal(mem);}
        Refresh();
    }
    public static void Refresh() { Notify(IntPtr.Zero,39,IntPtr.Zero,0);Notify(IntPtr.Zero,37,IntPtr.Zero,0); }
}
static class Program {
    public const string PacUrl="http://127.0.0.1:19825/proxy.pac";
    public static string Root=AppDomain.CurrentDomain.BaseDirectory;
    const string Inet=@"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    const string Run=@"Software\Microsoft\Windows\CurrentVersion\Run";
    static string Backup {get{return Path.Combine(Root,"settings-before.xml");}}
    static RegistryKey Hive(string hive){return hive=="HKCU"?Registry.CurrentUser:Registry.LocalMachine;}
    static SavedValue Read(string hive,string path,string name) {
        SavedValue s=new SavedValue();s.Hive=hive;s.Path=path;s.Name=name;
        using(RegistryKey k=Hive(hive).OpenSubKey(path)) {
            object v=k==null?null:k.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            if(v==null)return s;s.Exists=true;s.Kind=k.GetValueKind(name).ToString();
            s.Text=v is byte[]?Convert.ToBase64String((byte[])v):Convert.ToString(v);
        } return s;
    }
    static Snapshot Capture() {
        Snapshot s=new Snapshot();
        foreach(string hive in new[]{"HKCU","HKLM"}) {
            foreach(string name in new[]{"AutoConfigURL","ProxyEnable","AutoDetect"})s.Values.Add(Read(hive,Inet,name));
            foreach(string name in new[]{"DefaultConnectionSettings","SavedLegacySettings"})s.Values.Add(Read(hive,Inet+@"\Connections",name));
        }
        s.Values.Add(Read("HKCU",Run,"AI-PUBG-PAC"));return s;
    }
    static void Save(Snapshot s,string file){using(var w=File.Create(file))new XmlSerializer(typeof(Snapshot)).Serialize(w,s);}
    static Snapshot Load(string file){using(var r=File.OpenRead(file))return (Snapshot)new XmlSerializer(typeof(Snapshot)).Deserialize(r);}
    static void Restore(Snapshot s) {
        foreach(SavedValue v in s.Values)using(RegistryKey k=Hive(v.Hive).CreateSubKey(v.Path)) {
            if(!v.Exists){k.DeleteValue(v.Name,false);continue;}
            RegistryValueKind kind=(RegistryValueKind)Enum.Parse(typeof(RegistryValueKind),v.Kind);
            object value=v.Text;if(kind==RegistryValueKind.Binary)value=Convert.FromBase64String(v.Text);
            if(kind==RegistryValueKind.DWord)value=int.Parse(v.Text);
            k.SetValue(v.Name,value,kind);
        } Native.Refresh();
    }
    public static string Pac() {
        string source;
        string file=Path.Combine(Root,"comss-source.pac");
        if(File.Exists(file))source=File.ReadAllText(file,Encoding.UTF8);
        else using(Stream stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("comss-source.pac"))
            using(StreamReader reader=new StreamReader(stream,Encoding.UTF8))source=reader.ReadToEnd();
        string marker="function FindProxyForURL(url, host) {";
        if(!source.Contains(marker))throw new Exception("В файле нет функции PAC.");
        string prefix=marker+"\n host = host.toLowerCase();\n var direct = ['antigravity.google','steampowered.com','steamcommunity.com','steamcontent.com','steamstatic.com','steamserver.net','steamusercontent.com','pubg.com','pubg.net','krafton.com','battlegrounds.pubg.com','battleye.com'];\n for(var j=0;j<direct.length;j++){if(host===direct[j] || dnsDomainIs(host,'.'+direct[j])) return 'DIRECT';}\n";
        return source.Replace(marker,prefix);
    }
    class LocalClient:WebClient {
        protected override WebRequest GetWebRequest(Uri address){WebRequest r=base.GetWebRequest(address);r.Timeout=1200;return r;}
    }
    static WebClient DirectClient(){WebClient c=new LocalClient();c.Proxy=null;c.Encoding=Encoding.UTF8;return c;}
    static bool ServerReady() {
        try {using(WebClient c=DirectClient())return c.DownloadString(PacUrl)==Pac();}catch{return false;}
    }
    public static void EnsureServer() {
        if(ServerReady())return;
        Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--serve"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        for(int i=0;i<30;i++){Thread.Sleep(100);if(ServerReady())return;}
        throw new Exception("Не удалось запустить PAC на 127.0.0.1:19825. Возможно, порт занят.");
    }
    static void Serve() {
        bool created;using(Mutex mutex=new Mutex(true,@"Local\AI-PUBG-PAC-19825",out created)) {
            if(!created)return;Pac(); TcpListener listener=new TcpListener(IPAddress.Loopback,19825);listener.Start();
            while(true)using(TcpClient client=listener.AcceptTcpClient()) {
                try {
                    client.ReceiveTimeout=1500;client.SendTimeout=1500;
                    using(NetworkStream stream=client.GetStream()) {
                        // Read a bounded request; listen only on loopback, never accept proxy requests.
                        byte[] req=new byte[4096];int n=stream.Read(req,0,req.Length);
                        string first=Encoding.ASCII.GetString(req,0,n).Split('\n')[0].Trim();
                        bool valid=first=="GET /proxy.pac HTTP/1.1"||first=="GET /proxy.pac HTTP/1.0";
                        byte[] body=Encoding.UTF8.GetBytes(valid?Pac():"Not found");
                        byte[] head=Encoding.ASCII.GetBytes("HTTP/1.1 "+(valid?"200 OK":"404 Not Found")+"\r\nContent-Type: application/x-ns-proxy-autoconfig; charset=utf-8\r\nCache-Control: no-cache\r\nConnection: close\r\nContent-Length: "+body.Length+"\r\n\r\n");
                        stream.Write(head,0,head.Length);stream.Write(body,0,body.Length);
                    }
                }catch(IOException){}catch(SocketException){}
            }
        }
    }
    static void Apply(string mode) {
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new Exception("Нужны права администратора для системных настроек прокси.");
        if(mode!="ai"&&mode!="direct"&&mode!="restore")throw new Exception("Неизвестный режим.");
        if(mode=="restore"&&!File.Exists(Backup))throw new Exception("Резервная копия ещё не создана.");
        if(mode=="ai")EnsureServer();
        Snapshot before=Capture();if(!File.Exists(Backup))Save(before,Backup);
        string transaction=Path.Combine(Root,"last-transaction.xml");Save(before,transaction);
        try {
            if(mode=="restore")Restore(Load(Backup));
            else {
                foreach(string hive in new[]{"HKCU","HKLM"})using(RegistryKey k=Hive(hive).CreateSubKey(Inet)) {
                    k.SetValue("ProxyEnable",0,RegistryValueKind.DWord);k.SetValue("AutoDetect",0,RegistryValueKind.DWord);
                    if(mode=="ai")k.SetValue("AutoConfigURL",PacUrl);else k.DeleteValue("AutoConfigURL",false);
                }
                Native.Set(mode=="ai");
                using(RegistryKey k=Registry.CurrentUser.CreateSubKey(Run)) {
                    if(mode=="ai")k.SetValue("AI-PUBG-PAC","\""+Application.ExecutablePath+"\" --serve");
                    else k.DeleteValue("AI-PUBG-PAC",false);
                }
                ProxyState s=Native.Query();
                if((mode=="ai" && ((s.Flags&14)!=4||s.Url!=PacUrl)) || (mode=="direct" && (s.Flags&14)!=0))throw new Exception("Windows не подтвердила переключение. Настройки восстановлены.");
            }
            File.WriteAllText(Path.Combine(Root,"last-result.txt"),DateTime.Now.ToString("s")+" OK "+mode,Encoding.UTF8);
        } catch {Restore(before);throw;}
    }
    public static string Status() {
        ProxyState s=Native.Query();
        if((s.Flags&14)==0)return "PUBG: системный прокси WinINet выключен";
        if((s.Flags&14)==4&&s.Url==PacUrl)return ServerReady()?"ИИ: PAC включён и доступен":"ИИ: PAC включён, сервер недоступен";
        return "Другие настройки прокси Windows\r\nPAC: "+s.Url;
    }
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length>0) {
                if(args[0]=="--serve"){Serve();return 0;}
                if(args[0]=="--apply"&&args.Length==2){Apply(args[1]);return 0;}
                if(args[0]=="--selftest"){
                    ProxyState before=Native.Query();EnsureServer();
                    File.WriteAllText(Path.Combine(Root,"effective.pac"),Pac(),new UTF8Encoding(false));
                    string report="PAC server: PASS\r\nNative proxy query: PASS\r\nFlags: "+before.Flags+"\r\nCurrent URL: "+before.Url+"\r\nSettings were not changed.\r\n";
                    File.WriteAllText(Path.Combine(Root,"selftest.txt"),report,Encoding.UTF8);return 0;
                }
                throw new Exception("Неизвестная команда.");
            }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SwitcherForm());return 0;
        }catch(Exception ex){File.WriteAllText(Path.Combine(Root,"last-error.txt"),ex.ToString(),Encoding.UTF8);if(args.Length==0)MessageBox.Show(ex.Message,"ИИ / PUBG");return 1;}
    }
}
class SwitcherForm:Form {
    Label status;Button ai,game,restore;System.Windows.Forms.Timer timer;
    public SwitcherForm() {
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); Text="ИИ / PUBG — переключатель прокси";ClientSize=new Size(580,380);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
        Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(245,247,251);StartPosition=FormStartPosition.CenterScreen;
        Label title=new Label{Text="Прокси для ИИ. Прямое подключение для игры.",Location=new Point(24,24),Size=new Size(535,32),Font=new Font("Segoe UI",13,FontStyle.Bold)};Controls.Add(title);
        status=new Label{Location=new Point(24,70),Size=new Size(530,56),ForeColor=Color.FromArgb(35,65,100)};Controls.Add(status);
        ai=Button("ИИ — включить",24,135,255,54,delegate{Switch("ai");});
        game=Button("PUBG — выключить",298,135,255,54,delegate{Switch("direct");});
        restore=Button("Восстановить исходные настройки",24,209,529,39,delegate{Switch("restore");});
        Controls.Add(new Label{Text="Перед переключением закрой COMSS, чтобы он не менял настройки обратно.\r\nПосле переключения перезапусти Steam / браузер. Игра не закрывается автоматически.\r\nDNS, hosts, VPN и переменные среды не меняются. Доступ к ИИ идёт через COMSS.",Location=new Point(24,269),Size=new Size(530,96),Font=new Font("Segoe UI",9)});
        timer=new System.Windows.Forms.Timer{Interval=4000};timer.Tick+=delegate{RefreshStatus();};timer.Start();RefreshStatus();
    }
    Button Button(string text,int x,int y,int w,int h,EventHandler click){Button b=new Button{Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.System};b.Click+=click;Controls.Add(b);return b;}
    void RefreshStatus(){try{status.Text=Program.Status();}catch(Exception e){status.Text="Не удалось прочитать настройки: "+e.Message;}restore.Enabled=File.Exists(Path.Combine(Program.Root,"settings-before.xml"));}
    async void Switch(string mode) {
        ai.Enabled=game.Enabled=restore.Enabled=false;timer.Stop();
        try {
            if(mode=="ai")Program.EnsureServer();
            var info=new ProcessStartInfo(Application.ExecutablePath,"--apply "+mode){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            using(Process p=Process.Start(info)){await System.Threading.Tasks.Task.Run(delegate{p.WaitForExit();});if(p.ExitCode!=0)throw new Exception("Переключение не выполнено. Подробности: last-error.txt рядом с программой.");}
            RefreshStatus();MessageBox.Show(mode=="restore"?"Исходные настройки восстановлены.":"Настройки переключены. Перезапусти Steam или браузер.\r\nДоступ к ИИ и запуск PUBG нужно проверить в самих приложениях.","ИИ / PUBG",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }catch(System.ComponentModel.Win32Exception e){if(e.NativeErrorCode==1223)MessageBox.Show("Запрос прав отменён. Настройки не переключались.");else MessageBox.Show(e.Message);}
        catch(Exception e){MessageBox.Show(e.Message,"Не удалось переключить",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{ai.Enabled=game.Enabled=true;RefreshStatus();timer.Start();}
    }
}
