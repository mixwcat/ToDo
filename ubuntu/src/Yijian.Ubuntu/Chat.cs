using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Ubuntu;

public sealed partial class MainWindow {
    void BuildChat() {
        chat=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),IsVisible=false};Place(layout,chat,0,2);
        var header=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(23,18,14,12)};Place(chat,header);
        var title=new StackPanel{Spacing=5};title.Children.Add(Label("任务伙伴",17));title.Children.Add(Label("一起把事情理清楚",10,"Muted"));Place(header,title);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};newChat=Btn("↺","新对话",()=>{ai.Clear();messages.Children.Clear();Welcome();});actions.Children.Add(newChat);actions.Children.Add(Btn("⚙","AI 配置",()=>_ = AiSettingsAsync()));actions.Children.Add(Btn("›","收起 AI 对话",()=>ToggleAi(false)));Place(header,actions,0,1);
        messages=new StackPanel{Margin=new Thickness(19,10)};chatScroll=new ScrollViewer{Content=messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Place(chat,chatScroll,1);
        var input=new StackPanel{Margin=new Thickness(20,12,20,18)};Place(chat,input,2);
        composer=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=75,MaxHeight=150,MaxLength=12000};Localize(composer,AutomationProperties.NameProperty,"与 AI 对话输入框");ScrollViewer.SetVerticalScrollBarVisibility(composer,ScrollBarVisibility.Auto);
        composer.AddHandler(KeyDownEvent,(s,e)=>{if(e.Key==Key.Enter&&(e.KeyModifiers&(KeyModifiers.Shift|KeyModifiers.Alt))==0){e.Handled=true;if(!busy)_=SendAsync();}},RoutingStrategies.Tunnel);input.Children.Add(composer);
        var bottom=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(0,9,0,0)};Place(bottom,Label("Enter 发送 · Shift+Enter 换行",10,"Muted"));
        send=Btn("发送 ↑","发送消息",()=>{if(busy)aiCancellation?.Cancel();else _=SendAsync();},true);Place(bottom,send,0,1);input.Children.Add(bottom);Welcome();
    }
    void Welcome() {
        welcome=new StackPanel();var intro=Label("AI 可以陪你聊聊，也可以帮你拆解、调整任务。你要求修改时，结果会直接出现在左侧清单中。",13);intro.LineHeight=23;
        welcome.Children.Add(new Border{Background=B("Card"),CornerRadius=new CornerRadius(12),Padding=new Thickness(13,10),Margin=new Thickness(0,0,16,12),Child=intro});
        foreach(var suggestion in new[]{"帮我看看今天的任务安排","把一个大任务拆成容易开始的小步骤"}){var button=Btn(suggestion,suggestion,()=>{composer.Text=T(suggestion);composer.Focus();},true);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,5,0,0);welcome.Children.Add(button);}messages.Children.Add(welcome);
    }
    void DismissWelcome(){if(welcome!=null)messages.Children.Remove(welcome);welcome=null;}
    void ToggleAi(bool open) {
        aiOpen=open;chat.IsVisible=open;chatColumn.Width=new GridLength(open?410:0);MinWidth=open?790:380;
        var area=Screens.ScreenFromWindow(this)?.WorkingArea;
        if(WindowState==WindowState.Normal){Width=open?Math.Min(850,(area?.Width??1100)/RenderScaling):420;if(area.HasValue&&Position.X+Width*RenderScaling>area.Value.Right)Position=new PixelPoint((int)Math.Max(area.Value.X,area.Value.Right-Width*RenderScaling),Position.Y);}
        if(open)composer.Focus();
    }
    SelectableTextBlock Bubble(string text,bool user) {
        var label=Label(text,13,translate:false);label.LineHeight=23;
        messages.Children.Add(new Border{Background=B(user?"Soft":"Card"),CornerRadius=new CornerRadius(12),Padding=new Thickness(13,10),Margin=new Thickness(user?32:0,0,user?0:16,12),Child=label});TrimChat();ScrollChat();return label;
    }
    void TrimChat(){while(messages.Children.Count>100)messages.Children.RemoveAt(0);}
    void ScrollChat()=>Dispatcher.UIThread.Post(()=>chatScroll.ScrollToEnd(),DispatcherPriority.Background);
    void ChatUndo(string text,TaskSnapshot before,long revision,bool canUndo) {
        var card=new StackPanel{Margin=new Thickness(0,0,12,13)};card.Children.Add(Label(text,11,"Accent",false));
        if(canUndo){var undo=Btn("撤销这次修改","撤销这次修改",()=>{});undo.HorizontalAlignment=HorizontalAlignment.Left;undo.Click+=(s,e)=>{var restored=store.Restore(before,revision);undo.IsEnabled=false;Localize(undo,ContentControl.ContentProperty,restored?"已撤销":"后续已有修改，无法撤销此轮");if(restored)ai.Clear();};card.Children.Add(undo);}else card.Children.Add(Label("清单已有其他修改，本轮不提供整体撤销",10,"Muted"));
        messages.Children.Add(card);TrimChat();ScrollChat();
    }
    async Task SendAsync() {
        if(busy||string.IsNullOrWhiteSpace(composer.Text))return;
        busy=true;newChat.IsEnabled=false;
        try{await EnsureKeyAsync();AiClient.Endpoint(state.AiUrl);if(string.IsNullOrWhiteSpace(state.AiModel))throw new ArgumentException(T("请先在 AI 配置中填写模型名称，再发送消息。"));}
        catch(Exception ex)when(ex is ArgumentException or IOException){busy=false;newChat.IsEnabled=true;DismissWelcome();Bubble(ex.Message,false);composer.Focus();return;}
        var prompt=composer.Text!.Trim();composer.Clear();DismissWelcome();Bubble(prompt,true);var reply=Bubble(T("正在想一想…"),false);
        busy=true;newChat.IsEnabled=false;Localize(send,ContentControl.ContentProperty,"停止 □");Localize(send,ToolTip.TipProperty,"停止 □");
        aiCancellation=new();var operations=0;
        try {
            var turn=await ai.RunAsync(prompt,new(state.AiUrl,state.AiModel,key,state.Language),store,
                async(name,args)=>await Dispatcher.UIThread.InvokeAsync(()=>store.Execute(name,args)),
                op=>Dispatcher.UIThread.Invoke(()=>{operations++;reply.Text=F("正在整理任务… 已执行 {0} 项操作",operations);}),aiCancellation.Token);
            reply.Text=turn.Error??turn.Text;
            if(turn.Operations.Count>0)ChatUndo(string.Join("\n",turn.Operations.Select(x=>"✓ "+x.Detail)),turn.Before,turn.AfterRevision,turn.CanUndo);
        }catch(Exception ex)when(ex is ArgumentException or IOException or InvalidOperationException){reply.Text=ex.Message;}
        finally{busy=false;newChat.IsEnabled=true;Localize(send,ContentControl.ContentProperty,"发送 ↑");Localize(send,ToolTip.TipProperty,"发送消息");aiCancellation.Dispose();aiCancellation=null;ScrollChat();composer.Focus();}
    }
}
