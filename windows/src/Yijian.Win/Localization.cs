using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Yijian.Core;

namespace Yijian.Win;

// Bind UI chrome to the catalog; changing language never rebuilds the window.
internal sealed class LocalizedTextConverter : IValueConverter {
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>Strings.T((string)parameter);
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed partial class MainWindow {
    static readonly LocalizedTextConverter textConverter=new();
    internal static void Localize(DependencyObject target,DependencyProperty property,string source) {
        if(Strings.Contains(source)) BindingOperations.SetBinding(target,property,new Binding(nameof(Strings.Revision)){Mode=BindingMode.OneWay,Source=Strings.Current,Converter=textConverter,ConverterParameter=source});
        else target.SetValue(property,source);
    }
    void ApplyLanguage(string language) {
        state.Language=language=="en-US"?"en-US":"zh-CN";
        Strings.Current.SetLanguage(state.Language);
        date.Text=DateTime.Now.ToString(state.Language=="en-US"?"MMMM d  dddd":"M月d日  dddd",Strings.Current.Culture);
        Refresh();tray?.UpdateLanguage();
    }
}
