using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WeChatPet.Runtime {
    public sealed class ModelOption {
        public string Id {get;set;} public string Name {get;set;} public string Hint {get;set;}
        public string[] Efforts=new string[0]; public string DefaultEffort="";
        public override string ToString(){return Name;}
        public static ModelOption Create(string id,string name,string provider) {
            if(String.IsNullOrWhiteSpace(name)||name==id)name=id.StartsWith("gpt-")?"GPT-"+id.Substring(4).Replace("-"," "):id;
            if(name.StartsWith("DeepSeek-"))name=name.Replace("DeepSeek-","DeepSeek ").Replace("-Flash"," Flash").Replace("-Pro"," Pro");
            string hint=provider=="chatgpt"?"使用 ChatGPT 会员额度":"使用当前 DeepSeek 连接";
            if(id=="deepseek-flash")hint="日常对话与回复";
            if(id=="deepseek-v4-pro")hint="复杂任务与推理";
            return new ModelOption{Id=id,Name=name,Hint=hint};
        }
        public static ModelOption[] Parse(object raw,string provider) {
            var list=new List<ModelOption>();
            foreach(object item in (IEnumerable)raw) {
                var data=item as Dictionary<string,object>;if(data==null)continue;
                string id=Convert.ToString(data["id"]);var option=Create(id,Convert.ToString(data["name"]),provider);
                object levels;if(data.TryGetValue("efforts",out levels)&&levels is IEnumerable)option.Efforts=((IEnumerable)levels).Cast<object>().Select(Convert.ToString).ToArray();
                object def;if(data.TryGetValue("defaultEffort",out def))option.DefaultEffort=Convert.ToString(def);
                list.Add(option);
            }
            return list.ToArray();
        }
    }
    public sealed class ModelConnectionWindow : Window {
        readonly DesktopRuntime runtime; readonly RuntimeSettings config; readonly bool designPreview;
        readonly Button[] providers=new Button[3]; readonly ComboBox models,effort,instances;
        readonly PasswordBox key; readonly TextBlock status,accountTitle,accountHint,badge,modelDetail,catalogNote,effortNote,recoveryNotice;
        readonly Button login,save,test,refresh,logout,cancel; readonly FrameworkElement tabs;
        readonly StackPanel apiFields,instancePanel,connectionActions; readonly Border surface;readonly Button cancelKey,changeKey;
        bool loading,busy,closed,cancelled,editingKey; string operation=""; bool previewCredential;
        readonly string[] names={"ChatGPT 会员","DeepSeek 账号","DeepSeek API"};
        readonly string[] ids={"chatgpt","deepseek-account","deepseek-api"};
        readonly Dictionary<string,ModelOption[]> catalogs=new Dictionary<string,ModelOption[]>();
        public FrameworkElement PreviewSurface {get{return surface;}}
        bool HasCredential {get{return designPreview?previewCredential:runtime.Models!=null&&runtime.Models.HasCredential(config.Provider);}}
        public ModelConnectionWindow(DesktopRuntime runtime):this(runtime,runtime.Settings,false){}
        internal ModelConnectionWindow(RuntimeSettings settings):this(null,settings,true){}
        ModelConnectionWindow(DesktopRuntime runtime,RuntimeSettings settings,bool preview) {
            this.runtime=runtime;config=settings;designPreview=preview;previewCredential=preview;
            if(config.ModelsByProvider==null)config.ModelsByProvider=new Dictionary<string,string>();
            Title="模型连接 · 微信 AI 桌宠";Width=430;SizeToContent=SizeToContent.Height;MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-24);
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            AppBrand.ApplyIcon(this);FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;UseLayoutRounding=true;SnapsToDevicePixels=true;Topmost=true;
            TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
            surface=new Border{MaxWidth=418,HorizontalAlignment=HorizontalAlignment.Stretch,Background=SettingsTheme.Paper,BorderBrush=SettingsTheme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(16)};
            var scroll=new ScrollViewer{Content=surface,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            Content=new Border{Margin=new Thickness(6),Child=scroll};
            var panel=new StackPanel();surface.Child=panel;
            var header=new DockPanel{Margin=new Thickness(0,0,0,12)};var close=SettingsTheme.Button("×","关闭模型设置");close.Width=30;close.Height=30;close.Padding=new Thickness(0);close.FontSize=22;close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            var minimize=SettingsTheme.Button("—","最小化到任务栏");minimize.Width=30;minimize.Height=30;minimize.Padding=new Thickness(0);minimize.Click+=delegate{if(runtime!=null)runtime.MinimizeToTaskbar();};DockPanel.SetDock(minimize,Dock.Right);header.Children.Add(minimize);
            var title=SettingsTheme.Text("模型连接",21);header.Children.Add(title);SettingsTheme.EnableHeaderDrag(this,header);panel.Children.Add(header);
            tabs=SettingsTheme.Tabs(true,delegate{if(!busy&&runtime!=null)runtime.OpenAppearance();},delegate{});panel.Children.Add(tabs);
            recoveryNotice=SettingsTheme.Text("",12,SettingsTheme.Muted);recoveryNotice.Margin=new Thickness(0,0,0,10);recoveryNotice.Visibility=Visibility.Collapsed;panel.Children.Add(recoveryNotice);
            var choices=new UniformGrid{Rows=1,Columns=3,Margin=new Thickness(-3,0,-3,10)};
            for(int i=0;i<3;i++) {int index=i;var text=new StackPanel();var label=SettingsTheme.Text(names[i],12);label.HorizontalAlignment=HorizontalAlignment.Center;text.Children.Add(label);var button=SettingsTheme.Button(text,"选择"+names[i]);button.Height=38;button.Margin=new Thickness(3,0,3,0);button.Padding=new Thickness(4);button.Click+=async delegate{await ChangeProvider(index);};providers[i]=button;choices.Children.Add(button);}panel.Children.Add(choices);
            var account=new StackPanel();var accountRow=new DockPanel();badge=SettingsTheme.Text("",11,SettingsTheme.Accent);var badgeBox=new Border{Background=SettingsTheme.Brush("#EDF4ED"),CornerRadius=new CornerRadius(7),Padding=new Thickness(8,4,8,4),Child=badge};DockPanel.SetDock(badgeBox,Dock.Right);accountRow.Children.Add(badgeBox);accountTitle=SettingsTheme.Text("",14);accountTitle.FontWeight=FontWeights.SemiBold;accountRow.Children.Add(accountTitle);account.Children.Add(accountRow);
            accountHint=SettingsTheme.Text("",12,SettingsTheme.Muted);accountHint.Margin=new Thickness(0,9,0,0);account.Children.Add(accountHint);
            login=ActionButton("登录 ChatGPT",async delegate{if(config.Provider=="deepseek-api"){editingKey=true;UpdateControls();return;}await Run("auth","请在浏览器中完成授权…",async delegate{if(!Invalidate())return;await runtime.Models.Request("auth/start",Fields());if(closed||cancelled)return;await LoadModels();if(!closed&&!cancelled)await Validate();});},true);login.Margin=new Thickness(0,12,0,0);account.Children.Add(login);
            apiFields=new StackPanel{Margin=new Thickness(0,8,0,0)};key=new PasswordBox{Height=32,BorderThickness=new Thickness(0),Padding=new Thickness(6),Background=Brushes.Transparent};AutomationProperties.SetName(key,"DeepSeek API 密钥");apiFields.Children.Add(new Border{Background=SettingsTheme.Paper,BorderBrush=SettingsTheme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(5),Child=key});
            save=ActionButton("保存密钥并验证",async delegate{await Run("validate","正在保存密钥…",async delegate{if(!Invalidate())return;string secret=key.Password;key.Clear();await runtime.Models.Request("apiKey/set",new Dictionary<string,object>{{"key",secret}});if(closed||cancelled)return;await LoadModels();await Validate();editingKey=false;});},true);save.Margin=new Thickness(0,10,0,0);apiFields.Children.Add(save);account.Children.Add(apiFields);key.PasswordChanged+=delegate{UpdateControls();};panel.Children.Add(SettingsTheme.Card(account));
            cancelKey=ActionButton("取消更换",delegate{key.Clear();editingKey=false;UpdateControls();});cancelKey.Height=26;apiFields.Children.Add(cancelKey);
            var modelPanel=new StackPanel();var modelHead=new DockPanel();refresh=ActionButton("↻  刷新",async delegate{await Run("catalog","正在获取可用模型…",LoadModels);});refresh.Height=26;refresh.FontSize=11;refresh.Padding=new Thickness(5,0,5,0);DockPanel.SetDock(refresh,Dock.Right);modelHead.Children.Add(refresh);var modelLabel=SettingsTheme.Text("回复模型",13);modelHead.Children.Add(modelLabel);modelPanel.Children.Add(modelHead);
            models=SettingsTheme.ModelPicker();models.Margin=new Thickness(0,10,0,0);modelPanel.Children.Add(models);models.SelectionChanged+=delegate{if(loading)return;var m=models.SelectedItem as ModelOption;if(m==null)return;config.Model=m.Id;config.ModelsByProvider[config.Provider]=m.Id;config.ReasoningEffort="auto";if(!Invalidate())return;UpdateModelDetails(true);UpdateControls();status.Text="选择已保存，验证后用于新回复";};
            modelDetail=SettingsTheme.Text("",10,SettingsTheme.Muted);modelDetail.Visibility=Visibility.Collapsed;modelPanel.Children.Add(modelDetail);
            var effortHeader=SettingsTheme.Text("思考强度",12);effortHeader.Margin=new Thickness(0,10,0,6);modelPanel.Children.Add(effortHeader);
            effort=SettingsTheme.ModelPicker();AutomationProperties.SetName(effort,"思考强度");effort.SelectionChanged+=delegate{if(loading)return;var option=effort.SelectedItem as ModelOption;if(option==null)return;config.ReasoningEffort=option.Id;if(!Invalidate())return;status.Text="思考强度已保存，验证后生效";UpdateControls();};modelPanel.Children.Add(effort);
            effortNote=SettingsTheme.Text("",11,SettingsTheme.Muted);effortNote.Margin=new Thickness(0,8,0,0);modelPanel.Children.Add(effortNote);catalogNote=SettingsTheme.Text("",10,SettingsTheme.Muted);catalogNote.Margin=new Thickness(0,10,0,0);modelPanel.Children.Add(catalogNote);panel.Children.Add(SettingsTheme.Card(modelPanel));
            status=SettingsTheme.Text(config.Validated?"连接已验证，可以继续接收推荐":"连接后，验证一次即可开始推荐",12,SettingsTheme.Accent);status.Margin=new Thickness(0,0,0,10);panel.Children.Add(status);
            test=ActionButton("验证并启用",async delegate{await Run("validate","正在验证连接…",Validate);},true);test.Height=34;panel.Children.Add(test);
            cancel=ActionButton("取消本次操作",async delegate{await CancelOperation();});cancel.Margin=new Thickness(0,6,0,0);panel.Children.Add(cancel);
            logout=ActionButton("退出账号",async delegate{await Run("logout","正在移除本机凭证…",async delegate{if(!Invalidate())return;await runtime.Models.Request("auth/logout",Fields());if(!closed)status.Text="已移除本机凭证";});});logout.Foreground=SettingsTheme.Muted;logout.FontSize=12;logout.Height=28;
            changeKey=ActionButton("更换密钥",delegate{editingKey=true;UpdateControls();});changeKey.FontSize=12;changeKey.Height=28;
            connectionActions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};connectionActions.Children.Add(changeKey);connectionActions.Children.Add(logout);panel.Children.Add(connectionActions);
            instancePanel=new StackPanel{Margin=new Thickness(0,12,0,0)};instancePanel.Children.Add(SettingsTheme.Text("连接的微信",12));instances=new ComboBox{Height=32,Margin=new Thickness(0,6,0,0)};instances.SelectionChanged+=delegate{if(loading)return;var choice=instances.SelectedItem as ProcessChoice;if(choice!=null&&runtime!=null)runtime.SelectProcess(choice);};instancePanel.Children.Add(instances);panel.Children.Add(instancePanel);
            loading=true;SetCatalog(String.IsNullOrEmpty(config.Model)?new ModelOption[0]:new[]{ModelOption.Create(config.Model,config.Model,config.Provider)},false);loading=false;UpdateControls();UpdateInstances();
            Loaded+=async delegate{if(!designPreview&&HasCredential)await Run("catalog","正在获取可用模型…",LoadModels);};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&!models.IsDropDownOpen&&!effort.IsDropDownOpen){Close();e.Handled=true;}};
            Closed+=async delegate{closed=true;await CancelOperation();};
        }
        Button ActionButton(string name,Action action,bool primary=false){var b=SettingsTheme.Button(name,name,primary);b.Click+=delegate{action();};return b;}
        bool Invalidate(){config.Validated=false;return runtime==null||runtime.ConfigurationChanged(true);}
        internal void RefreshSavedSettings(bool restore) {
            if(restore) {
                bool previous=loading;loading=true;
                try {
                    ModelOption[] options;if(!catalogs.TryGetValue(config.Provider,out options))options=String.IsNullOrEmpty(config.Model)?new ModelOption[0]:new[]{ModelOption.Create(config.Model,config.Model,config.Provider)};
                    models.ItemsSource=options;models.SelectedItem=options.FirstOrDefault(m=>m.Id==config.Model);UpdateModelDetails();UpdateInstances();
                } finally {loading=previous;}
            }
            status.Text=runtime.SettingsError??(config.Validated?"连接已验证，可以继续接收推荐":"连接后，验证一次即可开始推荐");UpdateControls();
        }
        Dictionary<string,object> Fields(){return new Dictionary<string,object>{{"provider",config.Provider}};}
        async Task ChangeProvider(int index) {
            if(busy||ids[index]==config.Provider)return;
            config.ModelsByProvider[config.Provider]=config.Model;config.Provider=ids[index];string remembered;config.Model=config.ModelsByProvider.TryGetValue(config.Provider,out remembered)?remembered:"";config.ReasoningEffort="auto";editingKey=false;key.Clear();if(!Invalidate())return;
            SetCatalog(String.IsNullOrEmpty(config.Model)?new ModelOption[0]:new[]{ModelOption.Create(config.Model,config.Model,config.Provider)},false);status.Text="请选择模型并验证连接";UpdateControls();
            if(HasCredential)await Run("catalog","正在获取可用模型…",LoadModels);
        }
        public void UpdateInstances(){if(runtime==null){instancePanel.Visibility=Visibility.Collapsed;return;}bool previous=loading;loading=true;try{instances.ItemsSource=runtime.Candidates;instancePanel.Visibility=runtime.Candidates.Count>1?Visibility.Visible:Visibility.Collapsed;instances.SelectedItem=runtime.Candidates.FirstOrDefault(p=>p.Id==config.SelectedProcess&&p.Birth==config.SelectedProcessBirth);}finally{loading=previous;}}
        internal void RefreshDiagnosticNotice() {
            string recovery=runtime==null?null:runtime.Models==null?runtime.Status:runtime.Models.RecoveryNotice;
            string warning=runtime==null?null:runtime.DiagnosticsWarning;
            recoveryNotice.Text=String.Join("\n",new[]{recovery,warning}.Where(value=>!String.IsNullOrEmpty(value)));
            recoveryNotice.Visibility=String.IsNullOrEmpty(recoveryNotice.Text)?Visibility.Collapsed:Visibility.Visible;
        }
        void UpdateControls() {
            if(test==null)return;
            bool saved=runtime==null||runtime.SettingsError==null;if(!saved)status.Text=runtime.SettingsError;
            RefreshDiagnosticNotice();
            int active=Array.IndexOf(ids,config.Provider);for(int i=0;i<3;i++){providers[i].Background=i==active?SettingsTheme.Brush("#E7F0E8"):Brushes.White;providers[i].BorderBrush=i==active?SettingsTheme.Accent:SettingsTheme.Line;providers[i].IsEnabled=!busy;}
            bool api=config.Provider=="deepseek-api",connected=HasCredential;
            status.Visibility=!busy&&config.Validated&&new[]{"连接已验证，可以继续接收推荐","模型列表已更新，当前连接保持可用","连接通过，已开始等待新来信"}.Contains(status.Text)?Visibility.Collapsed:Visibility.Visible;
            accountTitle.Text=api?"API 密钥":"账号授权";badge.Text=connected?(config.Validated&&saved?"已连接":"待验证"):"未连接";
            accountHint.Text=api?(connected?"密钥已保存在本机。填写新密钥可更换。":"填写 DeepSeek 平台的 API 密钥。"):connected?"授权已保存在本机，下次启动自动恢复。":config.Provider=="chatgpt"?"在浏览器授权，使用你的 ChatGPT 会员额度。":"在浏览器登录 DeepSeek，使用账号服务。";
            accountHint.Visibility=connected?Visibility.Collapsed:Visibility.Visible;accountTitle.ToolTip=accountHint.Text;
            apiFields.Visibility=api&&(!connected||editingKey)?Visibility.Visible:Visibility.Collapsed;
            cancelKey.Visibility=api&&connected&&editingKey?Visibility.Visible:Visibility.Collapsed;cancelKey.IsEnabled=!busy;
            connectionActions.Visibility=connected?Visibility.Visible:Visibility.Collapsed;
            login.Visibility=api||connected?Visibility.Collapsed:Visibility.Visible;
            changeKey.Visibility=api&&connected&&!editingKey?Visibility.Visible:Visibility.Collapsed;changeKey.IsEnabled=!busy;
            login.Content=api?"更换密钥":config.Provider=="chatgpt"?"Continue with ChatGPT":"登录 DeepSeek";
            login.Background=api?SettingsTheme.Paper:SettingsTheme.Accent;login.Foreground=api?SettingsTheme.Ink:Brushes.White;
            login.IsEnabled=!busy;save.IsEnabled=!busy&&key.Password.Length>=10;test.IsEnabled=!busy&&connected&&models.SelectedItem!=null;refresh.IsEnabled=!busy&&connected;models.IsEnabled=!busy&&models.Items.Count>0;effort.IsEnabled=!busy&&effort.Items.Count>1;tabs.IsEnabled=!busy;
            test.Content=config.Validated&&saved?"重新验证连接":"验证并启用";logout.Content=api?"删除密钥":"退出账号";AutomationProperties.SetName(logout,Convert.ToString(logout.Content));logout.Visibility=connected?Visibility.Visible:Visibility.Collapsed;logout.IsEnabled=!busy;cancel.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        }
        void UpdateModelDetails(bool enforce=false) {
            var m=models.SelectedItem as ModelOption;modelDetail.Text=m==null?"连接后获取可用模型":"调用标识  "+m.Id;models.ToolTip=modelDetail.Text;
            bool previous=loading;loading=true;
            var levels=new List<ModelOption>{new ModelOption{Id="auto",Name="跟随模型",Hint=m!=null&&!String.IsNullOrEmpty(m.DefaultEffort)?"模型默认："+EffortName(m.DefaultEffort):"使用服务的默认设置"}};
            if(m!=null)foreach(string level in m.Efforts.Distinct())levels.Add(new ModelOption{Id=level,Name=EffortName(level),Hint=level=="none"?"关闭思考模式":level=="low"||level=="minimal"?"减少思考时间":"更充分地思考，可能需要更长时间"});
            effort.ItemsSource=levels;effort.SelectedItem=levels.FirstOrDefault(x=>x.Id==config.ReasoningEffort)??levels[0];
            // Never silently keep a setting that the newly discovered model does not support.
            if(enforce&&((ModelOption)effort.SelectedItem).Id!=config.ReasoningEffort){config.ReasoningEffort="auto";if(!Invalidate()){loading=previous;return;}}
            effortNote.Text="低强度更快，适合日常聊天。";effort.ToolTip=effortNote.Text;effortNote.Visibility=Visibility.Collapsed;
            if(config.Provider.StartsWith("deepseek")&&m!=null&&m.Id=="deepseek-flash")catalogNote.Text="官方版本参考：V4.1 Flash · 1017（2026-10-21 核对）";
            else catalogNote.Text="";
            catalogNote.Visibility=Visibility.Collapsed;
            loading=previous;
        }
        public static string EffortName(string id){switch(id){case "none":return "关闭思考";case "minimal":return "最低";case "low":return "低";case "medium":return "中";case "high":return "高";case "xhigh":return "更高";case "max":return "最高";case "ultra":return "极高";default:return id;}}
        internal void SetCatalog(ModelOption[] options,bool fetched) {
            bool previous=loading;loading=true;models.ItemsSource=options;var selected=options.FirstOrDefault(m=>m.Id==config.Model);
            if(selected==null&&String.IsNullOrEmpty(config.Model)){selected=options.FirstOrDefault(m=>m.Id=="deepseek-flash")??options.FirstOrDefault();if(selected!=null){config.Model=selected.Id;config.ModelsByProvider[config.Provider]=selected.Id;}}
            models.SelectedItem=selected;loading=previous;UpdateModelDetails(fetched);
            if(fetched&&selected==null&&!String.IsNullOrEmpty(config.Model)){if(!Invalidate())return;status.Text="之前选择的模型已不在列表，请重新选择";}
            if(runtime==null||runtime.SettingsError==null)catalogs[config.Provider]=options;
            UpdateControls();
        }
        async Task Run(string kind,string message,Func<Task> action) {
            if(busy||runtime==null||runtime.Models==null)return;busy=true;cancelled=false;operation=kind;status.Text=message;UpdateControls();
            try{await runtime.Models.Initialization;if(!closed&&!cancelled)await action();}catch(Exception e){if(!closed)status.Text=cancelled?"操作已取消":ModelClient.Explain(e);}
            finally{busy=false;operation="";if(!closed)UpdateControls();}
        }
        async Task LoadModels() {
            object raw=await runtime.Models.Request("models/list",Fields());if(closed||cancelled)return;
            var options=ModelOption.Parse(raw,config.Provider);if(options.Length==0)throw new InvalidOperationException("NO_AVAILABLE_MODELS");
            SetCatalog(options,true);if(runtime.SettingsError!=null)return;if(models.SelectedItem!=null)status.Text=config.Validated?"模型列表已更新，当前连接保持可用":"模型列表已更新，验证后开始推荐";
        }
        async Task Validate() {
            if(closed||cancelled)return;var selected=models.SelectedItem as ModelOption;if(selected==null)throw new InvalidOperationException("NO_AVAILABLE_MODELS");
            config.Model=selected.Id;if(!Invalidate())return;status.Text="正在用固定测试文字验证…";
            var fields=Fields();fields["model"]=selected.Id;fields["effort"]=config.ReasoningEffort;fields["messages"]=new[]{new ChatMessage{Role="peer",Text="你好，今天过得怎么样？",Type=1}};
            await runtime.Models.Request("recommend",fields);if(closed||cancelled)return;config.Validated=true;if(!runtime.ConfigurationChanged(false))return;status.Text="连接通过，已开始等待新来信";
        }
        async Task CancelOperation() {
            if(!busy||runtime==null||runtime.Models==null)return;cancelled=true;
            try{if(operation=="auth")await runtime.Models.Request("auth/cancel");if(operation=="catalog"||operation=="auth"||operation=="validate")await runtime.Models.Request("models/cancel");if(operation=="validate"||operation=="auth")runtime.Models.Cancel();if(!closed)status.Text="操作已取消";}catch(Exception e){if(!closed)status.Text=ModelClient.Explain(e);}
        }
    }
}
