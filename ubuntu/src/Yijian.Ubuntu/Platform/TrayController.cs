using Avalonia;
using Avalonia.Controls;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu.Platform;

internal sealed class TrayController : IDisposable {
    readonly TrayIcon icon;
    readonly MainWindow window;
    readonly Action quit;
    public TrayController(MainWindow window,Action quit) {
        this.window=window;this.quit=quit;
        icon=new TrayIcon{Icon=new WindowIcon(Path.Combine(AppContext.BaseDirectory,"Assets","app.ico")),IsVisible=true};
        icon.Clicked+=(s,e)=>window.RestoreWindow();UpdateLanguage();
        TrayIcon.SetIcons(Application.Current!,new TrayIcons{icon});
    }
    public void UpdateLanguage() {
        icon.ToolTipText=T("一件 Todo · 点击打开，右键查看菜单");
        var menu=new NativeMenu();var open=new NativeMenuItem(T("打开一件 Todo"));open.Click+=(s,e)=>window.RestoreWindow();menu.Items.Add(open);menu.Items.Add(new NativeMenuItemSeparator());var exit=new NativeMenuItem(T("退出程序"));exit.Click+=(s,e)=>quit();menu.Items.Add(exit);icon.Menu=menu;
    }
    public void Dispose(){icon.Dispose();TrayIcon.SetIcons(Application.Current!,null);}
}
