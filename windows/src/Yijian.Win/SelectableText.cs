using static Yijian.Core.Strings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Yijian.Win;

// Read-only content only. Action labels use normal text so clicks reach their controls.
public sealed class SelectableText : TextBox {
    public double LineHeight { get=>TextBlock.GetLineHeight(this); set=>TextBlock.SetLineHeight(this,value); }
    public SelectableText() {
        IsReadOnly=true; IsReadOnlyCaretVisible=false; IsTabStop=false;
        AcceptsReturn=true; TextWrapping=TextWrapping.Wrap; Cursor=Cursors.IBeam;
        ContextMenu=new ContextMenu();
    }
    protected override void OnContextMenuOpening(ContextMenuEventArgs e) {
        // Task text keeps its task menu; Ctrl+C still copies the selected part.
        for(DependencyObject? p=VisualTreeHelper.GetParent(this);p!=null;p=VisualTreeHelper.GetParent(p))
            if(p is ListBoxItem){e.Handled=true;return;}
        var menu=new ContextMenu();
        if(Window.GetWindow(this) is Window window)menu.Resources.MergedDictionaries.Add(window.Resources);
        var copy=new MenuItem { Header=T("复制"), Icon="⧉", IsEnabled=SelectionLength>0 };
        copy.Click+=(s,args)=>Copy();
        var all=new MenuItem { Header=T("全选"), Icon="≡" };
        all.Click+=(s,args)=>{Focus();SelectAll();};
        var copyAll=new MenuItem { Header=T("复制全部文字"), Icon="⧉" };
        copyAll.Click+=(s,args)=>Clipboard.SetText(Text??"");
        menu.Items.Add(copy);menu.Items.Add(all);menu.Items.Add(copyAll);ContextMenu=menu;
        base.OnContextMenuOpening(e);
    }
}
