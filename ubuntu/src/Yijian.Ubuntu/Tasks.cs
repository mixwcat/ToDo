using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu;

public sealed partial class MainWindow {
    string? draggedId;
    Point dragOrigin;
    bool dragging;
    Border? dropHighlight;
    void BuildTaskList(Grid area) {
        taskList=new ListBox{Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0)};
        ScrollViewer.SetHorizontalScrollBarVisibility(taskList,ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(taskList,ScrollBarVisibility.Auto);
        taskList.ItemTemplate=new FuncDataTemplate<TaskRow>((row,scope)=>row==null?new Border():TaskCard(row));area.Children.Add(taskList);
    }
    Control TaskCard(TaskRow row) {
        var task=row.Task;
        var content=new Grid{ColumnDefinitions=new ColumnDefinitions("23,25,*,24,23"),MinHeight=28};
        var border=new Border{Tag=row,Margin=new Thickness(12+Math.Min(row.Depth,10)*18,3,12,3),Padding=new Thickness(5,state.Compact?5:11),CornerRadius=new CornerRadius(10),BorderBrush=B("Line"),BorderThickness=new Thickness(1),Background=B("Card"),Opacity=task.Done?.60:1,Child=content};
        var drag=Btn("⠿","拖动调整同级未完成任务",()=>{});drag.Cursor=new Cursor(StandardCursorType.SizeAll);drag.Foreground=B("Muted");drag.Padding=new Thickness(2,0);drag.Height=28;
        drag.AddHandler(PointerPressedEvent,(s,e)=>{if(task.Done||!e.GetCurrentPoint(drag).Properties.IsLeftButtonPressed)return;draggedId=task.Id;dragOrigin=e.GetPosition(taskList);dragging=false;e.Pointer.Capture(drag);e.Handled=true;},RoutingStrategies.Tunnel);
        drag.AddHandler(PointerMovedEvent,(s,e)=>DragTask(e),RoutingStrategies.Tunnel);
        drag.AddHandler(PointerReleasedEvent,(s,e)=>DropTask(drag,e),RoutingStrategies.Tunnel);
        drag.PointerCaptureLost+=(s,e)=>{draggedId=null;dragging=false;ClearDropHighlight();};Place(content,drag);
        var check=new CheckBox{IsChecked=task.Done,Height=28,VerticalAlignment=VerticalAlignment.Center};Localize(check,ToolTip.TipProperty,"标记完成");check.Click+=(s,e)=>{store.Complete(task.Id,!task.Done);e.Handled=true;};Place(content,check,0,1);
        var title=new TextBlock{Text=task.Title,Foreground=B("Fg"),TextWrapping=TextWrapping.Wrap,FontSize=13,LineHeight=22,VerticalAlignment=VerticalAlignment.Center,TextDecorations=task.Done?TextDecorations.Strikethrough:null};
        void Toggle(){if(row.HasChildren)store.Collapse(task.Id,!task.Collapsed);}
        var name=Btn("",row.HasChildren?(task.Collapsed?"点击展开子任务":"点击收起子任务"):"右键管理任务",Toggle);name.Content=title;name.MinHeight=28;name.Padding=new Thickness(3,0);name.VerticalAlignment=VerticalAlignment.Center;name.HorizontalContentAlignment=HorizontalAlignment.Stretch;Place(content,name,0,2);
        var arrow=Btn(row.HasChildren?(task.Collapsed?"▸":"▾"):"",row.HasChildren?(task.Collapsed?"点击展开子任务":"点击收起子任务"):"右键管理任务",Toggle);arrow.Padding=new Thickness(2,0);arrow.Height=28;arrow.Foreground=B("Muted");Place(content,arrow,0,3);
        var more=Btn("⋯","任务菜单",()=>{var menu=TaskMenu(task.Id);menu.Open(border);});more.Padding=new Thickness(2,0);more.Height=28;more.Foreground=B("Muted");Place(content,more,0,4);
        border.ContextMenu=TaskMenu(task.Id);return border;
    }
    void RefreshTasks() {
        if(taskList==null)return;
        taskList.ItemsSource=store.Visible(state.ShowCompleted);
        var all=store.Snapshot().Tasks;var done=all.Count(t=>t.Done);count.Text=(all.Count-done).ToString(Strings.Current.Culture);
        progressText.Text=F("已完成 {0} / {1} 件事",done,all.Count);progress.Value=all.Count==0?0:done*100.0/all.Count;
        empty.IsVisible=taskList.ItemCount==0;completedFilter.IsChecked=state.ShowCompleted;
    }
    ContextMenu TaskMenu(string id) {
        var menu=PopupMenu();var task=store.Snapshot().Tasks.First(t=>t.Id==id);
        void Add(string key,string icon,Action action,bool danger=false){var item=new MenuItem{Header=T(key),Icon=new TextBlock{Text=icon,Foreground=B(danger?"Danger":"Muted")}};if(danger)item.Classes.Add("danger");item.Click+=(s,e)=>{action();e.Handled=true;};menu.Items.Add(item);}
        Add("创建子任务","＋",()=>EditTask(null,id));if(!task.Done)Add("标记任务为已完成","✓",()=>store.Complete(id,true));Add("修改任务","✎",()=>EditTask(id,null));
        Add("复制任务文字","⧉",async()=>{if(Clipboard!=null)await Clipboard.SetTextAsync(task.Title);});menu.Items.Add(new Separator());
        Add("删除任务","×",()=>{var before=store.Snapshot();store.Remove(id);ShowUndo("已删除任务",before,store.Snapshot().Revision);},true);return menu;
    }
    void ClearUndo(){undoSnapshot=null;if(undoNotice!=null)undoNotice.IsVisible=false;}
    void ShowUndo(string text,TaskSnapshot before,long revision){undoSnapshot=before;undoRevision=revision;Localize(undoLabel,TextBlock.TextProperty,text);undoNotice.IsVisible=true;}
    void UndoTask(){var before=undoSnapshot;var revision=undoRevision;ClearUndo();if(before!=null&&store.Restore(before,revision))ai.Clear();}

