using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WeChatPet.Runtime {
    // Display only. The original recommendation, including line breaks and
    // emoji names, never passes through trimming or scrolling on its way to Tab.
    internal sealed class PetReplyBubble : Border {
        internal readonly TextBlock Body,Hint;
        internal readonly Button Toggle;
        internal readonly ScrollViewer Scroll;
        readonly Grid footer;
        bool recommendation;
        string rawText="";
        internal string RawText {get{return rawText;}}
        internal bool FullPreviewRequired {get;private set;}
        internal bool Expanded {get;private set;}
        internal event Action LayoutChanged;

        internal PetReplyBubble() {
            Width=280;Visibility=Visibility.Collapsed;HorizontalAlignment=HorizontalAlignment.Center;
            Background=Brushes.White;BorderBrush=SettingsTheme.Brush("#E4E9E7");BorderThickness=new Thickness(1);
            CornerRadius=new CornerRadius(13);Padding=new Thickness(13,10,13,9);Margin=new Thickness(0,0,0,5);
            var words=new StackPanel();Child=words;
            Body=new TextBlock{FontSize=13,Foreground=SettingsTheme.Brush("#24352E"),TextWrapping=TextWrapping.Wrap,MaxHeight=88,TextTrimming=TextTrimming.CharacterEllipsis,LineHeight=21};
            Scroll=new ScrollViewer{Content=Body,MaxHeight=88,Padding=new Thickness(0),BorderThickness=new Thickness(0),VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false,Focusable=false,IsTabStop=false};
            ReplyPreviewStyle.Apply(Scroll);
            words.Children.Add(Scroll);
            footer=new Grid{Margin=new Thickness(0,5,0,0)};
            footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            Toggle=SettingsTheme.Button("查看全文","查看推荐全文");Toggle.FontSize=10;Toggle.Height=21;Toggle.Padding=new Thickness(8,0,8,0);
            Toggle.HorizontalAlignment=HorizontalAlignment.Left;Toggle.Background=SettingsTheme.Brush("#EFF3F0");Toggle.Foreground=SettingsTheme.Accent;
            Toggle.Focusable=false;Toggle.IsTabStop=false;Toggle.Visibility=Visibility.Collapsed;
            Toggle.Click+=delegate{SetExpanded(!Expanded);};footer.Children.Add(Toggle);
            Hint=new TextBlock{Text="Tab 填入",FontSize=9,Foreground=SettingsTheme.Muted,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(Hint,1);footer.Children.Add(Hint);words.Children.Add(footer);
        }
        void RefreshFooter(){Hint.Visibility=recommendation?Visibility.Visible:Visibility.Collapsed;footer.Visibility=recommendation?Visibility.Visible:Visibility.Collapsed;}
        internal void SetReply(string text,bool isRecommendation) {
            text=text??"";
            if(rawText!=text||recommendation!=isRecommendation){Expanded=false;Scroll.ScrollToTop();}
            rawText=text;recommendation=isRecommendation;
            ReplyEmoji.Set(Body,text);
            RefreshFooter();
        }
        internal void SetExpanded(bool value) {
            value=value&&FullPreviewRequired&&recommendation;
            if(Expanded==value)return;
            Expanded=value;Scroll.ScrollToTop();
            if(LayoutChanged!=null)LayoutChanged();
        }
        double ChromeHeight(double width) {
            footer.Measure(new Size(width,Double.PositiveInfinity));
            return Padding.Top+Padding.Bottom+BorderThickness.Top+BorderThickness.Bottom+Margin.Top+Margin.Bottom+footer.DesiredSize.Height;
        }
        internal void PrepareLayout(double availableBubbleHeight) {
            double width=Math.Max(1,Width-Padding.Left-Padding.Right-BorderThickness.Left-BorderThickness.Right);
            // Measure the actual runs/images at the collapsed width, without a
            // height limit or scrollbar. A line break alone is not overflow.
            Body.MaxHeight=Double.PositiveInfinity;Body.TextTrimming=TextTrimming.None;
            Body.InvalidateMeasure();Body.Measure(new Size(width,Double.PositiveInfinity));
            double fullHeight=Body.DesiredSize.Height;
            Toggle.Visibility=Visibility.Collapsed;
            double capacity=Math.Max(1,Math.Min(88,availableBubbleHeight-ChromeHeight(width)));
            FullPreviewRequired=recommendation&&fullHeight>capacity+.01;
            if(!FullPreviewRequired)Expanded=false;
            Toggle.Visibility=FullPreviewRequired?Visibility.Visible:Visibility.Collapsed;
            Toggle.Content=Expanded?"收起全文":"查看全文";
            AutomationProperties.SetName(Toggle,Expanded?"收起推荐全文":"查看推荐全文");
            double viewport=Math.Max(1,Math.Min(Expanded?240:88,availableBubbleHeight-ChromeHeight(width)));
            Body.MaxHeight=Expanded?Double.PositiveInfinity:viewport;
            Body.TextTrimming=Expanded?TextTrimming.None:TextTrimming.CharacterEllipsis;
            // A ScrollViewer's template can still report its previous desired
            // height during the same update that replaces inline content. Give
            // the viewport its measured height before the owner sizes itself.
            Scroll.Height=Math.Min(fullHeight,viewport);
            Scroll.MaxHeight=viewport;
            Scroll.VerticalScrollBarVisibility=Expanded?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
            Body.InvalidateMeasure();Scroll.InvalidateMeasure();Child.InvalidateMeasure();InvalidateMeasure();
        }
    }
}
