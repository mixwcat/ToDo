using static Yijian.Core.Strings;
using System.Text.Json;

namespace Yijian.Core;

public sealed record TodoTask(string Id, string Title, string? ParentId = null, bool Done = false, long CompletedAt = 0, bool Collapsed = false);
public sealed record TaskSnapshot(long Revision, List<TodoTask> Tasks);
public sealed record TaskRow(TodoTask Task, int Depth, bool HasChildren);

// Platform-neutral, atomic task operations. Hosts subscribe to Changed to persist and render.
public sealed class TaskStore
{
    readonly object gate = new();
    List<TodoTask> tasks;
    long revision, lastCompletion;
    public event Action? Changed;
    public TaskStore(IEnumerable<TodoTask>? initial = null)
    {
        tasks = (initial ?? Samples()).ToList(); Validate(tasks);
        lastCompletion = tasks.Select(t => t.CompletedAt).DefaultIfEmpty().Max();
    }
    public static List<TodoTask> Samples() => [
        new("sample-1", "把今天的想法整理下来"), new("sample-2", "完成 Todo 工具的第一个原型"),
        new("sample-3", "梳理主要界面与交互", "sample-2", true), new("sample-4", "试试右键，创建一个子任务", "sample-2"),
        new("sample-5", "读几页一直想看的书"), new("sample-6", "出门走走，给自己充充电", null, true)];
    public TaskSnapshot Snapshot() { lock (gate) return new(revision, [.. tasks]); }
    public static string Title(string text)
    { if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 200) throw new ArgumentException(T("任务名称需为 1–200 个字符。")); return text.Trim(); }
    public static void Validate(List<TodoTask> items)
    {
        if (items.Count > 20000) throw new ArgumentException(T("任务数量超过 20000 条。"));
        var map = new Dictionary<string, TodoTask>();
        foreach (var t in items) { Title(t.Title); if (string.IsNullOrWhiteSpace(t.Id) || !map.TryAdd(t.Id,t)) throw new ArgumentException(T("任务 ID 无效或重复。")); }
        foreach (var t in items) {
            var visited = new HashSet<string> { t.Id }; var pid = t.ParentId;
            while (pid != null) {
                if (!visited.Add(pid) || visited.Count > 64) throw new ArgumentException(T("不能形成循环，任务最多支持 64 层。"));
                if (!map.TryGetValue(pid, out var parent)) throw new ArgumentException(T("父任务不存在。")); pid = parent.ParentId;
            }
        }
    }
    static int Find(List<TodoTask> list, string id) { var n = list.FindIndex(t => t.Id == id); return n >= 0 ? n : throw new ArgumentException(T("任务不存在，请重新读取清单。")); }
    static void Reopen(List<TodoTask> list, string? id) { while (id != null) { var i = Find(list,id); list[i] = list[i] with { Done=false, CompletedAt=0 }; id=list[i].ParentId; } }
    void Change(Action<List<TodoTask>> change) {
        lock(gate) { var next = new List<TodoTask>(tasks); change(next); Validate(next); tasks=next; revision++; }
        if (Changed != null) foreach (Action observer in Changed.GetInvocationList()) { try { observer(); } catch { /* A failed view must not undo a committed operation. */ } }
    }
    public TodoTask Create(string title, string? parentId=null) {
        var t = new TodoTask(Guid.NewGuid().ToString("N"), Title(title), parentId);
        Change(list => { if(parentId!=null) { var i=Find(list,parentId); list[i]=list[i] with { Collapsed=false }; Reopen(list,parentId); } list.Add(t); }); return t;
    }
    public void Update(string id, string? title=null, string? parentId=null, bool changeParent=false) => Change(list => {
        var i=Find(list,id); list[i]=list[i] with { Title=title==null?list[i].Title:Title(title), ParentId=changeParent?parentId:list[i].ParentId };
        Validate(list); if (!list[i].Done) Reopen(list,list[i].ParentId);
    });
    public void Complete(string id, bool done) => Change(list => {
        var i=Find(list,id);
        var timestamp = lastCompletion = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),lastCompletion+1);
        if(done) {
            var selected=new HashSet<string> { id }; bool added;
            do { added=false; foreach(var t in list) if(t.ParentId!=null && selected.Contains(t.ParentId)) added |= selected.Add(t.Id); } while(added);
            for(var n=0;n<list.Count;n++) if(selected.Contains(list[n].Id) && !list[n].Done) list[n]=list[n] with { Done=true, CompletedAt=timestamp };
        } else { list[i]=list[i] with { Done=false, CompletedAt=0 }; Reopen(list,list[i].ParentId); }
    });
    public void Collapse(string id, bool collapsed) => Change(list => { var i=Find(list,id); list[i]=list[i] with { Collapsed=collapsed }; });
    public void Move(string id,string targetId,bool after=false) => Change(list => {
        var from=Find(list,id); var to=Find(list,targetId); var item=list[from];
        if(item.Done || list[to].Done || item.ParentId!=list[to].ParentId) throw new ArgumentException(T("只能调整同一层级的未完成任务。"));
        if(id==targetId) return; list.RemoveAt(from); to=Find(list,targetId); list.Insert(to+(after?1:0),item);
    });
    public void Remove(string id) => Change(list => {
        Find(list,id); var selected=new HashSet<string> {id}; bool added;
        do { added=false; foreach(var t in list) if(t.ParentId!=null && selected.Contains(t.ParentId)) added |= selected.Add(t.Id); } while(added);
        list.RemoveAll(t=>selected.Contains(t.Id));
    });
    public bool Restore(TaskSnapshot snapshot, long expectedRevision) {
        lock(gate) { if(revision!=expectedRevision) return false; Validate(snapshot.Tasks); tasks=[..snapshot.Tasks]; revision++; }
        if(Changed!=null) foreach(Action observer in Changed.GetInvocationList()) {try {observer();} catch {}} return true;
    }
    public List<TaskRow> Visible(bool showCompleted) {
        var all=Snapshot().Tasks; var groups=all.GroupBy(t=>t.ParentId??"").ToDictionary(g=>g.Key,g=>g.OrderBy(t=>t.Done).ThenByDescending(t=>t.Done?t.CompletedAt:0).ToList());
        var rows=new List<TaskRow>();
        void Add(string pid,int depth) { if(!groups.TryGetValue(pid,out var children)) return; foreach(var t in children) { if(t.Done&&!showCompleted) continue; var has=groups.ContainsKey(t.Id); rows.Add(new(t,depth,has)); if(has&&!t.Collapsed) Add(t.Id,depth+1); } }
        Add("",0); return rows;
    }
    public object Execute(string name, JsonElement args) {
        if(args.ValueKind!=JsonValueKind.Object) throw new ArgumentException(T("工具参数必须是对象。"));
        string[] allowed=name switch {"list_tasks"=>[],"create_task"=>["title","parentId"],"update_task"=>["id","title","parentId"],"set_task_completed"=>["id","completed"],"move_task"=>["id","targetId","after"],"delete_task"=>["id"],_=>throw new ArgumentException(T("不支持的任务工具。"))};
        foreach(var p in args.EnumerateObject()) if(!allowed.Contains(p.Name)) throw new ArgumentException(T("未知参数：")+p.Name);
        string Str(string key) => args.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()!:throw new ArgumentException(T("缺少字符串参数：")+key);
        bool Bool(string key) => args.TryGetProperty(key,out var v)&&(v.ValueKind==JsonValueKind.True||v.ValueKind==JsonValueKind.False)?v.GetBoolean():throw new ArgumentException(T("缺少布尔参数：")+key);
        string? Parent() => !args.TryGetProperty("parentId",out var v)||v.ValueKind==JsonValueKind.Null?null:Str("parentId");
        switch(name) {
            case "list_tasks": return Snapshot();
            case "create_task": return Create(Str("title"),Parent());
            case "update_task": Update(Str("id"),args.TryGetProperty("title",out _)?Str("title"):null,Parent(),args.TryGetProperty("parentId",out _)); break;
            case "set_task_completed": Complete(Str("id"),Bool("completed")); break;
            case "move_task": Move(Str("id"),Str("targetId"),args.TryGetProperty("after",out _)?Bool("after"):false); break;
            case "delete_task": Remove(Str("id")); break;
        }
        return new { ok=true, revision=Snapshot().Revision };
    }
}
