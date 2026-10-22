using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WeChatPet.Runtime;

// Offline only: isolated encrypted memory, synthetic identities, no shown window.
public static class GroupMemoryPanelTests {
    static int checks;
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    static GroupMemory Group(char id,string name,long updated,char account='f'){return new GroupMemory{Id=new string(id,64),AccountId=new string(account,64),Name=name,UpdatedTicks=updated};}
    static ChatSnapshot Snapshot(GroupMemory group){return new ChatSnapshot{Available=true,GroupChat=true,AccountId=group.AccountId,GroupId=group.Id,Contact=group.Name,CapturedTicks=DateTime.UtcNow.Ticks};}
    static T Find<T>(DependencyObject parent,string name) where T:DependencyObject {
        if(parent is T&&AutomationProperties.GetName(parent)==name)return (T)parent;
        foreach(object child in LogicalTreeHelper.GetChildren(parent))if(child is DependencyObject){var found=Find<T>((DependencyObject)child,name);if(found!=null)return found;}
        return null;
    }
    static IEnumerable<T> Elements<T>(DependencyObject parent) where T:DependencyObject {
        if(parent is T)yield return (T)parent;
        foreach(object child in LogicalTreeHelper.GetChildren(parent))if(child is DependencyObject)foreach(var found in Elements<T>((DependencyObject)child))yield return found;
    }
    static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,Private).GetValue(value);}
    static void Render(FrameworkElement root,string path) {
        foreach(var element in Elements<FrameworkElement>(root)){element.InvalidateMeasure();element.InvalidateArrange();}
        double height=0;
        for(int pass=0;pass<3;pass++){root.Measure(new Size(430,Double.PositiveInfinity));height=root.DesiredSize.Height;root.Arrange(new Rect(0,0,430,height));root.UpdateLayout();}
        height=Math.Max(height,root.DesiredSize.Height);
        var image=new RenderTargetBitmap(645,(int)Math.Ceiling(height*1.5),144,144,PixelFormats.Pbgra32);image.Render(root);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))png.Save(stream);
    }
    static void RenderWindow(MemoryStore store,GroupMemory group,string directory) {
        Directory.CreateDirectory(directory);var current=Snapshot(group);var window=new MemoryWindow(store,current,delegate{},delegate{return current;});
        try {
            var root=(FrameworkElement)window.Content;root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,window.FontFamily);root.SetValue(System.Windows.Documents.TextElement.FontSizeProperty,window.FontSize);window.Content=null;
            Render(root,Path.Combine(directory,"group-memory-settings.png"));
            var panel=Elements<GroupMemoryPanel>(root).Single();
            Click(Find<Button>(panel,"编辑或保存本群回复偏好"));Click(Find<Button>(panel,"编辑或保存本群群内约定"));
            Render(root,Path.Combine(directory,"group-memory-settings-edit.png"));
        }finally{window.Close();}
    }
    [STAThread] public static int Main(string[] args) {
        string isolated=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"group-memory-ui-test-"+Guid.NewGuid().ToString("N"));
        try {
            var store=new MemoryStore(isolated);var first=Group('a',"工作群",1);ChatSnapshot current=Snapshot(first);int notifications=0;Action afterSave=null;
            var panel=new GroupMemoryPanel(store,delegate{return current;},delegate{notifications++;if(afterSave!=null)afterSave();});
            var picker=Find<ComboBox>(panel,"群聊记忆");var search=Field<TextBox>(panel,"groupSearch");var locate=Find<Button>(panel,"定位当前群聊");
            var preferences=Find<TextBox>(panel,"本群回复偏好");var agreements=Find<TextBox>(panel,"本群群内约定");
            var savePreferences=Find<Button>(panel,"编辑或保存本群回复偏好");var saveAgreements=Find<Button>(panel,"编辑或保存本群群内约定");
            var enabled=Find<ToggleButton>(panel,"使用本群记忆");var more=Find<Button>(panel,"更多群记忆操作");
            Check(picker!=null&&preferences!=null&&agreements!=null,"group selector and two editors have accessible names");
            Check(picker.Items.Count==0&&!picker.IsEnabled&&!savePreferences.IsEnabled&&!saveAgreements.IsEnabled,"empty group roster disables editing");
            Click(savePreferences);Click(saveAgreements);Check(notifications==0&&preferences.Visibility==Visibility.Collapsed&&agreements.Visibility==Visibility.Collapsed,"empty selection cannot start editing or save");
            store.Data.Groups.Add(first);panel.Refresh();
            Check(picker.Items.Count==1&&Object.ReferenceEquals(picker.SelectedItem,first)&&locate.IsEnabled,"current group appears without reopening");
            Check(preferences.Visibility==Visibility.Collapsed&&agreements.Visibility==Visibility.Collapsed,"group memory starts with compact summaries");
            Check(savePreferences.IsEnabled&&saveAgreements.IsEnabled&&more.IsEnabled,"known group enables edit and clear controls");
            var second=Group('b',"工作群",2);store.Data.Groups.Add(second);panel.Refresh();
            Check(picker.Items.Count==2&&Object.ReferenceEquals(picker.SelectedItem,first),"adding a group preserves selection");
            Check(picker.Items.Cast<GroupMemory>().Select(g=>g.DisplayName).Distinct().Count()==2,"same-name groups remain distinguishable");
            Check(first.DisplayName.Contains("当前聊天")&&!second.DisplayName.Contains("当前聊天"),"current group is identified in the roster");
            var presets=Find<ComboBox>(panel,"群聊回复风格");
            Check(presets.Visibility==Visibility.Collapsed,"group preset selector is hidden until editing");
            Click(savePreferences);var efficient=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="协作高效");int beforePreset=notifications;presets.SelectedItem=efficient;
            Check(preferences.Text==efficient.Text&&first.Preferences==""&&notifications==beforePreset,"group preset changes only current draft without saving or invalidating");
            panel.Refresh();picker.SelectedItem=second;picker.SelectedItem=first;
            Check(preferences.Text==efficient.Text&&presets.SelectedItem==efficient&&second.Preferences=="","group switch and periodic refresh preserve isolated preset draft");
            preferences.Text+="请少用表情。";Check(((ReplyPreferencePreset)presets.SelectedItem).Name=="自定义","manual edits to a group preset remain custom");
            current=Snapshot(Group('a',"另一个账号群",1,'e'));string stalePresetDraft=preferences.Text;presets.SelectedItem=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="谨慎表达");
            Check(preferences.Text==stalePresetDraft&&first.Preferences==""&&notifications==beforePreset,"unrefreshed account switch rejects previous group's preset change");
            current=Snapshot(first);panel.Refresh();panel.Visibility=Visibility.Collapsed;presets.SelectedItem=efficient;
            Check(preferences.Text==stalePresetDraft,"hidden group category cannot apply presets");panel.Visibility=Visibility.Visible;
            panel.Refresh();presets.SelectedItem=presets.Items.Cast<ReplyPreferencePreset>().First(p=>p.Name=="跟随聊天（默认）");Click(savePreferences);
            Check(first.Preferences==""&&agreements.Text==""&&presets.Visibility==Visibility.Collapsed,"default style saves without inventing or modifying group agreements");
            Click(savePreferences);preferences.Text="甲群未保存的语气\n自然简短";Click(saveAgreements);agreements.Text="甲群未保存约定\n每周五汇总";
            picker.SelectedItem=second;
            Check(preferences.Text==""&&agreements.Text==""&&preferences.Visibility==Visibility.Collapsed&&agreements.Visibility==Visibility.Collapsed,"switching groups isolates both editor drafts");
            Click(savePreferences);preferences.Text="乙群正式简短";Click(savePreferences);
            Click(saveAgreements);agreements.Text="乙群周一提交汇总";Click(saveAgreements);
            Check(second.Preferences=="乙群正式简短"&&second.Agreements=="乙群周一提交汇总"&&first.Preferences==""&&first.Agreements=="","saving fields changes only selected group");
            var reloaded=new MemoryStore(isolated).FindGroup(second.AccountId,second.Id);
            Check(reloaded!=null&&reloaded.Preferences==second.Preferences&&reloaded.Agreements==second.Agreements,"group fields persist in isolated encrypted storage");
            Check(preferences.Visibility==Visibility.Collapsed&&agreements.Visibility==Visibility.Collapsed,"saving returns to compact summaries");
            picker.SelectedItem=first;
            Check(preferences.Text=="甲群未保存的语气\n自然简短"&&agreements.Text=="甲群未保存约定\n每周五汇总"&&preferences.Visibility==Visibility.Visible&&agreements.Visibility==Visibility.Visible,"returning to group restores only its drafts");
            panel.Refresh();panel.Refresh();
            Check(preferences.Text.StartsWith("甲群未保存")&&agreements.Text.StartsWith("甲群未保存"),"periodic refresh preserves unsaved editors");
            Click(savePreferences);
            Check(first.Preferences.StartsWith("甲群未保存")&&agreements.Text.StartsWith("甲群未保存")&&agreements.Visibility==Visibility.Visible,"saving reply preference preserves unsaved agreement");
            afterSave=delegate{picker.SelectedItem=second;};Click(saveAgreements);afterSave=null;
            Check(first.Agreements.StartsWith("甲群未保存")&&second.Agreements=="乙群周一提交汇总","save remains bound to original group during selection-changing notification");
            Check(picker.SelectedItem==second&&agreements.Text==second.Agreements,"notification selection change renders the new group");
            first.Name="新工作群";panel.Refresh();Check(first.DisplayName.StartsWith("新工作群"),"group rename refreshes its label");
            search.Text="新工作";Check(picker.Items.Count==1&&picker.SelectedItem==first,"search filters group names within the account");
            search.Text="不存在";Check(picker.Items.Count==0&&picker.SelectedItem==null&&!savePreferences.IsEnabled&&!saveAgreements.IsEnabled,"no search match cannot edit a hidden group");
            Click(locate);Check(search.Text==""&&picker.SelectedItem==first,"locating current group clears search and selects it");
            current=Snapshot(second);panel.Refresh();Click(locate);Check(picker.SelectedItem==second,"locate follows the current group after chat switch");
            var foreign=Group('b',"工作群",3,'e');foreign.Preferences="另一个账号语气";foreign.Agreements="另一个账号约定";store.Data.Groups.Add(foreign);panel.Refresh();
            Check(picker.Items.Count==2&&!picker.Items.Contains(foreign),"same name and group id from another account never enters roster");
            Click(savePreferences);preferences.Text="乙群留存草稿";Click(saveAgreements);agreements.Text="乙群留存约定";
            current=Snapshot(foreign);Click(savePreferences);Click(saveAgreements);
            Check(second.Preferences=="乙群正式简短"&&second.Agreements=="乙群周一提交汇总"&&foreign.Preferences=="另一个账号语气","account switch before refresh blocks stale editor save");
            panel.Refresh();Check(picker.Items.Count==1&&picker.SelectedItem==foreign&&preferences.Text==foreign.Preferences&&agreements.Text==foreign.Agreements,"account switch shows only new account group data");
            Check(preferences.Visibility==Visibility.Collapsed&&agreements.Visibility==Visibility.Collapsed,"same group id under different account starts separate editor state");
            Click(savePreferences);preferences.Text="另一个账号已保存语气";Click(savePreferences);
            Check(foreign.Preferences=="另一个账号已保存语气"&&second.Preferences=="乙群正式简短","cross-account group save does not alter original account");
            current=Snapshot(second);panel.Refresh();
            Check(preferences.Text=="乙群留存草稿"&&agreements.Text=="乙群留存约定"&&preferences.Visibility==Visibility.Visible,"returning to account restores its own unsaved group drafts");
            current=null;panel.Refresh();
            Check(picker.Items.Count==0&&preferences.Text==""&&agreements.Text==""&&!savePreferences.IsEnabled&&!enabled.IsEnabled&&!more.IsEnabled,"unknown account hides values and disables mutation");
            current=new ChatSnapshot{Available=true,AccountId=second.AccountId,FriendId=new string('c',64),Contact="私聊好友"};panel.Refresh();
            Check(picker.Items.Count==2&&!locate.IsEnabled,"known private chat can manage own account groups without pretending to be current group");
            var friend=new FriendMemory{AccountId=second.AccountId,Id=second.Id,Name=second.Name,Preferences="私聊偏好"};store.Data.Friends.Add(friend);panel.Refresh();
            Check(picker.Items.Cast<object>().All(g=>g is GroupMemory)&&!picker.Items.Contains(friend),"friend with same identifiers never becomes a group choice");
            current=Snapshot(second);panel.Refresh();Click(locate);Click(savePreferences);Click(saveAgreements);
            Check(second.Preferences=="乙群留存草稿"&&second.Agreements=="乙群留存约定"&&friend.Preferences=="私聊偏好","group drafts save independently from same-id friend");
            enabled.IsChecked=false;enabled.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(!second.Enabled&&first.Enabled&&foreign.Enabled,"disabling memory affects only selected group");
            Check(Field<TextBlock>(panel,"scopeNote").Text.Contains("本群记忆已暂停"),"disabled group has a compact paused note");
            enabled.IsChecked=true;enabled.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(second.Enabled,"selected group memory can be re-enabled");
            store.SetEnabled(false,DateTime.UtcNow.Ticks);panel.Refresh();Check(Field<TextBlock>(panel,"scopeNote").Text=="记忆已暂停 · 内容保留"&&second.Enabled,"global memory pause does not overwrite group switch preference");
            store.SetEnabled(true,DateTime.UtcNow.Ticks);panel.Refresh();
            Click(savePreferences);preferences.Text="失败后保留的群偏好";Click(saveAgreements);agreements.Text="失败后保留的群约定";
            Directory.CreateDirectory(Path.Combine(isolated,"memory.dpapi.tmp"));string beforePreference=second.Preferences,beforeAgreement=second.Agreements;Click(savePreferences);Click(saveAgreements);panel.Refresh();
            Check(store.Error!=null&&second.Preferences==beforePreference&&second.Agreements==beforeAgreement,"failed save leaves both committed fields intact");
            Check(preferences.Text=="失败后保留的群偏好"&&agreements.Text=="失败后保留的群约定"&&preferences.Visibility==Visibility.Visible&&agreements.Visibility==Visibility.Visible&&savePreferences.IsEnabled,"failed save retains drafts and retry controls");
            Directory.Delete(Path.Combine(isolated,"memory.dpapi.tmp"));Click(savePreferences);Click(saveAgreements);
            Check(store.Error==null&&second.Preferences=="失败后保留的群偏好"&&second.Agreements=="失败后保留的群约定","retry saves after storage recovers");
            Click(savePreferences);preferences.Text=new string('a',401);Click(savePreferences);
            Check(second.Preferences=="失败后保留的群偏好"&&preferences.Visibility==Visibility.Visible,"oversize reply preference cannot silently replace committed text");
            panel.Refresh();Check(Field<TextBlock>(panel,"error").Visibility==Visibility.Visible&&Field<TextBlock>(panel,"error").Text.Length>0,"validation error survives periodic refresh until correction");
            preferences.Text="正常偏好";Click(savePreferences);Click(saveAgreements);agreements.Text=new string('b',2001);Click(saveAgreements);
            Check(second.Agreements=="失败后保留的群约定"&&agreements.Visibility==Visibility.Visible,"oversize agreement cannot silently replace committed text");
            picker.SelectedItem=first;Check(Field<TextBlock>(panel,"error").Visibility==Visibility.Collapsed,"validation error does not follow a different group");picker.SelectedItem=second;
            agreements.Text="正常约定";Click(saveAgreements);
            typeof(MemoryStore).GetField("loadFailed",Private).SetValue(store,true);panel.Refresh();
            Check(!savePreferences.IsEnabled&&!saveAgreements.IsEnabled&&!enabled.IsEnabled&&!more.IsEnabled,"unreadable memory blocks edits and clearing");
            typeof(MemoryStore).GetField("loadFailed",Private).SetValue(store,false);panel.Refresh();
            Click(savePreferences);preferences.Text="移除前草稿";store.Data.Groups.Remove(second);panel.Refresh();
            var replacement=Group('b',"工作群",4);store.Data.Groups.Add(replacement);panel.Refresh();Click(locate);
            Check(picker.SelectedItem==replacement&&preferences.Text==""&&agreements.Text==""&&preferences.Visibility==Visibility.Collapsed,"recreated group does not inherit stale object drafts");
            Check(preferences.MaxHeight==104&&agreements.MaxHeight==116&&preferences.VerticalScrollBarVisibility==ScrollBarVisibility.Auto&&agreements.VerticalScrollBarVisibility==ScrollBarVisibility.Auto,"both expanded editors stay bounded and scroll internally");
            panel.Measure(new Size(386,1000));panel.Arrange(new Rect(0,0,386,panel.DesiredSize.Height));panel.UpdateLayout();
            Check(panel.ActualHeight<330,"collapsed panel remains compact");
            Click(savePreferences);Click(saveAgreements);panel.Measure(new Size(386,1000));panel.Arrange(new Rect(0,0,386,panel.DesiredSize.Height));panel.UpdateLayout();
            Check(panel.ActualHeight<570,"two expanded editors fit a bounded settings layout");
            Check(preferences.MaxLength==400&&agreements.MaxLength==2000,"editor input limits match storage and model contract");
            Check(!Elements<Expander>(panel).Any()&&!Elements<TextBlock>(panel).Any(t=>t.Text=="好友偏好"||t.Text=="表达示例"),"group panel has no private excerpts or unnecessary accordion");
            if(args.Length==2&&args[0]=="--render") {
                replacement.Name="项目协作群";store.SetGroupPreferences(replacement,"自然简短，直接回应当前话题；不用过多表情，不替我承诺时间。");store.SetGroupAgreements(replacement,"每周五汇总项目进度。\n涉及费用先确认负责人，再决定是否安排。");RenderWindow(store,replacement,args[1]);
            }
            store.ClearAccount(first.AccountId,DateTime.UtcNow.Ticks);panel.Refresh();
            Check(picker.Items.Count==0&&preferences.Text==""&&agreements.Text==""&&!savePreferences.IsEnabled,"account clear removes visible and pending group state");
            Check(store.FindGroup(foreign.AccountId,foreign.Id)==foreign,"current account clear preserves other account group");
            Check(Window.GetWindow(panel)==null&&store.Error==null,"offline checks never show a window or use real memories");
            Console.WriteLine(checks+" group memory panel checks passed");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(isolated))Directory.Delete(isolated,true);}
    }
}
