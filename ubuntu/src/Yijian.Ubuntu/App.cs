using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Yijian.Core;
using Yijian.Ubuntu.Platform;

namespace Yijian.Ubuntu;

public sealed class App : Application {
    internal static string DataRoot {get;set;}=DataPaths.Default;
    internal static SingleInstance? Instance {get;set;}
    public override void Initialize()=>AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted() {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var window=new MainWindow(new StateStorage(DataRoot));desktop.MainWindow=window;
            if(Instance!=null) {
                Instance.Activated+=()=>Dispatcher.UIThread.Post(window.RestoreWindow);
                Instance.OpenNoteRequested+=()=>Dispatcher.UIThread.Post(()=>window.OpenNewNote());
            }
            if(Environment.GetCommandLineArgs().Contains("--open-note")) {
                Dispatcher.UIThread.Post(window.OpenNewNote);
            }
            Dispatcher.UIThread.UnhandledException+=(s,e)=>{window.ReportError(e.Exception.Message);e.Handled=true;};
        }
        base.OnFrameworkInitializationCompleted();
    }
}
