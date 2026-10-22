using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WeChatPet;
using WeChatPet.Runtime;

// Offline WPF layout and recommendation contracts only. No window is shown,
// no observer is started, and no keyboard, clipboard or WeChat API is called.
internal static class PetFullReplyTests {
    static readonly List<object> checks=new List<object>();
    static void Check(string name,bool value) {checks.Add(new{name=name,passed=value});if(!value)throw new Exception(name);}
    static string LongReply() {return String.Join("\n",Enumerable.Range(1,24).Select(i=>"第"+i+"段：先按刚才讨论的安排处理，细节我们晚点确认。[调皮]"))+"\n全文最后一句不能丢失。";}
    static void Layout(FrameworkElement element,double width) {
        element.Measure(new Size(width,Double.PositiveInfinity));
        element.Arrange(new Rect(0,0,width,element.DesiredSize.Height));
        element.UpdateLayout();
    }
    static void Pump() {
        var frame=new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{frame.Continue=false;}));
        Dispatcher.PushFrame(frame);
    }
    static TextBlock FullBody(PetReplyBubble bubble) {
        var body=bubble.Scroll.Content as TextBlock;
        if(body==null)throw new Exception("Full-reply scroll content must expose the complete rendered TextBlock");
        return body;
    }
    static PetReplyBubble Bubble() {
        var bubble=new PetReplyBubble{Visibility=Visibility.Visible};
        TextElement.SetFontFamily(bubble,new FontFamily("Microsoft YaHei UI"));
        return bubble;
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {
        var match=root as T;if(match!=null)yield return match;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    static void ScrollStyleChecks(PetReplyBubble bubble) {
        var bar=Descendants<ScrollBar>(bubble.Scroll).FirstOrDefault(x=>x.Orientation==Orientation.Vertical&&x.Visibility==Visibility.Visible);
        Check("展开后的真实纵向滚动条保持细窄",bar!=null&&Math.Abs(bar.ActualWidth-9)<.1);
        var track=Descendants<Track>(bar).Single();
        var buttons=Descendants<RepeatButton>(bar).ToList();
        var up=buttons.SingleOrDefault(b=>b.Command==ScrollBar.LineUpCommand);
        var down=buttons.SingleOrDefault(b=>b.Command==ScrollBar.LineDownCommand);
        Check("细滚动条保留两个透明翻页区域和两个小箭头",buttons.Count==4&&buttons.Contains(track.DecreaseRepeatButton)&&buttons.Contains(track.IncreaseRepeatButton)&&up!=null&&down!=null&&Math.Abs(up.ActualHeight-10)<.1&&Math.Abs(down.ActualHeight-10)<.1);
        Check("细滚动条与滑块不抢键盘焦点",!bar.Focusable&&!bar.IsTabStop&&!track.Thumb.Focusable&&buttons.All(b=>!b.Focusable&&!b.IsTabStop));
        bubble.Scroll.ScrollToVerticalOffset(60);Pump();Layout(bubble,280);
        double beforeArrow=bubble.Scroll.VerticalOffset;
        var downCommand=down.Command as RoutedCommand;
        bool downEnabled=downCommand!=null&&downCommand.CanExecute(down.CommandParameter,down);
        if(downEnabled)downCommand.Execute(down.CommandParameter,down);
        Pump();Layout(bubble,280);
        double afterDown=bubble.Scroll.VerticalOffset;
        Check("下箭头按行滚动并同步滑块值",downEnabled&&afterDown>beforeArrow&&afterDown-beforeArrow<bubble.Scroll.ViewportHeight&&Math.Abs(bar.Value-afterDown)<1);
        var upCommand=up.Command as RoutedCommand;
        bool upEnabled=upCommand!=null&&upCommand.CanExecute(up.CommandParameter,up);
        if(upEnabled)upCommand.Execute(up.CommandParameter,up);
        Pump();Layout(bubble,280);
        Check("上箭头反向按行滚动并同步滑块值",upEnabled&&bubble.Scroll.VerticalOffset<afterDown&&Math.Abs(bubble.Scroll.VerticalOffset-beforeArrow)<1&&Math.Abs(bar.Value-bubble.Scroll.VerticalOffset)<1);
        bubble.Scroll.ScrollToTop();Pump();Layout(bubble,280);
        var page=track.IncreaseRepeatButton;var command=page.Command as RoutedCommand;
        bool enabled=command!=null&&command.CanExecute(page.CommandParameter,page);
        if(enabled)command.Execute(page.CommandParameter,page);
        Pump();Layout(bubble,280);
        Check("透明轨道的翻页命令仍滚动正文",enabled&&bubble.Scroll.VerticalOffset>0);
        Check("翻页后滑块值与正文位置同步",Math.Abs(bar.Value-bubble.Scroll.VerticalOffset)<1);
        bubble.Scroll.ScrollToTop();Pump();Layout(bubble,280);
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0,0));
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(0,30));
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(0,30,false));
        Pump();Layout(bubble,280);
        Check("合成滑块拖动能更新正文位置",bubble.Scroll.VerticalOffset>0&&Math.Abs(bar.Value-bubble.Scroll.VerticalOffset)<1);
        bubble.Scroll.ScrollToEnd();Pump();Layout(bubble,280);
        Check("正文滚动到末尾后细滑块同步到最大值",Math.Abs(bar.Maximum-bar.Value)<1&&Math.Abs(bar.Value-bubble.Scroll.VerticalOffset)<1);
    }
    static void ComponentChecks() {
        var bubble=Bubble();
        foreach(string shortReply in new[]{"收到，晚点聊。","第一段\n第二段","第一行\n第二行\n第三行\n第四行"}) {
            bubble.SetReply(shortReply,true);bubble.PrepareLayout(600);Layout(bubble,280);
            Check("完整多行不增加全文入口："+shortReply.Replace('\n','/'),!bubble.FullPreviewRequired&&bubble.Toggle.Visibility==Visibility.Collapsed&&!bubble.Expanded);
        }
        string fiveLines="第一行\n第二行\n第三行\n第四行\n第五行";
        bubble.SetReply(fiveLines,true);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("超过折叠高度的第五行出现全文入口",bubble.FullPreviewRequired&&bubble.Toggle.Visibility==Visibility.Visible);
        bubble.SetReply(new string('i',120),true);bubble.PrepareLayout(600);Layout(bubble,280);
        bool narrowFits=!bubble.FullPreviewRequired;
        bubble.SetReply(new string('W',120),true);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("相同字符数按实际字形排版判断",narrowFits&&bubble.FullPreviewRequired);
        bubble.SetReply("[色][调皮][偷笑]",true);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("三个原生表情用图形显示且不虚构省略",bubble.Body.Inlines.OfType<InlineUIContainer>().Count()==3&&!bubble.FullPreviewRequired&&bubble.Toggle.Visibility==Visibility.Collapsed);
        string longReply=LongReply();
        bubble.SetReply(longReply,true);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("长回复默认折叠且保留完整原文",bubble.FullPreviewRequired&&!bubble.Expanded&&bubble.RawText==longReply&&AutomationProperties.GetName(bubble.Body)==longReply);
        bubble.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));bubble.PrepareLayout(600);Layout(bubble,280);
        Check("点击查看全文展开不改变正文",bubble.Expanded&&bubble.RawText==longReply&&AutomationProperties.GetName(FullBody(bubble))==longReply);
        Check("展开正文不裁切或省略并保留换行",FullBody(bubble).TextTrimming==TextTrimming.None&&FullBody(bubble).TextWrapping==TextWrapping.Wrap&&bubble.Scroll.Visibility==Visibility.Visible);
        Check("全文超过视口可滚动",bubble.Scroll.ExtentHeight>bubble.Scroll.ViewportHeight+20&&bubble.Scroll.ViewportHeight>40);
        ScrollStyleChecks(bubble);
        bubble.Scroll.ScrollToEnd();Pump();Layout(bubble,280);
        Check("滚动能到达最后一段",bubble.Scroll.VerticalOffset>0&&Math.Abs(bubble.Scroll.VerticalOffset-bubble.Scroll.ScrollableHeight)<1);
        double oldOffset=bubble.Scroll.VerticalOffset;
        bubble.SetReply(longReply,true);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("同一推荐刷新保留展开与滚动位置",bubble.Expanded&&Math.Abs(bubble.Scroll.VerticalOffset-oldOffset)<1);
        bubble.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));bubble.PrepareLayout(600);Layout(bubble,280);
        Check("收起全文恢复折叠并保留查看入口",!bubble.Expanded&&bubble.FullPreviewRequired&&bubble.Toggle.Visibility==Visibility.Visible&&bubble.RawText==longReply);
        bubble.SetExpanded(true);bubble.PrepareLayout(600);Layout(bubble,280);
        bubble.SetReply(longReply+"这是下一条建议。",true);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("换成另一条长回复默认收起",!bubble.Expanded&&bubble.RawText.EndsWith("这是下一条建议。",StringComparison.Ordinal));
        bubble.SetExpanded(true);bubble.PrepareLayout(170);Layout(bubble,280);
        Check("矮工作区限制全文视口而非整个窗口越界",bubble.DesiredSize.Height<=170.1&&bubble.Scroll.ViewportHeight>20&&bubble.Scroll.ExtentHeight>bubble.Scroll.ViewportHeight);
        bubble.SetReply("好。",true);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("长回复变短立即去掉入口并收起全文",!bubble.FullPreviewRequired&&!bubble.Expanded&&bubble.Toggle.Visibility==Visibility.Collapsed&&bubble.RawText=="好。");
        bubble.SetReply(longReply,true);bubble.SetExpanded(true);bubble.SetReply("读取暂不可用",false);bubble.PrepareLayout(600);Layout(bubble,280);
        Check("普通状态提示不伪装成可采用的长回复",!bubble.Expanded&&!bubble.FullPreviewRequired&&bubble.Toggle.Visibility==Visibility.Collapsed&&bubble.Hint.Visibility==Visibility.Collapsed);
        Check("全文按钮与滚动区不抢键盘焦点",!bubble.Toggle.Focusable&&!bubble.Toggle.IsTabStop&&!bubble.Scroll.Focusable);
    }
    static void RenderPet(PetWindow pet,string file,double dpi=96) {
        var element=(FrameworkElement)pet.Content;
        element.Measure(new Size(pet.Width,pet.Height));element.Arrange(new Rect(0,0,pet.Width,pet.Height));element.UpdateLayout();
        if(file==null)return;
        double ratio=dpi/96;
        var image=new RenderTargetBitmap((int)Math.Ceiling(pet.Width*ratio),(int)Math.Ceiling(pet.Height*ratio),dpi,dpi,PixelFormats.Pbgra32);
        image.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using(var stream=File.Create(file))encoder.Save(stream);
    }
    static void PetChecks(string assets,string settings,string output) {
        var pet=new PetWindow(assets,settings);
        try {
            pet.SetMotion(false);pet.SelectSkin("panda");
            pet.WorkAreaForPoint=delegate(Point p){return new Rect(0,0,1200,900);};pet.Left=400;pet.Top=550;
            pet.Present(PetState.Ready,"好，就按这个安排。[调皮]",true);
            RenderPet(pet,Path.Combine(output,"pet-full-reply-short.png"),144);
            Check("正式气泡的短回复保持简洁",pet.IsBubbleVisible&&!pet.ReplyBubble.FullPreviewRequired&&!pet.ReplyBubble.Expanded);
            string raw=LongReply();pet.Present(PetState.Ready,raw,true);
            RenderPet(pet,Path.Combine(output,"pet-full-reply-collapsed.png"),144);
            Check("折叠长气泡布局后窗口容纳完整宠物：窗口="+pet.Height+"，气泡="+pet.ReplyBubble.DesiredSize.Height,Math.Abs(pet.Height-(pet.ReplyBubble.DesiredSize.Height+pet.AnimationVisual.Height+8))<.1);
            double center=pet.PetCenterX,bottom=pet.PetBottomY,collapsedHeight=pet.Height;
            pet.ReplyBubble.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RenderPet(pet,Path.Combine(output,"pet-full-reply-expanded.png"),144);
            Check("展开长气泡布局后窗口容纳完整宠物",Math.Abs(pet.Height-(pet.ReplyBubble.DesiredSize.Height+pet.AnimationVisual.Height+8))<.1);
            Check("正式气泡点击展开并重新计算窗口高度",pet.ReplyBubble.Expanded&&pet.Height>collapsedHeight+20);
            Check("有空间时展开保持宠物本体位置",Math.Abs(pet.PetCenterX-center)<.1&&Math.Abs(pet.PetBottomY-bottom)<.1);
            Check("展开后原始推荐和表情标记完整保留",pet.CurrentMessage==raw&&pet.ReplyBubble.RawText==raw&&AutomationProperties.GetName(FullBody(pet.ReplyBubble))==raw);
            pet.ReplyBubble.Scroll.ScrollToEnd();Pump();RenderPet(pet,null);double position=pet.ReplyBubble.Scroll.VerticalOffset;
            pet.Present(PetState.Ready,raw,true);pet.SetListening(true);RenderPet(pet,null);
            Check("真实状态刷新不打断全文阅读",pet.ReplyBubble.Expanded&&Math.Abs(position-pet.ReplyBubble.Scroll.VerticalOffset)<1);
            pet.ReplyBubble.SetExpanded(false);RenderPet(pet,null);
            Check("正式气泡收起后立即恢复正确窗口高度",!pet.ReplyBubble.Expanded&&Math.Abs(pet.Height-collapsedHeight)<.1&&Math.Abs(pet.Height-(pet.ReplyBubble.DesiredSize.Height+pet.AnimationVisual.Height+8))<.1);
            pet.Present(PetState.Ready,"好。",true);RenderPet(pet,null);
            Check("正式气泡长变短不保留空白或多余按钮",!pet.ReplyBubble.FullPreviewRequired&&pet.Height<collapsedHeight-20&&Math.Abs(pet.Height-(pet.ReplyBubble.DesiredSize.Height+pet.AnimationVisual.Height+8))<.1);
            pet.Present(PetState.Ready,raw,true);RenderPet(pet,null);
            Check("正式气泡短变长再次扩高且默认折叠",pet.ReplyBubble.FullPreviewRequired&&!pet.ReplyBubble.Expanded&&Math.Abs(pet.Height-collapsedHeight)<.1);
            foreach(var work in new[]{new Rect(0,0,900,460),new Rect(-1000,-100,1000,520),new Rect(2000,100,1200,600)}) {
                pet.WorkAreaForPoint=delegate(Point p){return work;};pet.Left=work.Left+300;pet.Top=work.Top;
                pet.SetSize(1.25);pet.ReplyBubble.SetExpanded(true);RenderPet(pet,null);
                Check("顶部展开及较大宠物仍处于工作区 "+work.Left,pet.Top>=work.Top-.1&&pet.Top+pet.Height<=work.Bottom+.1&&pet.Left>=work.Left-.1&&pet.Left+pet.Width<=work.Right+.1);
                Check("受限工作区仍能滚动全文 "+work.Left,pet.ReplyBubble.Scroll.ViewportHeight>20&&pet.ReplyBubble.Scroll.ExtentHeight>pet.ReplyBubble.Scroll.ViewportHeight);
            }
            pet.Present(PetState.Thinking,"正在生成推荐",true);RenderPet(pet,null);
            Check("撤销推荐立即收起全文和旧正文",!pet.IsBubbleVisible&&!pet.ReplyBubble.Expanded&&pet.ReplyBubble.RawText=="");
            pet.Present(PetState.Ready,raw,true);pet.ReplyBubble.SetExpanded(true);RenderPet(pet,null);
            pet.ShowRecommendationNotice("输入状态暂未确认");RenderPet(pet,null);
            Check("状态通知覆盖时收起全文且不显示Tab",!pet.ReplyBubble.Expanded&&pet.ReplyBubble.Hint.Visibility==Visibility.Collapsed&&pet.CurrentMessage==raw);
            pet.Present(PetState.Idle,"等待",true);pet.ShowRecommendationNotice(null);pet.Present(PetState.Ready,raw,true);pet.ReplyBubble.SetExpanded(true);RenderPet(pet,null);
            pet.HideToTray();
            Check("隐藏宠物关闭可见气泡的全文状态",pet.IsInTray&&!pet.ReplyBubble.Expanded&&!pet.IsVisible);
            Check("离线测试没有显示顶层窗口或激活桌面",!pet.IsVisible&&!pet.ShowActivated&&!pet.Focusable);
        }finally{pet.CloseForExit();}
    }
    static ChatSnapshot Snapshot(string tail) {
        string[] keys=tail=="old"?new[]{"old"}:new[]{"old",tail};
        return new ChatSnapshot{Available=true,AtEnd=true,DraftEmpty=true,DraftConsistent=true,Session="synthetic-session",InputIdentity="synthetic-input",Tail=tail,Keys=keys,Messages=keys.Select(k=>new ChatMessage{Key=k,Role="peer",Text="合成测试来信",Type=1,LocalId=1}).ToList()};
    }
    static void RecommendationChecks() {
        var controller=new RecommendationController{Connected=true};controller.Observe(Snapshot("old"),0);
        controller.Observe(Snapshot("new"),TimeSpan.TicksPerSecond);controller.Observe(Snapshot("new"),TimeSpan.TicksPerMillisecond*1610);
        string raw=LongReply();Check("合成长推荐进入控制器",controller.Complete(controller.PendingId,raw,TimeSpan.TicksPerMillisecond*1700));
        var bubble=Bubble();bubble.SetReply(controller.Ready.Text,true);bubble.PrepareLayout(500);Layout(bubble,280);bubble.SetExpanded(true);Layout(bubble,280);bubble.SetExpanded(false);
        var serializer=new JavaScriptSerializer();var message=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(new{type="arm",recommendation=controller.Ready}));
        var armed=serializer.ConvertToType<Recommendation>(message["recommendation"]);
        Check("查看与收起后Tab采用协议仍包含完整原文",Recommendation.ValidText(armed.Text)&&armed.Text==raw&&armed.Text.EndsWith("全文最后一句不能丢失。",StringComparison.Ordinal));
    }
    [STAThread] public static int Main(string[] args) {
        string executable=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\','/');
        string assets=args.Length>0?Path.GetFullPath(args[0]):Path.GetFullPath(Path.Combine(executable,".."));
        string output=Path.Combine(assets,"tests");
        string isolated=Path.Combine(output,"pet-full-reply-settings-"+Guid.NewGuid().ToString("N")+".json");
        try {
            Directory.CreateDirectory(output);ComponentChecks();PetChecks(assets,isolated,output);RecommendationChecks();
            Console.WriteLine(new JavaScriptSerializer().Serialize(new{passed=checks.Count,checks=checks,visibleWindows=0,wechatInteractions=0,ocr=false,messagesSent=0}));return 0;
        }catch(Exception e){Console.WriteLine(new JavaScriptSerializer().Serialize(new{passed=checks.Count-1,checks=checks,error=e.ToString()}));return 1;}
        finally{if(File.Exists(isolated))File.Delete(isolated);}
    }
}