    // Pointer capture keeps drag behavior local to this list, under both X11 and Wayland.
    // Only realized cards are inspected; the virtualized list does not retain off-screen cards.
    Border? DropTarget(Point point) {
        if(draggedId==null||!new Rect(taskList.Bounds.Size).Contains(point))return null;
        var source=store.Snapshot().Tasks.FirstOrDefault(t=>t.Id==draggedId);if(source==null||source.Done)return null;
        return taskList.GetVisualDescendants().OfType<Border>().FirstOrDefault(card=>{
            if(card.Tag is not TaskRow row||row.Task.Id==source.Id||row.Task.Done||row.Task.ParentId!=source.ParentId)return false;
            var origin=card.TranslatePoint(default,taskList);return origin.HasValue&&new Rect(origin.Value,card.Bounds.Size).Contains(point);
        });
    }
    void ClearDropHighlight(){if(dropHighlight!=null){dropHighlight.BorderBrush=B("Line");dropHighlight=null;}}
    void DragTask(PointerEventArgs e) {
        if(draggedId==null||!e.GetCurrentPoint(taskList).Properties.IsLeftButtonPressed)return;
        var point=e.GetPosition(taskList);if(!dragging&&Math.Abs(point.Y-dragOrigin.Y)<6&&Math.Abs(point.X-dragOrigin.X)<6)return;
        dragging=true;ClearDropHighlight();dropHighlight=DropTarget(point);if(dropHighlight!=null)dropHighlight.BorderBrush=B("Accent");
        var scroll=taskList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if(scroll!=null&&(point.Y<28||point.Y>taskList.Bounds.Height-28))scroll.Offset=new Vector(scroll.Offset.X,Math.Clamp(scroll.Offset.Y+(point.Y<28?-16:16),0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height)));
        e.Handled=true;
    }
    void DropTask(Button handle,PointerReleasedEventArgs e) {
        var id=draggedId;var target=dragging?DropTarget(e.GetPosition(taskList)):null;
        var row=target?.Tag as TaskRow;var after=target!=null&&e.GetPosition(target).Y>target.Bounds.Height/2;
        draggedId=null;dragging=false;ClearDropHighlight();e.Pointer.Capture(null);
        if(id!=null&&row!=null)store.Move(id,row.Task.Id,after);e.Handled=true;
    }
}
