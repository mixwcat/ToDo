using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yijian.Core;
using Yijian.Ubuntu.Platform;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu;

public sealed partial class MainWindow : Window {
    readonly StateStorage storage;
    readonly AppState state;
    readonly TaskStore store;
    readonly SecretStore secrets;
    readonly AiClient ai=new();
    readonly Dictionary<string,SolidColorBrush> palette=[];
    readonly List<NoteWindow> openNotes=[];
    readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(300)};
    readonly Action changedHandler;
    readonly Task<string> initialKey;
    bool keyLoaded,aiOpen,busy,exitRequested,forceExit,closingToTray,capturingShortcut;
    string key="";
    DateTime lastNoteOpened=DateTime.MinValue;
    CancellationTokenSource? aiCancellation;
    TrayController? tray;
    GlobalHotkey? globalHotkey;
    Grid root=null!,layout=null!,chat=null!;
    ColumnDefinition chatColumn=null!;
    ListBox taskList=null!;
    CheckBox completedFilter=null!;
    SelectableTextBlock date=null!,poem=null!,count=null!,progressText=null!,status=null!,empty=null!;
    ProgressBar progress=null!;
    Button taskUndoButton=null!;
    StackPanel messages=null!;
    StackPanel? welcome;
    ScrollViewer chatScroll=null!;
    TextBox composer=null!;
    Button send=null!,newChat=null!;

    internal IReadOnlyDictionary<string,SolidColorBrush> Palette => palette;

    public MainWindow(StateStorage storage) {
        this.storage=storage;state=storage.Load();store=new(state.Tasks);secrets=new(storage.DirectoryPath);
        state.Language=state.Language=="en-US"?"en-US":"zh-CN";Strings.Current.SetLanguage(state.Language);
        if(string.IsNullOrWhiteSpace(state.NoteShortcut))state.NoteShortcut="F2";
        initialKey=state.RememberKey?secrets.ReadAsync():Task.FromResult("");
        Width=420;Height=double.IsFinite(state.Height)?Math.Clamp(state.Height,560,1400):780;MinWidth=380;MinHeight=560;
        SystemDecorations=SystemDecorations.None;CanResize=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Icon=new WindowIcon(Path.Combine(AppContext.BaseDirectory,"Assets","app.ico"));
        Localize(this,TitleProperty,"一件 Todo");ApplyTheme();Build();RefreshTasks();
        AddHandler(KeyDownEvent,OnMainWindowKeyDown,RoutingStrategies.Tunnel);
        saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};
        changedHandler=()=>{if(Dispatcher.UIThread.CheckAccess())Changed();else Dispatcher.UIThread.Post(Changed);};store.Changed+=changedHandler;
        Opened+=async(s,e)=>{
            var work=Screens.ScreenFromWindow(this)?.WorkingArea;
            if(work.HasValue){Height=Math.Min(Height,work.Value.Height/RenderScaling);if(double.IsFinite(state.Left)&&double.IsFinite(state.Top))Position=new PixelPoint((int)Math.Clamp(state.Left,work.Value.X,Math.Max(work.Value.X,work.Value.Right-Width*RenderScaling)),(int)Math.Clamp(state.Top,work.Value.Y,Math.Max(work.Value.Y,work.Value.Bottom-Height*RenderScaling)));}
            tray=new TrayController(this,Quit);globalHotkey=new GlobalHotkey(state.NoteShortcut,OpenNewNote);if(storage.LoadWarning!=null)Localize(status,TextBlock.TextProperty,storage.LoadWarning);
            try{await EnsureKeyAsync();}catch(IOException ex){ReportError(ex.Message);}
        };
        Closing+=OnClosing;
        Closed+=(s,e)=>{saveTimer.Stop();store.Changed-=changedHandler;aiCancellation?.Cancel();globalHotkey?.Dispose();foreach(var n in openNotes.ToList())n.Close();tray?.Dispose();};
    }
    internal IBrush B(string name)=>palette[name];
    void ApplyTheme() {
        RequestedThemeVariant=state.Dark?ThemeVariant.Dark:ThemeVariant.Light;
        var colors=state.Dark
            ?new[]{"#1e2421","#29322c","#f0f3ef","#b3beb5","#3a463e","#a1c6a7","#303e34","#d99b8f"}
            :new[]{"#fbfcf9","#ffffff","#28382d","#8a978d","#e5ebe3","#4f765c","#edf3e9","#ad645a"};
        var names=new[]{"Bg","Card","Fg","Muted","Line","Accent","Soft","Danger"};
        for(var i=0;i<names.Length;i++){if(palette.TryGetValue(names[i],out var brush))brush.Color=Color.Parse(colors[i]);else{brush=new(Color.Parse(colors[i]));palette.Add(names[i],brush);Resources[names[i]]=brush;}}
        Background=B("Bg");Foreground=B("Fg");
        foreach(var noteWin in openNotes)noteWin.ApplyTheme();
    }
    internal static void Localize(AvaloniaObject target,AvaloniaProperty property,string source) {
        if(Strings.Contains(source))target.Bind(property,new Binding(nameof(Strings.Revision)){Source=Strings.Current,Mode=BindingMode.OneWay,Converter=new FuncValueConverter<int,string>(_=>T(source))});
        else target.SetValue(property,source);
    }
    internal SelectableTextBlock Label(string text,double size=13,string color="Fg",bool translate=true) {
        var label=new SelectableTextBlock{FontSize=size,Foreground=B(color),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
        if(translate)Localize(label,TextBlock.TextProperty,text);else label.Text=text;
        label.ContextMenu=new ContextMenu();label.ContextRequested+=(s,e)=>{
            var menu=PopupMenu();var copy=new MenuItem{Header=T("复制"),IsEnabled=!string.IsNullOrEmpty(label.SelectedText)};copy.Click+=(s,e)=>label.Copy();menu.Items.Add(copy);
            var all=new MenuItem{Header=T("全选")};all.Click+=(s,e)=>label.SelectAll();menu.Items.Add(all);
            var copyAll=new MenuItem{Header=T("复制全部文字")};copyAll.Click+=async(s,e)=>{if(Clipboard!=null)await Clipboard.SetTextAsync(label.Text??"");};menu.Items.Add(copyAll);label.ContextMenu=menu;
        };return label;
    }
    internal Button Btn(string text,string hint,Action action,bool soft=false) {
        var button=new Button();Localize(button,ContentControl.ContentProperty,text);Localize(button,ToolTip.TipProperty,hint);Localize(button,AutomationProperties.NameProperty,hint);
        button.Click+=(s,e)=>action();if(soft)button.Classes.Add("soft");return button;
    }
    internal ContextMenu PopupMenu(){var menu=new ContextMenu();foreach(var (name,brush) in palette)menu.Resources[name]=brush;return menu;}
    internal static void Place(Grid parent,Control child,int row=0,int column=0){Grid.SetRow(child,row);Grid.SetColumn(child,column);parent.Children.Add(child);}
    void Build() {
        // A transparent background makes empty space participate in pointer hit testing.
        root=new Grid{Background=Brushes.Transparent};Content=root;
        layout=new Grid{ColumnDefinitions=new ColumnDefinitions("*,1,0")};layout.ColumnDefinitions[0].MinWidth=370;chatColumn=layout.ColumnDefinitions[2];root.Children.Add(layout);
        root.PointerPressed+=(s,e)=>{
            if(root.Children.Count>1||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
            var visual=e.Source as Visual;
            if(visual!=null&&visual.GetSelfAndVisualAncestors().Any(x=>x is Button or ToggleButton or TextBox or SelectableTextBlock or ScrollBar or ListBoxItem))return;
            BeginMoveDrag(e);e.Handled=true;
        };
        var main=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto")};Place(layout,main);
        var header=new Grid{Background=Brushes.Transparent,ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(22,17,12,0),Height=38};Place(main,header);
        // The title is a drag handle; keep selectable text in the content area only.
        var brandTitle=new TextBlock{FontSize=17,Foreground=B("Fg"),VerticalAlignment=VerticalAlignment.Center};Localize(brandTitle,TextBlock.TextProperty,"一件");
        var brandTag=new TextBlock{FontSize=10,Foreground=B("Muted"),VerticalAlignment=VerticalAlignment.Center};Localize(brandTag,TextBlock.TextProperty,"TODO");
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};brand.Children.Add(new Border{Background=B("Accent"),CornerRadius=new CornerRadius(8),Width=30,Height=30,Child=new TextBlock{Text="✓",Foreground=B("Bg"),FontSize=22,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});brand.Children.Add(brandTitle);brand.Children.Add(brandTag);Place(header,brand);
        var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(Btn("✍","新建便签",OpenNewNote));controls.Children.Add(Btn("⚙","设置",Settings));controls.Children.Add(Btn("—","最小化",()=>WindowState=WindowState.Minimized));controls.Children.Add(Btn("✕","关闭",Close));Place(header,controls,0,1);
        var hero=new StackPanel{Margin=new Thickness(28,31,25,24)};Place(main,hero,1);date=Label("",11,"Muted");UpdateDate();hero.Children.Add(date);
        poem=Label("行到水穷处，\n坐看云起时。",23,translate:false);poem.FontFamily=new FontFamily("Noto Serif CJK SC,serif");poem.LineHeight=35;poem.Margin=new Thickness(0,13,0,13);hero.Children.Add(poem);
        try{var picked=new PoemCatalog(Path.Combine(AppContext.BaseDirectory,"Assets","poem-lines.txt")).Pick(state.PreviousPoem);state.PreviousPoem=picked.Index;poem.Text=picked.Text.Replace("，","，\n");}catch(IOException){}
        var companion=Btn("✧  与 AI 对话","与 AI 对话",()=>ToggleAi(!aiOpen),true);companion.HorizontalAlignment=HorizontalAlignment.Left;companion.Padding=new Thickness(12,8);hero.Children.Add(companion);
        var summary=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,Auto"),ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(28,0,27,15)};Place(main,summary,2);
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=9};heading.Children.Add(Label("我的任务",16));count=Label("",11,"Muted");heading.Children.Add(count);Place(summary,heading);
        taskUndoButton=Btn("↶","撤销重命名或删除",UndoTask);taskUndoButton.Width=28;taskUndoButton.Height=28;taskUndoButton.FontSize=20;taskUndoButton.Padding=new Thickness(0);taskUndoButton.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(taskUndoButton);UpdateTaskUndo();
        completedFilter=new CheckBox{IsChecked=state.ShowCompleted,VerticalAlignment=VerticalAlignment.Center};completedFilter.Classes.Add("pill");Localize(completedFilter,ContentControl.ContentProperty,"显示已完成");completedFilter.Click+=(s,e)=>{state.ShowCompleted=completedFilter.IsChecked==true;RefreshTasks();QueueSave();};Place(summary,completedFilter,0,1);
        progressText=Label("",11,"Muted");progressText.Margin=new Thickness(0,13,0,7);Grid.SetColumnSpan(progressText,2);Place(summary,progressText,1);
        progress=new ProgressBar{Height=3,Minimum=0,Maximum=100,Foreground=B("Accent"),Background=B("Line"),ShowProgressText=false};Grid.SetColumnSpan(progress,2);Place(summary,progress,2);
        var listArea=new Grid();Place(main,listArea,3);BuildTaskList(listArea);
        empty=Label("给今天留一点方向\n点击下方 ＋，添加一件事",13,"Muted");empty.LineHeight=27;empty.TextAlignment=TextAlignment.Center;empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;listArea.Children.Add(empty);
        var add=Btn("＋   添加任务","添加任务",()=>EditTask(null,null),true);add.Margin=new Thickness(27,17,27,9);add.Height=44;add.FontSize=14;add.HorizontalAlignment=HorizontalAlignment.Stretch;Place(main,add,4);
        var footer=new StackPanel();Place(main,footer,5);
        status=Label("所有改变，都会被好好记下",10,"Muted");status.TextAlignment=TextAlignment.Center;status.Margin=new Thickness(12,5,12,16);footer.Children.Add(status);
        Place(layout,new Border{Background=B("Line")},0,1);BuildChat();
    }
    void UpdateDate()=>date.Text=DateTime.Now.ToString(state.Language=="en-US"?"MMMM d  dddd":"M月d日  dddd",Strings.Current.Culture);
    void ApplyLanguage(string language){state.Language=language;Strings.Current.SetLanguage(language);UpdateDate();RefreshTasks();tray?.UpdateLanguage();}
    void Changed(){UpdateTaskUndo();RefreshTasks();QueueSave();}
    void QueueSave(){Localize(status,TextBlock.TextProperty,"正在保存…");saveTimer.Stop();saveTimer.Start();}
    bool Save() {
        try{state.Tasks=store.Snapshot().Tasks;if(WindowState==WindowState.Normal){state.Height=Height;state.Left=Position.X;state.Top=Position.Y;}storage.Save(state);Localize(status,TextBlock.TextProperty,"已保存在此设备");return true;}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){status.Text=T("保存失败：")+ex.Message;return false;}
    }
    public void ReportError(string message)=>status.Text=message;
    public void RestoreWindow(){ShowInTaskbar=true;Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    void Quit(){exitRequested=true;Close();}
    async void OnClosing(object? sender,WindowClosingEventArgs e) {
        saveTimer.Stop();
        if(e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)exitRequested=true;
        if(!Save()&&!forceExit){e.Cancel=true;Confirm("保存失败","任务未能保存。仍然退出？",()=>{forceExit=true;Quit();});return;}
        if(state.CloseToTray&&!exitRequested){e.Cancel=true;if(closingToTray)return;closingToTray=true;try{var hasHost=tray!=null&&await DesktopTools.HasTrayHostAsync();if(exitRequested)return;if(hasHost){ShowInTaskbar=false;Hide();}else{WindowState=WindowState.Minimized;Localize(status,TextBlock.TextProperty,"托盘不可用，窗口已最小化到任务栏。");}}finally{closingToTray=false;}return;}
        aiCancellation?.Cancel();
    }
    async Task EnsureKeyAsync(){if(keyLoaded)return;try{key=await initialKey;}finally{keyLoaded=true;}}

    void OnMainWindowKeyDown(object? sender,KeyEventArgs e) {
        if(capturingShortcut||e.Handled)return;
        if(MatchesNoteShortcut(e)){e.Handled=true;OpenNewNote();}
    }
    internal bool MatchesNoteShortcut(KeyEventArgs e) {
        if(capturingShortcut)return false;
        var shortcut=string.IsNullOrWhiteSpace(state.NoteShortcut)?"F2":state.NoteShortcut.Trim();
        try {
            var gesture=KeyGesture.Parse(shortcut);
            return gesture.Matches(e);
        } catch(Exception) {
            return e.Key==Key.F2&&e.KeyModifiers==KeyModifiers.None;
        }
    }
    internal static string? FormatShortcutFromKeyEvent(KeyEventArgs e) {
        if(e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
            return null;
        var parts=new List<string>();
        if(e.KeyModifiers.HasFlag(KeyModifiers.Control))parts.Add("Ctrl");
        if(e.KeyModifiers.HasFlag(KeyModifiers.Alt))parts.Add("Alt");
        if(e.KeyModifiers.HasFlag(KeyModifiers.Shift))parts.Add("Shift");
        if(e.KeyModifiers.HasFlag(KeyModifiers.Meta))parts.Add("Meta");
        parts.Add(e.Key.ToString());
        return string.Join("+",parts);
    }
    internal void UpdateGlobalHotkey()=>globalHotkey?.Update(state.NoteShortcut);
    internal void OpenNewNote() {
        if(capturingShortcut)return;
        var now=DateTime.UtcNow;
        if((now-lastNoteOpened).TotalMilliseconds<220)return;
        lastNoteOpened=now;
        double left=double.NaN,top=double.NaN;
        var offset=(openNotes.Count%6)*26;
        if(WindowState==WindowState.Normal){
            left=Position.X+60+offset;
            top=Position.Y+60+offset;
        }
        var win=new NoteWindow(this,left,top);
        openNotes.Add(win);
        win.Show();
        win.Activate();
    }
    internal void OnNoteWindowClosed(NoteWindow win) {
        openNotes.Remove(win);
    }
}
