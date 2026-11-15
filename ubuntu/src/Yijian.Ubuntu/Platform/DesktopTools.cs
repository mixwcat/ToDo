using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu.Platform;

internal static class DesktopTools {
    // Arguments never contain API keys; secrets travel only over redirected stdin/stdout.
    public static async Task<(int Code,string Output)> RunAsync(string name,IEnumerable<string> args,string? input=null,int timeoutSeconds=10) {
        var start=new ProcessStartInfo(name){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=start};
        try{process.Start();}catch(System.ComponentModel.Win32Exception){throw new IOException(T("缺少系统组件，请安装 libsecret-tools 后重试。"));}
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try {
            var output=process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors=process.StandardError.ReadToEndAsync(deadline.Token);
            if(input!=null)await process.StandardInput.WriteAsync(input.AsMemory(),deadline.Token);
            process.StandardInput.Close();await process.WaitForExitAsync(deadline.Token);
            await errors;return(process.ExitCode,await output);
        }catch(OperationCanceledException){try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}throw new IOException(T("系统密钥环未响应，请解锁密钥环后重试。"));}
    }
    public static async Task<bool> HasTrayHostAsync() {
        if(!OperatingSystem.IsLinux())return false;
        try{var result=await RunAsync("gdbus",["call","--session","--dest","org.freedesktop.DBus","--object-path","/org/freedesktop/DBus","--method","org.freedesktop.DBus.NameHasOwner","org.kde.StatusNotifierWatcher"],timeoutSeconds:3);return result.Code==0&&result.Output.Contains("true",StringComparison.Ordinal);}
        catch(IOException){return false;}
    }
}

internal sealed class SecretStore(string directory) {
    readonly string profile=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory))))[..24];
    public async Task<string> ReadAsync() {
        var result=await DesktopTools.RunAsync("secret-tool",["lookup","application","yijian-todo","profile",profile]);
        if(result.Code is not(0 or 1))throw new IOException(T("无法访问系统密钥环，请先解锁密钥环。"));
        return result.Output.TrimEnd('\r','\n');
    }
    public async Task SaveAsync(string key,bool remember,bool previouslyRemembered) {
        if(!remember&&!previouslyRemembered)return;
        var result=remember&&!string.IsNullOrEmpty(key)
            ?await DesktopTools.RunAsync("secret-tool",["store","--label=Yijian Todo API key","application","yijian-todo","profile",profile],key+"\n")
            :await DesktopTools.RunAsync("secret-tool",["clear","application","yijian-todo","profile",profile]);
        if(result.Code!=0&&!(result.Code==1&&(!remember||string.IsNullOrEmpty(key))))throw new IOException(T("无法访问系统密钥环，请先解锁密钥环。"));
    }
}
