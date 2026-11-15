using Avalonia;
using Yijian.Ubuntu.Platform;

namespace Yijian.Ubuntu;

internal static class Program {
    [STAThread]
    public static void Main(string[] args) {
        var dataRoot=DataPaths.Default;
        if(args.Length==2&&args[0]=="--data-dir")dataRoot=Path.GetFullPath(args[1]);
        DataPaths.EnsurePrivate(dataRoot);
        using var instance=new SingleInstance(dataRoot);
        if(!instance.IsFirst){instance.ActivateExisting();return;}
        App.DataRoot=dataRoot;App.Instance=instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
