using static Yijian.Core.Strings;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Yijian.Core;

namespace Yijian.Win;

public sealed class App : Application {
    Mutex? mutex;
    [STAThread] public static void Main() { var app=new App(); app.Run(); }
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YijianTodo");
        var openNote=false;
        for(int i=0;i<e.Args.Length;i++) {
            if(e.Args[i]=="--data-dir"&&i+1<e.Args.Length) root=Path.GetFullPath(e.Args[++i]);
            else if(e.Args[i]=="--open-note") openNote=true;
        }
        mutex=new Mutex(true,"Local\\YijianTodo-"+Environment.UserName,out var created);
        if(!created) { Native.ActivateExisting(openNote); Shutdown(); return; }
        DispatcherUnhandledException+=(s,args)=> { MessageBox.Show(T("操作未完成：")+args.Exception.Message,T("一件 Todo")); args.Handled=true; };
        var win=new MainWindow(new StateStorage(root));
        win.Show();
        if(openNote) win.Dispatcher.InvokeAsync(win.OpenNewNote);
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
internal static class Native {
    public const int WmHotkey=0x0312;
    public const int HotkeyId=0x2F02;
    public const uint ModAlt=0x0001,ModControl=0x0002,ModShift=0x0004,ModWin=0x0008,ModNoRepeat=0x4000;
    public static readonly int ActivateMessage=(int)RegisterWindowMessage("YijianTodo.Activate.v1");
    public static readonly int OpenNoteMessage=(int)RegisterWindowMessage("YijianTodo.OpenNote.v1");
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string? cls,string title);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd,int id,uint fsModifiers,uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd,int id);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    public static void ActivateExisting(bool openNote=false) { var h=FindWindow(null,"一件 Todo"); if(h==IntPtr.Zero)h=FindWindow(null,"Yijian Todo"); if(h!=IntPtr.Zero)PostMessage(h,openNote?OpenNoteMessage:ActivateMessage,IntPtr.Zero,IntPtr.Zero); }
    public static void Round(Window window) { int preference=2; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,33,ref preference,4); }
}
internal static class SecretStore {
    [StructLayout(LayoutKind.Sequential)] struct Blob {public int Length;public IntPtr Data;}
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CryptProtectData(ref Blob input,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static byte[] Transform(byte[] bytes,bool protect) {
        var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)}; Blob output=default;
        try { Marshal.Copy(bytes,0,input.Data,bytes.Length);
            bool ok=protect?CryptProtectData(ref input,"一件 Todo API Key",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok) throw new IOException(T("无法访问 Windows 加密存储。")); var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        } finally { for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data); }
    }
    public static string Read(string root) {try {var path=Path.Combine(root,"key.dpapi");return File.Exists(path)?Encoding.UTF8.GetString(Transform(File.ReadAllBytes(path),false)):"";}catch{return "";}}
    public static void Save(string root,string key,bool remember) {Directory.CreateDirectory(root);var path=Path.Combine(root,"key.dpapi");if(remember&&!string.IsNullOrEmpty(key)){var temp=path+".tmp";File.WriteAllBytes(temp,Transform(Encoding.UTF8.GetBytes(key),true));File.Move(temp,path,true);}else if(File.Exists(path))File.Delete(path);}
}
