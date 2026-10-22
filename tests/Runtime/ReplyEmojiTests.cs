using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatPet.Runtime {
    internal static class ReplyEmojiTests {
        internal static void Run(Action<string,bool> check,string outputDirectory) {
            check("已核实微信原生表情映射随包加载",ReplyEmoji.NativeAliasCount==115);
            var parts=ReplyEmoji.Split("好呀[捂脸] 明天见[笑脸]");
            check("文本与微信表情逐段保留原文",String.Concat(parts.Select(p=>p.Text))=="好呀[捂脸] 明天见[笑脸]"&&parts.Count(p=>p.NativeName!=null)==2);
            check("未知名称和普通括号不被替换",ReplyEmoji.Split("[项目编号] [未知表情]").All(p=>p.Code==null));
            check("经典表情使用微信原图而非通用图标",ReplyEmoji.Split("[色][调皮][微笑][抱拳]").All(p=>p.Code==null&&p.NativeName!=null));
            check("笑脸和微笑不是同一张表情",ReplyEmoji.Split("[笑脸][微笑]").Count(p=>p.NativeName=="笑脸")==1&&ReplyEmoji.NativeImageFor("微笑")!=null&&!Object.ReferenceEquals(ReplyEmoji.NativeImageFor("笑脸"),ReplyEmoji.NativeImageFor("微笑")));
            check("色和调皮来自各自原图",ReplyEmoji.NativeImageFor("色")!=null&&ReplyEmoji.NativeImageFor("调皮")!=null&&!Object.ReferenceEquals(ReplyEmoji.NativeImageFor("色"),ReplyEmoji.NativeImageFor("调皮")));
            check("标准Unicode表情可使用本地图像",ReplyEmoji.Split("😜❤️").Count(p=>p.Code!=null)==2);
            check("肤色和ZWJ序列不会拆坏",ReplyEmoji.Split("👍🏽👩‍💻").All(p=>p.Code==null));
            check("文字变体选择符不强制转换成图片",ReplyEmoji.Split("\u2764\uFE0E").All(p=>p.Code==null));
            var text=new TextBlock{FontSize=14,TextWrapping=TextWrapping.Wrap};
            string raw="好呀[捂脸]\n明天见[笑脸]";
            var recommendation=new Recommendation{Text=raw};
            ReplyEmoji.Set(text,recommendation.Text);
            check("推荐预览含两张表情图",text.Inlines.OfType<InlineUIContainer>().Count()==2);
            check("显示替换不改推荐填入正文",recommendation.Text==raw&&AutomationProperties.GetName(text)==raw);
            var first=text.Inlines.FirstInline;ReplyEmoji.Set(text,raw);
            check("相同推荐刷新复用显示内容",Object.ReferenceEquals(first,text.Inlines.FirstInline));
            ReplyEmoji.Set(text,"[捂脸]",true);
            check("灰字单行中的表情保持紧凑",((Image)text.Inlines.OfType<InlineUIContainer>().Single().Child).Height<28);
            ReplyEmoji.Set(text,"[捂脸]");
            check("纯表情推荐显示更清楚",((Image)text.Inlines.OfType<InlineUIContainer>().Single().Child).Height==28);
            ReplyEmoji.Set(text,"");check("撤销推荐清除旧图片",text.Inlines.Count==0);
            var aliasFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","wechat-pet","emoji","aliases.json");
            var aliases=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,string>>(File.ReadAllText(aliasFile));
            check("所有已配置表情均有可解码本地图像",aliases.Values.Distinct().All(code=>ReplyEmoji.ImageFor(code)!=null));
            var nativeFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","wechat-pet","emoji","native-aliases.json");
            var native=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,string>>(File.ReadAllText(nativeFile));
            check("所有原生名称都有独立可解码原图",native.Keys.All(name=>ReplyEmoji.NativeImageFor(name)!=null));
            check("未知原图名称不越过资源目录",ReplyEmoji.NativeImageFor("../2_05")==null);
            RenderPreview(outputDirectory);
        }
        static void RenderPreview(string directory) {
            var panel=new StackPanel{Margin=new Thickness(20)};
            panel.Children.Add(new TextBlock{Text="表情推荐",FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=20,Margin=new Thickness(0,0,0,14),Foreground=SettingsTheme.Ink});
            foreach(string raw in new[]{"好，那就这么定。","这都被你发现了[捂脸]","这次稳了[机智]","[笑脸][微笑][破涕为笑][合十]","[色][调皮][抱拳]","通用 emoji：你好😜 [未知表情]"}) {
                var message=new TextBlock{FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=14,TextWrapping=TextWrapping.Wrap,Foreground=SettingsTheme.Ink};
                ReplyEmoji.Set(message,raw);
                var content=new StackPanel();content.Children.Add(message);content.Children.Add(new TextBlock{Text="Tab 填入",FontSize=10,Foreground=SettingsTheme.Muted,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)});
                panel.Children.Add(new Border{Child=content,Background=Brushes.White,BorderBrush=SettingsTheme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10)});
            }
            var surface=new Border{Width=440,Background=SettingsTheme.Paper,Child=panel};
            surface.Measure(new Size(440,Double.PositiveInfinity));surface.Arrange(new Rect(new Point(),surface.DesiredSize));surface.UpdateLayout();
            var bitmap=new RenderTargetBitmap(660,(int)Math.Ceiling(surface.ActualHeight*1.5),144,144,PixelFormats.Pbgra32);bitmap.Render(surface);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var output=File.Create(Path.Combine(directory,"emoji-reply-preview.png")))encoder.Save(output);
        }
    }
}
