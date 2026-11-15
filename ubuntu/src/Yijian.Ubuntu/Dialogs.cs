using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu;

public sealed partial class MainWindow {
    sealed class Overlay {
        readonly MainWindow owner;
        readonly Grid layer;
        readonly Border body;
        readonly IInputElement? previousFocus;
        public Overlay(MainWindow owner,double width=420,double maxHeight=670) {
            this.owner=owner;previousFocus=owner.FocusManager?.GetFocusedElement();
            layer=new Grid{Background=new SolidColorBrush(Color.FromArgb(90,18,28,20)),Focusable=true};
            KeyboardNavigation.SetTabNavigation(layer,KeyboardNavigationMode.Cycle);
            var inner=new Grid{RowDefinitions=new RowDefinitions("32,*")};
            var close=owner.Btn("✕","关闭对话框",Close);close.HorizontalAlignment=HorizontalAlignment.Right;close.Margin=new Thickness(0,2,7,0);Place(inner,close);
            body=new Border();Place(inner,body,1);
            var actualWidth=owner.Bounds.Width>0?owner.Bounds.Width:owner.Width;var actualHeight=owner.Bounds.Height>0?owner.Bounds.Height:owner.Height;
            var frame=new Border{Width=Math.Min(width,actualWidth-28),MaxHeight=Math.Min(maxHeight,actualHeight-34),CornerRadius=new CornerRadius(13),BorderThickness=new Thickness(1),BorderBrush=owner.B("Line"),Background=owner.B("Bg"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=inner};
            layer.Children.Add(frame);layer.KeyDown+=(s,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        }
        public void Show(Control content){body.Child=content;owner.layout.IsHitTestVisible=false;owner.root.Children.Add(layer);layer.Focus();}
        public void Close(){if(!owner.root.Children.Remove(layer))return;owner.layout.IsHitTestVisible=true;previousFocus?.Focus();}
    }
    void Confirm(string title,string text,Action confirmed) {
        var dialog=new Overlay(this,390,350);var panel=new StackPanel{Margin=new Thickness(22),Spacing=16};panel.Children.Add(Label(title,17));panel.Children.Add(Label(text));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(Btn("取消","取消",dialog.Close));actions.Children.Add(Btn("确定","确定",()=>{dialog.Close();confirmed();},true));panel.Children.Add(actions);dialog.Show(panel);
    }
    void EditTask(string? id,string? parent) {
        var current=id==null?null:store.Snapshot().Tasks.FirstOrDefault(t=>t.Id==id);var dialog=new Overlay(this,360,320);
        var panel=new StackPanel{Margin=new Thickness(22)};panel.Children.Add(Label(id!=null?"修改任务":parent!=null?"创建子任务":"添加任务",17));
        var input=new TextBox{Text=current?.Title??"",MaxLength=200,Margin=new Thickness(0,18,0,10)};panel.Children.Add(input);var error=Label("",11,"Danger");panel.Children.Add(error);
        void SaveTask(){if(string.IsNullOrWhiteSpace(input.Text))return;try{if(id==null)store.Create(input.Text,parent);else store.Update(id,input.Text);dialog.Close();}catch(ArgumentException ex){error.Text=ex.Message;}}
        input.KeyDown+=(s,e)=>{if(e.Key==Key.Enter){SaveTask();e.Handled=true;}};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};actions.Children.Add(Btn("取消","取消",dialog.Close));actions.Children.Add(Btn("保存","保存任务",SaveTask,true));panel.Children.Add(actions);dialog.Show(panel);Dispatcher.UIThread.Post(()=>{input.Focus();input.SelectAll();});
    }
    void Settings() {
        var dialog=new Overlay(this,420,650);var panel=new StackPanel{Margin=new Thickness(25,12,25,20)};panel.Children.Add(Label("偏好设置",19));
        var caption=Label("界面语言 / Language",11,"Muted");caption.Margin=new Thickness(0,18,0,8);panel.Children.Add(caption);
        var languages=new StackPanel{Orientation=Orientation.Horizontal,Spacing=24,Margin=new Thickness(0,0,0,18)};
        var chinese=new RadioButton{Content="简体中文",GroupName="Language",IsChecked=state.Language=="zh-CN"};var english=new RadioButton{Content="English",GroupName="Language",IsChecked=state.Language=="en-US"};languages.Children.Add(chinese);languages.Children.Add(english);panel.Children.Add(languages);
        var compact=new CheckBox{Content=T("紧凑布局"),IsChecked=state.Compact,Margin=new Thickness(0,0,0,12)};var dark=new CheckBox{Content=T("深色主题"),IsChecked=state.Dark,Margin=new Thickness(0,0,0,22)};panel.Children.Add(compact);panel.Children.Add(dark);
        panel.Children.Add(Label("点击右上角 × 时",12,"Muted"));var exit=new RadioButton{Content=T("退出程序"),GroupName="CloseAction",IsChecked=!state.CloseToTray,Margin=new Thickness(0,10,0,9)};var toTray=new RadioButton{Content=T("最小化到托盘"),GroupName="CloseAction",IsChecked=state.CloseToTray,Margin=new Thickness(0,0,0,16)};panel.Children.Add(exit);panel.Children.Add(toTray);
        var configure=Btn("AI 配置","AI 配置",()=>{dialog.Close();_=AiSettingsAsync();},true);configure.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(configure);
        var files=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,16,0,12)};files.Children.Add(Btn("导出任务","导出任务",()=>_ = ExportAsync()));files.Children.Add(Btn("导入任务","导入任务",()=>{dialog.Close();_=ImportAsync();}));panel.Children.Add(files);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(Btn("恢复默认设置","恢复默认设置",()=>{chinese.IsChecked=true;dark.IsChecked=false;compact.IsChecked=false;exit.IsChecked=true;}));
        actions.Children.Add(Btn("保存","保存设置",()=>{state.Dark=dark.IsChecked==true;state.Compact=compact.IsChecked==true;state.CloseToTray=toTray.IsChecked==true;ApplyLanguage(english.IsChecked==true?"en-US":"zh-CN");ApplyTheme();QueueSave();dialog.Close();},true));panel.Children.Add(actions);dialog.Show(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    async Task AiSettingsAsync() {
        string? loadError=null;try{await EnsureKeyAsync();}catch(IOException ex){loadError=ex.Message;}
        var dialog=new Overlay(this,420,690);var panel=new StackPanel{Margin=new Thickness(25,12,25,20)};panel.Children.Add(Label("AI 配置",19));
        TextBox Field(string caption,string value){var label=Label(caption,11,"Muted");label.Margin=new Thickness(0,13,0,6);panel.Children.Add(label);var input=new TextBox{Text=value};panel.Children.Add(input);return input;}
        var url=Field("接口 URL",state.AiUrl);var model=Field("模型名称",state.AiModel);var secret=Field("API Key（本地模型可留空）",key);secret.PasswordChar='●';
        var show=new CheckBox{Content=T("显示 Key"),Margin=new Thickness(0,10,0,10)};show.Click+=(s,e)=>secret.PasswordChar=show.IsChecked==true?'\0':'●';panel.Children.Add(show);
        var remember=new CheckBox{Content=new TextBlock{Text=T("在此设备记住 Key（系统密钥环）"),TextWrapping=TextWrapping.Wrap},IsChecked=state.RememberKey,Margin=new Thickness(0,0,0,12)};panel.Children.Add(remember);
        panel.Children.Add(Label("默认只在本次运行中使用 Key。对话时仅发送当前任务及对话内容；诗库保留在本地。",10,"Muted"));
        var error=Label(loadError??"",11,"Danger",false);error.Margin=new Thickness(0,12,0,0);panel.Children.Add(error);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};actions.Children.Add(Btn("恢复默认连接","恢复默认连接",()=>{url.Text="https://api.openai.com/v1";model.Text="";secret.Text="";remember.IsChecked=false;}));actions.Children.Add(Btn("取消","取消",dialog.Close));
        var save=Btn("保存","保存设置",()=>{},true);save.Click+=async(s,e)=>{
            save.IsEnabled=false;
            try{AiClient.Endpoint(url.Text??"");var nextKey=secret.Text??"";if(nextKey.Contains('\r')||nextKey.Contains('\n'))throw new ArgumentException(T("Key 中不能含有换行。"));
                await secrets.SaveAsync(nextKey,remember.IsChecked==true,state.RememberKey);key=nextKey;keyLoaded=true;state.RememberKey=remember.IsChecked==true;state.AiUrl=url.Text!.Trim();state.AiModel=model.Text?.Trim()??"";
                if(Save())dialog.Close();else error.Text=T("AI 配置未保存");
            }catch(Exception ex)when(ex is ArgumentException or IOException or UnauthorizedAccessException){error.Text=ex.Message;}finally{save.IsEnabled=true;}
        };actions.Children.Add(save);panel.Children.Add(actions);dialog.Show(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    static FilePickerFileType JsonFileType()=>new(T("JSON 任务文件")){Patterns=["*.json"],MimeTypes=["application/json"]};
    async Task ExportAsync() {
        try{var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=T("导出任务"),SuggestedFileName="YijianTodo-"+DateTime.Now.ToString("yyyyMMdd")+".json",DefaultExtension="json",FileTypeChoices=[JsonFileType()]});if(file==null)return;
            await using var stream=await file.OpenWriteAsync();stream.SetLength(0);await JsonSerializer.SerializeAsync(stream,store.Snapshot(),StateStorage.Json);
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ReportError(ex.Message);}
    }
    async Task ImportAsync() {
        try{var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=T("导入任务"),AllowMultiple=false,FileTypeFilter=[JsonFileType()]});if(files.Count==0)return;
            await using var stream=await files[0].OpenReadAsync();using var buffer=new MemoryStream();var chunk=new byte[8192];int read;
            while((read=await stream.ReadAsync(chunk))>0){if(buffer.Length+read>8*1024*1024)throw new IOException(T("文件超过 8 MB。"));await buffer.WriteAsync(chunk.AsMemory(0,read));}
            using var json=JsonDocument.Parse(buffer.ToArray());var element=json.RootElement.ValueKind==JsonValueKind.Array?json.RootElement:json.RootElement.GetProperty("tasks");var tasks=JsonSerializer.Deserialize<List<TodoTask>>(element,StateStorage.Json)??throw new IOException(T("没有任务数据。"));TaskStore.Validate(tasks);
            Confirm("导入任务","用导入文件替换当前任务？",()=>{var before=store.Snapshot();if(store.Restore(new(0,tasks),before.Revision)){ai.Clear();ShowUndo("已导入任务",before,store.Snapshot().Revision);}});
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException){ReportError(T("导入失败")+": "+ex.Message);}
    }
}
