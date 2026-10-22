using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WeChatPet
{
    public sealed class PetPreferences
    {
        public string Skin { get; set; }
        public double Size { get; set; }
        public bool Motion { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double PetCenterX { get; set; }
        public double PetBottom { get; set; }
        public bool PositionSaved { get; set; }
        public PetPreferences() { Skin = "personal"; Size = 1; Motion = true; Left = -1; Top = -1; PetCenterX = -1; PetBottom = -1; }
    }

    public sealed class PetWindow : Window
    {
        private const double BasePetSize = 160;
        private readonly string settingsPath;
        private readonly string workspaceRoot;
        private readonly PetVisual visual;
        private readonly DispatcherTimer timer;
        private readonly DispatcherTimer clickTimer;
        private readonly Runtime.PetClickIntent clickIntent=new Runtime.PetClickIntent();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Runtime.PetReplyBubble bubble;
        internal Runtime.PetReplyBubble ReplyBubble {get{return bubble;}}
        private readonly Border petHit;
        private PetPreferences prefs;
        private string appearanceReadError;
        internal const string AppearanceReadFailure = "外观设置无法读取，原文件已保留。当前外观仅临时显示，请解除文件占用或检查文件后重启桌宠。";
        internal const string AppearanceSaveFailure = "外观更改尚未保存，当前仅临时显示。请检查文件占用、目录权限或磁盘空间后重试。";
        public string AppearanceError { get; private set; }
        internal bool CanRetryAppearance { get { return AppearanceError != null && appearanceReadError == null; } }
        private PetSettingsWindow settingsWindow;
        private PetState businessState = PetState.Idle;
        private string businessMessage = "陪你一起，把话说得刚刚好。";
        private bool live, paused, closed, listening, allowClose, taskbarMinimized, hiddenToTray;
        public bool IsPetHidden { get { return hiddenToTray || WindowState == WindowState.Minimized; } }
        public bool IsInTray { get { return hiddenToTray; } }
        public event Action MinimizedToTaskbar;
        private PetState? preview;
        private double previewUntil;
        private double stateStarted;
        private string transientNotice;
        private string recommendationHint;
        internal Func<Point,Rect> WorkAreaForPoint;
        private double noticeUntil;
        public event Action<bool> PauseChanged;
        public event Action QuitRequested;
        public event Action ListenRequested;
        public event Action ConnectionRequested;
        public event Action MemoryRequested;
        public event Action<string> RecommendationRequested;
        public Func<string> RecommendationContextToken {get;set;}
        public void OpenMemory() {if(settingsWindow!=null)settingsWindow.Close();if(MemoryRequested!=null)MemoryRequested();}
        public void OpenConnection() { if (settingsWindow != null) settingsWindow.Close(); if (ConnectionRequested != null) ConnectionRequested(); }
        public string CurrentSkin { get { return visual.CurrentSkin; } }
        public bool MotionEnabled { get { return visual.MotionEnabled; } }
        public PetState CurrentState { get { return visual.CurrentState; } }
        public PetState BusinessState { get { return businessState; } }
        public bool IsLive { get { return live; } }
        public bool IsListening { get { return listening; } }
        public string CurrentMessage { get { return businessMessage; } }
        public string LastAssetError { get { return visual.LastAssetError; } }
        public PetVisual AnimationVisual { get { return visual; } }
        public double PetScale { get { return prefs.Size; } }
        public bool IsBubbleVisible { get { return bubble.Visibility == Visibility.Visible; } }
        public double PetCenterX { get { return Left + 4 + petHit.Margin.Left + visual.Width / 2; } }
        public double PetBottomY { get { return Top + Height - 16; } }
        public bool HasPersistentControls { get { return ContainsVisibleControl(Content as DependencyObject); } }

        public PetWindow(string workspaceRoot, string settingsFile)
        {
            WorkAreaForPoint=MonitorWorkArea;
            this.workspaceRoot = workspaceRoot;
            settingsPath = settingsFile; prefs = ReadPreferences();
            Title = "微信 AI 桌宠"; Width = BasePetSize + 8; Height = BasePetSize + 8;
            AppBrand.ApplyIcon(this);
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowActivated = false; ShowInTaskbar = false; Topmost = true; Focusable = false;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Grid root = new Grid { Margin = new Thickness(4) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            bubble = new Runtime.PetReplyBubble();root.Children.Add(bubble);
            bubble.LayoutChanged+=UpdateHeight;
            visual = new PetVisual(workspaceRoot) { Width = BasePetSize, Height = BasePetSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            petHit = new Border { Width = BasePetSize, Height = BasePetSize, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Child = visual, Cursor = Cursors.SizeAll }; Grid.SetRow(petHit, 1); root.Children.Add(petHit);
            clickTimer=new DispatcherTimer(DispatcherPriority.Input);
            clickTimer.Tick+=delegate {
                string token=clickIntent.Take(clock.ElapsedMilliseconds);
                if(token==null)return;
                clickTimer.Stop();
                if(!closed&&!IsPetHidden&&RecommendationRequested!=null)RecommendationRequested(token);
            };
            petHit.MouseLeftButtonDown+=PetMouseDown;
            petHit.MouseMove+=PetMouseMove;
            petHit.MouseLeftButtonUp+=PetMouseUp;
            petHit.LostMouseCapture+=delegate {if(clickIntent.IsPressed)CancelPetClick();};
            ContextMenu menu = new ContextMenu();
            CompactMenu.Apply(menu);
            AddMenu(menu, "桌宠设置…", OpenSettings);
            AddMenu(menu, "模型连接…", OpenConnection);
            menu.Opened += delegate { CancelPetClick(); PopulateMenu(menu); };
            root.ContextMenu = menu;
            SourceInitialized += delegate { InstallNoActivate(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (!allowClose && IsLoaded) { e.Cancel = true; HideToTray(); }
            };
            StateChanged += delegate {
                if (taskbarMinimized && WindowState == WindowState.Normal) {
                    taskbarMinimized = false; ShowInTaskbar = false;
                    InstallNoActivateStyle(); UpdateView(); ClampPosition();
                }
            };
            Closed += delegate { closed = true; timer.Stop(); CancelPetClick(); SavePosition(); if (settingsWindow != null) settingsWindow.Close(); };
            visual.SelectSkin(prefs.Skin == "whale" || prefs.Skin == "panda" ? prefs.Skin : "personal"); prefs.Skin = visual.CurrentSkin;
            visual.SetMotion(prefs.Motion); ApplySize(prefs.Size); SetInitialPosition();
            timer = new DispatcherTimer(DispatcherPriority.Background); timer.Interval = TimeSpan.FromMilliseconds(prefs.Motion ? 33 : 150);
            timer.Tick += delegate { Tick(); }; timer.Start(); UpdateView();
            if (AppearanceError != null) Notice(AppearanceError);
            else if (visual.LastAssetError != null) Notice(visual.LastAssetError);
        }

        private void CancelPetClick() {clickTimer.Stop();clickIntent.Cancel();if(petHit.IsMouseCaptured)petHit.ReleaseMouseCapture();}
        private void PetMouseDown(object sender,MouseButtonEventArgs e) {
            e.Handled=true;
            if(e.ClickCount>=2){CancelPetClick();OpenSettings();return;}
            clickTimer.Stop();
            Point p=e.GetPosition(petHit);
            string token=RecommendationContextToken==null?null:RecommendationContextToken();
            clickIntent.Begin(p.X,p.Y,token);petHit.CaptureMouse();
        }
        private void PetMouseMove(object sender,MouseEventArgs e) {
            if(!clickIntent.IsPressed)return;
            if(e.LeftButton!=MouseButtonState.Pressed){CancelPetClick();return;}
            Point p=e.GetPosition(petHit);
            if(!clickIntent.Move(p.X,p.Y,SystemParameters.MinimumHorizontalDragDistance,SystemParameters.MinimumVerticalDragDistance))return;
            clickTimer.Stop();if(petHit.IsMouseCaptured)petHit.ReleaseMouseCapture();
            try {DragMove();UpdateHeight();ClampPosition();SavePosition();}catch(InvalidOperationException) { }
            e.Handled=true;
        }
        private void PetMouseUp(object sender,MouseButtonEventArgs e) {
            e.Handled=true;
            int interval=System.Windows.Forms.SystemInformation.DoubleClickTime;
            bool wait=clickIntent.Release(clock.ElapsedMilliseconds,interval);
            if(petHit.IsMouseCaptured)petHit.ReleaseMouseCapture();
            if(wait){clickTimer.Interval=TimeSpan.FromMilliseconds(interval);clickTimer.Start();}
        }
        public void SetRecommendationHint(string text) {
            if(recommendationHint==text)return;recommendationHint=text;UpdateView();
        }

        public void Present(PetState state, string message, bool isLive)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(delegate { Present(state, message, isLive); })); return; }
            if (closed) return;
            bool changed = businessState != state || live != isLive || businessMessage != (message ?? "");
            businessState = state; businessMessage = message ?? ""; live = isLive; paused = state == PetState.Paused;
            if(changed&&live&&(state==PetState.Ready||state==PetState.Thinking))transientNotice=null;
            if (live) preview = null;
            if (changed) { UpdateView(); if (settingsWindow != null) RefreshSettings(); }
        }

        public bool SelectSkin(string skin)
        {
            bool result = visual.SelectSkin(skin); prefs.Skin = visual.CurrentSkin; SavePreferences(); UpdateView();
            if (!result) Notice(visual.LastAssetError);
            RefreshSettings();
            return result;
        }
        public void SetMotion(bool enabled) { prefs.Motion = enabled; visual.SetMotion(enabled); timer.Interval = TimeSpan.FromMilliseconds(enabled ? 33 : 150); stateStarted = clock.Elapsed.TotalSeconds; SavePreferences(); RefreshSettings(); }
        public void SetListening(bool value) { if (listening != value) { listening = value; UpdateView(); if (settingsWindow != null) RefreshSettings(); } }
        public void SetSize(double size) { ApplySize(size); ClampPosition(); SavePosition(); RefreshSettings(); }
        public void Preview(PetState state)
        {
            if (live) return;
            preview = state; previewUntil = clock.Elapsed.TotalSeconds + 8; UpdateView();
        }
        public void ApplyFrame(double time) { visual.ApplyFrame(time); }
        public void StopPreview() { preview = null; UpdateView(); }

        private void Tick()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (preview.HasValue && now >= previewUntil) { preview = null; UpdateView(); }
            if (transientNotice != null && now >= noticeUntil) { transientNotice = null; UpdateView(); }
            if (prefs.Motion) visual.ApplyFrame(now - stateStarted);
        }
        private void UpdateView()
        {
            PetState display = preview.HasValue && !live ? preview.Value : businessState;
            if (visual.CurrentState != display) { visual.SetState(display); stateStarted = clock.Elapsed.TotalSeconds; }
            bool suggestion = live && display == PetState.Ready && !String.IsNullOrWhiteSpace(businessMessage);
            bubble.Visibility = suggestion || transientNotice != null ? Visibility.Visible : Visibility.Collapsed;
            bubble.SetReply(transientNotice ?? (suggestion ? businessMessage : ""),suggestion && transientNotice == null);
            petHit.ToolTip = StateName(display) + (String.IsNullOrEmpty(recommendationHint)?"":" · "+recommendationHint) + " · 右键设置 · 双击打开设置";
            UpdateHeight();
        }
        private static string StateName(PetState state) { return new[] { "待机", "思考中", "推荐就绪", "已填入草稿", "已暂停", "连接异常" }[(int)state]; }
        private void Notice(string text) { transientNotice = text; noticeUntil = clock.Elapsed.TotalSeconds + 7; UpdateView(); }
        internal void ShowRecommendationNotice(string text) { if(transientNotice!=text)Notice(text); }
        private void TogglePause()
        {
            if (!live) { Preview(PetState.Paused); return; }
            paused = !paused; if (PauseChanged != null) PauseChanged(paused);
        }
        private void RequestQuit() { if (QuitRequested != null) QuitRequested(); else CloseForExit(); }
        public void CloseForExit() { allowClose = true; Close(); }
        public void HideToTray() {
            if (closed || hiddenToTray) return;
            CancelPetClick();
            bubble.SetExpanded(false);
            SavePosition();
            if (settingsWindow != null) settingsWindow.Close();
            if (MinimizedToTaskbar != null) MinimizedToTaskbar();
            hiddenToTray = true; taskbarMinimized = false;
            Hide(); ShowInTaskbar = false;
        }
        public void RestorePet() {
            if (closed) return;
            hiddenToTray = false; taskbarMinimized = false;
            WindowState = WindowState.Normal; ShowInTaskbar = false;
            InstallNoActivateStyle(); UpdateView(); ClampPosition(); Show();
        }
        public void MinimizeToTaskbar() {
            if (closed || taskbarMinimized) return;
            CancelPetClick();
            bubble.SetExpanded(false);
            hiddenToTray = false;
            SavePosition();
            if (settingsWindow != null) settingsWindow.Close();
            if (MinimizedToTaskbar != null) MinimizedToTaskbar();
            taskbarMinimized = true;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) & ~0x08000080);
            ShowInTaskbar = true; Show(); WindowState = WindowState.Minimized;
        }
        private bool CanStartListening { get { return live && !listening && !paused && (businessState == PetState.Idle || businessState == PetState.Filled || businessState == PetState.Error); } }
        private void RequestListen()
        {
            if (!CanStartListening || ListenRequested == null) return;
            listening = true; if (settingsWindow != null) RefreshSettings(); ListenRequested();
        }
        private void PopulateMenu(ContextMenu menu)
        {
            menu.Items.Clear();
            AddMenu(menu, "桌宠设置…", OpenSettings);
            AddMenu(menu, "模型连接…", OpenConnection);
            if (live)
            {
                menu.Items.Add(new Separator());
                AddMenu(menu, paused ? "继续推荐" : "暂停推荐", TogglePause);
            }
            menu.Items.Add(new Separator()); AddMenu(menu, "隐藏到托盘", HideToTray); AddMenu(menu, "退出桌宠", RequestQuit);
        }

        public void OpenSettings()
        {
            CancelPetClick();
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            settingsWindow = new PetSettingsWindow(this, workspaceRoot, RequestListen, TogglePause, RequestQuit, delegate { return CanStartListening; });
            settingsWindow.Closed += delegate { settingsWindow = null; };
            settingsWindow.Show();
        }
        private void RefreshSettings()
        {
            if (settingsWindow != null) settingsWindow.Refresh();
        }
        private static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        private static bool ContainsVisibleControl(DependencyObject node)
        {
            if (node == null) return false;
            UIElement element = node as UIElement;
            if (element != null && element.Visibility != Visibility.Visible) return false;
            if (node is System.Windows.Controls.Primitives.ButtonBase || node is ComboBox || node is Slider) return true;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) if (ContainsVisibleControl(VisualTreeHelper.GetChild(node, i))) return true;
            return false;
        }
        private static MenuItem AddMenu(ItemsControl menu, string title, Action action) { MenuItem item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item); return item; }

        private PetPreferences ReadPreferences()
        {
            try
            {
                if (new FileInfo(settingsPath).Length >= 16384) throw new InvalidDataException();
                PetPreferences result = new JavaScriptSerializer().Deserialize<PetPreferences>(File.ReadAllText(settingsPath));
                if (result == null) throw new InvalidDataException();
                return result;
            }
            catch (FileNotFoundException) { return new PetPreferences(); }
            catch (DirectoryNotFoundException) { return new PetPreferences(); }
            catch (Exception ex) {
                if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                AppearanceError = appearanceReadError = AppearanceReadFailure;
            }
            return new PetPreferences();
        }
        internal bool RetrySaveAppearance() { return SavePreferences(); }
        private bool SavePreferences()
        {
            if (appearanceReadError != null) { AppearanceError = appearanceReadError; RefreshSettings(); return false; }
            string temporary = settingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                string dir = Path.GetDirectoryName(settingsPath); if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(prefs));
                if (File.Exists(settingsPath)) File.Replace(temporary, settingsPath, null);
                else File.Move(temporary, settingsPath);
                AppearanceError = null;
                if (transientNotice == AppearanceSaveFailure) { transientNotice = null; UpdateView(); }
                RefreshSettings();
                return true;
            }
            catch (Exception ex) {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)) throw;
                AppearanceError = AppearanceSaveFailure;
                if (!closed && settingsWindow == null) Notice(AppearanceError);
                RefreshSettings();
                return false;
            }
            finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }
        private void ApplySize(double size)
        {
            double center = Double.IsNaN(Left) ? Double.NaN : PetCenterX;
            double bottom = Double.IsNaN(Top) ? Double.NaN : Top + Height;
            if (Double.IsNaN(size) || Double.IsInfinity(size)) size = 1;
            prefs.Size = Math.Max(.75, Math.Min(1.25, size));
            visual.Width = BasePetSize * prefs.Size; visual.Height = BasePetSize * prefs.Size;
            petHit.Width = visual.Width; petHit.Height = visual.Height;
            UpdateBounds(center, bottom);
        }
        private void UpdateHeight()
        {
            if (WindowState == WindowState.Minimized) return;
            double center = Double.IsNaN(Left) ? Double.NaN : Left + 4 + petHit.Margin.Left + petHit.Width / 2;
            double bottom = Double.IsNaN(Top) ? Double.NaN : Top + Height;
            UpdateBounds(center, bottom);
        }
        private void UpdateBounds(double center, double bottom)
        {
            Rect work=WorkAreaForPoint(new Point(Double.IsNaN(center)?SystemParameters.WorkArea.Left+24:center,Double.IsNaN(bottom)?SystemParameters.WorkArea.Bottom-20:bottom-visual.Height/2));
            double nextWidth = (IsBubbleVisible ? Math.Max(280, visual.Width) : visual.Width) + 8;
            // Inline images invalidate their TextBlock immediately, while WPF can
            // defer invalidating its ancestors until the next layout pass. This
            // synchronous window resize must measure the new content, not the
            // previous reply's cached bubble height.
            bubble.PrepareLayout(Math.Max(1,work.Height-visual.Height-8));
            bubble.Measure(new Size(280, Double.PositiveInfinity));
            Width = nextWidth; Height = (IsBubbleVisible ? bubble.DesiredSize.Height : 0) + visual.Height + 8;
            if (!Double.IsNaN(center))
            {
                Left = Math.Max(work.Left, Math.Min(work.Right - Width, center - Width / 2));
                petHit.Margin = new Thickness(Math.Max(0, Math.Min(Width-8-visual.Width,center - Left - 4 - visual.Width / 2)), 0, 0, 0);
            }
            else petHit.Margin = new Thickness((Width - 8 - visual.Width) / 2, 0, 0, 0);
            if (!Double.IsNaN(bottom)) Top = Math.Max(work.Top,Math.Min(work.Bottom-Height,bottom-Height));
        }
        private void SetInitialPosition()
        {
            Rect work = SystemParameters.WorkArea;
            bool anchor=Finite(prefs.PetCenterX)&&Finite(prefs.PetBottom)&&(prefs.PositionSaved||(prefs.PetCenterX!=-1&&prefs.PetBottom!=-1));
            bool legacy=Finite(prefs.Left)&&Finite(prefs.Top)&&(prefs.PositionSaved||(prefs.Left!=-1&&prefs.Top!=-1));
            Left = anchor ? prefs.PetCenterX-Width/2 : legacy ? prefs.Left : work.Left+24;
            Top = anchor ? prefs.PetBottom-Height : legacy ? prefs.Top : work.Bottom-Height-20; ClampPosition();
        }
        private void ClampPosition()
        {
            Rect work = WorkAreaForPoint(new Point(PetCenterX,Top+Height-visual.Height/2));
            Left = Math.Max(work.Left, Math.Min(work.Right - Width, Left)); Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, Top));
        }
        static bool Finite(double value){return !Double.IsNaN(value)&&!Double.IsInfinity(value);}
        Rect MonitorWorkArea(Point anchor) {
            var source=PresentationSource.FromVisual(this);
            Matrix toDevice=source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformToDevice:Matrix.Identity;
            if(source==null){double dpi=System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width/SystemParameters.PrimaryScreenWidth;toDevice.Scale(dpi,dpi);}
            Point pixel=toDevice.Transform(anchor);
            var area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(pixel.X),(int)Math.Round(pixel.Y))).WorkingArea;
            toDevice.Invert();Point topLeft=toDevice.Transform(new Point(area.Left,area.Top)),bottomRight=toDevice.Transform(new Point(area.Right,area.Bottom));
            return new Rect(topLeft,bottomRight);
        }
        private void SavePosition() { if (WindowState == WindowState.Minimized || Double.IsNaN(Left) || Double.IsNaN(Top)) return; prefs.PositionSaved=true;prefs.Left = Left; prefs.Top = Top; prefs.PetCenterX = Left + 4 + petHit.Margin.Left + visual.Width / 2; prefs.PetBottom = Top + Height; SavePreferences(); }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        private void InstallNoActivate()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            InstallNoActivateStyle();
            HwndSource source = HwndSource.FromHwnd(hwnd); if (source != null) source.AddHook(WindowMessage);
        }
        private void InstallNoActivateStyle() {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && !ShowInTaskbar) SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x00000080);
        }
        private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg == 0x0021) { handled = true; return new IntPtr(3); }
            return IntPtr.Zero;
        }
    }
}
