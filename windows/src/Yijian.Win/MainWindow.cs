using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Win;

public sealed class TaskView(TaskRow row,bool compact) {
    public string Id=>row.Task.Id;
    public string Title=>row.Task.Title;
    public bool Done=>row.Task.Done;
    public bool HasChildren=>row.HasChildren;
    public string Arrow=>row.HasChildren?(row.Task.Collapsed?"▸":"▾"):"";
    public string ToggleTag=>"toggle:"+Id;
    public string CheckTag=>"check:"+Id;
    public string MenuTag=>"menu:"+Id;
    public string DragTag=>"drag:"+Id;
    public Thickness Indent=>new(12+Math.Min(row.Depth,10)*18,3,12,3);
    public Thickness Padding=>new(5,compact?5:11,5,compact?5:11);
    public double Opacity=>Done?.60:1;
    public TextDecorationCollection? Decorations=>Done?TextDecorations.Strikethrough:null;
    public string Hint=>T(row.HasChildren?(row.Task.Collapsed?"点击展开子任务":"点击收起子任务"):"右键管理任务");
    public string DragHint=>T("拖动调整同级未完成任务");
    public string CheckHint=>T("标记完成");
    public string MenuHint=>T("任务菜单");
}

public sealed partial class MainWindow : Window {
    readonly StateStorage storage; readonly AppState state; readonly TaskStore store;
    readonly AiClient ai=new();
    readonly Dictionary<string,ThemeColor> palette=[];
    sealed class ThemeColor(Color value) : INotifyPropertyChanged { public event PropertyChangedEventHandler? PropertyChanged; Color color=value; public Color Value {get=>color;set{color=value;PropertyChanged?.Invoke(this,new(nameof(Value)));}} }
    readonly DispatcherTimer saveTimer;
    Grid root=null!,layout=null!,chat=null!; ColumnDefinition chatColumn=null!;
    ListBox taskList=null!; SelectableText poem=null!,date=null!,count=null!,progressLabel=null!,status=null!,empty=null!;
    ProgressBar progress=null!; CheckBox showCompleted=null!;
    Border taskUndoNotice=null!; SelectableText taskUndoLabel=null!; TaskSnapshot? taskUndoSnapshot; long taskUndoRevision;
    ScrollViewer chatScroll=null!; StackPanel messages=null!; StackPanel? welcome; TextBox composer=null!; Button send=null!,newChat=null!;
    CancellationTokenSource? aiCancellation; string key=""; bool aiOpen,busy,initializing=true;
    Point dragStart; string? dragId;
    TrayIcon? tray; bool exitRequested;
    static readonly Brush Transparent=Brushes.Transparent;

