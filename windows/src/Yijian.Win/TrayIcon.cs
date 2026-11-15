using static Yijian.Core.Strings;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Yijian.Win;

// Shell notification area integration, using the existing window's message loop.
// No extra UI framework, worker thread, or polling timer is needed.
internal sealed class TrayIcon : IDisposable {
    const int Callback=0x8000+42;
    readonly HwndSource source;
    readonly Action restore,menu;
    readonly uint taskbarCreated;
    IconData data;
    bool disposed;
    public bool IsAvailable {get;private set;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct IconData {
        public uint Size; public IntPtr Window; public uint Id,Flags,CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message,ref IconData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    public TrayIcon(Window window,Action restore,Action menu) {
        this.restore=restore;this.menu=menu;
        var handle=new WindowInteropHelper(window).Handle;
        source=HwndSource.FromHwnd(handle)??throw new InvalidOperationException(T("窗口尚未初始化。"));
        data=new IconData { Size=(uint)Marshal.SizeOf<IconData>(),Window=handle,Id=1,Flags=7,CallbackMessage=Callback,
            Icon=LoadImage(IntPtr.Zero,Path.Combine(AppContext.BaseDirectory,"Assets","app.ico"),1,32,32,0x10),
            Tip=T("一件 Todo · 点击打开，右键查看菜单"),Info="",InfoTitle="" };
        taskbarCreated=RegisterWindowMessage("TaskbarCreated");source.AddHook(Message);
        IsAvailable=data.Icon!=IntPtr.Zero&&Shell_NotifyIcon(0,ref data);
    }
    IntPtr Message(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
        if(disposed)return IntPtr.Zero;
        if(message==taskbarCreated) IsAvailable=Shell_NotifyIcon(0,ref data);
        else if(message==Native.ActivateMessage) { restore();handled=true; }
        else if(message==Callback) {
            var action=lParam.ToInt64()&0xffff;
            if(action is 0x202 or 0x203) restore();
            else if(action is 0x205 or 0x7b) {SetForegroundWindow(handle);menu();}
            handled=true;
        }
        return IntPtr.Zero;
    }
    public void UpdateLanguage() {if(disposed)return;data.Tip=T("一件 Todo · 点击打开，右键查看菜单");if(IsAvailable)Shell_NotifyIcon(1,ref data);}
    public void Dispose() {
        if(disposed)return;disposed=true;Shell_NotifyIcon(2,ref data);IsAvailable=false;
        if(!source.IsDisposed)source.RemoveHook(Message);if(data.Icon!=IntPtr.Zero)DestroyIcon(data.Icon);
    }
}
