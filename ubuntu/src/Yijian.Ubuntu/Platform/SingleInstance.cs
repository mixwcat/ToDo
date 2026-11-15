using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Yijian.Ubuntu.Platform;

// Named pipes are local Unix sockets on Linux. No polling or open TCP port.
internal sealed class SingleInstance : IDisposable {
    readonly Mutex mutex;
    readonly string pipeName;
    readonly CancellationTokenSource stop=new();
    readonly Task? listener;
    public bool IsFirst {get;}
    public event Action? Activated;
    public SingleInstance(string directory) {
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName+Path.GetFullPath(directory))))[..24];
        pipeName="yijian-todo-"+hash;
        mutex=new Mutex(true,pipeName,out var created);IsFirst=created;
        if(created)listener=ListenAsync();
    }
    async Task ListenAsync() {
        while(!stop.IsCancellationRequested) {
            try {
                using var server=new NamedPipeServerStream(pipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                var buffer=new byte[1];if(await server.ReadAsync(buffer,stop.Token)>0&&buffer[0]==1)Activated?.Invoke();
            }catch(OperationCanceledException){break;}catch(IOException){break;}
        }
    }
    public void ActivateExisting() {
        try{using var client=new NamedPipeClientStream(".",pipeName,PipeDirection.Out,PipeOptions.CurrentUserOnly);client.Connect(1500);client.WriteByte(1);}catch(IOException){}catch(TimeoutException){}
    }
    public void Dispose(){stop.Cancel();mutex.Dispose();/* Pending pipe I/O observes cancellation; no thread is blocked. */}
}
