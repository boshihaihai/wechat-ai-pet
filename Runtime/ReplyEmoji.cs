using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatPet.Runtime {
    // Display-only: raw recommendation text and draft writes never pass through this class.
    internal static class ReplyEmoji {
        internal sealed class Part {
            internal string Text,Code,NativeName;
        }
        static readonly string assetDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","wechat-pet","emoji");
        static readonly Dictionary<string,string> aliases=LoadAliases();
        static readonly Dictionary<string,string> nativeAliases=LoadNativeAliases();
        static readonly HashSet<string> codes=new HashSet<string>(aliases.Values,StringComparer.Ordinal);
        static readonly Dictionary<string,ImageSource> images=new Dictionary<string,ImageSource>(StringComparer.Ordinal);
        // Consume whole emoji sequences before lookup: never replace only the head of a
        // skin-tone or ZWJ sequence and leave a broken suffix in the preview.
        const string scalar=@"(?:[\uD83C-\uD83E][\uDC00-\uDFFF]|[\u2300-\u27FF])";
        static readonly Regex tokens=new Regex(@"\[[^\[\]\r\n]{1,16}\]|"+scalar+@"[\uFE0E\uFE0F]?(?:\uD83C[\uDFFB-\uDFFF])?(?:\u200D"+scalar+@"[\uFE0E\uFE0F]?(?:\uD83C[\uDFFB-\uDFFF])?)*",RegexOptions.Compiled);
        static readonly DependencyProperty RenderKeyProperty=DependencyProperty.RegisterAttached("RenderKey",typeof(string),typeof(ReplyEmoji));
        internal static int AliasCount {get{return aliases.Count;}}
        internal static int NativeAliasCount {get{return nativeAliases.Count;}}
        static Dictionary<string,string> LoadNativeAliases() {
            try {
                var file=Path.Combine(assetDirectory,"native-aliases.json");
                if(!File.Exists(file)||new FileInfo(file).Length>32768)return new Dictionary<string,string>();
                var parsed=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(file));
                if(parsed==null)return new Dictionary<string,string>();
                return parsed.Where(x=>Regex.IsMatch(x.Key,@"^[^\[\]\r\n]{1,16}$")&&Regex.IsMatch(x.Value,@"^[A-Za-z0-9_]{1,40}\.png$")).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
            }catch(IOException){return new Dictionary<string,string>();}
             catch(UnauthorizedAccessException){return new Dictionary<string,string>();}
             catch(ArgumentException){return new Dictionary<string,string>();}
             catch(InvalidOperationException){return new Dictionary<string,string>();}
        }
        static Dictionary<string,string> LoadAliases() {
            try {
                var file=Path.Combine(assetDirectory,"aliases.json");
                if(!File.Exists(file)||new FileInfo(file).Length>32768)return new Dictionary<string,string>();
                var parsed=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(file));
                return parsed.Where(x=>Regex.IsMatch(x.Key,@"^[^\[\]\r\n]{1,16}$")&&Regex.IsMatch(x.Value,@"^[a-f0-9]{4,6}(?:-[a-f0-9]{4,6})*$")).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
            }catch(IOException){return new Dictionary<string,string>();}
             catch(UnauthorizedAccessException){return new Dictionary<string,string>();}
             catch(ArgumentException){return new Dictionary<string,string>();}
             catch(InvalidOperationException){return new Dictionary<string,string>();}
        }
        internal static List<Part> Split(string text) {
            text=text??"";var parts=new List<Part>();int at=0;
            foreach(Match m in tokens.Matches(text)) {
                if(m.Index>at)parts.Add(new Part{Text=text.Substring(at,m.Index-at)});
                string code=null,nativeName=null;
                // Never stand in a generic emoji for a named WeChat expression.
                // Only the names with a verified resource mapping get an image.
                if(m.Value[0]=='[') {
                    string name=m.Value.Substring(1,m.Value.Length-2);
                    if(nativeAliases.ContainsKey(name))nativeName=name;
                }
                else if(!m.Value.Contains("\uFE0E")) {
                    var values=new List<string>();
                    for(int i=0;i<m.Value.Length;i++) {
                        int value=Char.ConvertToUtf32(m.Value,i);if(Char.IsHighSurrogate(m.Value[i]))i++;
                        if(value!=0xFE0F)values.Add(value.ToString("x",CultureInfo.InvariantCulture));
                    }
                    string candidate=String.Join("-",values);if(codes.Contains(candidate))code=candidate;
                }
                parts.Add(new Part{Text=m.Value,Code=code,NativeName=nativeName});at=m.Index+m.Length;
            }
            if(at<text.Length)parts.Add(new Part{Text=text.Substring(at)});
            return parts;
        }
        internal static ImageSource ImageFor(string code) {
            ImageSource source;if(images.TryGetValue(code,out source))return source;
            try {
                string file=Path.Combine(assetDirectory,code+".png");
                if(!codes.Contains(code)||!File.Exists(file)||new FileInfo(file).Length>32768)return null;
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(file);bitmap.EndInit();bitmap.Freeze();source=bitmap;
            }catch(IOException){source=null;}catch(UnauthorizedAccessException){source=null;}catch(NotSupportedException){source=null;}catch(FileFormatException){source=null;}
            images[code]=source;return source;
        }
        internal static ImageSource NativeImageFor(string name) {
            string filename;if(!nativeAliases.TryGetValue(name,out filename))return null;
            string key="native:"+filename;ImageSource source;if(images.TryGetValue(key,out source))return source;
            try {
                string file=Path.Combine(assetDirectory,"native",filename);
                if(!File.Exists(file)||new FileInfo(file).Length>65536)return null;
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(file);bitmap.EndInit();bitmap.Freeze();source=bitmap;
            }catch(IOException){source=null;}catch(UnauthorizedAccessException){source=null;}catch(NotSupportedException){source=null;}catch(FileFormatException){source=null;}
            images[key]=source;return source;
        }
        internal static void Set(TextBlock target,string raw,bool singleLine=false) {
            string text=singleLine?Regex.Replace(raw??"",@"\s+"," ").Trim():(raw??"");
            string key=target.FontSize.ToString(CultureInfo.InvariantCulture)+"|"+singleLine+"|"+text;
            // Keep the original text current even if whitespace normalization
            // lets the single-line visual reuse its existing runs.
            AutomationProperties.SetName(target,raw??"");
            if((string)target.GetValue(RenderKeyProperty)==key)return;
            target.SetValue(RenderKeyProperty,key);target.Inlines.Clear();
            var parts=Split(text);
            bool emojiOnly=!singleLine&&parts.Count>0&&parts.Count<=6&&parts.All(p=>p.Code!=null||p.NativeName!=null||String.IsNullOrWhiteSpace(p.Text));
            double size=emojiOnly?28:Math.Ceiling(target.FontSize*1.3);
            foreach(var part in parts) {
                var image=part.NativeName!=null?NativeImageFor(part.NativeName):(part.Code==null?null:ImageFor(part.Code));
                if(image==null){target.Inlines.Add(new Run(part.Text));continue;}
                var icon=new Image{Source=image,Width=size,Height=size,Stretch=Stretch.Uniform,Margin=new Thickness(1,0,1,0),Focusable=false,ToolTip=part.Text};
                AutomationProperties.SetName(icon,part.Text);
                target.Inlines.Add(new InlineUIContainer(icon){BaselineAlignment=BaselineAlignment.Center});
            }
        }
    }
}
