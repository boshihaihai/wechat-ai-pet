using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WeChatPet.Runtime {
    // Synthetic WPF layout only: never show either window, read WeChat or write a draft.
    internal static class InlineOverflowTests {
        internal static void Run(Action<string,bool> check) {
            var window=new InlineSuggestionWindow(()=>false,()=>null,()=>null);
            try {
                var panel=(Grid)window.Content;
                var rendered=(TextBlock)panel.Children[0];
                check("短回复完整显示时隐藏全文入口",!window.LayoutReply("好啦，不笑了 [调皮]",500,1,true,1)&&panel.ColumnDefinitions[1].Width.Value==0);
                string longReply=String.Concat(Enumerable.Repeat("那就按刚才商量的安排，晚点再确认一下。",12));
                var reply=new Recommendation{Text=longReply};
                check("长回复省略时保留全文入口",window.LayoutReply(reply.Text,500,1,true,1)&&panel.ColumnDefinitions[1].Width.Value==72);
                check("省略仅影响显示而不截断原文",reply.Text==longReply&&AutomationProperties.GetName(rendered)==longReply);
                check("缩窄窗口恢复全文入口",!window.LayoutReply("明天 10:30 在 A 楼见，带上 USB-C 线。",800,1,true,1)&&window.LayoutReply("明天 10:30 在 A 楼见，带上 USB-C 线。",180,1,false,1));
                window.FullPreview.SetExpanded(true); // No recommendation ID: no HWND is created.
                check("放宽后关闭已展开全文并释放按钮空间",!window.LayoutReply("明天 10:30 在 A 楼见，带上 USB-C 线。",800,1,true,1)&&!window.FullPreview.Expanded&&panel.ColumnDefinitions[1].Width.Value==0);

                // Equal character counts have different actual glyph advances.
                var narrow=Text("iiiiiiii",14);var wide=Text("WWWWWWWW",14);
                double narrowWidth=Width(narrow),wideWidth=Width(wide),middle=(narrowWidth+wideWidth)/2;
                check("采用字体实际宽度而非字符数判断",wideWidth>narrowWidth&&!InlineSuggestionWindow.NeedsFullPreview(narrow,"iiiiiiii",middle,1)&&InlineSuggestionWindow.NeedsFullPreview(wide,"WWWWWWWW",middle,1));

                var emoji=Text("[捂脸]",14);
                double emojiWidth=Width(emoji),nameWidth=Width(new TextBlock{Text="[捂脸]",FontFamily=emoji.FontFamily,FontSize=14});
                check("表情按实际图片宽度测量",emoji.Inlines.OfType<InlineUIContainer>().Count()==1&&emojiWidth<nameWidth&&!InlineSuggestionWindow.NeedsFullPreview(emoji,"[捂脸]",Math.Ceiling(emojiWidth)+1,1));
                check("Unicode表情和中英混排可完整显示",!window.LayoutReply("OK，明天见 😜[笑脸]",420,1,true,1)&&rendered.Inlines.OfType<InlineUIContainer>().Count()==2);
                check("未知表情名称按原文字宽度测量",window.LayoutReply("[一个尚未支持的表情名字]",100,1,false,1));

                foreach(string newline in new[]{"\n","\r\n","\u2028","\u2029"})
                    check("多行回复保留全文入口 "+Escape(newline),window.LayoutReply("第一段"+newline+"第二段",800,1,true,1));
                window.LayoutReply("第一段 第二段",800,1,true,1);
                window.LayoutReply("第一段\n第二段",800,1,true,1);
                check("视觉相同时仍保留原文换行",AutomationProperties.GetName(rendered)=="第一段\n第二段");

                // The reply fits only after removing the full-text button. It must
                // not retain that button because the previous frame had one.
                const string edge="现在这样就可以，晚点再说。";
                double fitted=Width(Text(edge,14))+102;
                window.LayoutReply(longReply,fitted,1,true,1);
                bool stable=true;
                for(int i=0;i<8;i++) {
                    stable&=!window.LayoutReply(edge,fitted,1,true,1);
                    // Exercise the real finite-width layout between refreshes;
                    // a prior trimmed measurement must not contaminate the next one.
                    panel.Measure(new Size(fitted,28));panel.Arrange(new Rect(0,0,fitted,28));
                    stable&=rendered.ActualWidth>=Width(Text(edge,14));
                }
                check("临界宽度不预留全文按钮且反复布局不闪动",stable&&panel.ColumnDefinitions[1].Width.Value==0);
                check("越过实际宽度边界才显示全文入口",window.LayoutReply(edge,fitted-4,1,true,1)&&!window.LayoutReply(edge,fitted,1,true,1));

                check("隐藏Tab提示时回收其空间",window.LayoutReply(edge,Width(Text(edge,14))+2,1,true,1)&&!window.LayoutReply(edge,Width(Text(edge,14))+2,1,false,1));
                check("150%缩放下中英表情宽度判断正确",!window.LayoutReply("明天 10:30 见 [调皮]",750,1.5,true,1)&&window.LayoutReply(longReply,750,1.5,true,1));
                check("跨DPI相同比例下完整短句保持隐藏",!window.LayoutReply("收到，谢谢 [微笑]",500,1,true,1.5)&&!window.LayoutReply("收到，谢谢 [微笑]",500,1,true,2));
                check("灰字继续禁止激活并支持点击穿透",!window.ShowActivated&&!window.Focusable&&!window.IsHitTestVisible&&!window.FullPreview.ShowActivated&&!window.FullPreview.Focusable);
                check("离线布局不创建可见窗口",!window.IsVisible&&!window.FullPreview.IsVisible);
            } finally {window.Stop();}
        }
        static TextBlock Text(string raw,double fontSize) {
            var text=new TextBlock{FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=fontSize,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
            ReplyEmoji.Set(text,raw,true);return text;
        }
        static double Width(TextBlock text){text.Measure(new Size(Double.PositiveInfinity,Double.PositiveInfinity));return text.DesiredSize.Width;}
        static string Escape(string value){return String.Join("+",value.Select(c=>"U+"+((int)c).ToString("X4")));}
    }
}
