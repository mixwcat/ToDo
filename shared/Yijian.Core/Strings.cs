using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Yijian.Core;

// Shared by hosts and service errors. Chinese source text is the stable resource key;
// user task titles and AI replies are data and never pass through this catalog.
public sealed class Strings : INotifyPropertyChanged {
    public static Strings Current { get; } = new();
    readonly Dictionary<string,string> english;
    string language="zh-CN";
    public string Language => language;
    public CultureInfo Culture => CultureInfo.GetCultureInfo(language);
    public int Revision { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    Strings() {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Yijian.Core.Resources.en-US.json")!;
        english=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
    public void SetLanguage(string? value) {
        var next=value=="en-US"?"en-US":"zh-CN";
        if(next==language)return;
        language=next;Revision++;PropertyChanged?.Invoke(this,new(nameof(Revision)));
    }
    public static bool Contains(string key)=>Current.english.ContainsKey(key);
    public static string T(string key)=>Current.language=="en-US"&&Current.english.TryGetValue(key,out var value)?value:key;
    public static string F(string key,params object[] args)=>string.Format(Current.Culture,T(key),args);
}
