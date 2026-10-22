using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace WeChatPet.Runtime {
    public sealed class MemoryWindow : Window {
        readonly MemoryStore store;readonly Action changed;readonly StackPanel details=new StackPanel();
        readonly StackPanel friendPage=new StackPanel();
        readonly GroupMemoryPanel groupPage;
        readonly Button friendTab=SettingsTheme.Button("好友","好友记忆分类");
        readonly Button groupTab=SettingsTheme.Button("群聊","群聊记忆分类");
        readonly System.Windows.Controls.Primitives.ToggleButton memoryEnabled;
        bool groupMode;
        readonly ComboBox friends;
        readonly Func<ChatSnapshot> currentChat;
        readonly TextBox friendSearch=new TextBox{Padding=new Thickness(9,7,9,7),BorderThickness=new Thickness(0),Background=Brushes.Transparent,MaxLength=80};
        readonly Button locateFriend=SettingsTheme.Button("当前好友","定位当前聊天好友");
        readonly TextBlock accountHint=SettingsTheme.Text("",12,SettingsTheme.Muted);
        readonly DockPanel capacityRow=new DockPanel{Margin=new Thickness(0,0,0,8)};
        readonly TextBlock capacityHint=SettingsTheme.Text("",11,SettingsTheme.Muted);
        readonly Button capacityManage=SettingsTheme.Button("清理…","清理容量已满的记忆");
        readonly TextBlock searchEmpty=SettingsTheme.Text("没有匹配的好友",12,SettingsTheme.Muted);
        string scopeAccount;
        readonly TextBlock note=SettingsTheme.Text("",11,SettingsTheme.Muted);
        readonly TextBlock prefSummary=SettingsTheme.Text("",12,SettingsTheme.Muted);
        // Keep pasted prose intact; enforce the storage limit visibly on save.
        readonly TextBox pref=Editor("",104,"好友设定",0);
        readonly TextBlock prefError=SettingsTheme.Text("",11,SettingsTheme.Muted);
        readonly ReplyPreferencePicker preferencePresets;
        readonly Button savePref=SettingsTheme.Button("编辑","编辑或保存好友设定");
        readonly Button legacyReview=SettingsTheme.Button("旧版偏好待归属 · 查看","查看旧版回复偏好");
        readonly Dictionary<string,PreferenceDraft> preferenceDrafts=new Dictionary<string,PreferenceDraft>();
        FriendMemory preferenceFriend;
        readonly Expander examplesSection=new Expander{Header="表达示例",FontSize=12,Margin=new Thickness(0,8,0,0)};
        readonly TextBox examples=Editor("",82,"这位好友的表达示例",0);
        readonly Button saveExamples=SettingsTheme.Button("保存","保存表达示例");
        readonly TextBlock examplesError=SettingsTheme.Text("",11,SettingsTheme.Muted);
        readonly Dictionary<string,ExampleDraft> exampleDrafts=new Dictionary<string,ExampleDraft>();
        FriendMemory exampleFriend;
        sealed class PreferenceDraft {
            public FriendMemory Friend;public string Text,Saved,Error;public bool Editing;public long IgnoreBefore;
        }
        sealed class ExampleDraft {
            public FriendMemory Friend;public string Text,Saved,Error;public bool Expanded;public long IgnoreBefore;
        }
        sealed class ItemViewState {
            internal MemoryItem Item;internal bool Expanded,SourceExpanded;
        }
        sealed class DetailViewState {
            internal FriendMemory Friend;internal long IgnoreBefore;
            internal bool RecentExpanded,OutdatedExpanded;
            internal double RecentOffset,OutdatedOffset;
            internal readonly Dictionary<string,ItemViewState> Items=new Dictionary<string,ItemViewState>();
        }
        sealed class ItemView {
            internal ItemViewState State;internal Expander Item,Source;
        }
        readonly Dictionary<string,DetailViewState> detailStates=new Dictionary<string,DetailViewState>();
        readonly List<ItemView> itemViews=new List<ItemView>();
        DetailViewState detailState;ScrollViewer recentScroll,outdatedScroll;
        bool recentScrollPending,outdatedScrollPending;
        string shown="",friendsShown;bool syncingFriends;
        string Revision(){var f=Selected;return store.Data.Enabled+"|"+store.Error+"|"+store.CapacityNotice(ReadCurrent())+"|"+store.Data.LegacyPreferences+"|"+(f==null?"":PreferenceKey(f)+"|"+f.Enabled+"|"+f.IgnoreBefore+"|"+f.Preferences+"|"+ExampleText(f)+"|"+String.Join("|",f.Items.Select(i=>i.Id+":"+i.UpdatedTicks+":"+i.Superseded)));}
        FriendMemory Selected {get{var friend=friends.SelectedItem as FriendMemory;return CanManage(friend)?friend:null;}}
        public MemoryWindow(MemoryStore store,ChatSnapshot current,Action changed):this(store,current,changed,null) { }
        public MemoryWindow(MemoryStore store,ChatSnapshot current,Action changed,Func<ChatSnapshot> currentChat) {
            this.store=store;this.changed=changed;this.currentChat=currentChat??delegate{return current;};
            preferencePresets=new ReplyPreferencePicker(false,delegate{return !groupMode&&CanManage(preferenceFriend)&&Object.ReferenceEquals(preferenceFriend,Selected)&&store.CanEdit&&pref.Visibility==Visibility.Visible;},delegate(string text){prefError.Text="";prefError.Visibility=Visibility.Collapsed;pref.Text=text;CapturePreferences();pref.Focus();},delegate{return pref.Text;},delegate(string error){PreferenceError(error);});
            pref.TextChanged+=delegate{prefError.Text="";prefError.Visibility=Visibility.Collapsed;preferencePresets.Refresh(pref.Text,pref.Visibility==Visibility.Visible,CanManage(preferenceFriend)&&store.CanEdit);};
            groupMode=current!=null&&current.GroupChat;
            groupPage=new GroupMemoryPanel(store,this.currentChat,delegate{Notify();Refresh();});
            var searchHeader=new StackPanel();var searchGrid=new Grid();searchGrid.Children.Add(friendSearch);
            var placeholder=SettingsTheme.Text("搜索昵称或备注",12,SettingsTheme.Muted);placeholder.Margin=new Thickness(9,0,0,0);placeholder.IsHitTestVisible=false;searchGrid.Children.Add(placeholder);
            searchHeader.Children.Add(new Border{Background=SettingsTheme.Paper,CornerRadius=new CornerRadius(8),Child=searchGrid,Margin=new Thickness(0,0,0,5)});
            searchEmpty.Margin=new Thickness(10,12,10,12);searchEmpty.Visibility=Visibility.Collapsed;searchHeader.Children.Add(searchEmpty);
            friends=SettingsTheme.ModelPicker(searchHeader);friends.IsTextSearchEnabled=false;
            AutomationProperties.SetName(friendSearch,"搜索当前账号好友");
            friendSearch.TextChanged+=delegate{placeholder.Visibility=friendSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(!syncingFriends)Refresh();};
            friendSearch.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
                if(e.Key==Key.Escape){friends.IsDropDownOpen=false;friends.Focus();e.Handled=true;}
                else if(e.Key==Key.Down&&friends.Items.Count>0){friends.SelectedIndex=0;friends.UpdateLayout();var row=friends.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem;if(row!=null)row.Focus();e.Handled=true;}
            };
            friends.DropDownOpened+=delegate{Refresh();Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate{if(friends.IsDropDownOpen&&friends.IsEnabled)friendSearch.Focus();}));};
            Title="记忆 · 微信 AI 桌宠";Width=430;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-24;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brushes.Transparent;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;UseLayoutRounding=true;SnapsToDevicePixels=true;
            FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;AppBrand.ApplyIcon(this);
            var panel=new StackPanel{Margin=new Thickness(16)};
            Content=new Border{Background=SettingsTheme.Paper,BorderBrush=SettingsTheme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Margin=new Thickness(6),Child=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
            var header=new DockPanel();var close=SettingsTheme.Button("×","关闭记忆设置");close.Width=32;close.Padding=new Thickness(0);close.FontSize=22;close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            memoryEnabled=SettingsTheme.Switch("使用记忆",store.Data.Enabled);memoryEnabled.IsEnabled=store.CanEdit;memoryEnabled.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(memoryEnabled,Dock.Right);header.Children.Add(memoryEnabled);
            memoryEnabled.Click+=delegate{store.SetEnabled(memoryEnabled.IsChecked==true,DateTime.UtcNow.Ticks);Notify();Refresh();};
            var title=SettingsTheme.Text("记忆",21);header.Children.Add(title);SettingsTheme.EnableHeaderDrag(this,header);panel.Children.Add(header);
            accountHint.Margin=new Thickness(0,3,0,10);accountHint.ToolTip="只显示当前已识别微信账号的记忆；好友和群聊分别保存。";panel.Children.Add(accountHint);
            var tabs=new System.Windows.Controls.Primitives.UniformGrid{Rows=1,Columns=2};tabs.Children.Add(friendTab);tabs.Children.Add(groupTab);
            friendTab.Click+=delegate{SelectMemoryKind(false);};groupTab.Click+=delegate{SelectMemoryKind(true);};
            panel.Children.Add(new Border{Background=SettingsTheme.Brush("#EFF2ED"),CornerRadius=new CornerRadius(11),Padding=new Thickness(3),Margin=new Thickness(0,0,0,10),Child=tabs});
            capacityManage.Height=26;capacityManage.Padding=new Thickness(6,0,6,0);DockPanel.SetDock(capacityManage,Dock.Right);capacityRow.Children.Add(capacityManage);
            AutomationProperties.SetName(capacityHint,"记忆容量提示");capacityRow.Children.Add(capacityHint);panel.Children.Add(capacityRow);
            panel.Children.Add(friendPage);panel.Children.Add(groupPage);
            var labelFactory=new FrameworkElementFactory(typeof(TextBlock));labelFactory.SetBinding(TextBlock.TextProperty,new Binding("DisplayName"));labelFactory.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);labelFactory.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);friends.ItemTemplate=new DataTemplate{VisualTree=labelFactory};friends.Height=36;friends.Margin=new Thickness(0,0,0,10);
            AutomationProperties.SetName(friends,"好友记忆");
            friends.SelectionChanged+=delegate{if(syncingFriends)return;Refresh();};
            var friendHeading=new DockPanel{Margin=new Thickness(0,0,0,4)};locateFriend.Height=24;locateFriend.Padding=new Thickness(6,0,6,0);locateFriend.FontSize=11;locateFriend.Click+=delegate{LocateCurrentFriend();};DockPanel.SetDock(locateFriend,Dock.Right);friendHeading.Children.Add(locateFriend);friendHeading.Children.Add(SettingsTheme.Text("好友",11,SettingsTheme.Muted));friendPage.Children.Add(friendHeading);friendPage.Children.Add(friends);
            var preferences=new StackPanel();var prefHeader=new DockPanel();preferences.Children.Add(prefHeader);
            prefSummary.TextWrapping=TextWrapping.NoWrap;prefSummary.TextTrimming=TextTrimming.CharacterEllipsis;prefSummary.Margin=new Thickness(0,2,0,2);preferences.Children.Add(prefSummary);
            preferences.Children.Add(preferencePresets.Control);
            pref.Height=104;pref.MaxHeight=104;pref.Padding=new Thickness(9);pref.Margin=new Thickness(0,5,0,0);pref.Visibility=Visibility.Collapsed;pref.ToolTip="可写你们的关系、对方背景和回复方式。例如：认识多年的朋友，做设计；聊天自然亲近，不说教。最多 400 字，仅用于这位好友。";preferences.Children.Add(pref);
            prefError.Margin=new Thickness(0,5,0,0);prefError.Visibility=Visibility.Collapsed;AutomationProperties.SetName(prefError,"好友设定提示");preferences.Children.Add(prefError);
            savePref.Height=26;savePref.Click+=delegate{EditOrSavePreferences(preferenceFriend);};DockPanel.SetDock(savePref,Dock.Right);prefHeader.Children.Add(savePref);var prefTitle=SettingsTheme.Text("好友设定");prefTitle.ToolTip="手动描述你们的关系、对方背景和相处方式；不会从聊天中猜测或自动改写。";prefHeader.Children.Add(prefTitle);
            var exampleBody=new StackPanel();
            var exampleHelp=SettingsTheme.Text("写几句你平时对这位好友说的话，只参考语气",11,SettingsTheme.Muted);exampleHelp.Margin=new Thickness(0,6,0,0);exampleBody.Children.Add(exampleHelp);
            examples.Height=82;examples.MaxHeight=82;examples.Margin=new Thickness(0,6,0,4);examples.ToolTip="每行一句，最多 5 条，每条 200 字";exampleBody.Children.Add(examples);
            var exampleActions=new DockPanel();saveExamples.Height=26;saveExamples.Click+=delegate{SaveExpressionExamples(exampleFriend);};DockPanel.SetDock(saveExamples,Dock.Right);exampleActions.Children.Add(saveExamples);exampleActions.Children.Add(SettingsTheme.Text("每行一句 · 最多 5 条，每条 200 字",11,SettingsTheme.Muted));exampleBody.Children.Add(exampleActions);
            examplesError.Visibility=Visibility.Collapsed;exampleBody.Children.Add(examplesError);examplesSection.Content=exampleBody;AutomationProperties.SetName(examplesSection,"表达示例");preferences.Children.Add(examplesSection);
            var prefCard=SettingsTheme.Card(preferences);prefCard.Padding=new Thickness(10);prefCard.Margin=new Thickness(0,0,0,4);friendPage.Children.Add(prefCard);
            legacyReview.Height=28;legacyReview.HorizontalAlignment=HorizontalAlignment.Left;legacyReview.Margin=new Thickness(0,2,0,0);legacyReview.Click+=delegate{ReviewLegacyPreferences(preferenceFriend);};friendPage.Children.Add(legacyReview);
            friendPage.Children.Add(details);friendPage.Children.Add(note);
            var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};
            var menu=new ContextMenu{FontFamily=FontFamily,FontSize=13};
            var clearFriend=new MenuItem{Header="清空此好友记忆"};clearFriend.Click+=delegate{
                var f=Selected;if(f==null)return;
                if(MessageBox.Show(this,"清空当前账号中「"+f.Name+"」的回复偏好、表达示例和记忆？好友仍保留，旧聊天不会重新学习。","清空此好友记忆",MessageBoxButton.OKCancel)==MessageBoxResult.OK&&CanManage(f)){store.ClearFriend(f,DateTime.UtcNow.Ticks);if(store.Error==null){ForgetPreferenceDraft(f);ForgetExampleDraft(f);}Notify();Refresh();}
            };menu.Items.Add(clearFriend);
            var removeFriend=new MenuItem{Header="移除好友记录"};removeFriend.Click+=delegate{
                var f=Selected;if(f==null)return;
                if(MessageBox.Show(this,"移除当前账号中「"+f.Name+"」的本机记忆和列表记录？不会删除微信好友。对方以后发来新消息时可以重新记忆。","移除好友记录",MessageBoxButton.OKCancel)==MessageBoxResult.OK&&CanManage(f)){if(store.RemoveFriend(f,DateTime.UtcNow.Ticks)){ForgetPreferenceDraft(f);ForgetExampleDraft(f);}Notify();Refresh();}
            };menu.Items.Add(removeFriend);menu.Items.Add(new Separator());
            var clear=new MenuItem{Header="清空当前账号记忆"};clear.Click+=delegate{
                string account=CurrentAccount();if(account==null||account!=scopeAccount)return;
                if(MessageBox.Show(this,"清空当前微信账号的全部好友和群聊记忆，包括回复偏好、表达示例和群内约定？其他微信账号不受影响，旧聊天不会重新学习。","清空当前账号记忆",MessageBoxButton.OKCancel)==MessageBoxResult.OK&&CurrentAccount()==account&&scopeAccount==account){store.ClearAccount(account,DateTime.UtcNow.Ticks);Notify();Refresh();}
            };menu.Items.Add(clear);
            var more=SettingsTheme.Button("···","更多记忆操作");more.ToolTip="清空记忆或移除好友";more.Width=32;more.Height=26;more.Padding=new Thickness(0);more.ContextMenu=menu;
            Action<Button> openCleanup=delegate(Button trigger){Refresh();clearFriend.IsEnabled=removeFriend.IsEnabled=trigger==more&&Selected!=null;clear.IsEnabled=scopeAccount!=null&&scopeAccount==CurrentAccount();menu.PlacementTarget=trigger;menu.IsOpen=true;};
            more.Click+=delegate{openCleanup(more);};capacityManage.Click+=delegate{openCleanup(capacityManage);};DockPanel.SetDock(more,Dock.Right);footer.Children.Add(more);
            var scope=SettingsTheme.Text("仅从当前打开的私聊更新",11,SettingsTheme.Muted);scope.ToolTip="微信未最小化时可继续监听当前私聊；不会自动切换好友。只记录实际聊天，草稿和推荐不会进入记忆。";footer.Children.Add(scope);friendPage.Children.Add(footer);
            RefreshFriends(store.Current(current));
            Refresh();
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
            timer.Tick+=delegate{if(IsVisible&&OwnedWindows.Count==0){bool groupsChanged=groupPage.Refresh();if(RefreshFriends()||shown!=Revision()||groupsChanged)Refresh();}};
            Closed+=delegate{timer.Stop();};timer.Start();
        }
        static TextBox Editor(string text,double height,string label,int max) {
            var box=new TextBox{Text=text??"",MinHeight=height,MaxHeight=140,MaxLength=max,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderBrush=SettingsTheme.Line,Padding=new Thickness(9),Margin=new Thickness(0,8,0,8)};AutomationProperties.SetName(box,label);return box;
        }
        static string PreferenceKey(FriendMemory f){return (f.AccountId??"").Length+":"+f.AccountId+":"+f.Id;}
        ChatSnapshot ReadCurrent(){try{return currentChat();}catch{return null;}}
        string CurrentAccount(){return AccountOf(ReadCurrent());}
        static string AccountOf(ChatSnapshot snapshot){return MemoryStore.Identified(snapshot)||MemoryStore.GroupIdentified(snapshot)?snapshot.AccountId:null;}
        void SelectMemoryKind(bool groups){CapturePreferences();CaptureExamples();CaptureDetailState();groupMode=groups;Refresh();}
        void RefreshMemoryKind() {
            friendPage.Visibility=groupMode?Visibility.Collapsed:Visibility.Visible;groupPage.Visibility=groupMode?Visibility.Visible:Visibility.Collapsed;
            friendTab.Background=groupMode?Brushes.Transparent:Brushes.White;groupTab.Background=groupMode?Brushes.White:Brushes.Transparent;
            friendTab.Foreground=groupMode?SettingsTheme.Muted:SettingsTheme.Ink;groupTab.Foreground=groupMode?SettingsTheme.Ink:SettingsTheme.Muted;
            friendTab.FontWeight=groupMode?FontWeights.Normal:FontWeights.SemiBold;groupTab.FontWeight=groupMode?FontWeights.SemiBold:FontWeights.Normal;
            accountHint.Text=scopeAccount==null?"打开微信聊天以确认账号":"当前微信账号 · 记忆独立保存";
        }
        bool HasFriend(FriendMemory f){return f!=null&&store.Data.Friends.Any(x=>Object.ReferenceEquals(x,f));}
        bool CanManage(FriendMemory f){return HasFriend(f)&&scopeAccount!=null&&f.AccountId==scopeAccount&&CurrentAccount()==scopeAccount;}
        bool DraftCurrent(PreferenceDraft draft){return HasFriend(draft.Friend)&&draft.IgnoreBefore==draft.Friend.IgnoreBefore&&draft.Saved==(draft.Friend.Preferences??"");}
        void CapturePreferences() {
            if(preferenceFriend==null)return;
            PreferenceDraft draft;
            if(preferenceDrafts.TryGetValue(PreferenceKey(preferenceFriend),out draft)&&Object.ReferenceEquals(draft.Friend,preferenceFriend)&&DraftCurrent(draft)) {
                draft.Text=pref.Text;draft.Editing=pref.Visibility==Visibility.Visible;draft.Error=prefError.Text;
            }
        }
        void ForgetPreferenceDraft(FriendMemory friend) {
            preferenceDrafts.Remove(PreferenceKey(friend));
            if(Object.ReferenceEquals(preferenceFriend,friend))preferenceFriend=null;
        }
        void RefreshPreferences() {
            CapturePreferences();
            // Cache only drafts for the same live account/friend object. Clearing or
            // deleting memory invalidates the editor as well as the stored text.
            foreach(var staleKey in preferenceDrafts.Where(p=>p.Key!=PreferenceKey(p.Value.Friend)||!DraftCurrent(p.Value)).Select(p=>p.Key).ToList())preferenceDrafts.Remove(staleKey);
            var friend=Selected;preferenceFriend=friend;
            bool canEdit=CanManage(friend)&&store.CanEdit;savePref.IsEnabled=canEdit;pref.IsEnabled=canEdit;
            legacyReview.Visibility=String.IsNullOrWhiteSpace(store.Data.LegacyPreferences)?Visibility.Collapsed:Visibility.Visible;
            legacyReview.IsEnabled=store.CanEdit;
            if(friend==null) {
                pref.Text="";prefError.Text="";prefError.Visibility=Visibility.Collapsed;pref.Visibility=Visibility.Collapsed;prefSummary.Visibility=Visibility.Visible;prefSummary.Text="先选择好友";prefSummary.ToolTip=null;savePref.Content="编辑";preferencePresets.Refresh("",false,false);return;
            }
            string key=PreferenceKey(friend);PreferenceDraft draft;
            if(!preferenceDrafts.TryGetValue(key,out draft)) {
                draft=new PreferenceDraft{Friend=friend,Text=friend.Preferences??"",Saved=friend.Preferences??"",IgnoreBefore=friend.IgnoreBefore,Error=""};preferenceDrafts[key]=draft;
            }
            if(pref.Text!=draft.Text)pref.Text=draft.Text;
            pref.Visibility=draft.Editing?Visibility.Visible:Visibility.Collapsed;prefSummary.Visibility=draft.Editing?Visibility.Collapsed:Visibility.Visible;savePref.Content=draft.Editing?"保存":"编辑";
            preferencePresets.Refresh(pref.Text,draft.Editing,canEdit);
            prefError.Text=draft.Error??"";prefError.Visibility=String.IsNullOrEmpty(prefError.Text)?Visibility.Collapsed:Visibility.Visible;
            prefSummary.Text=String.IsNullOrWhiteSpace(friend.Preferences)?"未设置，按当前聊天回复":friend.Preferences;prefSummary.ToolTip=friend.Preferences;
        }
        void PreferenceError(string text){prefError.Text=text;prefError.Visibility=Visibility.Visible;CapturePreferences();}
        void EditOrSavePreferences(FriendMemory friend) {
            // The editor owns this captured friend; never resolve a target from the
            // picker after editing begins or after a save/notification callback.
            if(groupMode||!CanManage(friend)||!Object.ReferenceEquals(friend,preferenceFriend)||!Object.ReferenceEquals(friend,Selected)||!store.CanEdit)return;
            if(pref.Visibility!=Visibility.Visible){pref.Visibility=Visibility.Visible;prefSummary.Visibility=Visibility.Collapsed;savePref.Content="保存";preferencePresets.Refresh(pref.Text,true,true);CapturePreferences();pref.Focus();return;}
            if(pref.Text.Length>400){PreferenceError("最多 400 字，请精简后保存");return;}
            store.SetPreferences(friend,pref.Text);Notify();if(store.Error!=null)return;
            ForgetPreferenceDraft(friend);Refresh();
        }
        static string ExampleText(FriendMemory friend){return String.Join("\n",friend.ExpressionExamples??new List<string>());}
        bool ExampleDraftCurrent(ExampleDraft draft){return HasFriend(draft.Friend)&&draft.IgnoreBefore==draft.Friend.IgnoreBefore&&draft.Saved==ExampleText(draft.Friend);}
        void CaptureExamples() {
            if(exampleFriend==null)return;
            ExampleDraft draft;
            if(exampleDrafts.TryGetValue(PreferenceKey(exampleFriend),out draft)&&Object.ReferenceEquals(draft.Friend,exampleFriend)&&ExampleDraftCurrent(draft)) {
                draft.Text=examples.Text;draft.Expanded=examplesSection.IsExpanded;draft.Error=examplesError.Text;
            }
        }
        void ForgetExampleDraft(FriendMemory friend) {
            exampleDrafts.Remove(PreferenceKey(friend));
            if(Object.ReferenceEquals(exampleFriend,friend))exampleFriend=null;
        }
        void RefreshExamples() {
            CaptureExamples();
            foreach(var staleKey in exampleDrafts.Where(p=>p.Key!=PreferenceKey(p.Value.Friend)||!ExampleDraftCurrent(p.Value)).Select(p=>p.Key).ToList())exampleDrafts.Remove(staleKey);
            var friend=Selected;exampleFriend=friend;
            bool canEdit=CanManage(friend)&&store.CanEdit;examplesSection.IsEnabled=canEdit;examples.IsEnabled=canEdit;saveExamples.IsEnabled=canEdit;
            if(friend==null){examples.Text="";examplesSection.IsExpanded=false;examplesSection.Header="表达示例";examplesError.Text="";examplesError.Visibility=Visibility.Collapsed;return;}
            string key=PreferenceKey(friend);ExampleDraft draft;
            if(!exampleDrafts.TryGetValue(key,out draft)){draft=new ExampleDraft{Friend=friend,Text=ExampleText(friend),Saved=ExampleText(friend),IgnoreBefore=friend.IgnoreBefore,Error=""};exampleDrafts[key]=draft;}
            if(examples.Text!=draft.Text)examples.Text=draft.Text;
            examplesSection.IsExpanded=draft.Expanded;int count=friend.ExpressionExamples==null?0:friend.ExpressionExamples.Count;
            examplesSection.Header="表达示例"+(count>0?" · "+count:"");examplesError.Text=draft.Error??"";examplesError.Visibility=String.IsNullOrEmpty(examplesError.Text)?Visibility.Collapsed:Visibility.Visible;
        }
        void SaveExpressionExamples(FriendMemory friend) {
            if(!CanManage(friend)||!Object.ReferenceEquals(friend,exampleFriend)||!store.CanEdit)return;
            try {
                store.SetExpressionExamples(friend,examples.Text.Split(new[]{"\r\n","\n","\r"},StringSplitOptions.None));
                if(store.Error!=null){examplesError.Text=store.Error;examplesError.Visibility=Visibility.Visible;CaptureExamples();Notify();return;}
                ForgetExampleDraft(friend);Notify();Refresh();
            }catch(InvalidOperationException e){examplesError.Text=e.Message;examplesError.Visibility=Visibility.Visible;CaptureExamples();}
        }
        void ReviewLegacyPreferences(FriendMemory friend) {
            string legacy=store.Data.LegacyPreferences;if(String.IsNullOrWhiteSpace(legacy))return;
            var dialog=new Window{Title="旧版回复偏好",Owner=this,Width=380,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=SettingsTheme.Paper,FontFamily=FontFamily};
            var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=panel;
            panel.Children.Add(SettingsTheme.Text("尚未指定好友，不用于推荐",12,SettingsTheme.Muted));
            var text=Editor(legacy,110,"旧版回复偏好内容",0);text.IsReadOnly=true;panel.Children.Add(text);
            var destination=SettingsTheme.Text(CanManage(friend)?"用于回复 "+friend.Name:"请先在记忆设置中选择好友",12,SettingsTheme.Muted);panel.Children.Add(destination);
            var error=SettingsTheme.Text("",11,SettingsTheme.Muted);panel.Children.Add(error);
            var apply=SettingsTheme.Button("用于这位好友","使用旧版回复偏好",true);apply.Margin=new Thickness(0,12,0,0);apply.IsEnabled=CanManage(friend)&&store.CanEdit;
            apply.Click+=delegate {
                if(!CanManage(friend)||store.Data.LegacyPreferences!=legacy){error.Text="账号或内容已变化，请关闭后重新查看";return;}
                CapturePreferences();PreferenceDraft draft;
                bool unsaved=preferenceDrafts.TryGetValue(PreferenceKey(friend),out draft)&&draft.Editing&&draft.Text!=draft.Saved;
                if((!String.IsNullOrWhiteSpace(friend.Preferences)||unsaved)&&MessageBox.Show(dialog,"将替换这位好友已有或尚未保存的回复偏好，是否继续？","使用旧版偏好",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
                if(!CanManage(friend)){error.Text="微信账号已变化，请关闭后重试";return;}
                store.UseLegacyPreferences(friend);Notify();if(store.Error!=null){error.Text=store.Error;return;}
                ForgetPreferenceDraft(friend);dialog.Close();Refresh();
            };
            panel.Children.Add(apply);dialog.ShowDialog();
        }
        void Notify(){changed();note.Text=store.Error??"已保存";note.Visibility=Visibility.Visible;}
        internal void Restore() {
            // Hidden settings retain edits; refresh their account scope before
            // showing anything after a possible WeChat account switch.
            Refresh();Show();Activate();
        }
        void LocateCurrentFriend() {
            RefreshFriends();var current=store.Current(ReadCurrent());if(!CanManage(current))return;
            syncingFriends=true;try{friendSearch.Text="";}finally{syncingFriends=false;}
            friendsShown=null;RefreshFriends(current);friends.IsDropDownOpen=false;Refresh();
        }
        bool RefreshFriends(FriendMemory preferred=null) {
            var snapshot=ReadCurrent();string account=AccountOf(snapshot);
            bool accountChanged=scopeAccount!=account;
            var old=preferred??friends.SelectedItem as FriendMemory;
            CapturePreferences();CaptureExamples();CaptureDetailState();
            if(accountChanged){scopeAccount=account;syncingFriends=true;try{friendSearch.Text="";friends.IsDropDownOpen=false;}finally{syncingFriends=false;}}
            var roster=store.Data.Friends.Where(f=>account!=null&&f.AccountId==account).ToList();
            var current=account==null?null:store.Current(snapshot);
            locateFriend.IsEnabled=current!=null;friends.IsEnabled=account!=null&&roster.Count>0;friendSearch.IsEnabled=friends.IsEnabled;
            accountHint.Text=account==null?"打开微信聊天以确认账号":"当前微信账号 · 记忆独立保存";
            string query=friendSearch.Text.Trim();
            // Refresh recent ordering while closed; incoming messages must not
            // move an option underneath the pointer while the picker is open.
            string revision=(account??"")+"|"+(current==null?"":current.Id)+"|"+query.Length+":"+query+"|"+String.Join("|",roster.Select(f=>f.AccountId+":"+f.Id+":"+(f.Name??"").Length+":"+f.Name+":"+(friends.IsDropDownOpen?0:f.UpdatedTicks)));
            if(!accountChanged&&friendsShown==revision&&friends.Items.Cast<FriendMemory>().All(HasFriend))return false;
            foreach(var group in roster.GroupBy(f=>f.Name)){int index=0;foreach(var f in group.OrderBy(f=>f.Id))f.DisplayName=f.Name+(group.Count()>1?" · 联系人 "+(++index):"")+(Object.ReferenceEquals(f,current)?" · 当前聊天":"");}
            var visible=roster.Where(f=>query.Length==0||(f.Name??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(f=>Object.ReferenceEquals(f,current)).ThenByDescending(f=>f.UpdatedTicks).ThenBy(f=>f.Name).ToList();
            var selected=old==null?null:visible.FirstOrDefault(f=>f.Id==old.Id&&f.AccountId==old.AccountId);
            if(accountChanged||selected==null)selected=visible.FirstOrDefault(f=>Object.ReferenceEquals(f,current))??(query.Length==0?visible.FirstOrDefault():null);
            syncingFriends=true;
            try {
                friends.ItemsSource=visible;
                friends.SelectedItem=selected;
            }finally{syncingFriends=false;}
            searchEmpty.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
            friendsShown=revision;return true;
        }
        void CaptureDetailState() {
            if(detailState==null)return;
            foreach(var view in itemViews){if(view.Item!=null)view.State.Expanded=view.Item.IsExpanded;if(view.Source!=null)view.State.SourceExpanded=view.Source.IsExpanded;}
            // A just-built ScrollViewer has not applied its restore request yet.
            // Do not replace the saved offset with its temporary zero offset.
            if(!recentScrollPending&&detailState.RecentExpanded&&recentScroll!=null)detailState.RecentOffset=recentScroll.VerticalOffset;
            if(!outdatedScrollPending&&detailState.OutdatedExpanded&&outdatedScroll!=null)detailState.OutdatedOffset=outdatedScroll.VerticalOffset;
        }
        void PrepareDetailState(FriendMemory friend) {
            CaptureDetailState();
            foreach(var key in detailStates.Where(p=>!HasFriend(p.Value.Friend)||p.Value.IgnoreBefore!=p.Value.Friend.IgnoreBefore||p.Key!=PreferenceKey(p.Value.Friend)).Select(p=>p.Key).ToList())detailStates.Remove(key);
            itemViews.Clear();recentScroll=null;outdatedScroll=null;detailState=null;recentScrollPending=false;outdatedScrollPending=false;
            if(friend==null)return;
            string id=PreferenceKey(friend);
            if(!detailStates.TryGetValue(id,out detailState)){detailState=new DetailViewState{Friend=friend,IgnoreBefore=friend.IgnoreBefore};detailStates[id]=detailState;}
            foreach(var key in detailState.Items.Where(p=>!friend.Items.Any(i=>i.Id==p.Key&&Object.ReferenceEquals(i,p.Value.Item))).Select(p=>p.Key).ToList())detailState.Items.Remove(key);
        }
        ItemView ItemState(MemoryItem item) {
            ItemViewState state;
            if(!detailState.Items.TryGetValue(item.Id,out state)){state=new ItemViewState{Item=item};detailState.Items[item.Id]=state;}
            var view=new ItemView{State=state};itemViews.Add(view);return view;
        }
        void RestoreDetailScroll(bool recentArchive) {
            var captured=detailState;if(captured==null)return;
            var scroll=recentArchive?recentScroll:outdatedScroll;if(scroll==null)return;
            if(recentArchive)recentScrollPending=true;else outdatedScrollPending=true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {
                if(detailState!=captured||(recentArchive?recentScroll:outdatedScroll)!=scroll)return;
                // A collapsed archive has no measured viewport. Keep its pending
                // offset until Expanded schedules restoration after layout.
                if(!(recentArchive?captured.RecentExpanded:captured.OutdatedExpanded))return;
                scroll.UpdateLayout();if(scroll.ViewportHeight<=0)return;
                scroll.ScrollToVerticalOffset(recentArchive?captured.RecentOffset:captured.OutdatedOffset);scroll.UpdateLayout();
                if(recentArchive)recentScrollPending=false;else outdatedScrollPending=false;
            }));
        }
        void Refresh() {
            memoryEnabled.IsChecked=store.Data.Enabled;memoryEnabled.IsEnabled=store.CanEdit;
            RefreshFriends();
            var snapshot=ReadCurrent();capacityHint.Text=store.CapacityNotice(snapshot)??"";capacityRow.Visibility=capacityHint.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
            capacityManage.IsEnabled=scopeAccount!=null&&scopeAccount==AccountOf(snapshot)&&(snapshot.GroupChat?store.Data.Groups.Any(group=>group.AccountId==scopeAccount):store.Data.Friends.Any(friend=>friend.AccountId==scopeAccount));
            groupPage.Refresh();RefreshMemoryKind();
            RefreshPreferences();
            RefreshExamples();
            var f=Selected;if(f!=null)store.Refresh(f,DateTime.UtcNow.Ticks);
            PrepareDetailState(f);details.Children.Clear();note.Text=store.Error??"";note.Visibility=store.Error==null?Visibility.Collapsed:Visibility.Visible;
            if(f==null){shown=Revision();details.Children.Add(SettingsTheme.Text(scopeAccount==null?"确认当前账号后显示好友记忆。":friendSearch.Text.Trim().Length>0?"请在搜索结果中选择好友。":"此账号暂无好友记忆。",12,SettingsTheme.Muted));return;}
            shown=Revision();
            var enabledRow=new DockPanel{Margin=new Thickness(0,10,0,8)};var enabled=SettingsTheme.Switch("使用这位好友的记忆",f.Enabled);enabled.IsEnabled=store.CanEdit;enabled.Click+=delegate{if(!CanManage(f))return;store.SetFriendEnabled(f,enabled.IsChecked==true,DateTime.UtcNow.Ticks);Notify();Refresh();};DockPanel.SetDock(enabled,Dock.Right);enabledRow.Children.Add(enabled);enabledRow.Children.Add(SettingsTheme.Text("记住这位好友",12));details.Children.Add(enabledRow);
            if(!store.Data.Enabled||!f.Enabled){var paused=SettingsTheme.Text("记忆已暂停，已有内容仍保留",11,SettingsTheme.Muted);paused.Margin=new Thickness(0,0,0,6);details.Children.Add(paused);}
            int activeCount=f.Items.Count(i=>!i.Superseded&&MemoryStore.Subject(i)=="peer"&&(i.Manual||i.Pinned||i.Kind=="stated"));
            var headingRow=new DockPanel{Margin=new Thickness(0,2,0,0)};
            var add=SettingsTheme.Button("＋ 添加","添加好友记忆");add.Height=28;add.Click+=delegate{Edit(f,null);};DockPanel.SetDock(add,Dock.Right);headingRow.Children.Add(add);
            var heading=SettingsTheme.Text("自动记忆"+(activeCount>0?"（"+activeCount+"）":""));heading.ToolTip="自动记住对方明确表达的喜好和习惯；也可手动补充。改口以最新明确表达为准，关系和身份由你确认。";headingRow.Children.Add(heading);details.Children.Add(headingRow);
            if(activeCount==0){var empty=SettingsTheme.Text("暂未记住明确偏好",12,SettingsTheme.Muted);empty.Margin=new Thickness(0,4,0,6);details.Children.Add(empty);}
            var pending=new StackPanel();int pendingCount=0;
            var recent=new StackPanel();int recentCount=0;var outdated=new StackPanel();int outdatedCount=0;
            foreach(var item in f.Items.OrderByDescending(i=>i.Pinned).ThenByDescending(i=>i.UpdatedTicks).ToList()) {
                var view=ItemState(item);
                var captured=item;var body=new StackPanel();
                body.Children.Add(SettingsTheme.Text(item.Text));
                string label=item.Manual?(MemoryStore.Subject(item)==null?"尚未确认归属，不用于推荐":MemoryStore.Subject(item)=="self"?"关于我 · 你已确认":"关于好友 · 你已确认"):item.Kind=="stated"?"好友自述 · 未核实":"近期摘录 · "+(item.SourceRole=="self"?"我已发送":"对方");
                if(item.Pinned)label+=" · 已固定";
                if(item.Superseded)label+=" · 已更新，不用于推荐";
                var metadata=SettingsTheme.Text(label+" · "+new DateTime(item.SourceTicks,DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm"),11,SettingsTheme.Muted);metadata.Margin=new Thickness(0,6,0,0);body.Children.Add(metadata);
                if(!String.IsNullOrEmpty(item.SourceText)){var source=new Expander{Header="查看来源",IsExpanded=view.State.SourceExpanded,FontSize=11,Margin=new Thickness(0,6,0,0),Content=SettingsTheme.Text(item.SourceText,12,SettingsTheme.Muted)};view.Source=source;body.Children.Add(source);}
                var actions=new StackPanel{Orientation=Orientation.Horizontal};
                var edit=SettingsTheme.Button("编辑","编辑记忆");edit.Click+=delegate{Edit(f,captured);};actions.Children.Add(edit);
                var remove=SettingsTheme.Button("删除","删除记忆");remove.Click+=delegate{if(!CanManage(f))return;store.Remove(f,captured);Notify();Refresh();};actions.Children.Add(remove);body.Children.Add(actions);
                var card=SettingsTheme.Card(body);card.Padding=new Thickness(10);card.Margin=new Thickness(0,6,0,0);
                if(item.Superseded){outdated.Children.Add(card);outdatedCount++;}
                else if(item.Manual&&MemoryStore.Subject(item)==null) {
                    // An old unassigned note is visible but cannot look like an
                    // active friend preference until the user assigns it.
                    card.Child=null;
                    var row=new DockPanel{Margin=new Thickness(0,5,0,0)};
                    var confirm=SettingsTheme.Button("用于此好友","确认此备注用于当前好友");confirm.Height=26;confirm.Padding=new Thickness(6,0,6,0);confirm.IsEnabled=store.CanEdit;
                    confirm.ToolTip="确认这条备注描述的是 "+f.Name;confirm.Click+=delegate{ConfirmFriendNote(f,captured);};DockPanel.SetDock(confirm,Dock.Right);row.Children.Add(confirm);
                    var excerpt=SettingsTheme.Text(item.Text,12);excerpt.TextWrapping=TextWrapping.NoWrap;excerpt.TextTrimming=TextTrimming.CharacterEllipsis;excerpt.MaxWidth=238;
                    view.Item=new Expander{Header=excerpt,Content=body,IsExpanded=view.State.Expanded,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                    row.Children.Add(view.Item);pending.Children.Add(row);pendingCount++;
                }
                else if(MemoryStore.Subject(item)=="self"||item.Kind=="recent"&&!item.Manual&&!item.Pinned){
                    var summary=new StackPanel{Width=290};var excerpt=SettingsTheme.Text(item.Text,12);excerpt.TextWrapping=TextWrapping.NoWrap;excerpt.TextTrimming=TextTrimming.CharacterEllipsis;summary.Children.Add(excerpt);
                    var stamp=SettingsTheme.Text((item.SourceRole=="self"?"我":"对方")+" · "+new DateTime(item.SourceTicks,DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm"),11,SettingsTheme.Muted);stamp.Margin=new Thickness(0,3,0,0);summary.Children.Add(stamp);
                    view.Item=new Expander{Header=summary,Content=body,IsExpanded=view.State.Expanded,HorizontalContentAlignment=HorizontalAlignment.Stretch};card.Child=view.Item;card.Padding=new Thickness(7);recent.Children.Add(card);recentCount++;
                }else details.Children.Add(card);
            }
            if(pendingCount>0){var pendingTitle=SettingsTheme.Text("待确认（"+pendingCount+"）",12,SettingsTheme.Muted);pendingTitle.Margin=new Thickness(0,9,0,0);details.Children.Add(pendingTitle);details.Children.Add(pending);}
            if(recentCount>0)AddArchive("近期聊天（"+recentCount+"）",recent,true);else detailState.RecentOffset=0;
            if(outdatedCount>0)AddArchive("已更新的旧记录（"+outdatedCount+"）",outdated,false);else detailState.OutdatedOffset=0;
        }
        void AddArchive(string title,StackPanel items,bool recentArchive) {
            var state=detailState;var section=Archive(title,items,recentArchive?state.RecentExpanded:state.OutdatedExpanded);
            var scroll=(ScrollViewer)section.Content;if(recentArchive)recentScroll=scroll;else outdatedScroll=scroll;
            section.Expanded+=delegate(object sender,RoutedEventArgs e){
                if(e.OriginalSource!=section||detailState!=state||(recentArchive?recentScroll:outdatedScroll)!=scroll)return;
                if(recentArchive)state.RecentExpanded=true;else state.OutdatedExpanded=true;
                RestoreDetailScroll(recentArchive);
            };
            section.Collapsed+=delegate(object sender,RoutedEventArgs e){
                if(e.OriginalSource!=section||detailState!=state||(recentArchive?recentScroll:outdatedScroll)!=scroll)return;
                // Capture before the collapse layout can reset VerticalOffset.
                if(recentArchive){if(!recentScrollPending)state.RecentOffset=scroll.VerticalOffset;state.RecentExpanded=false;}
                else {if(!outdatedScrollPending)state.OutdatedOffset=scroll.VerticalOffset;state.OutdatedExpanded=false;}
            };
            scroll.Loaded+=delegate{if(detailState==state&&(recentArchive?recentScroll:outdatedScroll)==scroll)RestoreDetailScroll(recentArchive);};
            details.Children.Add(section);RestoreDetailScroll(recentArchive);
        }
        Expander Archive(string title,StackPanel items,bool expanded) {
            return new Expander{Header=title,IsExpanded=expanded,Margin=new Thickness(0,10,0,0),Content=new ScrollViewer{Content=items,MaxHeight=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,3,0)}};
        }
        void ConfirmFriendNote(FriendMemory friend,MemoryItem item) {
            if(!store.CanEdit||!CanManage(friend)||!Object.ReferenceEquals(Selected,friend)||item==null||!friend.Items.Contains(item)||!item.Manual||item.Superseded||MemoryStore.Subject(item)!=null||String.IsNullOrWhiteSpace(item.Text))return;
            try {
                store.Put(friend,item,item.Text,item.Pinned,DateTime.UtcNow.Ticks);
                if(store.Error==null)Notify();
                Refresh();
            }catch(InvalidOperationException e){note.Text=e.Message;note.Visibility=Visibility.Visible;}
        }
        void Edit(FriendMemory friend,MemoryItem item) {
            if(!CanManage(friend)||!store.CanEdit)return;
            var dialog=new Window{Title=item==null?"添加记忆":"编辑记忆",Owner=this,Width=380,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=SettingsTheme.Paper,FontFamily=FontFamily};
            var panel=new StackPanel{Margin=new Thickness(20)};dialog.Content=panel;
            panel.Children.Add(SettingsTheme.Text(item!=null&&MemoryStore.Subject(item)=="self"?"关于我 · 与 "+friend.Name+" 的聊天":"关于 "+friend.Name));var text=Editor(item==null?"":item.Text,110,"记忆内容",400);panel.Children.Add(text);
            var pin=new CheckBox{Content="固定保留",IsChecked=item!=null&&item.Pinned,Margin=new Thickness(0,4,0,14)};panel.Children.Add(pin);
            var error=SettingsTheme.Text("",11,SettingsTheme.Muted);panel.Children.Add(error);
            var save=SettingsTheme.Button("保存","保存记忆",true);save.Click+=delegate{
                if(!CanManage(friend)){error.Text="微信账号已变化，请关闭后重试";return;}
                if(String.IsNullOrWhiteSpace(text.Text)){error.Text="请填写记忆内容";return;}
                try{store.Put(friend,item,text.Text,pin.IsChecked==true,DateTime.UtcNow.Ticks);Notify();if(store.Error!=null){error.Text=store.Error;return;}dialog.Close();Refresh();}catch(InvalidOperationException e){error.Text=e.Message;}
            };panel.Children.Add(save);dialog.ShowDialog();
        }
    }
}
