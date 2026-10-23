using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Win32;

static class Setup {
    static readonly string Dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AI-PUBG-Switcher");
    static readonly string Exe=Path.Combine(Dir,"AI-PUBG-Switcher.exe");
    const string Reg=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AI-PUBG-Switcher";
    static readonly string[] Payload={"AI-PUBG-Switcher.exe","AI-icon.ico","AI-icon.png","Readme.txt"};
    static string Menu { get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ИИ и PUBG");} }
    static string Desktop {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"ИИ и PUBG.lnk");}}
    static byte[] Resource(string name) {
        using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if(s==null)throw new Exception("Нет файла в установщике: "+name);
            using(MemoryStream m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}
        }
    }
    static string Hash(byte[] b){using(SHA256 s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(b)).Replace("-","");}
    static void Shortcut(string path,string target,string args) {
        Type t=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(t);
        object link=t.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
        Type lt=link.GetType();
        foreach(var item in new[]{new[]{"TargetPath",target},new[]{"Arguments",args},new[]{"WorkingDirectory",Dir},new[]{"IconLocation",Path.Combine(Dir,"AI-icon.ico")+",0"}})
            lt.InvokeMember(item[0],BindingFlags.SetProperty,null,link,new object[]{item[1]});
        lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }
    static void RemoveShortcut(string path,string expected) {
        if(!File.Exists(path))return;
        Type t=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(t);
        object link=t.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
        string target=(string)link.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        if(string.Equals(target,expected,StringComparison.OrdinalIgnoreCase))File.Delete(path);
    }
    static void StopInstalled() {
        foreach(Process p in Process.GetProcessesByName("AI-PUBG-Switcher")) {
            using(p) {
                string path;
                try {path=p.MainModule.FileName;} catch {continue;}
                if(!string.Equals(path,Exe,StringComparison.OrdinalIgnoreCase))continue;
                p.Kill();if(!p.WaitForExit(5000))throw new Exception("Не удалось закрыть программу. Закрой её и повтори установку.");
            }
        }
    }
    static void Apply(string mode) {
        using(Process p=Process.Start(new ProcessStartInfo(Exe,"--apply "+mode){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})) {
            p.WaitForExit();if(p.ExitCode!=0)throw new Exception("Windows не подтвердила настройку. Подробности: "+Path.Combine(Dir,"last-error.txt"));
        }
    }
    static void Install() {
        Directory.CreateDirectory(Dir);
        byte[] binary=Resource(Payload[0]);
        if(File.Exists(Exe)&&Hash(File.ReadAllBytes(Exe))!=Hash(binary))StopInstalled();
        foreach(string name in Payload) {
            string path=Path.Combine(Dir,name);byte[] data=Resource(name);
            if(!File.Exists(path)||Hash(File.ReadAllBytes(path))!=Hash(data))File.WriteAllBytes(path,data);
        }
        string installedSetup=Path.Combine(Dir,"Setup.exe");
        if(!string.Equals(Application.ExecutablePath,installedSetup,StringComparison.OrdinalIgnoreCase))File.Copy(Application.ExecutablePath,installedSetup,true);
        // Launch the persistent background process as the signed-in user before requesting elevation.
        Process.Start(new ProcessStartInfo(Exe,"--serve"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        Apply("ai");
        Directory.CreateDirectory(Menu);
        Shortcut(Desktop,Exe,"");Shortcut(Path.Combine(Menu,"ИИ и PUBG.lnk"),Exe,"");
        Shortcut(Path.Combine(Menu,"Удалить.lnk"),installedSetup,"--uninstall");
        using(RegistryKey k=Registry.CurrentUser.CreateSubKey(Reg)) {
            k.SetValue("DisplayName","ИИ и PUBG");k.SetValue("DisplayVersion","1.0.0");
            k.SetValue("DisplayIcon",Path.Combine(Dir,"AI-icon.ico"));k.SetValue("InstallLocation",Dir);
            k.SetValue("UninstallString","\""+installedSetup+"\" --uninstall");
            k.SetValue("NoModify",1,RegistryValueKind.DWord);k.SetValue("NoRepair",1,RegistryValueKind.DWord);
        }
    }
    static void Uninstall() {
        // Restore first: never remove a PAC server while Windows still depends on it.
        if(!File.Exists(Path.Combine(Dir,"settings-before.xml")))throw new Exception("Не найдена резервная копия настроек. Удаление остановлено, чтобы не оставить Windows без PAC.");
        Apply("restore");StopInstalled();
        RemoveShortcut(Desktop,Exe);RemoveShortcut(Path.Combine(Menu,"ИИ и PUBG.lnk"),Exe);
        RemoveShortcut(Path.Combine(Menu,"Удалить.lnk"),Path.Combine(Dir,"Setup.exe"));
        if(Directory.Exists(Menu)&&Directory.GetFileSystemEntries(Menu).Length==0)Directory.Delete(Menu);
        // Keep the original snapshot outside the application directory for manual recovery.
        string backup=Dir+"-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(backup);
        foreach(string name in new[]{"settings-before.xml","last-transaction.xml"})
            if(File.Exists(Path.Combine(Dir,name)))File.Copy(Path.Combine(Dir,name),Path.Combine(backup,name));
        using(RegistryKey k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall",true))if(k!=null)k.DeleteSubKeyTree("AI-PUBG-Switcher",false);
        var files=new List<string>(Payload);files.AddRange(new[]{"Setup.exe","settings-before.xml","last-transaction.xml","last-result.txt","last-error.txt","selftest.txt","effective.pac"});
        foreach(string name in files)if(File.Exists(Path.Combine(Dir,name)))File.Delete(Path.Combine(Dir,name));
        if(Directory.Exists(Dir)&&Directory.GetFileSystemEntries(Dir).Length==0)Directory.Delete(Dir);
    }
    static int Verify(string destination) {
        Directory.CreateDirectory(destination);StringBuilder report=new StringBuilder();
        foreach(string name in Payload){byte[] data=Resource(name);File.WriteAllBytes(Path.Combine(destination,name),data);report.AppendLine(name+" SHA256 "+Hash(data));}
        byte[] exe=Resource(Payload[0]);if(exe.Length<2||exe[0]!=77||exe[1]!=90)throw new Exception("Invalid PE payload");
        using(Icon icon=new Icon(Path.Combine(destination,"AI-icon.ico")))if(icon.Width<16)throw new Exception("Invalid icon");
        report.AppendLine("Payload extraction and icon: PASS. System settings were not changed.");
        File.WriteAllText(Path.Combine(destination,"verification.txt"),report.ToString(),Encoding.UTF8);return 0;
    }
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length==2&&args[0]=="--verify-payload")return Verify(Path.GetFullPath(args[1]));
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            bool preview=args.Length==2&&args[0]=="--preview";
            bool remove=args.Length>0&&(args[0]=="--uninstall"||args[0]=="--remove-installed");
            if(args.Length>0&&!remove&&!preview)throw new Exception("Неизвестная команда.");
            if(remove&&string.Equals(Application.ExecutablePath,Path.Combine(Dir,"Setup.exe"),StringComparison.OrdinalIgnoreCase)) {
                string temp=Path.Combine(Path.GetTempPath(),"AI-PUBG-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");
                File.Copy(Application.ExecutablePath,temp);Process.Start(new ProcessStartInfo(temp,"--remove-installed"){UseShellExecute=true});return 0;
            }
            using(Form f=new Form()) {
                f.Text=remove?"Удаление ИИ и PUBG":"Установка ИИ и PUBG";f.ClientSize=new Size(560,340);
                f.FormBorderStyle=FormBorderStyle.FixedDialog;f.MaximizeBox=false;f.StartPosition=FormStartPosition.CenterScreen;
                f.Font=new Font("Segoe UI",10);f.Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                PictureBox picture=new PictureBox{Location=new Point(22,22),Size=new Size(64,64),SizeMode=PictureBoxSizeMode.Zoom};
                using(MemoryStream ms=new MemoryStream(Resource("AI-icon.png")))using(Image i=Image.FromStream(ms))picture.Image=new Bitmap(i);f.Controls.Add(picture);
                f.Controls.Add(new Label{Text=remove?"Удаление программы":"ИИ автоматически при входе в Windows",Location=new Point(102,28),Size=new Size(435,58),Font=new Font("Segoe UI",14,FontStyle.Bold)});
                f.Controls.Add(new Label{Text=remove?"Будут восстановлены настройки прокси, сохранённые перед установкой. Ярлыки и автозапуск программы будут удалены.":"Установит программу, создаст ярлыки и включит ИИ в фоне.\r\nДоступ к ИИ идёт через серверы COMSS.\r\nSteam / PUBG используют прямые маршруты из правил.\r\nWindows запросит разрешение на изменение прокси.\r\nЗакрой COMSS AI перед установкой.",Location=new Point(24,103),Size=new Size(510,124)});
                f.Controls.Add(new Label{Text=remove?"Резервная копия настроек останется в LocalAppData.":"Для текущей учётной записи: %LOCALAPPDATA%\\AI-PUBG-Switcher\r\nWindows 10/11 x64. Доступность зависит от сети и COMSS.",Location=new Point(24,237),Size=new Size(515,42),Font=new Font("Segoe UI",9)});
                Button go=new Button{Text=remove?"Удалить":"Установить и включить",Location=new Point(226,291),Size=new Size(215,34)};
                Button cancel=new Button{Text="Отмена",Location=new Point(448,291),Size=new Size(88,34)};cancel.Click+=delegate{f.Close();};
                go.Click+=delegate {
                    go.Enabled=cancel.Enabled=false;f.UseWaitCursor=true;f.Refresh();
                    try {
                        if(remove)Uninstall();else Install();
                        MessageBox.Show(f,remove?"Программа удалена. Исходные настройки восстановлены.":"Готово! ИИ включён и запускается при входе в Windows.\r\nНа рабочем столе появился ярлык «ИИ и PUBG».\r\nПерезапусти браузер и проверь нужный ИИ-сервис.","ИИ и PUBG",MessageBoxButtons.OK,MessageBoxIcon.Information);f.Close();
                    }catch(System.ComponentModel.Win32Exception e){MessageBox.Show(f,e.NativeErrorCode==1223?"Разрешение Windows отменено. Установка не завершена. Можно повторить.":e.Message);}
                    catch(Exception e){MessageBox.Show(f,e.Message,"Операция не завершена",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                    finally{go.Enabled=cancel.Enabled=true;f.UseWaitCursor=false;}
                };
                f.Controls.Add(go);f.Controls.Add(cancel);f.AcceptButton=go;f.CancelButton=cancel;
                if(preview){f.Show();Application.DoEvents();using(Bitmap b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.GetFullPath(args[1]),System.Drawing.Imaging.ImageFormat.Png);}return 0;}
                Application.Run(f);
            }
            return 0;
        }catch(Exception ex){
            if(args.Length==2&&args[0]=="--verify-payload")File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]),"error.txt"),ex.ToString());
            else MessageBox.Show(ex.Message,"ИИ и PUBG",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
        }
    }
}
