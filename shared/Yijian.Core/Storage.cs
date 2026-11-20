using System.Text;
using System.Text.Json;

namespace Yijian.Core;

public sealed class AppState {
    public int Version { get; set; } = 1;
    public List<TodoTask> Tasks { get; set; } = TaskStore.Samples();
    public bool ShowCompleted { get; set; } = true;
    public string Language { get; set; } = "zh-CN";
    public bool Dark { get; set; }
    public bool Compact { get; set; }
    public bool CloseToTray { get; set; }
    public string AiUrl { get; set; } = "https://api.openai.com/v1";
    public string AiModel { get; set; } = "";
    public bool RememberKey { get; set; }
    public int PreviousPoem { get; set; } = -1;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Height { get; set; } = 780;
    public string NoteShortcut { get; set; } = "F2";
}
public sealed class StateStorage(string directory) {
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, WriteIndented=true, NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public string DirectoryPath => directory;
    public string? LoadWarning { get; private set; }
    public AppState Load() {
        try { var legacyDir=Path.Combine(directory,"note-images"); if(Directory.Exists(legacyDir)) Directory.Delete(legacyDir,true); } catch(IOException) {} catch(UnauthorizedAccessException) {}
        var path=Path.Combine(directory,"state.json");
        foreach(var file in new[]{path,path+".bak"}) {
            if(!File.Exists(file)) continue;
            try {
                var raw=File.ReadAllText(file);
                var s=JsonSerializer.Deserialize<AppState>(raw,Json)??throw new IOException("数据为空");
                TaskStore.Validate(s.Tasks);
                if(raw.Contains("\"notes\"",StringComparison.OrdinalIgnoreCase)||raw.Contains("\"noteImageDir\"",StringComparison.OrdinalIgnoreCase)) {
                    try { Save(s); } catch(IOException) {} catch(UnauthorizedAccessException) {}
                }
                return s;
            }
            catch(Exception e) when(e is IOException or JsonException or ArgumentException) {
                LoadWarning="本地数据读取失败，已尝试恢复备份。原文件已保留。";
                try { File.Copy(file,file+".corrupt-"+DateTime.Now.ToString("yyyyMMddHHmmssfff"),false); } catch(IOException) {} catch(UnauthorizedAccessException) {}
            }
        }
        return new();
    }
    public void Save(AppState state) {
        Directory.CreateDirectory(directory); var path=Path.Combine(directory,"state.json"); var temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { JsonSerializer.Serialize(stream,state,Json); stream.Flush(true); }
        if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
    }
}

// Read just the chosen couplet; the small index is shared with the bundled corpus.
public sealed class PoemCatalog {
    readonly string path; readonly uint[] offsets;
    public int Count=>offsets.Length;
    public PoemCatalog(string source,string? indexPath=null) {
        path=source; indexPath??=Path.ChangeExtension(source,"idx"); var length=new FileInfo(source).Length;
        try {
            using var reader=new BinaryReader(File.OpenRead(indexPath));
            if(reader.ReadUInt32()!=0x31504A59 || reader.ReadInt64()!=length) throw new IOException();
            var count=reader.ReadInt32(); if(count<1||count>1000000||reader.BaseStream.Length!=16L+count*4L) throw new IOException();
            offsets=new uint[count]; uint previous=0;
            for(var n=0;n<count;n++) { var v=reader.ReadUInt32(); if(v>=length||(n>0&&v<=previous)) throw new IOException(); offsets[n]=previous=v; }
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException) {
            using var stream=File.OpenRead(source); var list=new List<uint>{0}; long position=0; var buffer=new byte[65536]; int read;
            while((read=stream.Read(buffer))>0) { for(var n=0;n<read;n++) if(buffer[n]==10&&position+n+1<length) list.Add(checked((uint)(position+n+1))); position+=read; }
            offsets=list.ToArray();
        }
    }
    public (int Index,string Text) Pick(int previous) {
        int i=Random.Shared.Next(Count); if(Count>1&&i==previous) i=(i+1+Random.Shared.Next(Count-1))%Count;
        return(i,Read(i));
    }
    public string Read(int i) {
        using var stream=File.OpenRead(path); stream.Seek(offsets[i],SeekOrigin.Begin);
        using var reader=new StreamReader(stream,Encoding.UTF8,false,128); return (reader.ReadLine()??"").Trim('\uFEFF','\r');
    }
}
