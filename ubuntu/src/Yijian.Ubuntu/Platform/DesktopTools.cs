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
    public static void OpenWithDefaultViewer(string filePath) {
        if(string.IsNullOrWhiteSpace(filePath)||!File.Exists(filePath))throw new FileNotFoundException(T("无法打开图片：")+filePath);
        try {
            if(OperatingSystem.IsLinux()) {
                var start=new ProcessStartInfo("xdg-open"){UseShellExecute=false,CreateNoWindow=true};
                start.ArgumentList.Add(filePath);
                Process.Start(start);
            } else {
                Process.Start(new ProcessStartInfo(filePath){UseShellExecute=true});
            }
        } catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException) {
            throw new IOException(T("无法打开图片：")+ex.Message);
        }
    }

    public static async Task<byte[]?> TryReadLinuxClipboardImageBytesAsync() {
        if(!OperatingSystem.IsLinux())return null;
        string[] mimeCandidates=["image/png","image/jpeg","image/bmp","image/webp","image/gif"];
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) {
            try {
                var (_,typesOut)=await RunBestEffortTextAsync("wl-paste",["--list-types"],timeoutSeconds:2);
                if(!string.IsNullOrWhiteSpace(typesOut)) {
                    var available=typesOut.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                    foreach(var mime in mimeCandidates) {
                        if(available.Any(x=>x.Equals(mime,StringComparison.OrdinalIgnoreCase))) {
                            var bytes=await RunBestEffortBytesAsync("wl-paste",["--no-newline","--type",mime],timeoutSeconds:3);
                            if(bytes is {Length:>0})return bytes;
                        }
                    }
                }
            } catch { }
        }
        try {
            var (code,targetsOut)=await RunBestEffortTextAsync("xclip",["-selection","clipboard","-t","TARGETS","-o"],timeoutSeconds:2);
            if(code==0&&!string.IsNullOrWhiteSpace(targetsOut)) {
                var available=targetsOut.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                foreach(var mime in mimeCandidates) {
                    if(available.Any(x=>x.Equals(mime,StringComparison.OrdinalIgnoreCase))) {
                        var bytes=await RunBestEffortBytesAsync("xclip",["-selection","clipboard","-t",mime,"-o"],timeoutSeconds:3);
                        if(bytes is {Length:>0})return bytes;
                    }
                }
            }
        } catch { }
        return null;
    }

    static async Task<(int Code,string Output)> RunBestEffortTextAsync(string name,IEnumerable<string> args,int timeoutSeconds) {
        var start=new ProcessStartInfo(name){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=start};
        try{if(!process.Start())return(-1,"");}catch{return(-1,"");}
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try {
            var output=await process.StandardOutput.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            return(process.ExitCode,output);
        } catch {
            try{process.Kill(entireProcessTree:true);}catch{}
            return(-1,"");
        }
    }

    static async Task<byte[]?> RunBestEffortBytesAsync(string name,IEnumerable<string> args,int timeoutSeconds) {
        var start=new ProcessStartInfo(name){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=start};
        try{if(!process.Start())return null;}catch{return null;}
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try {
            using var ms=new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms,deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            return process.ExitCode==0&&ms.Length>0?ms.ToArray():null;
        } catch {
            try{process.Kill(entireProcessTree:true);}catch{}
            return null;
        }
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
