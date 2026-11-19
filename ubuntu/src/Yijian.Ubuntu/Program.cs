using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Yijian.Ubuntu.Platform;

namespace Yijian.Ubuntu;

internal static class Program {
    [STAThread]
    public static void Main(string[] args) {
        var dataRoot=DataPaths.Default;
        var openNote=false;
        for(int i=0;i<args.Length;i++) {
            if(args[i]=="--data-dir"&&i+1<args.Length) { dataRoot=Path.GetFullPath(args[++i]); }
            else if(args[i]=="--open-note") { openNote=true; }
        }
        DataPaths.EnsurePrivate(dataRoot);
        using var instance=new SingleInstance(dataRoot);
        if(!instance.IsFirst){
            instance.ActivateExisting(openNote);
            return;
        }
        App.DataRoot=dataRoot;App.Instance=instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
