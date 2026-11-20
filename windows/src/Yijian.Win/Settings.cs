using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Yijian.Core;
using static Yijian.Core.Strings;

namespace Yijian.Win;

public sealed partial class MainWindow {
    void Settings() {
        var dialog=Dialog(T("设置 · 一件 Todo"),420,600);
        var panel=new StackPanel{Margin=new Thickness(25,12,25,20)};
        dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("偏好设置",19));
        var languageLabel=Text("界面语言 / Language",11,"Muted");languageLabel.Margin=new Thickness(0,18,0,6);panel.Children.Add(languageLabel);
        var languages=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,18)};
        var chinese=new RadioButton{Content="简体中文",GroupName="Language",IsChecked=state.Language=="zh-CN",Margin=new Thickness(0,0,24,0)};
        var english=new RadioButton{Content="English",GroupName="Language",IsChecked=state.Language=="en-US"};
        languages.Children.Add(chinese);languages.Children.Add(english);panel.Children.Add(languages);
        var compact=new CheckBox{Content=T("紧凑布局"),IsChecked=state.Compact,Margin=new Thickness(0,0,0,12)};
        var dark=new CheckBox{Content=T("深色主题"),IsChecked=state.Dark,Margin=new Thickness(0,0,0,22)};
        panel.Children.Add(compact);panel.Children.Add(dark);panel.Children.Add(Text("点击右上角 × 时",12,"Muted"));
        var exitOnClose=new RadioButton{Content=T("退出程序"),IsChecked=!state.CloseToTray,GroupName="CloseAction",Margin=new Thickness(0,10,0,9)};
        var trayOnClose=new RadioButton{Content=T("最小化到托盘"),IsChecked=state.CloseToTray,GroupName="CloseAction",Margin=new Thickness(0,0,0,16)};
        panel.Children.Add(exitOnClose);panel.Children.Add(trayOnClose);

        var shortcutLabel=Text("打开便签快捷键",11,"Muted");shortcutLabel.Margin=new Thickness(0,4,0,6);panel.Children.Add(shortcutLabel);
        var shortcutBox=new TextBox{Text=string.IsNullOrWhiteSpace(state.NoteShortcut)?"F2":state.NoteShortcut,IsReadOnly=true,ToolTip=T("点击后按下快捷键…"),Margin=new Thickness(0,0,0,16)};
        Automation(shortcutBox,"打开便签快捷键");
        InputMethod.SetIsInputMethodEnabled(shortcutBox,false);
        shortcutBox.GotKeyboardFocus+=(s,e)=>capturingShortcut=true;
        shortcutBox.LostKeyboardFocus+=(s,e)=>capturingShortcut=false;
        shortcutBox.PreviewKeyDown+=(s,e)=>{
            if(e.Key==Key.Tab)return;
            if(e.Key==Key.Escape){Keyboard.ClearFocus();e.Handled=true;return;}
            var formatted=FormatShortcutFromKeyEvent(e);
            if(!string.IsNullOrEmpty(formatted))shortcutBox.Text=formatted;
            e.Handled=true;
        };
        panel.Children.Add(shortcutBox);

        var configure=Btn("AI 配置","AI 配置",()=>{capturingShortcut=false;dialog.Close();AiSettings();});configure.Background=B("Soft");configure.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(configure);
        var files=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,16,0,12)};
        files.Children.Add(Btn("导出任务","导出任务",Export));files.Children.Add(Btn("导入任务","导入任务",()=>{capturingShortcut=false;dialog.Close();Import();}));panel.Children.Add(files);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(Btn("恢复默认设置","恢复默认设置",()=>{chinese.IsChecked=true;dark.IsChecked=false;compact.IsChecked=false;exitOnClose.IsChecked=true;shortcutBox.Text="F2";}));
        var save=Btn("保存","保存设置",()=>{
            capturingShortcut=false;
            state.Dark=dark.IsChecked==true;state.Compact=compact.IsChecked==true;state.CloseToTray=trayOnClose.IsChecked==true;
            var nextShortcut=shortcutBox.Text?.Trim();
            state.NoteShortcut=!string.IsNullOrWhiteSpace(nextShortcut)&&MainWindow.TryParseShortcut(nextShortcut,out _,out _)?nextShortcut:"F2";
            UpdateGlobalHotkey();
            ApplyLanguage(english.IsChecked==true?"en-US":"zh-CN");Theme();QueueSave();dialog.Close();
        });
        save.Background=B("Soft");actions.Children.Add(save);panel.Children.Add(actions);dialog.ShowDialog();
    }

    void AiSettings() {
        var dialog=Dialog(T("AI 配置"),420,680);
        var panel=new StackPanel{Margin=new Thickness(25,12,25,20)};
        dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("AI 配置",19));
        TextBox Field(string label,string value){var caption=Text(label,11,"Muted");caption.Margin=new Thickness(0,13,0,6);panel.Children.Add(caption);var box=new TextBox{Text=value};Automation(box,T(label));panel.Children.Add(box);return box;}
        var url=Field("接口 URL",state.AiUrl);var model=Field("模型名称",state.AiModel);
        var keyLabel=Text("API Key（本地模型可留空）",11,"Muted");keyLabel.Margin=new Thickness(0,13,0,6);panel.Children.Add(keyLabel);
        var secret=new PasswordBox{Password=key,Padding=new Thickness(10),Background=B("Card"),Foreground=B("Fg"),BorderBrush=B("Line")};Automation(secret,"API Key");panel.Children.Add(secret);
        var visible=new TextBox{Text=key,Visibility=Visibility.Collapsed};Automation(visible,T("显示的 API Key"));panel.Children.Add(visible);
        var show=new CheckBox{Content=T("显示 Key"),Margin=new Thickness(0,10,0,10)};
        show.Click+=(s,e)=>{if(show.IsChecked==true){visible.Text=secret.Password;secret.Visibility=Visibility.Collapsed;visible.Visibility=Visibility.Visible;}else{secret.Password=visible.Text;secret.Visibility=Visibility.Visible;visible.Visibility=Visibility.Collapsed;}};panel.Children.Add(show);
        var remember=new CheckBox{Content=new TextBlock{Text=T("在此设备记住 Key（Windows 加密）"),TextWrapping=TextWrapping.Wrap},IsChecked=state.RememberKey,Margin=new Thickness(0,0,0,12)};panel.Children.Add(remember);
        panel.Children.Add(Text("默认只在本次运行中使用 Key。对话时仅发送当前任务及对话内容；诗库保留在本地。",10,"Muted"));
        var error=Text("",11,"Danger");error.Margin=new Thickness(0,12,0,0);panel.Children.Add(error);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
        actions.Children.Add(Btn("恢复默认连接","恢复默认连接",()=>{url.Text="https://api.openai.com/v1";model.Text="";secret.Password="";visible.Text="";remember.IsChecked=false;}));
        actions.Children.Add(Btn("取消","取消",dialog.Close));
        var save=Btn("保存","保存设置",()=>{
            try{AiClient.Endpoint(url.Text);var nextKey=show.IsChecked==true?visible.Text:secret.Password;
                if(nextKey.Contains('\r')||nextKey.Contains('\n'))throw new ArgumentException(T("Key 中不能含有换行。"));
                SecretStore.Save(storage.DirectoryPath,nextKey,remember.IsChecked==true);
                state.AiUrl=url.Text.Trim();state.AiModel=model.Text.Trim();state.RememberKey=remember.IsChecked==true;key=nextKey;QueueSave();dialog.Close();
            }catch(Exception ex)when(ex is ArgumentException or IOException or UnauthorizedAccessException){error.Text=ex.Message;}
        });save.Background=B("Soft");actions.Children.Add(save);panel.Children.Add(actions);dialog.ShowDialog();
    }
}