    public MainWindow(StateStorage storage) {
        this.storage=storage;state=storage.Load();state.Language=state.Language=="en-US"?"en-US":"zh-CN";Strings.Current.SetLanguage(state.Language);store=new TaskStore(state.Tasks);key=state.RememberKey?SecretStore.Read(storage.DirectoryPath):"";
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","app.ico")));Localize(this,TitleProperty,"一件 Todo");Width=420;Height=Math.Clamp(state.Height,560,SystemParameters.WorkArea.Height);MinWidth=380;MinHeight=560;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;UseLayoutRounding=true;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),CornerRadius=new CornerRadius(12),GlassFrameThickness=new Thickness(0)});
        if(double.IsFinite(state.Left)&&double.IsFinite(state.Top)) {Left=Math.Clamp(state.Left,SystemParameters.VirtualScreenLeft,Math.Max(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width));Top=Math.Clamp(state.Top,SystemParameters.VirtualScreenTop,Math.Max(SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Height));WindowStartupLocation=WindowStartupLocation.Manual;}
        else WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Theme();Build();
        saveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};
        store.Changed+=()=>{ if(Dispatcher.CheckAccess()) Changed();else Dispatcher.Invoke(Changed); };
        SourceInitialized+=(s,e)=>{Native.Round(this);tray=new TrayIcon(this,RestoreFromTray,TrayMenu);};
        Closed+=(s,e)=>tray?.Dispose();
        Application.Current.SessionEnding+=(s,e)=>exitRequested=true;
        Loaded+=(s,e)=>{initializing=false;Refresh();QueueSave();if(storage.LoadWarning!=null)Localize(status,TextBox.TextProperty,storage.LoadWarning);};
        Closing+=OnClosing;
    }
    Brush B(string name)=>(Brush)Resources[name];
    void Theme() {
        if(Resources.MergedDictionaries.Count==0) Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/一件Todo;component/ControlStyles.xaml",UriKind.Absolute)});
        string bg=state.Dark?"#1e2421":"#fbfcf9",card=state.Dark?"#29322c":"#ffffff",fg=state.Dark?"#f0f3ef":"#28382d",muted=state.Dark?"#b3beb5":"#8a978d",line=state.Dark?"#3a463e":"#e5ebe3",accent=state.Dark?"#a1c6a7":"#4f765c",soft=state.Dark?"#303e34":"#edf3e9";
        foreach(var (name,color) in new[]{("Bg",bg),("Card",card),("Fg",fg),("Muted",muted),("Line",line),("Accent",accent),("Soft",soft),("Danger",state.Dark?"#d99b8f":"#ad645a")}) { var parsed=(Color)ColorConverter.ConvertFromString(color); if(palette.TryGetValue(name,out var existing)) existing.Value=parsed; else {var entry=new ThemeColor(parsed);palette[name]=entry;var brush=new SolidColorBrush();BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding("Value"){Source=entry});Resources[name]=brush;} }
        SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"Fg");
        var button=new Style(typeof(Button));button.Setters.Add(new Setter(Control.BackgroundProperty,Transparent));button.Setters.Add(new Setter(Control.ForegroundProperty,B("Fg")));button.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));button.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,6,8,6)));button.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));button.Setters.Add(new Setter(Control.FontFamilyProperty,FontFamily));
        button.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border x:Name='b' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='.68'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")));
        Resources[typeof(Button)]=button;
        var textbox=new Style(typeof(TextBox));textbox.Setters.Add(new Setter(Control.BackgroundProperty,B("Card")));textbox.Setters.Add(new Setter(Control.ForegroundProperty,B("Fg")));textbox.Setters.Add(new Setter(Control.BorderBrushProperty,B("Line")));textbox.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10)));textbox.Setters.Add(new Setter(TextBox.CaretBrushProperty,B("Accent")));Resources[typeof(TextBox)]=textbox;
        if(root!=null) { root.Background=B("Bg"); Refresh(); }
    }
    SelectableText Text(string value,double size=13,string color="Fg") {var t=new SelectableText{FontSize=size,VerticalAlignment=VerticalAlignment.Center};Localize(t,TextBox.TextProperty,value);t.SetResourceReference(Control.ForegroundProperty,color);return t;}
    Button Btn(string text,string tooltip,Action action) {var b=new Button{HorizontalContentAlignment=HorizontalAlignment.Center};Localize(b,ContentControl.ContentProperty,text);Localize(b,ToolTipProperty,tooltip);Automation(b,tooltip);b.Click+=(s,e)=>action();return b;}
    static void Automation(DependencyObject obj,string name)=>Localize(obj,System.Windows.Automation.AutomationProperties.NameProperty,name);
    void Build() {
        root=new Grid{Background=B("Bg")};root.PreviewMouseLeftButtonDown+=DragWindow;root.RowDefinitions.Add(new RowDefinition());Content=root;
        layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=370});layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1)});chatColumn=new ColumnDefinition{Width=new GridLength(0)};layout.ColumnDefinitions.Add(chatColumn);root.Children.Add(layout);
        var main=new Grid();foreach(var h in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})main.RowDefinitions.Add(new RowDefinition{Height=h});layout.Children.Add(main);
        var top=new Grid{Margin=new Thickness(22,17,12,0),Height=38};top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});main.Children.Add(top);
        var brand=new StackPanel{Orientation=Orientation.Horizontal};var mark=new Border{Background=B("Accent"),CornerRadius=new CornerRadius(8),Width=30,Height=30,Child=new TextBlock{Text="✓",Foreground=B("Bg"),FontSize=22,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};brand.Children.Add(mark);brand.Children.Add(Text("一件",17));((FrameworkElement)brand.Children[1]).Margin=new Thickness(10,0,9,0);brand.Children.Add(Text("TODO",10,"Muted"));top.Children.Add(brand);
        var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(Btn("⚙","设置",Settings));controls.Children.Add(Btn("—","最小化",()=>WindowState=WindowState.Minimized));controls.Children.Add(Btn("✕","关闭",Close));Grid.SetColumn(controls,1);top.Children.Add(controls);
        var hero=new StackPanel{Margin=new Thickness(28,31,25,24)};Grid.SetRow(hero,1);main.Children.Add(hero);date=Text(DateTime.Now.ToString(state.Language=="en-US"?"MMMM d  dddd":"M月d日  dddd",Strings.Current.Culture),11,"Muted");hero.Children.Add(date);
        poem=Text("山水有清音，\n人间有闲情。",23);poem.FontFamily=new FontFamily("楷体");poem.LineHeight=35;poem.Margin=new Thickness(0,13,0,13);hero.Children.Add(poem);
        try{var catalog=new PoemCatalog(Path.Combine(AppContext.BaseDirectory,"Assets","poem-lines.txt"));var picked=catalog.Pick(state.PreviousPoem);state.PreviousPoem=picked.Index;poem.Text=picked.Text.Replace("，","，\n");}catch(IOException){poem.Text="行到水穷处，\n坐看云起时。";}
        var aiButton=Btn("✧  与 AI 对话","与 AI 对话",()=>ToggleAi(!aiOpen));aiButton.HorizontalAlignment=HorizontalAlignment.Left;aiButton.Background=B("Soft");aiButton.Foreground=B("Accent");aiButton.Padding=new Thickness(12,8,12,8);hero.Children.Add(aiButton);
        var summary=new Grid{Margin=new Thickness(28,0,27,15)};summary.RowDefinitions.Add(new RowDefinition());summary.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});summary.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});summary.ColumnDefinitions.Add(new ColumnDefinition());summary.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetRow(summary,2);main.Children.Add(summary);
        var taskHeading=new StackPanel{Orientation=Orientation.Horizontal};taskHeading.Children.Add(Text("我的任务",16));count=Text("",11,"Muted");count.Margin=new Thickness(9,0,0,0);taskHeading.Children.Add(count);summary.Children.Add(taskHeading);
        showCompleted=new CheckBox{Content="显示已完成",Style=(Style)FindResource("CompletedFilter"),IsChecked=state.ShowCompleted,FontSize=11,VerticalAlignment=VerticalAlignment.Center};Localize(showCompleted,ContentControl.ContentProperty,"显示已完成");Automation(showCompleted,"显示已完成");showCompleted.Click+=(s,e)=>{state.ShowCompleted=showCompleted.IsChecked==true;Refresh();QueueSave();};Grid.SetColumn(showCompleted,1);summary.Children.Add(showCompleted);
        progressLabel=Text("",11,"Muted");progressLabel.Margin=new Thickness(0,13,0,7);Grid.SetRow(progressLabel,1);Grid.SetColumnSpan(progressLabel,2);summary.Children.Add(progressLabel);
        progress=new ProgressBar{Minimum=0,Maximum=100,Height=3,BorderThickness=new Thickness(0),Background=B("Line"),Foreground=B("Accent")};progress.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ProgressBar'><Grid><Border x:Name='PART_Track' Background='{TemplateBinding Background}' CornerRadius='2'/><Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}' CornerRadius='2'/></Grid></ControlTemplate>");Grid.SetRow(progress,2);Grid.SetColumnSpan(progress,2);summary.Children.Add(progress);
        var listArea=new Grid();Grid.SetRow(listArea,3);main.Children.Add(listArea);
        taskList=new ListBox{Background=Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,AllowDrop=true};Automation(taskList,"任务列表");ScrollViewer.SetHorizontalScrollBarVisibility(taskList,ScrollBarVisibility.Disabled);ScrollViewer.SetVerticalScrollBarVisibility(taskList,ScrollBarVisibility.Auto);ScrollViewer.SetCanContentScroll(taskList,true);VirtualizingPanel.SetIsVirtualizing(taskList,true);VirtualizingPanel.SetVirtualizationMode(taskList,VirtualizationMode.Recycling);VirtualizingPanel.SetScrollUnit(taskList,ScrollUnit.Pixel);
        var container=new Style(typeof(ListBoxItem));container.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));container.Setters.Add(new Setter(Control.MarginProperty,new Thickness(0)));container.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));container.Setters.Add(new Setter(Control.FocusableProperty,false));container.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'><ContentPresenter/></ControlTemplate>")));taskList.ItemContainerStyle=container;
        taskList.ItemTemplate=(DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Yijian.Win;assembly=一件Todo">
          <Border Margin="{Binding Indent}" Padding="{Binding Padding}" Background="{DynamicResource Card}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="10" Opacity="{Binding Opacity}">
            <Grid MinHeight="28" SnapsToDevicePixels="True"><Grid.ColumnDefinitions><ColumnDefinition Width="23"/><ColumnDefinition Width="25"/><ColumnDefinition Width="*"/><ColumnDefinition Width="24"/><ColumnDefinition Width="23"/></Grid.ColumnDefinitions>
              <Button Content="⠿" Tag="{Binding DragTag}" Height="28" VerticalAlignment="Center" Padding="2,0" Cursor="SizeAll" Foreground="{DynamicResource Muted}" ToolTip="{Binding DragHint}"/>
              <CheckBox Grid.Column="1" IsChecked="{Binding Done, Mode=OneWay}" Tag="{Binding CheckTag}" Height="28" VerticalAlignment="Center" VerticalContentAlignment="Center" Padding="0" ToolTip="{Binding CheckHint}"/>
              <Button Grid.Column="2" Tag="{Binding ToggleTag}" MinHeight="28" VerticalAlignment="Center" VerticalContentAlignment="Center" Padding="3,0" HorizontalContentAlignment="Stretch" ToolTip="{Binding Hint}"><TextBlock Text="{Binding Title}" TextDecorations="{Binding Decorations}" Foreground="{DynamicResource Fg}" FontSize="13" VerticalAlignment="Center" TextWrapping="Wrap" LineHeight="22" LineStackingStrategy="BlockLineHeight"/></Button>
              <Button Grid.Column="3" Tag="{Binding ToggleTag}" Content="{Binding Arrow}" Height="28" VerticalAlignment="Center" Foreground="{DynamicResource Muted}" Padding="2,0"/>
              <Button Grid.Column="4" Tag="{Binding MenuTag}" Content="⋯" Height="28" VerticalAlignment="Center" Foreground="{DynamicResource Muted}" Padding="2,0" ToolTip="{Binding MenuHint}"/>
            </Grid>
          </Border>
        </DataTemplate>
        """);
        taskList.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler(TaskClick));taskList.PreviewMouseRightButtonUp+=TaskRightClick;taskList.PreviewMouseLeftButtonDown+=DragBegin;taskList.PreviewMouseMove+=DragMoveTask;taskList.PreviewMouseLeftButtonUp+=(s,e)=>dragId=null;taskList.DragOver+=DragOverTask;taskList.Drop+=DropTask;taskList.DragLeave+=(s,e)=>taskList.BorderThickness=new Thickness(0);listArea.Children.Add(taskList);
        empty=Text("给今天留一点方向\n点击下方 ＋，添加一件事",13,"Muted");empty.TextAlignment=TextAlignment.Center;empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;empty.LineHeight=27;listArea.Children.Add(empty);
        var add=Btn("＋   添加任务","添加任务",()=>EditTask(null,null));add.Margin=new Thickness(27,17,27,9);add.Height=44;add.Background=B("Soft");add.Foreground=B("Accent");add.FontSize=14;Grid.SetRow(add,4);main.Children.Add(add);
        var footer=new StackPanel();Grid.SetRow(footer,5);main.Children.Add(footer);
        var undoContent=new Grid();undoContent.ColumnDefinitions.Add(new ColumnDefinition());undoContent.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});undoContent.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        taskUndoLabel=Text("",11,"Accent");undoContent.Children.Add(taskUndoLabel);
        var undoDelete=Btn("撤销","撤销删除",()=>{var before=taskUndoSnapshot;var revision=taskUndoRevision;if(before==null)return;ClearTaskUndo();if(store.Restore(before,revision))ai.Clear();});undoDelete.Foreground=B("Accent");Grid.SetColumn(undoDelete,1);undoContent.Children.Add(undoDelete);
        var dismissUndo=Btn("×","关闭撤销提示",ClearTaskUndo);Grid.SetColumn(dismissUndo,2);undoContent.Children.Add(dismissUndo);
        taskUndoNotice=new Border{Background=B("Soft"),CornerRadius=new CornerRadius(8),Padding=new Thickness(11,3,4,3),Margin=new Thickness(27,0,27,4),Child=undoContent,Visibility=Visibility.Collapsed};footer.Children.Add(taskUndoNotice);
        status=Text("所有改变，都会被好好记下",10,"Muted");status.TextAlignment=TextAlignment.Center;status.Margin=new Thickness(12,5,12,16);footer.Children.Add(status);
        var divider=new Border();divider.SetResourceReference(Border.BackgroundProperty,"Line");Grid.SetColumn(divider,1);layout.Children.Add(divider);BuildChat();
    }
    void Refresh() {
        if(taskList==null)return;var snapshot=store.Snapshot();taskList.ItemsSource=store.Visible(state.ShowCompleted).Select(r=>new TaskView(r,state.Compact)).ToList();empty.Visibility=taskList.Items.Count==0?Visibility.Visible:Visibility.Collapsed;
        var done=snapshot.Tasks.Count(t=>t.Done);count.Text=(snapshot.Tasks.Count-done).ToString();progressLabel.Text=F("已完成 {0} / {1} 件事",done,snapshot.Tasks.Count);progress.Value=snapshot.Tasks.Count==0?0:done*100.0/snapshot.Tasks.Count;
        if(showCompleted!=null)showCompleted.IsChecked=state.ShowCompleted;
    }
    void Changed(){ClearTaskUndo();Refresh();QueueSave();}
    void ClearTaskUndo(){taskUndoSnapshot=null;if(taskUndoNotice!=null)taskUndoNotice.Visibility=Visibility.Collapsed;}
    void ShowTaskUndo(string title,TaskSnapshot before,long revision){taskUndoSnapshot=before;taskUndoRevision=revision;Localize(taskUndoLabel,TextBox.TextProperty,title);taskUndoNotice.Visibility=Visibility.Visible;}
    void QueueSave(){if(initializing)return;Localize(status,TextBox.TextProperty,"正在保存…");saveTimer.Stop();saveTimer.Start();}
    bool Save() {
        try {state.Tasks=store.Snapshot().Tasks;state.Height=RestoreBounds.Height>0?RestoreBounds.Height:Height;state.Left=Left;state.Top=Top;storage.Save(state);Localize(status,TextBox.TextProperty,"已保存在此设备");return true;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) {status.Text=T("保存失败：")+e.Message;return false;}
    }
    static T? Ancestor<T>(DependencyObject? source) where T:DependencyObject {while(source!=null){if(source is T t)return t;source=VisualTreeHelper.GetParent(source);}return null;}
    void TaskClick(object sender,RoutedEventArgs e) {
        var b=Ancestor<ButtonBase>(e.OriginalSource as DependencyObject);if(b?.Tag is not string tag||!tag.Contains(':'))return;var parts=tag.Split(':',2);var item=store.Snapshot().Tasks.FirstOrDefault(t=>t.Id==parts[1]);if(item==null)return;
        switch(parts[0]){case "check":store.Complete(item.Id,!item.Done);break;case "toggle":if(store.Snapshot().Tasks.Any(t=>t.ParentId==item.Id))store.Collapse(item.Id,!item.Collapsed);break;case "menu":Menu(item.Id,b);break;}e.Handled=true;
    }
    void TaskRightClick(object sender,MouseButtonEventArgs e){var row=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);if(row?.DataContext is TaskView view){Menu(view.Id,row);e.Handled=true;}}
    void Menu(string id,FrameworkElement anchor) {
        var item=store.Snapshot().Tasks.FirstOrDefault(t=>t.Id==id);if(item==null)return;var menu=new ContextMenu{PlacementTarget=anchor};
        menu.Resources.MergedDictionaries.Add(Resources);
        void Add(string title,string icon,Action action,bool destructive=false){var entry=new MenuItem{Header=T(title),Icon=icon};if(destructive)entry.Style=(Style)FindResource("DeleteMenuItem");entry.Click+=(s,e)=>action();menu.Items.Add(entry);}
        Add("创建子任务","＋",()=>EditTask(null,id));if(!item.Done)Add("标记任务为已完成","✓",()=>store.Complete(id,true));Add("修改任务","✎",()=>EditTask(id,null));
        Add("复制任务文字","⧉",()=>Clipboard.SetText(item.Title));menu.Items.Add(new Separator());Add("删除任务","×",()=>{var before=store.Snapshot();store.Remove(id);ShowTaskUndo("已删除任务",before,store.Snapshot().Revision);},true);menu.IsOpen=true;
    }
    void EditTask(string? id,string? parent) {
        var current=id==null?null:store.Snapshot().Tasks.FirstOrDefault(t=>t.Id==id);var dialog=Dialog(T(id!=null?"修改任务":parent!=null?"创建子任务":"添加任务"),360,220);
        var panel=new StackPanel{Margin=new Thickness(22)};panel.Children.Add(Text(dialog.Title,17));var input=new TextBox{Text=current?.Title??"",MaxLength=200,Margin=new Thickness(0,18,0,15)};Automation(input,"任务名称");panel.Children.Add(input);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=Btn("取消","取消",()=>dialog.Close());var ok=Btn("保存","保存任务",()=>{if(string.IsNullOrWhiteSpace(input.Text))return;try{if(id==null)store.Create(input.Text,parent);else store.Update(id,input.Text);dialog.DialogResult=true;}catch(ArgumentException ex){MessageBox.Show(this,ex.Message);}});ok.Background=B("Soft");ok.IsDefault=true;cancel.IsCancel=true;actions.Children.Add(cancel);actions.Children.Add(ok);panel.Children.Add(actions);dialog.Content=panel;dialog.Loaded+=(s,e)=>{input.Focus();input.SelectAll();};dialog.ShowDialog();
    }
    sealed class OverlayDialog {
        readonly MainWindow owner; readonly Grid overlay; readonly Border body; readonly Border frame; readonly IInputElement? previousFocus;
        public string Title {get;}
        public double MaxHeight {set=>frame.MaxHeight=value;}
        public object Content {set=>body.Child=(UIElement)value;}
        public bool? DialogResult {set=>Close();}
        public event RoutedEventHandler Loaded {add=>overlay.Loaded+=value;remove=>overlay.Loaded-=value;}
        public OverlayDialog(MainWindow owner,string title,double width,double height) {
            this.owner=owner;Title=title;previousFocus=Keyboard.FocusedElement;
            overlay=new Grid{Background=new SolidColorBrush(Color.FromArgb(90,18,28,20)),Focusable=true};
            KeyboardNavigation.SetTabNavigation(overlay,KeyboardNavigationMode.Cycle);
            frame=new Border{Width=Math.Min(width,owner.ActualWidth-28),MaxHeight=Math.Min(height,owner.ActualHeight-34),Background=owner.B("Bg"),BorderBrush=owner.B("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var inner=new Grid();inner.RowDefinitions.Add(new RowDefinition{Height=new GridLength(32)});inner.RowDefinitions.Add(new RowDefinition());
            var close=owner.Btn("✕","关闭对话框",Close);close.HorizontalAlignment=HorizontalAlignment.Right;close.Margin=new Thickness(0,2,7,0);inner.Children.Add(close);
            body=new Border();Grid.SetRow(body,1);inner.Children.Add(body);frame.Child=inner;overlay.Children.Add(frame);
            overlay.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        }
        public void ShowDialog(){if(owner.root.Children.Contains(overlay))return;owner.layout.IsHitTestVisible=false;owner.root.Children.Add(overlay);Panel.SetZIndex(overlay,10);overlay.Focus();}
        public void Close(){owner.root.Children.Remove(overlay);owner.layout.IsHitTestVisible=true;if(previousFocus!=null)Keyboard.Focus(previousFocus);}
    }
    OverlayDialog Dialog(string title,double width,double height)=>new(this,title,width,height);

    void DragBegin(object sender,MouseButtonEventArgs e){var b=Ancestor<Button>(e.OriginalSource as DependencyObject);dragId=b?.Tag is string tag&&tag.StartsWith("drag:")?tag[5..]:null;dragStart=e.GetPosition(taskList);}
    void DragMoveTask(object sender,MouseEventArgs e){if(dragId==null||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(taskList);if(Math.Abs(p.X-dragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-dragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;var id=dragId;dragId=null;if(store.Snapshot().Tasks.First(t=>t.Id==id).Done)return;DragDrop.DoDragDrop(taskList,new DataObject("Yijian.Task",id),DragDropEffects.Move);}
    bool DropTarget(DragEventArgs e,out string id,out TaskView? target,out ListBoxItem? container){id=e.Data.GetData("Yijian.Task") as string??"";container=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);target=container?.DataContext as TaskView;if(target==null||id==""||target.Done)return false;var all=store.Snapshot().Tasks;var sourceId=id;var targetId=target.Id;var from=all.FirstOrDefault(t=>t.Id==sourceId);var to=all.FirstOrDefault(t=>t.Id==targetId);return from!=null&&to!=null&&!from.Done&&from.Id!=to.Id&&from.ParentId==to.ParentId;}
    void DragOverTask(object sender,DragEventArgs e){bool valid=DropTarget(e,out _,out _,out var container);e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;taskList.BorderBrush=B("Accent");taskList.BorderThickness=new Thickness(valid?1:0);if(valid&&container!=null){var point=e.GetPosition(taskList);if(point.Y<28)taskList.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=Mouse.MouseWheelEvent});else if(point.Y>taskList.ActualHeight-28)taskList.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.MouseWheelEvent});}}
    void DropTask(object sender,DragEventArgs e){taskList.BorderThickness=new Thickness(0);if(DropTarget(e,out var id,out var target,out var container))store.Move(id,target!.Id,e.GetPosition(container).Y>container!.ActualHeight/2);e.Handled=true;}
    void BuildChat() {
        chat=new Grid{Visibility=Visibility.Collapsed,Margin=new Thickness(0)};chat.SetResourceReference(Panel.BackgroundProperty,"Bg");foreach(var h in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})chat.RowDefinitions.Add(new RowDefinition{Height=h});Grid.SetColumn(chat,2);layout.Children.Add(chat);
        var header=new Grid{Margin=new Thickness(23,18,14,12)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});chat.Children.Add(header);var label=new StackPanel();label.Children.Add(Text("任务伙伴",17));var subtitle=Text("一起把事情理清楚",10,"Muted");subtitle.Margin=new Thickness(0,5,0,0);label.Children.Add(subtitle);header.Children.Add(label);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};newChat=Btn("↺","新对话",()=>{ai.Clear();messages.Children.Clear();Welcome();});buttons.Children.Add(newChat);buttons.Children.Add(Btn("⚙","AI 配置",AiSettings));buttons.Children.Add(Btn("›","收起 AI 对话",()=>ToggleAi(false)));Grid.SetColumn(buttons,1);header.Children.Add(buttons);
        messages=new StackPanel{Margin=new Thickness(19,10,19,10)};chatScroll=new ScrollViewer{Content=messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(chatScroll,1);chat.Children.Add(chatScroll);Welcome();
        var input=new StackPanel{Margin=new Thickness(20,12,20,18)};Grid.SetRow(input,2);chat.Children.Add(input);composer=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=75,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=12000};Automation(composer,"与 AI 对话输入框");composer.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Enter&&(Keyboard.Modifiers&(ModifierKeys.Shift|ModifierKeys.Alt))==0){e.Handled=true;if(!busy)_=SendAsync();}};input.Children.Add(composer);
        var bottom=new Grid{Margin=new Thickness(0,9,0,0)};bottom.ColumnDefinitions.Add(new ColumnDefinition());bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});bottom.Children.Add(Text("Enter 发送 · Shift+Enter 换行",10,"Muted"));send=Btn("发送 ↑","发送消息",()=>{if(busy)aiCancellation?.Cancel();else _=SendAsync();});send.Background=B("Soft");send.Foreground=B("Accent");Grid.SetColumn(send,1);bottom.Children.Add(send);input.Children.Add(bottom);
    }
    void Welcome(){
        welcome=new StackPanel();
        var intro=Text("AI 可以陪你聊聊，也可以帮你拆解、调整任务。你要求修改时，结果会直接出现在左侧清单中。",13);intro.LineHeight=23;
        welcome.Children.Add(new Border{Background=B("Card"),CornerRadius=new CornerRadius(12),Padding=new Thickness(13,10,13,10),Margin=new Thickness(0,0,16,12),Child=intro});
        foreach(var suggestion in new[]{"帮我看看今天的任务安排","把一个大任务拆成容易开始的小步骤"}){var b=Btn(suggestion,suggestion,()=>{composer.Text=T(suggestion);composer.Focus();});b.Margin=new Thickness(0,5,0,0);b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Background=B("Soft");welcome.Children.Add(b);}
        messages.Children.Add(welcome);
    }
    void DismissWelcome(){if(welcome==null)return;messages.Children.Remove(welcome);welcome=null;}
    void ToggleAi(bool open){aiOpen=open;chat.Visibility=open?Visibility.Visible:Visibility.Collapsed;chatColumn.Width=new GridLength(open?410:0);MinWidth=open?790:380;if(WindowState==WindowState.Normal){Width=open?Math.Min(850,SystemParameters.WorkArea.Width):420;if(Left+Width>SystemParameters.WorkArea.Right)Left=Math.Max(SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-Width);}if(open)composer.Focus();}
    SelectableText Bubble(string text,bool user){var content=new SelectableText{Text=text,FontSize=13};content.SetResourceReference(Control.ForegroundProperty,"Fg");content.LineHeight=23;var border=new Border{Background=B(user?"Soft":"Card"),CornerRadius=new CornerRadius(12),Padding=new Thickness(13,10,13,10),Margin=new Thickness(user?32:0,0,user?0:16,12),Child=content};messages.Children.Add(border);TrimChat();chatScroll?.ScrollToEnd();return content;}
    void TrimChat(){while(messages.Children.Count>100)messages.Children.RemoveAt(0);}
    void AddUndo(string title,TaskSnapshot before,long revision,bool canUndo){if(!aiOpen)ToggleAi(true);var card=new StackPanel{Margin=new Thickness(0,0,12,13)};card.Children.Add(Text(title,11,"Accent"));if(canUndo){var undo=Btn("撤销这次修改","撤销这次修改",()=>{});undo.HorizontalAlignment=HorizontalAlignment.Left;undo.Click+=(s,e)=>{if(store.Restore(before,revision)){undo.IsEnabled=false;Localize(undo,ContentControl.ContentProperty,"已撤销");ai.Clear();}else{undo.IsEnabled=false;Localize(undo,ContentControl.ContentProperty,"后续已有修改，无法撤销此轮");}};card.Children.Add(undo);}else card.Children.Add(Text("清单已有其他修改，本轮不提供整体撤销",10,"Muted"));messages.Children.Add(card);TrimChat();chatScroll.ScrollToEnd();}
    async Task SendAsync(){if(busy||string.IsNullOrWhiteSpace(composer.Text))return;try{AiClient.Endpoint(state.AiUrl);if(string.IsNullOrWhiteSpace(state.AiModel))throw new ArgumentException(T("请先在 AI 配置中填写模型名称，再发送消息。"));}catch(ArgumentException e){DismissWelcome();Bubble(e.Message,false);composer.Focus();return;}
        var prompt=composer.Text.Trim();composer.Clear();DismissWelcome();Bubble(prompt,true);var reply=Bubble(T("正在想一想…"),false);busy=true;newChat.IsEnabled=false;Localize(send,ContentControl.ContentProperty,"停止 □");Localize(send,ToolTipProperty,"停止 □");Automation(send,"停止 □");aiCancellation=new CancellationTokenSource();var operationCount=0;
        try {var turn=await ai.RunAsync(prompt,new(state.AiUrl,state.AiModel,key,state.Language),store,(name,args)=>Dispatcher.InvokeAsync(()=>store.Execute(name,args)).Task,op=>Dispatcher.Invoke(()=>{operationCount++;reply.Text=F("正在整理任务… 已执行 {0} 项操作",operationCount);}),aiCancellation.Token);reply.Text=turn.Error??turn.Text;if(turn.Operations.Count>0)AddUndo(string.Join("\n",turn.Operations.Select(x=>"✓ "+x.Detail)),turn.Before,turn.AfterRevision,turn.CanUndo);}
        catch(Exception e) when(e is ArgumentException or IOException or InvalidOperationException){reply.Text=e.Message;}
        finally{busy=false;newChat.IsEnabled=true;Localize(send,ContentControl.ContentProperty,"发送 ↑");Localize(send,ToolTipProperty,"发送消息");Automation(send,"发送消息");aiCancellation.Dispose();aiCancellation=null;chatScroll.ScrollToEnd();composer.Focus();}
    }
    void Export(){var picker=new SaveFileDialog{Filter=T("JSON 任务文件|*.json"),FileName="一件Todo-"+DateTime.Now.ToString("yyyyMMdd")+".json"};if(picker.ShowDialog(this)==true)try{File.WriteAllText(picker.FileName,JsonSerializer.Serialize(store.Snapshot(),StateStorage.Json));}catch(IOException e){MessageBox.Show(this,e.Message);}}
    void Import(){var picker=new OpenFileDialog{Filter=T("JSON 任务文件|*.json")};if(picker.ShowDialog(this)!=true)return;try{if(new FileInfo(picker.FileName).Length>8*1024*1024)throw new IOException(T("文件超过 8 MB。"));using var json=JsonDocument.Parse(File.ReadAllText(picker.FileName));var element=json.RootElement.ValueKind==JsonValueKind.Array?json.RootElement:json.RootElement.GetProperty("tasks");var tasks=JsonSerializer.Deserialize<List<TodoTask>>(element,StateStorage.Json)??throw new IOException(T("没有任务数据。"));TaskStore.Validate(tasks);if(MessageBox.Show(this,T("用导入文件替换当前任务？"),T("导入任务"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;var before=store.Snapshot();store.Restore(new(0,tasks),before.Revision);ai.Clear();AddUndo("已导入任务",before,store.Snapshot().Revision,true);}catch(Exception e)when(e is IOException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException){MessageBox.Show(this,e.Message,T("导入失败"));}}
    void DragWindow(object sender,MouseButtonEventArgs e) {
        if(e.ChangedButton!=MouseButton.Left||!layout.IsHitTestVisible)return;
        var source=e.OriginalSource as DependencyObject;
        if(Ancestor<ButtonBase>(source)!=null||Ancestor<TextBoxBase>(source)!=null||Ancestor<ScrollBar>(source)!=null||Ancestor<Thumb>(source)!=null||Ancestor<ListBoxItem>(source)!=null)return;
        if(e.ClickCount==2&&e.GetPosition(this).Y<65)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        else {try{DragMove();}catch(InvalidOperationException){}}
        e.Handled=true;
    }
    void RestoreFromTray(){ShowInTaskbar=true;Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    void TrayMenu(){
        var menu=new ContextMenu();menu.Resources.MergedDictionaries.Add(Resources);
        var open=new MenuItem{Header=T("打开一件 Todo"),Icon="↗"};open.Click+=(s,e)=>RestoreFromTray();menu.Items.Add(open);
        menu.Items.Add(new Separator());var exit=new MenuItem{Header=T("退出程序"),Icon="×",Style=(Style)FindResource("DeleteMenuItem")};
        exit.Click+=(s,e)=>{exitRequested=true;Close();};menu.Items.Add(exit);menu.IsOpen=true;
    }
    void OnClosing(object? sender,CancelEventArgs e){
        saveTimer.Stop();
        if(!Save()&&MessageBox.Show(this,T("任务未能保存。仍然退出？"),T("保存失败"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes){e.Cancel=true;exitRequested=false;return;}
        if(state.CloseToTray&&!exitRequested&&tray?.IsAvailable==true){e.Cancel=true;Hide();return;}
        aiCancellation?.Cancel();
    }
}
