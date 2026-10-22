using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace WeChatPet.Runtime {
 public static class RuntimeTests {
  static int count;static List<object> checks=new List<object>();
  static void Check(string name,bool ok){count++;checks.Add(new{name=name,passed=ok});if(!ok)throw new Exception(name);}
  static ChatSnapshot S(string tail,string session="A",string role="peer",uint type=1) {
   var keys=new List<string>{"old"};if(tail=="new2")keys.Add("new1");if(tail=="n2")keys.Add("n1");if(tail!="old")keys.Add(tail);
   return new ChatSnapshot{Available=true,AtEnd=true,DraftEmpty=true,DraftConsistent=true,Session=session,Tail=tail,Keys=keys.ToArray(),Messages=keys.Select(k=>new ChatMessage{Key=k,Role=role,Type=type,LocalId=1,Text="相同文字"}).ToList()};
  }
  static RecommendationController Controller(){return new RecommendationController{Connected=true};}
  static long T(int ms){return TimeSpan.TicksPerMillisecond*ms;}
  static string Trigger(RecommendationController c,string key,int time){c.Observe(S(key),T(time));c.Observe(S(key),T(time+610));return c.PendingId;}
  static void HookContentionChecks() {
   object gate=typeof(ObserverProgram).GetField("sync",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()) {
    ObserverProgram.ProcessTabState(false,null);
    ObserverProgram.ProcessTabState(true,delegate{return true;});
    var slowWriter=new Thread(delegate(){lock(gate){entered.Set();release.Wait(2500);}}){IsBackground=true};
    slowWriter.Start();
    try {
     if(!entered.Wait(2000))throw new Exception("慢写入锁竞争测试未启动");
     var clock=Stopwatch.StartNew();bool acceptedKeyUp=ObserverProgram.ProcessTabState(false,null);long keyUpMs=clock.ElapsedMilliseconds;
     int accepts=0,invalidations=0;Func<bool> accept=delegate{accepts++;return true;};
     bool down=ObserverProgram.ProcessTabState(true,accept),repeat=ObserverProgram.ProcessTabState(true,accept),up=ObserverProgram.ProcessTabState(false,null);
     bool invalidated=ObserverProgram.TryHookState(delegate{invalidations++;return true;});
     Check("慢写入期间已接管Tab的抬键不等待锁",acceptedKeyUp&&keyUpMs<250);
     Check("慢写入锁竞争时新Tab按下重复和抬起全部放行",!down&&!repeat&&!up&&accepts==0&&clock.ElapsedMilliseconds<250);
     Check("慢写入期间其它输入活动不等待共享锁",!invalidated&&invalidations==0);
    }finally {release.Set();slowWriter.Join(2000);ObserverProgram.ProcessTabState(false,null);}
   }
   int queued=0;Func<bool> queue=delegate{queued++;return true;};
   bool accepted=ObserverProgram.ProcessTabState(true,queue),repeated=ObserverProgram.ProcessTabState(true,queue),released=ObserverProgram.ProcessTabState(false,null),extraRelease=ObserverProgram.ProcessTabState(false,null);
   Check("锁释放后Tab恢复一次填入且按下抬起配对",accepted&&repeated&&released&&!extraRelease&&queued==1);
  }
  public static int Main() {
   try{
    Check("观察器仅接受推荐准备撤销和停止命令",ObserverProgram.SupportedCommand("arm")&&ObserverProgram.SupportedCommand("invalidate")&&ObserverProgram.SupportedCommand("stop"));
    Check("旧自动发送和未知命令不能进入执行路径",!ObserverProgram.SupportedCommand("auto_grant")&&!ObserverProgram.SupportedCommand("auto_stop")&&!ObserverProgram.SupportedCommand("send")&&!ObserverProgram.SupportedCommand(null));
    Check("发布程序集不再包含自动发送会话或点击通道",typeof(ObserverProgram).Assembly.GetType("WeChatPet.Runtime.AutoReplySession")==null&&typeof(ObserverProgram).Assembly.GetType("WeChatPet.Runtime.AutoReplySendClick")==null&&typeof(ObserverProgram).Assembly.GetType("WeChatPet.Runtime.AutoReplyWindow")==null);
    Check("运行入口不再提供自动发送授权且不读取发送按钮",typeof(DesktopRuntime).GetMethod("StartAutoReply")==null&&typeof(WeChatReader).GetProperty("SendButton",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)==null);
    Check("单纯移动鼠标不会取消手动填入",!ObserverProgram.IsPointerTakeover(0x200));
    foreach(uint message in new uint[]{0x201,0x204,0x207,0x20b,0x20a,0x20e})Check("鼠标点击与滚轮仍保护手动填入 "+message,ObserverProgram.IsPointerTakeover(message));
    MemoryTests.Run(Check);
    MemoryAccountTests.Run(Check);
    GroupMemoryTests.Run(Check);
    GroupMemoryRequestTests.Run(Check);
    FriendPreferenceTests.Run(Check);
    FriendProfileTests.Run(Check);
    AutomaticPreferenceTests.Run(Check);
    ExpressionExampleTests.Run(Check);
    MemoryUpdateTests.Run(Check);
    MemoryUnicodeTests.Run(Check);
    ModelRecoveryTests.Run(Check);
    MemoryRecoveryTests.Run(Check);
    HookContentionChecks();
    NativeEmojiDraftTests.Run(Check);
    NativeEmojiClipboardTests.Run(Check);
    NativeEmojiPasteShortcutTests.Run(Check);
    TabFillActivityTests.Run(Check);
    DiagnosticsWriteStatusTests.Run(Check);
    FillObservationPolicyTests.Run(Check);
    FillNoticePolicyTests.Run(Check);
    NativeEmojiPopupCompletionTests.Run(Check);
    QtCompositionPasteMenuTests.Run(Check);
    EmptyChatTests.Run(Check);
    TargetIdentityTests.Run(Check);
    ChatOpenTests.Run(Check);
    VisibleMessageTests.Run(Check);
    GroupChatReturnTests.Run(Check);
    GroupRecommendationTests.Run(Check);
    GroupReaderTests.Run(Check);
    PetClickIntentTests.Run(Check);
    ObserverFailureTests.Run(Check);
    GenerationRecoveryTests.Run(Check);
    var errorSnapshot=S("new");
    Check("同一来信的模型错误不会被正常快照覆盖",DesktopRuntime.KeepGenerationError("timeout","A","new",errorSnapshot));
    errorSnapshot.Session="B";Check("模型错误不会串到另一个好友",!DesktopRuntime.KeepGenerationError("timeout","A","new",errorSnapshot));errorSnapshot.Session="A";
    errorSnapshot.Tail="newer";Check("新来信清除上一条的模型错误",!DesktopRuntime.KeepGenerationError("timeout","A","new",errorSnapshot));errorSnapshot.Tail="new";
    errorSnapshot.DraftEmpty=false;Check("用户开始编辑后清除旧生成错误",!DesktopRuntime.KeepGenerationError("timeout","A","new",errorSnapshot));
    var c=Controller();c.Observe(S("old"),T(0));c.Observe(S("old"),T(2000));Check("启动时当前私聊已有对方文字触发打开推荐",c.TriggerCount==1&&c.LastTriggerOrigin=="chat_open");
    var id=Trigger(c,"new1",3000);Check("新增文字继续只触发一次",id!=null&&c.TriggerCount==2);
    c.Observe(S("new1"),T(5000));Check("重复采样不重复生成",c.TriggerCount==2);
    Check("正确结果可展示",c.Complete(id,"好的，晚点聊",T(5100))&&c.Ready!=null);
    var longController=Controller();longController.Observe(S("old"),0);var longId=Trigger(longController,"new",1000);
    string longReply=new string('长',3000)+"\n\n保留第二段。";
    Check("长回复完整进入推荐并保留段落",longController.Complete(longId,longReply,T(1700))&&longController.Ready.Text==longReply);
    var longJson=new JavaScriptSerializer().Serialize(new{type="arm",recommendation=longController.Ready});
    var longParsed=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(longJson);
    var longArmed=new JavaScriptSerializer().ConvertToType<Recommendation>(longParsed["recommendation"]);
    Check("观察器采用协议完整传输长回复",Recommendation.ValidText(longArmed.Text)&&longArmed.Text==longReply&&longJson.Length<262144);
    c.Observe(S("new1"),T(66000));Check("推荐60秒过期",c.Ready==null);
    id=Trigger(c,"new2",67000);Check("相同正文不同标识再次触发",c.TriggerCount==3&&id!=null);
    var draft=S("new2");draft.DraftEmpty=false;c.Observe(draft,T(68000));Check("草稿使迟到结果无效",!c.Complete(id,"不应填入",T(68100))&&c.Ready==null);
    c=Controller();c.Observe(S("old"),0);id=Trigger(c,"n1",1000);c.Observe(S("n2"),T(1700));Check("新消息撤销旧请求",c.PendingId==null&&!c.Complete(id,"旧结果",T(1800)));
    c.Observe(S("n2"),T(2400));Check("最新来信生成",c.PendingId!=null&&c.TriggerCount==2);
    c.Observe(S("n2","B"),T(2500));Check("切换会话撤销旧结果并等待稳定",c.PendingId==null&&c.Ready==null);c.Observe(S("n2","B"),T(3500));Check("切换后为新好友已有对方文字推荐",c.TriggerCount==3&&c.LastTriggerOrigin=="chat_open");
    c=Controller();c.Observe(S("old"),0);c.Observe(S("n1"),T(100));c.Observe(S("n2"),T(300));c.Observe(S("n2"),T(901));Check("连续来信600ms合并",c.TriggerCount==1);
    c.SetPaused(true);Check("暂停清除请求",c.PendingId==null);c.Observe(S("n3"),T(2000));c.SetPaused(false);c.Observe(S("n3"),T(3000));c.Observe(S("n3"),T(5000));Check("恢复不补发暂停时历史",c.TriggerCount==1);
    c=Controller();c.Observe(S("old"),0);var image=S("pic","A","peer",3);c.Observe(image,T(100));c.Observe(image,T(900));Check("图片不凭空生成",c.TriggerCount==0);
    c=Controller();c.Observe(S("old"),0);var self=S("self","A","self");c.Observe(self,T(100));c.Observe(self,T(900));Check("自己消息不触发",c.TriggerCount==0);
    c=Controller();c.Observe(S("old"),0);id=Trigger(c,"n",1000);var composing=S("n");composing.Composing=true;c.Observe(composing,T(2000));Check("组词撤销结果",!c.Complete(id,"旧推荐",T(2100)));
    c=Controller();c.Observe(S("old"),0);id=Trigger(c,"n",1000);var typing=S("n");typing.Activity=1;c.Observe(typing,T(2000));Check("输入活动使结果失效",!c.Complete(id,"旧推荐",T(2100)));
    c=Controller();c.Observe(S("old"),0);c.Observe(new ChatSnapshot(),T(100));c.Observe(S("new"),T(200));c.Observe(S("new"),T(2000));Check("读取恢复重新建基线",c.TriggerCount==0);
    c=Controller();c.Observe(S("old"),0);var scroll=S("new");scroll.AtEnd=false;c.Observe(scroll,T(100));c.Observe(S("new"),T(900));Check("历史滚动不触发",c.TriggerCount==0);
    c=Controller();c.Observe(S("old"),0);id=Trigger(c,"n",1000);c.Connected=false;Check("模型切换后结果无效",!c.Complete(id,"旧推荐",T(2000)));
    c=Controller();c.Observe(S("old"),0);id=Trigger(c,"n",1000);var changed=S("n");changed.ContentRevision="changed";c.Observe(changed,T(2000));Check("相同标识的内容变化撤销结果",!c.Complete(id,"旧内容推荐",T(2100)));
    c=Controller();c.Observe(S("old"),0);id=Trigger(c,"n",1000);changed=S("n");changed.InputIdentity="recreated";c.Observe(changed,T(2000));Check("输入控件重建撤销旧结果",!c.Complete(id,"旧输入框推荐",T(2100)));
    c=Controller();c.Observe(S("old"),T(10000));var history=S("late-history");c.Observe(history,T(11000));c.Observe(history,T(12000));Check("晚同步的旧时间消息不触发",c.TriggerCount==0);
    c=Controller();c.Observe(S("old"),0);var inserted=S("new");inserted.Keys=new[]{"unknown-history","old","new"};c.Observe(inserted,T(1000));c.Observe(inserted,T(2000));Check("历史插入不误判为纯追加",c.TriggerCount==0);
    string vault=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wechat-pet-vault-test-"+Guid.NewGuid()+".bin");
    c=Controller();c.Observe(S("old"),T(1000));c.Observe(new ChatSnapshot{Reason="controls_unavailable"},T(1200));c.Observe(S("new"),T(1400));c.Observe(S("new"),T(2100));Check("消息插入时短暂读取失败不吞掉新来信",c.TriggerCount==1);
    c=Controller();c.Observe(S("old"),T(1000));c.Observe(new ChatSnapshot{Reason="controls_unavailable"},T(2200));c.Observe(S("new"),T(2400));c.Observe(S("new"),T(3100));Check("读取中断超过1秒恢复仍建立历史基线",c.TriggerCount==0);
    c=Controller();c.Observe(S("old"),T(1000));c.Observe(new ChatSnapshot{Reason="input_activity"},T(1200));c.Observe(S("new"),T(1400));c.Observe(S("new"),T(2100));Check("输入期间短暂读取失效不吞掉之后的新来信",c.TriggerCount==1);
    c=Controller();c.Observe(S("old"),T(1000));c.Observe(new ChatSnapshot{Reason="wechat_hidden_or_minimized"},T(1200));c.Observe(S("new"),T(1400));c.Observe(S("new"),T(2100));Check("短时最小化恢复也不补触发历史",c.TriggerCount==0);
    c=Controller();c.Observe(S("old"),0);var delayed=S("n1");delayed.Messages.Last().Text=null;c.Observe(delayed,T(1000));c.Observe(S("n1"),T(1200));c.Observe(S("n1"),T(1750));
    Check("正文迟到后仍等待合并窗口",c.TriggerCount==0);
    c.Observe(S("n1"),T(1810));c.Observe(S("n1"),T(2100));Check("新来信标识先于正文时补齐后只触发一次",c.TriggerCount==1&&c.PendingId!=null);
    c=Controller();c.Observe(S("old"),0);c.Observe(delayed,T(1000));c.Observe(S("n1"),T(3101));c.Observe(S("n1"),T(4000));Check("正文等待超过两秒不补触发",c.TriggerCount==0&&c.LastDecision=="text_wait_expired");
    c=Controller();c.Observe(delayed,0);c.Observe(S("n1"),T(1000));c.Observe(S("n1"),T(2000));Check("首次打开聊天缺少正文补齐后生成打开推荐",c.TriggerCount==1&&c.LastTriggerOrigin=="chat_open");
    foreach(string protection in new[]{"draft","composing","activity","input"}) {
     c=Controller();c.Observe(S("old"),0);c.Observe(delayed,T(1000));var interrupted=S("n1");interrupted.Messages.Last().Text=null;
     if(protection=="draft")interrupted.DraftEmpty=false;
     if(protection=="composing")interrupted.Composing=true;
     if(protection=="activity")interrupted.Activity=1;
     if(protection=="input")interrupted.InputIdentity="recreated";
     c.Observe(interrupted,T(1200));c.Observe(S("n1"),T(1400));c.Observe(S("n1"),T(2100));Check("等待正文期间保护条件变化取消候选："+protection,c.TriggerCount==0);
    }
    c=Controller();c.Observe(S("old"),0);c.Observe(delayed,T(1000));c.Observe(S("n1","B"),T(1200));c.Observe(S("n1","B"),T(2000));Check("等待正文期间切换好友仅生成新好友打开推荐",c.TriggerCount==1&&c.LastTriggerOrigin=="chat_open"&&c.Complete(c.PendingId,"B的推荐",T(2010))&&c.Ready.Session=="B");
    c=Controller();c.Observe(S("old"),0);c.Observe(delayed,T(1000));c.Observe(S("n2"),T(1200));c.Observe(S("n2"),T(1810));Check("等待正文期间更新来信替换旧候选",c.TriggerCount==1&&c.Complete(c.PendingId,"latest",T(1900))&&c.Ready.Tail=="n2");
    var inlineSample=S("new");inlineSample.Window=123;inlineSample.Foreground=inlineSample.Focused=inlineSample.ImeAllowed=true;
    inlineSample.InputIdentity="input";inlineSample.ContentRevision="rev";inlineSample.CapturedTicks=T(1000);
    inlineSample.WindowLeft=100;inlineSample.WindowTop=100;inlineSample.WindowWidth=1000;inlineSample.WindowHeight=800;
    inlineSample.InputLeft=400;inlineSample.InputTop=700;inlineSample.InputWidth=650;inlineSample.InputHeight=150;
    var inlineReply=new Recommendation{Id="r",Session="A",Tail="new",Text="灰色预览",InputIdentity="input",ContentRevision="rev",CreatedTicks=T(1000)};
    Check("隐藏宠物且输入条件满足才显示灰字",InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200))&&!InlineSuggestionWindow.Eligible(false,inlineSample,inlineReply,T(1200)));
    inlineSample.DraftEmpty=false;Check("灰字不覆盖已有草稿",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.DraftEmpty=true;
    inlineSample.Composing=true;Check("中文组词不显示灰字",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.Composing=false;
    inlineSample.ImeAllowed=false;Check("未知输入法状态不显示灰字",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.ImeAllowed=true;
    inlineSample.Focused=false;Check("焦点离开输入框隐藏灰字",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.Focused=true;
    inlineSample.Foreground=false;Check("微信不在前台不显示灰字",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.Foreground=true;
    inlineSample.Session="B";Check("灰字不能跨联系人显示",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.Session="A";
    Check("灰字读取过期立即隐藏",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1600)));
    inlineSample.InputLeft=-500;Check("无效输入框位置不显示灰字",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.InputLeft=400;
    inlineSample.Activity=1;Check("输入活动撤销旧灰字",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(1200)));inlineSample.Activity=0;
    inlineSample.CapturedTicks=T(62000);Check("灰字推荐超过60秒不显示",!InlineSuggestionWindow.Eligible(true,inlineSample,inlineReply,T(62000)));
    try{LocalStore.Write(vault,"synthetic-secret-only",true);Check("凭证DPAPI加密及回读",Encoding.UTF8.GetString(File.ReadAllBytes(vault))!="synthetic-secret-only"&&LocalStore.Read(vault,true)=="synthetic-secret-only");LocalStore.Write(vault,"replacement",true);Check("凭证原子替换",LocalStore.Read(vault,true)=="replacement");}finally{File.Delete(vault);}
    string isolated=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"model-test-"+Guid.NewGuid().ToString("N"));
    try {
     using(var client=new ModelClient(AppDomain.CurrentDomain.BaseDirectory,new RuntimeSettings(),isolated)) {
      client.Initialization.GetAwaiter().GetResult();Check("打包Node与模型进程独立启动",!client.HasCredential("chatgpt")&&!client.HasCredential("deepseek-api"));
      client.Request("apiKey/set",new Dictionary<string,object>{{"key","synthetic-api-key-no-network"}}).GetAwaiter().GetResult();Check("真实进程通信提交加密凭证",client.HasCredential("deepseek-api"));
     }
     using(var client=new ModelClient(AppDomain.CurrentDomain.BaseDirectory,new RuntimeSettings(),isolated)) {
      client.Initialization.GetAwaiter().GetResult();Check("重启模型进程恢复独立凭证",client.HasCredential("deepseek-api"));
      client.Request("auth/logout",new Dictionary<string,object>{{"provider","deepseek-api"}}).GetAwaiter().GetResult();Check("真实进程通信删除凭证",!client.HasCredential("deepseek-api"));
     }
    } finally {if(Directory.Exists(isolated))Directory.Delete(isolated,true);}
    Console.WriteLine(new JavaScriptSerializer().Serialize(new{passed=count,checks=checks,networkCalls=0,wechatWrites=0,sendActions=0}));return 0;
   }catch(Exception e){Console.WriteLine(new JavaScriptSerializer().Serialize(new{passed=count-1,error=e.Message,checks=checks}));return 1;}
  }
 }
}
