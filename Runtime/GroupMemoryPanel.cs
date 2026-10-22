using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WeChatPet.Runtime {
    // Group settings deliberately have no friend excerpts or expression examples.
    public sealed class GroupMemoryPanel : StackPanel {
        readonly MemoryStore store;readonly Func<ChatSnapshot> currentChat;readonly Action changed;
        readonly ComboBox groups;
        readonly TextBox groupSearch=new TextBox{Padding=new Thickness(9,7,9,7),BorderThickness=new Thickness(0),Background=Brushes.Transparent,MaxLength=80};
        readonly TextBlock searchEmpty=SettingsTheme.Text("没有匹配的群聊",12,SettingsTheme.Muted);
        readonly TextBlock empty=SettingsTheme.Text("",12,SettingsTheme.Muted);
        readonly TextBlock error=SettingsTheme.Text("",11,SettingsTheme.Muted);
        readonly TextBlock scopeNote=SettingsTheme.Text("仅用于本群 · 手动维护",11,SettingsTheme.Muted);
        readonly Button locateGroup=SettingsTheme.Button("当前群聊","定位当前群聊");
        readonly ToggleButton groupEnabled=SettingsTheme.Switch("使用本群记忆",true);
        readonly DockPanel enabledRow=new DockPanel{Margin=new Thickness(0,2,0,10)};
        readonly Button more=SettingsTheme.Button("···","更多群记忆操作");
        readonly EditField preferences,agreements;
        bool refreshing;string scopeAccount,rosterShown,shown,validationScope,validationError;
        sealed class EditDraft {internal GroupMemory Group;internal string Text,Saved;internal bool Editing;}
        sealed class EditField {
            internal bool Agreements;internal GroupMemory Group;internal TextBox Box;internal TextBlock Summary;internal Button Save;internal Border Card;internal ReplyPreferencePicker Presets;
            internal readonly Dictionary<string,EditDraft> Drafts=new Dictionary<string,EditDraft>();
        }
        public GroupMemoryPanel(MemoryStore store,Func<ChatSnapshot> currentChat,Action changed) {
            if(store==null||currentChat==null)throw new ArgumentNullException();
            this.store=store;this.currentChat=currentChat;this.changed=changed;
            var searchHeader=new StackPanel();var searchGrid=new Grid();searchGrid.Children.Add(groupSearch);
            var placeholder=SettingsTheme.Text("搜索群名",12,SettingsTheme.Muted);placeholder.Margin=new Thickness(9,0,0,0);placeholder.IsHitTestVisible=false;searchGrid.Children.Add(placeholder);
            searchHeader.Children.Add(new Border{Background=SettingsTheme.Paper,CornerRadius=new CornerRadius(8),Child=searchGrid,Margin=new Thickness(0,0,0,5)});
            searchEmpty.Margin=new Thickness(10,12,10,12);searchEmpty.Visibility=Visibility.Collapsed;searchHeader.Children.Add(searchEmpty);
            groups=SettingsTheme.ModelPicker(searchHeader);groups.IsTextSearchEnabled=false;groups.Height=36;groups.Margin=new Thickness(0,0,0,10);
            var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding("DisplayName"));label.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);groups.ItemTemplate=new DataTemplate{VisualTree=label};
            AutomationProperties.SetName(groups,"群聊记忆");AutomationProperties.SetName(groupSearch,"搜索当前账号群聊");
            groupSearch.TextChanged+=delegate{placeholder.Visibility=groupSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(!refreshing)Refresh();};
            groupSearch.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
                if(e.Key==Key.Escape){groups.IsDropDownOpen=false;groups.Focus();e.Handled=true;}
                else if(e.Key==Key.Down&&groups.Items.Count>0){groups.SelectedIndex=0;groups.UpdateLayout();var row=groups.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem;if(row!=null)row.Focus();e.Handled=true;}
            };
            groups.SelectionChanged+=delegate{if(!refreshing)Refresh();};
            groups.DropDownOpened+=delegate{Refresh();Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate{if(groups.IsDropDownOpen&&groups.IsEnabled)groupSearch.Focus();}));};
            var heading=new DockPanel{Margin=new Thickness(0,0,0,4)};locateGroup.Height=24;locateGroup.Padding=new Thickness(6,0,6,0);locateGroup.FontSize=11;locateGroup.Click+=delegate{LocateCurrentGroup();};DockPanel.SetDock(locateGroup,Dock.Right);heading.Children.Add(locateGroup);heading.Children.Add(SettingsTheme.Text("群聊",11,SettingsTheme.Muted));Children.Add(heading);Children.Add(groups);
            empty.Margin=new Thickness(0,0,0,8);Children.Add(empty);
            DockPanel.SetDock(groupEnabled,Dock.Right);enabledRow.Children.Add(groupEnabled);enabledRow.Children.Add(SettingsTheme.Text("使用本群记忆",12));Children.Add(enabledRow);
            groupEnabled.Click+=delegate{var group=Selected;if(!CanManage(group)||!store.CanEdit)return;store.SetGroupEnabled(group,groupEnabled.IsChecked==true);Notify();Refresh();};
            preferences=MakeEditor(false,"回复偏好","本群回复偏好","未设置，按群聊语气推荐",104,400,"例如：简短自然，少用表情，不替我作承诺。");
            agreements=MakeEditor(true,"群内约定","本群群内约定","未设置",116,2000,"例如：每周五汇总进度；涉及费用先确认负责人。仅供本群推荐参考，由你维护。");
            Children.Add(preferences.Card);Children.Add(agreements.Card);
            error.Margin=new Thickness(0,2,0,4);Children.Add(error);
            var footer=new DockPanel{Margin=new Thickness(0,4,0,0)};more.Width=32;more.Height=26;more.Padding=new Thickness(0);DockPanel.SetDock(more,Dock.Right);footer.Children.Add(more);
            var menu=new ContextMenu{FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13};var clear=new MenuItem{Header="清空本群记忆"};menu.Items.Add(clear);more.ContextMenu=menu;
            clear.Click+=delegate{
                var group=Selected;if(!CanManage(group)||!store.CanEdit)return;
                var owner=Window.GetWindow(this);string prompt="清空当前账号中「"+group.Name+"」的回复偏好和群内约定？其他群聊和好友不受影响。";
                var result=owner==null?MessageBox.Show(prompt,"清空本群记忆",MessageBoxButton.OKCancel):MessageBox.Show(owner,prompt,"清空本群记忆",MessageBoxButton.OKCancel);
                if(result!=MessageBoxResult.OK||!CanManage(group))return;
                store.ClearGroup(group);if(store.Error==null){Forget(preferences,group);Forget(agreements,group);}Notify();Refresh();
            };
            more.Click+=delegate{Refresh();clear.IsEnabled=CanManage(Selected)&&store.CanEdit;menu.PlacementTarget=more;menu.IsOpen=true;};
            footer.Children.Add(scopeNote);Children.Add(footer);Refresh();
        }
        EditField MakeEditor(bool agreement,string title,string name,string placeholder,double height,int max,string tip) {
            var field=new EditField{Agreements=agreement};var body=new StackPanel();var header=new DockPanel();
            field.Save=SettingsTheme.Button("编辑","编辑或保存"+name);field.Save.Height=26;field.Save.Click+=delegate{EditOrSave(field);};DockPanel.SetDock(field.Save,Dock.Right);header.Children.Add(field.Save);header.Children.Add(SettingsTheme.Text(title));body.Children.Add(header);
            field.Summary=SettingsTheme.Text(placeholder,12,SettingsTheme.Muted);field.Summary.TextWrapping=TextWrapping.NoWrap;field.Summary.TextTrimming=TextTrimming.CharacterEllipsis;field.Summary.Margin=new Thickness(0,2,0,2);body.Children.Add(field.Summary);
            if(!agreement){field.Presets=new ReplyPreferencePicker(true,delegate{return Visibility==Visibility.Visible&&CanManage(field.Group)&&Object.ReferenceEquals(field.Group,Selected)&&store.CanEdit&&field.Box.Visibility==Visibility.Visible;},delegate(string text){field.Box.Text=text;Capture(field);field.Box.Focus();});body.Children.Add(field.Presets.Control);}
            field.Box=new TextBox{MinHeight=height,Height=height,MaxHeight=height,MaxLength=max,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,BorderBrush=SettingsTheme.Line,Padding=new Thickness(9),Margin=new Thickness(0,5,0,0),Visibility=Visibility.Collapsed,ToolTip=tip};AutomationProperties.SetName(field.Box,name);body.Children.Add(field.Box);
            field.Box.TextChanged+=delegate{if(!refreshing&&field.Group!=null&&validationScope==GroupKey(field.Group)){validationError=null;validationScope=null;error.Text=store.Error??"";error.Visibility=error.Text.Length==0?Visibility.Collapsed:Visibility.Visible;}};
            field.Box.TextChanged+=delegate{if(field.Presets!=null)field.Presets.Refresh(field.Box.Text,field.Box.Visibility==Visibility.Visible,CanManage(field.Group)&&store.CanEdit);};
            field.Card=SettingsTheme.Card(body);field.Card.Padding=new Thickness(10);field.Card.Margin=new Thickness(0,0,0,8);return field;
        }
        ChatSnapshot ReadCurrent(){try{return currentChat();}catch{return null;}}
        static string Account(ChatSnapshot s){return MemoryStore.Identified(s)||MemoryStore.GroupIdentified(s)?s.AccountId:null;}
        static string GroupKey(GroupMemory group){return group.AccountId+":"+group.Id;}
        static string Value(EditField field,GroupMemory group){return (field.Agreements?group.Agreements:group.Preferences)??"";}
        bool Owned(GroupMemory group){return group!=null&&store.Data.Groups.Any(g=>Object.ReferenceEquals(g,group));}
        bool CanManage(GroupMemory group){return Owned(group)&&scopeAccount!=null&&group.AccountId==scopeAccount&&Account(ReadCurrent())==scopeAccount;}
        GroupMemory Selected {get{var group=groups.SelectedItem as GroupMemory;return CanManage(group)?group:null;}}
        bool ValidDraft(EditField field,EditDraft draft){return Owned(draft.Group)&&draft.Saved==Value(field,draft.Group);}
        void Capture(EditField field) {
            if(field==null||field.Group==null)return;EditDraft draft;
            if(field.Drafts.TryGetValue(GroupKey(field.Group),out draft)&&Object.ReferenceEquals(draft.Group,field.Group)&&ValidDraft(field,draft)){draft.Text=field.Box.Text;draft.Editing=field.Box.Visibility==Visibility.Visible;}
        }
        void Forget(EditField field,GroupMemory group){field.Drafts.Remove(GroupKey(group));if(Object.ReferenceEquals(field.Group,group))field.Group=null;}
        void Render(EditField field,GroupMemory group) {
            foreach(var staleKey in field.Drafts.Where(p=>GroupKey(p.Value.Group)!=p.Key||!ValidDraft(field,p.Value)).Select(p=>p.Key).ToList())field.Drafts.Remove(staleKey);
            field.Group=group;field.Card.Visibility=group==null?Visibility.Collapsed:Visibility.Visible;field.Save.IsEnabled=field.Box.IsEnabled=CanManage(group)&&store.CanEdit;
            if(group==null){field.Box.Text="";field.Box.Visibility=Visibility.Collapsed;field.Summary.Text="";field.Summary.ToolTip=null;if(field.Presets!=null)field.Presets.Refresh("",false,false);return;}
            string key=GroupKey(group);EditDraft draft;
            if(!field.Drafts.TryGetValue(key,out draft)){draft=new EditDraft{Group=group,Text=Value(field,group),Saved=Value(field,group)};field.Drafts[key]=draft;}
            if(field.Box.Text!=draft.Text)field.Box.Text=draft.Text;
            field.Box.Visibility=draft.Editing?Visibility.Visible:Visibility.Collapsed;field.Summary.Visibility=draft.Editing?Visibility.Collapsed:Visibility.Visible;field.Save.Content=draft.Editing?"保存":"编辑";
            if(field.Presets!=null)field.Presets.Refresh(field.Box.Text,draft.Editing,CanManage(group)&&store.CanEdit);
            string value=Value(field,group);field.Summary.Text=String.IsNullOrWhiteSpace(value)?(field.Agreements?"未设置":"未设置，按群聊语气推荐"):value.Replace("\r\n"," ").Replace('\n',' ').Replace('\r',' ');field.Summary.ToolTip=String.IsNullOrWhiteSpace(value)?null:value;
        }
        void EditOrSave(EditField field) {
            var group=field.Group;if(!CanManage(group)||!Object.ReferenceEquals(group,Selected)||!store.CanEdit)return;
            if(field.Box.Visibility!=Visibility.Visible){field.Box.Visibility=Visibility.Visible;field.Summary.Visibility=Visibility.Collapsed;field.Save.Content="保存";if(field.Presets!=null)field.Presets.Refresh(field.Box.Text,true,true);Capture(field);field.Box.Focus();return;}
            try {if(field.Agreements)store.SetGroupAgreements(group,field.Box.Text);else store.SetGroupPreferences(group,field.Box.Text);}
            catch(InvalidOperationException e){validationScope=GroupKey(group);validationError=e.Message;error.Text=e.Message;error.Visibility=Visibility.Visible;return;}
            if(store.Error==null){validationError=null;validationScope=null;Forget(field,group);}Notify();Refresh();
        }
        void Notify(){if(changed!=null)changed();}
        public void LocateCurrentGroup() {
            Refresh();var group=store.CurrentGroup(ReadCurrent());if(!CanManage(group))return;
            refreshing=true;try{groupSearch.Text="";rosterShown=null;groups.IsDropDownOpen=false;}finally{refreshing=false;}
            Refresh();groups.SelectedItem=group;Refresh();
        }
        public bool Refresh() {
            if(refreshing)return false;refreshing=true;
            try {
                Capture(preferences);Capture(agreements);var snapshot=ReadCurrent();string account=Account(snapshot);bool accountChanged=scopeAccount!=account;
                var old=groups.SelectedItem as GroupMemory;
                if(accountChanged){scopeAccount=account;groupSearch.Text="";groups.IsDropDownOpen=false;}
                var roster=store.Data.Groups.Where(g=>account!=null&&g.AccountId==account).ToList();var current=store.CurrentGroup(snapshot);string query=groupSearch.Text.Trim();
                string rosterRevision=(account??"")+"|"+(current==null?"":current.Id)+"|"+query.Length+":"+query+"|"+String.Join("|",roster.Select(g=>g.Id+":"+(g.Name??"").Length+":"+g.Name+":"+(groups.IsDropDownOpen?0:g.UpdatedTicks)));
                if(accountChanged||rosterShown!=rosterRevision||!groups.Items.Cast<GroupMemory>().All(Owned)) {
                    foreach(var sameName in roster.GroupBy(g=>g.Name)){int index=0;foreach(var group in sameName.OrderBy(g=>g.Id))group.DisplayName=group.Name+(sameName.Count()>1?" · 群聊 "+(++index):"")+(Object.ReferenceEquals(group,current)?" · 当前聊天":"");}
                    var visible=roster.Where(g=>query.Length==0||(g.Name??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(g=>Object.ReferenceEquals(g,current)).ThenByDescending(g=>g.UpdatedTicks).ThenBy(g=>g.Name).ToList();
                    var selected=old==null?null:visible.FirstOrDefault(g=>g.AccountId==old.AccountId&&g.Id==old.Id);
                    if(accountChanged||selected==null)selected=visible.FirstOrDefault(g=>Object.ReferenceEquals(g,current))??(query.Length==0?visible.FirstOrDefault():null);
                    groups.ItemsSource=visible;groups.SelectedItem=selected;searchEmpty.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;rosterShown=rosterRevision;
                }
                groups.IsEnabled=groupSearch.IsEnabled=account!=null&&roster.Count>0;locateGroup.IsEnabled=CanManage(current);
                var selectedGroup=Selected;Render(preferences,selectedGroup);Render(agreements,selectedGroup);
                enabledRow.Visibility=selectedGroup==null?Visibility.Collapsed:Visibility.Visible;groupEnabled.IsEnabled=CanManage(selectedGroup)&&store.CanEdit;groupEnabled.IsChecked=selectedGroup!=null&&selectedGroup.Enabled;
                empty.Text=account==null?"打开微信聊天以确认当前账号。":roster.Count==0?"打开一个群聊后，可在这里设置本群记忆。":selectedGroup==null?"请在搜索结果中选择群聊。":"";empty.Visibility=empty.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
                if(selectedGroup==null||validationScope!=GroupKey(selectedGroup)){validationError=null;validationScope=null;}
                more.IsEnabled=CanManage(selectedGroup)&&store.CanEdit;error.Text=store.Error??validationError??"";error.Visibility=error.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
                scopeNote.Text=!store.Data.Enabled?"记忆已暂停 · 内容保留":selectedGroup!=null&&!selectedGroup.Enabled?"本群记忆已暂停 · 内容保留":"仅用于本群 · 手动维护";
                string revision=rosterRevision+"|"+store.Data.Enabled+"|"+store.CanEdit+"|"+store.Error+"|"+(selectedGroup==null?"":GroupKey(selectedGroup)+"|"+selectedGroup.Enabled+"|"+selectedGroup.Preferences+"|"+selectedGroup.Agreements);
                bool changedView=shown!=revision;shown=revision;return changedView;
            }finally{refreshing=false;}
        }
    }
}
