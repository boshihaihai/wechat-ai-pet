using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Windows.Threading;
using WeChatPet.Runtime;

// Offline UI-state checks: no window is shown and no real memory directory is used.
public static class MemoryWindowRefreshTests {
    static int checks;
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Refresh(MemoryWindow window){typeof(MemoryWindow).GetMethod("Refresh",Private).Invoke(window,null);}
    static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    static FriendMemory Friend(char id,string name,long updated){return new FriendMemory{Id=new string(id,64),AccountId=new string('f',64),Name=name,UpdatedTicks=updated};}
    static ChatSnapshot Snapshot(FriendMemory friend){return new ChatSnapshot{Available=true,AccountId=friend.AccountId,FriendId=friend.Id,Contact=friend.Name,CapturedTicks=DateTime.UtcNow.Ticks};}
    static T Find<T>(DependencyObject parent,string name) where T:DependencyObject {
        if(parent is T&&AutomationProperties.GetName(parent)==name)return (T)parent;
        foreach(object child in LogicalTreeHelper.GetChildren(parent))if(child is DependencyObject){var found=Find<T>((DependencyObject)child,name);if(found!=null)return found;}
        return null;
    }
    static IEnumerable<T> Elements<T>(DependencyObject parent) where T:DependencyObject {
        if(parent is T)yield return (T)parent;
        foreach(object child in LogicalTreeHelper.GetChildren(parent))if(child is DependencyObject)
            foreach(var found in Elements<T>((DependencyObject)child))yield return found;
    }
    static Expander Recent(MemoryWindow window){return Elements<Expander>(window.Content as DependencyObject).First(x=>Convert.ToString(x.Header).StartsWith("近期聊天（"));}
    static Expander Outdated(MemoryWindow window){return Elements<Expander>(window.Content as DependencyObject).First(x=>Convert.ToString(x.Header).StartsWith("已更新的旧记录（"));}
    static Expander Record(MemoryWindow window,string text){return Elements<Expander>(Recent(window)).First(x=>x.Header is DependencyObject&&Elements<TextBlock>((DependencyObject)x.Header).Any(t=>t.Text==text));}
    static Expander Source(Expander item){return Elements<Expander>(item.Content as DependencyObject).First(x=>Convert.ToString(x.Header)=="查看来源");}
    static void Layout(MemoryWindow window) {
        var root=(FrameworkElement)window.Content;root.Measure(new Size(430,900));root.Arrange(new Rect(0,0,430,900));root.UpdateLayout();
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);root.UpdateLayout();
    }
    static void UnassignedNoteChecks(string directory) {
        var store=new MemoryStore(directory);long now=DateTime.UtcNow.Ticks;
        var first=Friend('a',"ShuY",now);var second=Friend('b',"另一位好友",now);var otherAccount=Friend('a',"ShuY",now);otherAccount.AccountId=new string('e',64);
        var legacy=new MemoryItem{Id="legacy",Text="熟悉的朋友，聊天温柔自然",Kind="note",Manual=true,SourceTicks=now,UpdatedTicks=now};
        var own=new MemoryItem{Id="own",Text="我喜欢清淡的面条",Kind="note",Manual=true,SourceRole="self",SourceText="我喜欢清淡的面条",SourceTicks=now,UpdatedTicks=now};
        first.Items.Add(legacy);first.Items.Add(own);
        var another=new MemoryItem{Id="another",Text=legacy.Text,Kind="note",Manual=true,SourceTicks=now,UpdatedTicks=now};second.Items.Add(another);
        var crossAccount=new MemoryItem{Id="cross-account",Text=legacy.Text,Kind="note",Manual=true,SourceTicks=now,UpdatedTicks=now};otherAccount.Items.Add(crossAccount);
        store.Data.Friends.Add(first);store.Data.Friends.Add(second);store.Data.Friends.Add(otherAccount);
        ChatSnapshot current=Snapshot(first);int notifications=0;
        var window=new MemoryWindow(store,current,delegate{notifications++;},delegate{return current;});
        try {
            var root=window.Content as DependencyObject;
            var confirm=Find<Button>(root,"确认此备注用于当前好友");
            Check(confirm!=null&&confirm.Content.ToString()=="用于此好友","legacy note has a concise explicit assignment action");
            Check(Elements<TextBlock>(root).Any(t=>t.Text=="自动记忆")&&!Elements<TextBlock>(root).Any(t=>t.Text=="自动记忆（1）"),"unassigned and self notes are not counted as active friend preferences");
            Check(Elements<TextBlock>(root).Count(t=>t.Text=="待确认（1）")==1,"unassigned note is grouped separately without an active preference claim");
            Check(!store.Context(current,now).Contains(legacy.Text),"unassigned legacy note remains excluded before confirmation");
            var pending=Elements<Expander>(root).Single(x=>x.Header is TextBlock&&((TextBlock)x.Header).Text==legacy.Text);
            pending.IsExpanded=true;Refresh(window);
            Check(Elements<Expander>(root).Single(x=>x.Header is TextBlock&&((TextBlock)x.Header).Text==legacy.Text).IsExpanded,"pending note expansion survives routine refresh");
            current=Snapshot(otherAccount);Click(confirm);
            Check(MemoryStore.Subject(legacy)==null&&MemoryStore.Subject(crossAccount)==null&&notifications==0,"account change before refresh blocks a stale legacy-note confirmation");
            current=Snapshot(first);Refresh(window);confirm=Find<Button>(root,"确认此备注用于当前好友");
            var picker=(ComboBox)typeof(MemoryWindow).GetField("friends",Private).GetValue(window);picker.SelectedItem=second;Click(confirm);
            Check(MemoryStore.Subject(legacy)==null&&MemoryStore.Subject(another)==null&&notifications==0,"friend selection change blocks the previous friend's confirmation callback");
            picker.SelectedItem=first;
            var preferenceEdit=Find<Button>(root,"编辑或保存好友设定");var preferenceText=Find<TextBox>(root,"好友设定");
            Click(preferenceEdit);preferenceText.Text="尚未保存的个人语气";
            confirm=Find<Button>(root,"确认此备注用于当前好友");Click(confirm);
            Check(MemoryStore.Subject(legacy)=="peer"&&legacy.SourceRole==null&&legacy.Manual&&notifications==1,"confirmation explicitly assigns only the selected legacy note to this friend");
            Check(store.Context(Snapshot(first),DateTime.UtcNow.Ticks).Contains(legacy.Text),"confirmed friend note enters recommendation memory");
            Check(new MemoryStore(directory).Find(first.AccountId,first.Id).Items.Single(i=>i.Id==legacy.Id).SubjectRole=="peer","confirmed assignment persists in isolated encrypted storage");
            Check(MemoryStore.Subject(another)==null&&MemoryStore.Subject(crossAccount)==null,"same text in other friends and accounts is never assigned implicitly");
            Check(Elements<TextBlock>(root).Any(t=>t.Text=="自动记忆（1）")&&!Elements<TextBlock>(root).Any(t=>t.Text.StartsWith("待确认（"))&&Find<Button>(root,"确认此备注用于当前好友")==null,"confirmed note moves from pending into the active friend preference section");
            Check(preferenceText.Text=="尚未保存的个人语气"&&preferenceText.Visibility==Visibility.Visible&&first.Preferences=="","confirming a note preserves unsaved reply preference text");
            typeof(MemoryWindow).GetMethod("ConfirmFriendNote",Private).Invoke(window,new object[]{first,own});
            Check(MemoryStore.Subject(own)=="self"&&own.SourceRole=="self"&&notifications==1,"legacy confirmation cannot reassign an existing self message as a friend fact");
            Click(confirm);Check(notifications==1,"repeated stale confirmation does not resave an already assigned note");
            Check(!window.IsVisible,"legacy-note checks do not display a real window");
        }finally{window.Close();}
    }
    static void FriendSettingPresetChecks(string directory) {
        var store=new MemoryStore(directory);var first=Friend('a',"ShuY",DateTime.UtcNow.Ticks);var second=Friend('b',"另一位好友",DateTime.UtcNow.Ticks);
        store.Data.Friends.Add(first);store.Data.Friends.Add(second);ChatSnapshot current=Snapshot(first);int notifications=0;
        var window=new MemoryWindow(store,current,delegate{notifications++;},delegate{return current;});
        try {
            var root=window.Content as DependencyObject;var edit=Find<Button>(root,"编辑或保存好友设定");var text=Find<TextBox>(root,"好友设定");var error=Find<TextBlock>(root,"好友设定提示");
            var presets=Find<ComboBox>(root,"好友回复风格");var picker=(ComboBox)typeof(MemoryWindow).GetField("friends",Private).GetValue(window);
            var gentle=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="温和关心");var playful=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="轻松幽默");var reset=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Text=="");
            Check(edit!=null&&text!=null&&Elements<TextBlock>(root).Any(t=>t.Text=="好友设定")&&Elements<TextBlock>(root).Any(t=>t.Text=="自动记忆"),"friend settings distinguish manually supplied background from learned memories");
            Check(Elements<TextBlock>(root).Any(t=>t.Text=="未设置，按当前聊天回复"),"empty friend setting uses compact current-conversation guidance");
            Click(edit);string background="关系：多年朋友\n背景：设计师\n平时不喜欢空泛安慰。";text.Text=background;presets.SelectedItem=gentle;
            Check(text.Text==background+"\n回复方式："+gentle.Text&&first.Preferences==""&&notifications==0,"choosing style preserves relationship and background without saving");
            Refresh(window);Check(text.Text.StartsWith(background+"\n回复方式：")&&presets.SelectedItem==gentle,"background and labelled style remain recognizable after refresh");
            presets.SelectedItem=playful;presets.SelectedItem=gentle;
            Check(text.Text==background+"\n回复方式："+gentle.Text&&text.Text.Split('\n').Count(line=>line.StartsWith("回复方式："))==1,"repeated style changes replace one owned line without appending duplicates");
            picker.SelectedItem=second;Click(edit);text.Text="关系：同事";presets.SelectedItem=playful;picker.SelectedItem=first;
            Check(text.Text==background+"\n回复方式："+gentle.Text&&presets.SelectedItem==gentle,"switching friends restores independent background and style draft");
            presets.SelectedItem=reset;Check(text.Text==background,"default style removes only the labelled reply-style line");
            text.Text=gentle.Text;presets.SelectedItem=playful;
            Check(text.Text==playful.Text,"a pure known legacy preset can be replaced directly");
            presets.SelectedItem=reset;Check(text.Text=="","default style can clear a pure known preset without background");
            string handwritten="说话像朋友就行，不喜欢套路";text.Text=handwritten;presets.SelectedItem=gentle;presets.SelectedItem=reset;
            Check(text.Text==handwritten,"handwritten legacy style is preserved instead of treated as disposable background");
            text.Text=background+"\n回复方式："+gentle.Text+"\n回复方式："+playful.Text;presets.SelectedItem=gentle;
            Check(text.Text==background+"\n回复方式："+gentle.Text,"pre-existing duplicate labelled style lines are consolidated without losing background");
            string longBackground="关系："+new string('长',387);text.Text=longBackground;presets.SelectedItem=playful;
            Check(text.Text==longBackground&&error.Visibility==Visibility.Visible&&error.Text.Contains("400")&&notifications==0,"overlength style merge is rejected visibly without truncation or save");
            Refresh(window);Check(text.Text==longBackground&&error.Visibility==Visibility.Visible&&error.Text.Contains("400"),"style-limit feedback survives routine refresh");
            presets.SelectedItem=reset;Check(text.Text==longBackground&&error.Visibility==Visibility.Collapsed,"choosing default safely keeps long background and clears rejected-style feedback");
            text.Text=new string('长',401);Click(edit);
            Check(first.Preferences==""&&text.MaxLength==0&&text.Text.Length==401&&text.Visibility==Visibility.Visible&&error.Visibility==Visibility.Visible&&notifications==0,"overlength friend setting preserves pasted text and cannot be silently clipped during save");
            text.Text=background;presets.SelectedItem=gentle;Click(edit);
            Check(first.Preferences==background+"\n回复方式："+gentle.Text&&second.Preferences==""&&notifications==1,"combined friend setting persists only for the intended friend");
            Check(new MemoryStore(directory).Find(first.AccountId,first.Id).Preferences==first.Preferences,"relationship background and style round-trip through the existing preference field");
            Check(!window.IsVisible,"friend-setting checks do not display real UI");
        }finally{window.Close();}
    }
    static void SuspendedSettingsChecks(string directory) {
        var store=new MemoryStore(directory);
        var first=Friend('a',"同名好友",1);first.Preferences="账号甲已保存偏好";first.ExpressionExamples.Add("账号甲已保存示例");
        var second=Friend('a',"同名好友",2);second.AccountId=new string('e',64);second.Preferences="账号乙已保存偏好";second.ExpressionExamples.Add("账号乙已保存示例");
        store.Data.Friends.Add(first);store.Data.Friends.Add(second);
        var firstGroup=new GroupMemory{AccountId=first.AccountId,Id=new string('b',64),Name="同名群",Preferences="甲群已保存偏好",Agreements="甲群已保存约定"};
        var secondGroup=new GroupMemory{AccountId=second.AccountId,Id=firstGroup.Id,Name=firstGroup.Name,Preferences="乙群已保存偏好",Agreements="乙群已保存约定"};
        store.Data.Groups.Add(firstGroup);store.Data.Groups.Add(secondGroup);
        ChatSnapshot current=Snapshot(first);int notifications=0;
        var window=new MemoryWindow(store,current,delegate{notifications++;},delegate{return current;});bool closed=false;window.Closed+=delegate{closed=true;};
        // Exercise the production hide handler without constructing the runtime:
        // its constructor would start the live observer and model processes.
        var runtime=(DesktopRuntime)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DesktopRuntime));
        var memoryField=typeof(DesktopRuntime).GetField("memoryWindow",Private);memoryField.SetValue(runtime,window);
        var hide=typeof(DesktopRuntime).GetMethod("HideSettingsForPet",Private);
        try {
            var root=window.Content as DependencyObject;var pref=Find<TextBox>(root,"好友设定");var examples=Find<TextBox>(root,"这位好友的表达示例");
            var friendTab=Find<Button>(root,"好友记忆分类");var groupTab=Find<Button>(root,"群聊记忆分类");
            Click(Find<Button>(root,"编辑或保存好友设定"));pref.Text="账号甲未保存偏好";Find<Expander>(root,"表达示例").IsExpanded=true;examples.Text="账号甲未保存示例";
            hide.Invoke(runtime,null);
            Check(!closed&&Object.ReferenceEquals(memoryField.GetValue(runtime),window),"production pet-hide handler retains the memory window instance");
            Check(pref.Text=="账号甲未保存偏好"&&examples.Text=="账号甲未保存示例","hiding pet preserves unsaved friend preference and examples");
            Check(first.Preferences=="账号甲已保存偏好"&&first.ExpressionExamples.Single()=="账号甲已保存示例"&&notifications==0,"hiding does not silently save pending friend edits");
            Refresh(window);Click(groupTab);
            var groupPref=Find<TextBox>(root,"本群回复偏好");var agreements=Find<TextBox>(root,"本群群内约定");
            Click(Find<Button>(root,"编辑或保存本群回复偏好"));groupPref.Text="账号甲未保存群偏好";
            Click(Find<Button>(root,"编辑或保存本群群内约定"));agreements.Text="账号甲未保存群约定";
            hide.Invoke(runtime,null);Refresh(window);
            Check(!closed&&groupPref.Text=="账号甲未保存群偏好"&&agreements.Text=="账号甲未保存群约定","repeated pet hiding preserves pending group preference and agreement");
            Check(firstGroup.Preferences=="甲群已保存偏好"&&firstGroup.Agreements=="甲群已保存约定"&&notifications==0,"hiding does not persist group drafts");
            // Restore performs this refresh synchronously before Show/Activate;
            // keep the native window hidden in this offline suite.
            current=Snapshot(second);Refresh(window);
            Check(pref.Text==second.Preferences&&examples.Text==second.ExpressionExamples.Single()&&pref.Visibility==Visibility.Collapsed,"account change while hidden replaces friend values before display");
            Check(groupPref.Text==secondGroup.Preferences&&agreements.Text==secondGroup.Agreements&&groupPref.Visibility==Visibility.Collapsed&&agreements.Visibility==Visibility.Collapsed,"account change while hidden cannot show same-id group drafts from prior account");
            current=null;Refresh(window);
            Check(pref.Text==""&&examples.Text==""&&groupPref.Text==""&&agreements.Text=="","unidentified account after hiding exposes no retained editor values");
            current=Snapshot(first);Refresh(window);Click(friendTab);
            Check(pref.Text=="账号甲未保存偏好"&&examples.Text=="账号甲未保存示例"&&pref.Visibility==Visibility.Visible,"returning to original account restores only its friend drafts");
            Click(groupTab);
            Check(groupPref.Text=="账号甲未保存群偏好"&&agreements.Text=="账号甲未保存群约定"&&agreements.Visibility==Visibility.Visible,"returning to original account restores only its group drafts");
            window.Close();Check(closed,"explicit memory-window close still releases the window");
        }finally{if(!closed)window.Close();}
    }
    static void CapacityNoticeChecks(string directory) {
        var store=new MemoryStore(directory);var known=Friend('a',"已有好友",1);known.Preferences="已保存的设定";store.Data.Friends.Add(known);
        for(int i=0;i<499;i++)store.Data.Friends.Add(new FriendMemory{Id=i.ToString("x64"),AccountId=known.AccountId,Name="合成好友"});
        ChatSnapshot current=Snapshot(Friend('b',"新增好友",2));var window=new MemoryWindow(store,current,delegate{},delegate{return current;});
        try {
            var root=window.Content as DependencyObject;var notice=Find<TextBlock>(root,"记忆容量提示");var manage=Find<Button>(root,"清理容量已满的记忆");var row=(FrameworkElement)notice.Parent;
            Check(row.Visibility==Visibility.Visible&&notice.Text.Contains("好友记录已满")&&manage.IsEnabled,"full friend roster shows a compact notice with an existing cleanup entry");
            current=Snapshot(known);Refresh(window);
            Check(row.Visibility==Visibility.Collapsed&&store.Profile(current,DateTime.UtcNow.Ticks).ReplyPreferences==known.Preferences,"returning to a saved friend clears the notice and keeps its setting usable");
            current=Snapshot(Friend('b',"新增好友",2));Refresh(window);store.RemoveFriend(known,DateTime.UtcNow.Ticks);Refresh(window);
            Check(row.Visibility==Visibility.Collapsed&&store.Error==null,"successful record removal clears the visible capacity notice");
            for(int i=0;i<500;i++)store.Data.Groups.Add(new GroupMemory{Id=i.ToString("x64"),AccountId=current.AccountId,Name="合成群"});
            current=new ChatSnapshot{Available=true,GroupChat=true,AccountId=current.AccountId,GroupId=new string('c',64),Contact="新增群"};
            Click(Find<Button>(root,"群聊记忆分类"));Refresh(window);
            Check(row.Visibility==Visibility.Visible&&notice.Text.Contains("群聊记录已满")&&manage.IsEnabled,"group page exposes the same actionable capacity notice");
            current.AccountId=new string('e',64);Refresh(window);
            Check(notice.Text.Contains("切换到有记录的账号")&&!manage.IsEnabled,"an empty account cannot offer cleanup that would leave the full other-account roster unchanged");
            current=null;Refresh(window);
            Check(row.Visibility==Visibility.Collapsed&&!manage.IsEnabled&&!window.IsVisible,"unknown account clears capacity state without showing a real window");
        }finally{window.Close();}
    }
    [STAThread] public static int Main() {
        string isolated=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-ui-test-"+Guid.NewGuid().ToString("N"));
        var store=new MemoryStore(isolated);Action afterSave=null;int notifications=0;
        ChatSnapshot current=Snapshot(Friend('a',"测试好友甲",1));
        MemoryWindow window;
        try {window=new MemoryWindow(store,current,delegate{notifications++;if(afterSave!=null)afterSave();},delegate{return current;});}
        catch(Exception initError){Console.Error.WriteLine(initError.GetType().FullName);Console.Error.WriteLine(initError.Message);Console.Error.WriteLine(initError.StackTrace);return 2;}
        try {
            SuspendedSettingsChecks(Path.Combine(isolated,"suspended-window"));
            UnassignedNoteChecks(Path.Combine(isolated,"unassigned-note"));
            FriendSettingPresetChecks(Path.Combine(isolated,"friend-setting-presets"));
            CapacityNoticeChecks(Path.Combine(isolated,"capacity-notice"));
            var picker=(ComboBox)typeof(MemoryWindow).GetField("friends",Private).GetValue(window);
            var search=(TextBox)typeof(MemoryWindow).GetField("friendSearch",Private).GetValue(window);
            var locate=(Button)typeof(MemoryWindow).GetField("locateFriend",Private).GetValue(window);
            Action<FriendMemory> select=delegate(FriendMemory friend){
                if(current==null||current.AccountId!=friend.AccountId){current=Snapshot(friend);Refresh(window);}
                picker.SelectedItem=friend;
            };
            var edit=Find<Button>(window.Content as DependencyObject,"编辑或保存好友设定");
            var preferences=Find<TextBox>(window.Content as DependencyObject,"好友设定");
            var legacy=Find<Button>(window.Content as DependencyObject,"查看旧版回复偏好");
            var examples=Find<TextBox>(window.Content as DependencyObject,"这位好友的表达示例");
            var exampleSection=Find<Expander>(window.Content as DependencyObject,"表达示例");
            var saveExamples=Find<Button>(window.Content as DependencyObject,"保存表达示例");
            var exampleError=(TextBlock)typeof(MemoryWindow).GetField("examplesError",Private).GetValue(window);
            Check(picker.Items.Count==0,"empty roster starts empty");
            Check(!edit.IsEnabled&&!preferences.IsEnabled&&preferences.Text=="","no friend disables preference editing");
            Check(!exampleSection.IsExpanded&&!exampleSection.IsEnabled&&!saveExamples.IsEnabled&&examples.Text=="","expression examples start collapsed and disabled without a friend");
            Click(edit);Check(preferences.Visibility==Visibility.Collapsed&&notifications==0,"no selection cannot start or save a preference");
            var first=Friend('a',"测试好友甲",1);store.Data.Friends.Add(first);Refresh(window);
            Check(picker.Items.Count==1&&picker.SelectedItem==first,"first learned friend appears without reopening");
            Check(edit.IsEnabled&&preferences.IsEnabled,"identified friend enables preference editing");
            var layoutElements=Elements<FrameworkElement>(window.Content as DependencyObject).ToList();
            Check(layoutElements.IndexOf(picker)>=0&&layoutElements.IndexOf(picker)<layoutElements.IndexOf(edit),"friend selection precedes its reply preferences");
            first.Name="测试好友改名";Refresh(window);
            Check(((FriendMemory)picker.SelectedItem).DisplayName.StartsWith("测试好友改名"),"friend rename updates visible label");
            var second=Friend('b',"测试好友改名",2);store.Data.Friends.Add(second);Refresh(window);
            Check(picker.Items.Count==2&&picker.SelectedItem==first,"new friend preserves current selection");
            Check(picker.Items.Cast<FriendMemory>().Select(f=>f.DisplayName).Distinct().Count()==2,"duplicate names remain distinguishable");

            var presets=Find<ComboBox>(window.Content as DependencyObject,"好友回复风格");
            Check(presets.Visibility==Visibility.Collapsed,"friend preset shortcuts do not enlarge the collapsed card");
            Click(edit);var recommended=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="自然简短（推荐）");int beforePreset=notifications;presets.SelectedItem=recommended;
            Check(preferences.Text==recommended.Text&&first.Preferences==""&&notifications==beforePreset,"choosing friend preset changes only unsaved draft");
            Refresh(window);Check(preferences.Text==recommended.Text&&presets.SelectedItem==recommended,"refresh preserves chosen friend preset without saving");
            select(second);Click(edit);preferences.Text="第二位的自定义语气";select(first);
            Check(preferences.Text==recommended.Text&&presets.SelectedItem==recommended,"friend switch restores its own preset draft");
            preferences.Text+="可以称呼名字。";Check(((ReplyPreferencePreset)presets.SelectedItem).Name=="自定义","editing friend preset becomes custom without losing text");
            current=new ChatSnapshot{Available=true,AccountId=new string('e',64),FriendId=first.Id};string staleDraft=preferences.Text;presets.SelectedItem=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="礼貌得体");
            Check(preferences.Text==staleDraft&&first.Preferences==""&&notifications==beforePreset,"account change before refresh blocks stale friend preset application");
            current=Snapshot(first);Refresh(window);presets.SelectedItem=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="跟随聊天（默认）");Click(edit);
            Check(first.Preferences==staleDraft&&presets.Visibility==Visibility.Collapsed,"default chat style preserves handwritten additions instead of clearing the setting");
            Click(edit);preferences.Text="";Click(edit);
            select(second);preferences.Text="";Click(edit);select(first);

            Click(edit);preferences.Text="好友甲未保存的回复偏好\n第二行";
            select(second);
            Check(preferences.Text==""&&preferences.Visibility==Visibility.Collapsed,"switching friends loads separate preference and editing state");
            Click(edit);preferences.Text="好友乙用简短回复";Click(edit);
            Check(store.Error==null&&File.Exists(Path.Combine(isolated,"memory.dpapi"))&&new MemoryStore(isolated).Find(second.AccountId,second.Id).Preferences=="好友乙用简短回复","saved friend preference persists in isolated encrypted storage");
            Check(second.Preferences=="好友乙用简短回复"&&first.Preferences=="","save targets only selected friend");
            Check(preferences.Visibility==Visibility.Collapsed,"saved preference returns to compact summary");
            select(first);
            Check(preferences.Text=="好友甲未保存的回复偏好\n第二行"&&preferences.Visibility==Visibility.Visible,"returning to friend restores only its unsaved draft");
            first.Items.Add(new MemoryItem{Id="new-record",Text="新消息",Kind="recent",SourceRole="peer",SourceText="新消息",SourceTicks=DateTime.UtcNow.Ticks,UpdatedTicks=DateTime.UtcNow.Ticks});Refresh(window);
            Check(preferences.Text=="好友甲未保存的回复偏好\n第二行"&&preferences.Visibility==Visibility.Visible,"incoming record refresh preserves current editor");
            store.Data.Friends.Add(Friend('c',"测试好友丙",3));Refresh(window);
            Check(picker.SelectedItem==first&&preferences.Text=="好友甲未保存的回复偏好\n第二行","roster refresh preserves selected friend draft");
            Check(first.Preferences==""&&second.Preferences=="好友乙用简短回复","refresh never persists pending draft to another friend");
            afterSave=delegate{select(second);};Click(edit);afterSave=null;
            Check(first.Preferences=="好友甲未保存的回复偏好\n第二行"&&second.Preferences=="好友乙用简短回复","save remains bound to captured friend during selection-changing notification");
            Check(picker.SelectedItem==second&&preferences.Text==second.Preferences,"notification selection change loads new friend's own preference");

            var anotherAccount=Friend('a',"测试好友改名",4);anotherAccount.AccountId=new string('e',64);anotherAccount.Preferences="另一个账号用正式语气";store.Data.Friends.Add(anotherAccount);Refresh(window);
            Check(picker.Items.Count==3&&!picker.Items.Contains(anotherAccount),"same-name same-id friend under another account never appears in current roster");
            Check(picker.Items.Cast<FriendMemory>().All(f=>f.AccountId==current.AccountId),"all visible friends belong to the active account");
            search.Text="好友丙";
            Check(picker.Items.Count==1&&picker.Items.Cast<FriendMemory>().All(f=>f.Name=="测试好友丙"&&f.AccountId==current.AccountId),"search returns only matching friends within the active account");
            search.Text="测试好友改名";
            Check(picker.Items.Count==2&&picker.Items.Cast<FriendMemory>().All(f=>f.AccountId==current.AccountId)&&!picker.Items.Contains(anotherAccount),"same-name search cannot reveal another account's friend");
            Check(locate.IsEnabled,"locate stays available while current friend is filtered");
            Click(locate);
            Check(search.Text==""&&picker.Items.Count==3&&picker.SelectedItem==first,"locate current friend clears search and selects the current private chat");
            search.Text="不存在的好友";
            Check(picker.Items.Count==0&&!edit.IsEnabled&&!examples.IsEnabled,"empty search disables editors instead of editing a hidden friend");
            Click(locate);
            Check(picker.SelectedItem==first&&preferences.Text==first.Preferences,"locating after empty search restores only the current friend's preference");
            current=Snapshot(anotherAccount);Refresh(window);
            Check(preferences.Text==anotherAccount.Preferences&&preferences.Visibility==Visibility.Collapsed,"same friend id on another account loads its own preference");
            Check(picker.Items.Count==1&&picker.SelectedItem==anotherAccount&&!picker.Items.Contains(first),"account change replaces roster and discards previous account selection");
            Click(edit);preferences.Text="另一个账号的未保存修改";select(first);
            Check(preferences.Text==first.Preferences&&preferences.Visibility==Visibility.Collapsed,"cross-account draft does not enter original friend");
            select(anotherAccount);
            Check(preferences.Text=="另一个账号的未保存修改"&&preferences.Visibility==Visibility.Visible,"cross-account draft restored by composite identity");

            store.ClearFriend(anotherAccount,DateTime.UtcNow.Ticks);Refresh(window);
            Check(preferences.Text==""&&preferences.Visibility==Visibility.Collapsed,"clearing friend discards stale pending editor");
            Check(first.Preferences!=""&&second.Preferences!="","clearing one friend's preferences preserves others");
            Click(edit);preferences.Text="已删除好友的未保存草稿";
            store.Data.Friends.Remove(anotherAccount);Refresh(window);
            Check(picker.SelectedItem!=anotherAccount&&preferences.Text!="已删除好友的未保存草稿","deleted friend's draft is not assigned to fallback selection");
            var replacement=Friend('a',"测试好友改名",5);replacement.AccountId=new string('e',64);replacement.Preferences="重新添加后的新偏好";store.Data.Friends.Add(replacement);Refresh(window);select(replacement);
            Check(preferences.Text==replacement.Preferences&&preferences.Visibility==Visibility.Collapsed,"recreated friend object cannot inherit removed draft cache");
            typeof(MemoryWindow).GetMethod("EditOrSavePreferences",Private).Invoke(window,new object[]{anotherAccount});
            Check(replacement.Preferences=="重新添加后的新偏好","stale editor target cannot save into replacement object");
            Click(edit);preferences.Text="原对象未保存草稿";
            var sameIdentity=Friend('a',"测试好友改名",6);sameIdentity.AccountId=replacement.AccountId;sameIdentity.Preferences="相同标识的新对象内容";
            store.Data.Friends[store.Data.Friends.IndexOf(replacement)]=sameIdentity;Refresh(window);
            Check(picker.SelectedItem==sameIdentity&&preferences.IsEnabled&&preferences.Text==sameIdentity.Preferences&&preferences.Visibility==Visibility.Collapsed,"same-id same-name object replacement refreshes picker and discards stale draft");

            Click(edit);preferences.Text="换号前未保存的账号乙草稿";
            current=Snapshot(first);int beforeAccountRace=notifications;
            typeof(MemoryWindow).GetMethod("EditOrSavePreferences",Private).Invoke(window,new object[]{sameIdentity});
            Check(sameIdentity.Preferences=="相同标识的新对象内容"&&first.Preferences=="好友甲未保存的回复偏好\n第二行"&&notifications==beforeAccountRace,"account change before UI refresh blocks saving the previous account's editor");
            Refresh(window);
            Check(picker.Items.Cast<FriendMemory>().All(f=>f.AccountId==first.AccountId)&&picker.SelectedItem==first&&preferences.Text==first.Preferences,"account refresh selects current friend without carrying the old account's text");
            select(sameIdentity);
            Check(preferences.Text=="换号前未保存的账号乙草稿"&&preferences.Visibility==Visibility.Visible,"returning to original account restores its isolated unsaved preference");
            exampleSection.IsExpanded=true;examples.Text="未识别账号时不能保存的表达示例";
            current=null;Refresh(window);
            Check(picker.Items.Count==0&&picker.SelectedItem==null&&!picker.IsEnabled&&!search.IsEnabled&&!locate.IsEnabled,"unknown account clears and disables the roster, search and current-friend action");
            Check(!edit.IsEnabled&&!preferences.IsEnabled&&preferences.Text==""&&!examples.IsEnabled&&examples.Text=="","unknown account removes friend values from disabled editors");
            int beforeUnknownSave=notifications;
            typeof(MemoryWindow).GetMethod("EditOrSavePreferences",Private).Invoke(window,new object[]{sameIdentity});
            typeof(MemoryWindow).GetMethod("SaveExpressionExamples",Private).Invoke(window,new object[]{sameIdentity});
            Check(notifications==beforeUnknownSave&&sameIdentity.Preferences=="相同标识的新对象内容"&&sameIdentity.ExpressionExamples.Count==0,"unknown account blocks stale preference and example save callbacks");
            current=Snapshot(sameIdentity);current.Available=false;Refresh(window);
            Check(picker.Items.Count==0&&!preferences.IsEnabled,"unavailable snapshot cannot reuse a formerly identified account");
            current=Snapshot(sameIdentity);current.AccountId="not-a-confirmed-account";Refresh(window);
            Check(picker.Items.Count==0&&!preferences.IsEnabled,"malformed account identity cannot expose stored friends");
            current=Snapshot(sameIdentity);Refresh(window);
            Check(picker.SelectedItem==sameIdentity&&preferences.Text=="换号前未保存的账号乙草稿"&&examples.Text=="未识别账号时不能保存的表达示例","identity recovery restores only that account's own cached editors");

            store.Data.LegacyPreferences="旧版共用内容";Refresh(window);
            Check(legacy.Visibility==Visibility.Visible&&sameIdentity.Preferences=="相同标识的新对象内容","legacy preference remains reviewable without automatic assignment");
            store.Data.LegacyPreferences="";Refresh(window);
            Check(legacy.Visibility==Visibility.Collapsed,"assigned or removed legacy preference hides migration affordance");

            select(first);
            Check(!exampleSection.IsExpanded&&examples.IsEnabled&&saveExamples.IsEnabled,"friend expression examples remain compact until expanded");
            exampleSection.IsExpanded=true;examples.Text="哈哈，这都被你发现了\n行，那就这么说定了";
            select(second);
            Check(examples.Text==""&&!exampleSection.IsExpanded,"switching friend loads independent example text and fold state");
            exampleSection.IsExpanded=true;examples.Text=" 收到，我看下 \n收到，我看下\n稍后给你回复";Click(saveExamples);
            Check(second.ExpressionExamples.SequenceEqual(new[]{"收到，我看下","稍后给你回复"})&&first.ExpressionExamples.Count==0,"saving expression examples trims and deduplicates only the captured friend");
            Check(!exampleSection.IsExpanded&&exampleSection.Header.ToString()=="表达示例 · 2","saved example count stays visible in a compact folded header");
            Check(new MemoryStore(isolated).Find(second.AccountId,second.Id).ExpressionExamples.SequenceEqual(second.ExpressionExamples),"friend expression examples persist in isolated encrypted storage");
            select(first);
            Check(examples.Text=="哈哈，这都被你发现了\n行，那就这么说定了"&&exampleSection.IsExpanded,"returning friend restores only its own unsaved example draft");
            Refresh(window);
            Check(examples.Text=="哈哈，这都被你发现了\n行，那就这么说定了"&&exampleSection.IsExpanded,"routine memory refresh preserves example editor and expansion");
            afterSave=delegate{select(second);};Click(saveExamples);afterSave=null;
            Check(first.ExpressionExamples.Count==2&&first.ExpressionExamples[0]=="哈哈，这都被你发现了"&&second.ExpressionExamples[0]=="收到，我看下","example save stays bound to captured friend during notification selection change");
            Check(picker.SelectedItem==second&&examples.Text==String.Join("\n",second.ExpressionExamples),"example save callback displays new selected friend's own values");

            select(sameIdentity);exampleSection.IsExpanded=true;examples.Text="另一个账号的语气示例";
            select(first);
            Check(examples.Text==String.Join("\n",first.ExpressionExamples)&&!exampleSection.IsExpanded,"same-id cross-account example draft never enters original account");
            select(sameIdentity);
            Check(examples.Text=="另一个账号的语气示例"&&exampleSection.IsExpanded,"same-name cross-account example editor restores its own draft");
            examples.Text="一\n二\n三\n四\n五\n六";int beforeInvalid=notifications;Click(saveExamples);
            Check(sameIdentity.ExpressionExamples.Count==0&&examples.Text=="一\n二\n三\n四\n五\n六"&&exampleSection.IsExpanded&&notifications==beforeInvalid,"sixth expression example is rejected without truncating input or persisting");
            Check(exampleError.Visibility==Visibility.Visible&&exampleError.Text.Contains("5"),"too many expression examples shows an inline limit message");
            examples.Text=new string('长',201);Click(saveExamples);
            Check(examples.Text.Length==201&&sameIdentity.ExpressionExamples.Count==0&&exampleError.Visibility==Visibility.Visible&&exampleError.Text.Contains("200"),"overlength example is rejected visibly while preserving all input");
            examples.Text="新保存的表达方式";Click(saveExamples);
            Check(sameIdentity.ExpressionExamples.SequenceEqual(new[]{"新保存的表达方式"})&&exampleError.Visibility==Visibility.Collapsed&&!exampleSection.IsExpanded,"valid retry saves the friend and clears validation feedback");
            exampleSection.IsExpanded=true;examples.Text="将被外部更新替换的未保存草稿";
            store.SetExpressionExamples(sameIdentity,new[]{"外部更新的表达示例"});Refresh(window);
            Check(examples.Text=="外部更新的表达示例"&&!exampleSection.IsExpanded,"external expression update invalidates an older pending example draft");
            exampleSection.IsExpanded=true;examples.Text="清空前的表达示例草稿";store.ClearFriend(sameIdentity,DateTime.UtcNow.Ticks);Refresh(window);
            Check(examples.Text==""&&!exampleSection.IsExpanded&&sameIdentity.ExpressionExamples.Count==0,"clearing selected friend discards stored examples and pending example draft");
            Check(first.ExpressionExamples.Count==2&&second.ExpressionExamples.Count==2,"clearing one friend preserves every other friend's examples");
            exampleSection.IsExpanded=true;examples.Text="旧对象的未保存表达";var removedExampleFriend=sameIdentity;
            sameIdentity=Friend('a',"测试好友改名",8);sameIdentity.AccountId=removedExampleFriend.AccountId;sameIdentity.ExpressionExamples.Add("新对象的独立表达");
            store.Data.Friends[store.Data.Friends.IndexOf(removedExampleFriend)]=sameIdentity;Refresh(window);
            Check(picker.SelectedItem==sameIdentity&&examples.Text=="新对象的独立表达"&&!exampleSection.IsExpanded,"same-id friend replacement cannot inherit an old object's example draft");
            typeof(MemoryWindow).GetMethod("SaveExpressionExamples",Private).Invoke(window,new object[]{removedExampleFriend});
            Check(sameIdentity.ExpressionExamples.SequenceEqual(new[]{"新对象的独立表达"}),"stale example editor target cannot mutate a replacement friend");

            select(first);exampleSection.IsExpanded=true;
            examples.Text=String.Join("\n",Enumerable.Range(0,5).Select(i=>i+new string('长',170)));
            examples.Measure(new Size(330,82));examples.Arrange(new Rect(0,0,330,82));examples.UpdateLayout();examples.ScrollToEnd();examples.UpdateLayout();
            Check(examples.ActualHeight<=82&&examples.VerticalScrollBarVisibility==ScrollBarVisibility.Auto&&examples.ExtentHeight>examples.ViewportHeight,"long example editor keeps bounded height and scrollable overflow");
            Check(examples.VerticalOffset>0,"long expression examples can scroll to the end without growing settings");
            exampleSection.IsExpanded=false;Refresh(window);Check(!exampleSection.IsExpanded&&examples.Text.Length>800,"folding examples preserves pending text without saving it");
            store.SetExpressionExamples(first,new[]{"更新后的甲示例"});Refresh(window);
            Check(examples.Text=="更新后的甲示例"&&!exampleSection.IsExpanded,"external example update also expires a folded pending draft");

            long recordTime=DateTime.UtcNow.Ticks;
            for(int i=0;i<20;i++)first.Items.Add(new MemoryItem{Id="scroll-"+i,Text="滚动测试 "+i,Kind="recent",SourceRole="peer",SourceText="合成来源 "+i,SourceTicks=recordTime-TimeSpan.TicksPerMinute*(i+1),UpdatedTicks=recordTime-TimeSpan.TicksPerMinute*(i+1)});
            Refresh(window);Recent(window).IsExpanded=true;var expandedRecord=Record(window,"滚动测试 5");expandedRecord.IsExpanded=true;Source(expandedRecord).IsExpanded=true;Layout(window);
            var recentScroll=(ScrollViewer)Recent(window).Content;recentScroll.ScrollToVerticalOffset(135);Layout(window);double savedOffset=recentScroll.VerticalOffset;
            Check(savedOffset>0&&recentScroll.ScrollableHeight>0,"recent chat archive has bounded scrollable overflow in offscreen layout");
            Click(edit);preferences.Text="刷新期间保留的偏好草稿";exampleSection.IsExpanded=true;examples.Text="刷新期间保留的表达示例";
            first.Items.Add(new MemoryItem{Id="incoming-scroll",Text="新来信刷新",Kind="recent",SourceRole="peer",SourceText="新来信刷新",SourceTicks=recordTime,UpdatedTicks=recordTime});Refresh(window);Layout(window);
            Check(Recent(window).IsExpanded&&Record(window,"滚动测试 5").IsExpanded&&Source(Record(window,"滚动测试 5")).IsExpanded,"new incoming memory preserves archive, record and source expansion");
            Check(Math.Abs(((ScrollViewer)Recent(window).Content).VerticalOffset-savedOffset)<1,"incoming refresh preserves the recent chat scroll offset");
            Check(preferences.Text=="刷新期间保留的偏好草稿"&&examples.Text=="刷新期间保留的表达示例","detail refresh still preserves unsaved preference and example editors");
            Recent(window).IsExpanded=false;Layout(window);Refresh(window);Layout(window);Refresh(window);Layout(window);
            Check(!Recent(window).IsExpanded,"repeated refresh leaves collapsed recent archive collapsed");
            Recent(window).IsExpanded=true;Layout(window);
            Check(Math.Abs(((ScrollViewer)Recent(window).Content).VerticalOffset-savedOffset)<1,"recent scroll survives collapse and multiple refreshes until expansion is laid out");
            for(int i=0;i<8;i++)first.Items.Add(new MemoryItem{Id="outdated-"+i,Text="旧偏好 "+i,Kind="stated",Manual=true,Superseded=true,SourceRole="peer",SourceText="旧来源 "+i,SourceTicks=recordTime-TimeSpan.TicksPerMinute*(i+1),UpdatedTicks=recordTime-TimeSpan.TicksPerMinute*(i+1)});
            Refresh(window);Outdated(window).IsExpanded=true;Layout(window);var oldScroll=(ScrollViewer)Outdated(window).Content;oldScroll.ScrollToVerticalOffset(170);Layout(window);double oldOffset=oldScroll.VerticalOffset;
            Check(oldOffset>0,"outdated archive has independently scrollable content");
            Outdated(window).IsExpanded=false;Layout(window);Refresh(window);Layout(window);Refresh(window);Layout(window);Outdated(window).IsExpanded=true;Layout(window);
            Check(Math.Abs(((ScrollViewer)Outdated(window).Content).VerticalOffset-oldOffset)<1,"outdated scroll survives collapse and multiple refreshes until expansion is laid out");
            Check(Math.Abs(((ScrollViewer)Recent(window).Content).VerticalOffset-savedOffset)<1,"restoring outdated archive does not replace recent chat scroll state");
            second.Items.Add(new MemoryItem{Id="scroll-5",Text="乙的独立记录",Kind="recent",SourceRole="peer",SourceText="乙来源",SourceTicks=recordTime,UpdatedTicks=recordTime});select(second);Layout(window);
            Check(!Recent(window).IsExpanded&&!Record(window,"乙的独立记录").IsExpanded&&!Source(Record(window,"乙的独立记录")).IsExpanded,"same record id in another friend inherits no detail expansion");
            Check(((ScrollViewer)Recent(window).Content).VerticalOffset==0,"another friend starts with its own scroll position");
            select(first);Layout(window);
            Check(Record(window,"滚动测试 5").IsExpanded&&Source(Record(window,"滚动测试 5")).IsExpanded&&Math.Abs(((ScrollViewer)Recent(window).Content).VerticalOffset-savedOffset)<1,"returning to a friend restores only that friend's expanded detail and scroll");
            Recent(window).IsExpanded=false;Outdated(window).IsExpanded=false;Refresh(window);Recent(window).IsExpanded=true;Outdated(window).IsExpanded=true;select(second);Layout(window);
            Check(!Recent(window).IsExpanded&&((ScrollViewer)Recent(window).Content).VerticalOffset==0,"queued expansion restore cannot change another friend's archive");
            select(first);Layout(window);
            Check(Math.Abs(((ScrollViewer)Recent(window).Content).VerticalOffset-savedOffset)<1&&Math.Abs(((ScrollViewer)Outdated(window).Content).VerticalOffset-oldOffset)<1,"returning after a queued restore restores both original friend offsets");
            var removed=first.Items.First(i=>i.Id=="scroll-5");first.Items.Remove(removed);Refresh(window);Layout(window);
            first.Items.Add(new MemoryItem{Id="scroll-5",Text="相同标识的新记录",Kind="recent",SourceRole="peer",SourceText="新来源",SourceTicks=recordTime,UpdatedTicks=recordTime});Refresh(window);Layout(window);
            Check(!Record(window,"相同标识的新记录").IsExpanded&&!Source(Record(window,"相同标识的新记录")).IsExpanded,"deleted record's expansion is not restored onto a replacement with reused id");
            select(sameIdentity);sameIdentity.Items.Add(new MemoryItem{Id="scroll-5",Text="另一账号的记录",Kind="recent",SourceRole="peer",SourceText="另账号来源",SourceTicks=recordTime,UpdatedTicks=recordTime});Refresh(window);Layout(window);
            Check(!Recent(window).IsExpanded&&!Record(window,"另一账号的记录").IsExpanded,"same friend id under another account receives independent detail view state");
            select(first);Layout(window);Record(window,"相同标识的新记录").IsExpanded=true;Source(Record(window,"相同标识的新记录")).IsExpanded=true;
            store.ClearFriend(first,DateTime.UtcNow.Ticks);Refresh(window);Layout(window);
            first.Items.Add(new MemoryItem{Id="scroll-5",Text="清空后新的记录",Kind="recent",SourceRole="peer",SourceText="清空后的来源",SourceTicks=DateTime.UtcNow.Ticks,UpdatedTicks=DateTime.UtcNow.Ticks});Refresh(window);Layout(window);
            Check(!Recent(window).IsExpanded&&!Record(window,"清空后新的记录").IsExpanded&&!Source(Record(window,"清空后新的记录")).IsExpanded&&((ScrollViewer)Recent(window).Content).VerticalOffset==0,"clearing friend discards archive, record, source and scroll view caches");
            store.SetExpressionExamples(first,new[]{"更新后的甲示例"});Refresh(window);
            Directory.CreateDirectory(Path.Combine(isolated,"memory.dpapi.tmp"));exampleSection.IsExpanded=true;examples.Text="失败后保留的示例草稿";Click(saveExamples);Refresh(window);
            Check(store.Error!=null&&exampleSection.IsEnabled&&examples.IsEnabled&&saveExamples.IsEnabled,"write error preserves explicit editing and retry controls");
            Check(examples.Text=="失败后保留的示例草稿"&&first.ExpressionExamples.SequenceEqual(new[]{"更新后的甲示例"}),"failed example save retains draft without mutating stored examples");
            Click(edit);preferences.Text="失败后保留的回复偏好";string savedPreference=first.Preferences;Click(edit);Refresh(window);
            Check(store.Error!=null&&preferences.Visibility==Visibility.Visible&&preferences.Text=="失败后保留的回复偏好"&&first.Preferences==savedPreference&&edit.IsEnabled,"failed preference save keeps editor draft and old committed value");
            Directory.Delete(Path.Combine(isolated,"memory.dpapi.tmp"));Click(edit);Click(saveExamples);Refresh(window);
            Check(store.Error==null&&first.Preferences=="失败后保留的回复偏好"&&first.ExpressionExamples.Single()=="失败后保留的示例草稿","same save controls retry after storage recovers");
            var memorySwitch=Find<System.Windows.Controls.Primitives.ToggleButton>(window.Content as DependencyObject,"使用记忆");
            Directory.CreateDirectory(Path.Combine(isolated,"memory.dpapi.tmp"));memorySwitch.IsChecked=false;memorySwitch.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(store.Error!=null&&store.Data.Enabled&&memorySwitch.IsChecked==true&&memorySwitch.IsEnabled,"failed global memory switch returns to committed state and stays retryable");
            Directory.Delete(Path.Combine(isolated,"memory.dpapi.tmp"));memorySwitch.IsChecked=false;memorySwitch.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(store.Error==null&&!store.Data.Enabled&&memorySwitch.IsChecked==false&&!new MemoryStore(isolated).Data.Enabled,"same global memory switch retries and persists after recovery");
            store.SetEnabled(true,DateTime.UtcNow.Ticks);Refresh(window);
            var friendSwitch=Find<System.Windows.Controls.Primitives.ToggleButton>(window.Content as DependencyObject,"使用这位好友的记忆");
            Directory.CreateDirectory(Path.Combine(isolated,"memory.dpapi.tmp"));friendSwitch.IsChecked=false;friendSwitch.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            friendSwitch=Find<System.Windows.Controls.Primitives.ToggleButton>(window.Content as DependencyObject,"使用这位好友的记忆");
            Check(store.Error!=null&&first.Enabled&&friendSwitch.IsChecked==true&&friendSwitch.IsEnabled,"failed friend memory switch returns to committed state and stays retryable");
            Directory.Delete(Path.Combine(isolated,"memory.dpapi.tmp"));friendSwitch.IsChecked=false;friendSwitch.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(store.Error==null&&!first.Enabled&&!new MemoryStore(isolated).Find(first.AccountId,first.Id).Enabled,"same friend memory switch retries and persists after recovery");
            store.SetFriendEnabled(first,true,DateTime.UtcNow.Ticks);Refresh(window);
            typeof(MemoryStore).GetField("loadFailed",Private).SetValue(store,true);Refresh(window);
            Check(!exampleSection.IsEnabled&&!edit.IsEnabled&&!memorySwitch.IsEnabled&&!Find<System.Windows.Controls.Primitives.ToggleButton>(window.Content as DependencyObject,"使用这位好友的记忆").IsEnabled,"unreadable original memory remains blocked instead of overwriting it");
            typeof(MemoryStore).GetField("loadFailed",Private).SetValue(store,false);Refresh(window);
            var confirmedSelf=new MemoryItem{Id="confirmed-self",Text="我不喝咖啡",SourceText="我不喝咖啡",SourceRole="self",Kind="recent",SourceTicks=DateTime.UtcNow.Ticks,UpdatedTicks=DateTime.UtcNow.Ticks};first.Items.Add(confirmedSelf);
            store.Put(first,confirmedSelf,"我不喝咖啡！",true,DateTime.UtcNow.Ticks);Refresh(window);
            Check(Record(window,"我不喝咖啡！")!=null,"confirmed self excerpt stays in recent chat rather than friend preferences");
            Check(Elements<TextBlock>(Record(window,"我不喝咖啡！")).Any(t=>t.Text.StartsWith("关于我 · 你已确认")),"confirmed self excerpt visibly retains its subject");
            Click(edit);preferences.Text="清空前尚未保存";store.ClearAll(DateTime.UtcNow.Ticks);Refresh(window);
            Check(picker.Items.Count==0&&picker.SelectedItem==null,"cleared roster resets selection");
            Check(!edit.IsEnabled&&preferences.Text==""&&preferences.Visibility==Visibility.Collapsed,"clear all removes pending preferences and disables empty editor");
            Check(!exampleSection.IsEnabled&&!exampleSection.IsExpanded&&examples.Text==""&&!saveExamples.IsEnabled,"clear all removes example drafts and disables the empty editor");
            var last=Friend('d',"清空后的新好友",7);store.Data.Friends.Add(last);Refresh(window);
            Check(picker.Items.Count==1&&picker.SelectedItem==last,"new friends appear after roster was cleared");
            Check(preferences.Text==""&&preferences.Visibility==Visibility.Collapsed,"new friend after clear starts without previous editor state");
            Check(examples.Text==""&&!exampleSection.IsExpanded&&last.ExpressionExamples.Count==0,"friend learned after clear inherits no stored or pending expression examples");
            var friendTab=Find<Button>(window.Content as DependencyObject,"好友记忆分类");var groupTab=Find<Button>(window.Content as DependencyObject,"群聊记忆分类");
            var friendPage=(StackPanel)typeof(MemoryWindow).GetField("friendPage",Private).GetValue(window);
            var groupPage=(GroupMemoryPanel)typeof(MemoryWindow).GetField("groupPage",Private).GetValue(window);
            Check(friendTab!=null&&groupTab!=null&&friendPage.Visibility==Visibility.Visible&&groupPage.Visibility==Visibility.Collapsed,"private chat opens compact friend category by default");
            current=new ChatSnapshot{Available=true,GroupChat=true,AccountId=last.AccountId,GroupId=new string('b',64),Contact="测试群",CapturedTicks=DateTime.UtcNow.Ticks};
            store.ObserveGroup(current,DateTime.UtcNow.Ticks);Refresh(window);
            Check(picker.Items.Count==1&&picker.Items[0]==last,"opening a group preserves only current account's separate friend roster");
            Click(groupTab);Check(friendPage.Visibility==Visibility.Collapsed&&groupPage.Visibility==Visibility.Visible,"group category hides friend preference and recent chat controls");
            Click(friendTab);Check(friendPage.Visibility==Visibility.Visible&&groupPage.Visibility==Visibility.Collapsed&&preferences.Text=="","switching back to friend category never displays group preferences");
            var groupWindow=new MemoryWindow(store,current,delegate{},delegate{return current;});
            try{Check(((GroupMemoryPanel)typeof(MemoryWindow).GetField("groupPage",Private).GetValue(groupWindow)).Visibility==Visibility.Visible&&((StackPanel)typeof(MemoryWindow).GetField("friendPage",Private).GetValue(groupWindow)).Visibility==Visibility.Collapsed,"opening settings from group defaults to group category");}finally{groupWindow.Close();}
            current=new ChatSnapshot{Available=true,GroupChat=true,AccountId=new string('c',64),GroupId=new string('b',64),Contact="同名测试群"};Refresh(window);
            Check(picker.Items.Count==0&&!edit.IsEnabled&&preferences.Text=="","switching WeChat account while on group hides previous account's friend preferences");
            current=null;Refresh(window);Check(picker.Items.Count==0&&!edit.IsEnabled,"missing account confirmation hides friend data across both categories");
            Check(!window.IsVisible&&store.Error==null,"offline checks never show a window and use isolated encrypted storage");
            Console.WriteLine(checks+" memory window refresh checks passed");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{window.Close();if(Directory.Exists(isolated))Directory.Delete(isolated,true);}
    }
}
