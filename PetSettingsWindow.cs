using System;
using System.Diagnostics;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WeChatPet
{
    // This window only edits presentation preferences. It never starts the reply engine.
    public sealed class PetSettingsWindow : Window
    {
        private readonly PetWindow pet;
        private readonly Border surface;
        private readonly Button personalCard, whaleCard, pandaCard;
        private readonly Border personalCheck, whaleCheck, pandaCheck;
        private readonly Button[] sizeButtons = new Button[3];
        private readonly List<Button> previewButtons = new List<Button>();
        private readonly Dictionary<string, PetVisual> thumbnails = new Dictionary<string, PetVisual>();
        private readonly DispatcherTimer previewTimer = new DispatcherTimer(DispatcherPriority.Background);
        private readonly Stopwatch previewClock = new Stopwatch();
        private PetState previewState;
        private string previewSkin;
        private static readonly string[] PreviewNames = { "待机", "思考", "推荐就绪", "填入草稿", "暂停", "连接异常" };
        internal bool IsPreviewPlaying { get { return previewTimer.IsEnabled; } }
        internal PetState PreviewState { get { return previewState; } }
        private readonly ToggleButton motion;
        private readonly StackPanel previewBody;
        private readonly TextBlock previewArrow, previewNote;
        private readonly TextBlock saveStatus;
        private readonly Button retrySave;
        private readonly Action listen, togglePause, quit;
        private readonly Func<bool> canStart;
        private bool refreshing, expanded;
        private static readonly Brush Ink = Color("#25342E");
        private static readonly Brush Muted = Color("#77827B");
        private static readonly Brush Accent = Color("#315D50");
        public FrameworkElement PreviewSurface { get { return surface; } }

        public PetSettingsWindow(PetWindow pet, string workspaceRoot, Action listen, Action togglePause, Action quit, Func<bool> canStart)
        {
            this.pet = pet; this.listen = listen; this.togglePause = togglePause;
            this.quit = quit; this.canStart = canStart;
            Title = "桌宠设置"; Width = 430; SizeToContent = SizeToContent.Height;
            AppBrand.ApplyIcon(this); MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 24);
            ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.None;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = pet.ShowInTaskbar; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };

            surface = new Border {
                MaxWidth = 418, HorizontalAlignment = HorizontalAlignment.Stretch, Background = SettingsTheme.Paper, BorderBrush = SettingsTheme.Line,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16)
            };
            // Keep the shadow outside the renderable surface so exported checks stay clean.
            var frame = new Border { Margin = new Thickness(6), Child = new ScrollViewer { Content = surface, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            Content = frame;
            var panel = new StackPanel(); surface.Child = panel;

            var header = new Grid { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Label("外观", 21, Ink, FontWeights.Normal));
            var minimize = PlainButton("—", "最小化到任务栏", 30, 30, 0);
            minimize.Click += delegate { pet.MinimizeToTaskbar(); }; Grid.SetColumn(minimize, 1); header.Children.Add(minimize);
            var close = PlainButton(Cross(), "关闭设置", 30, 30, 9);
            close.Click += delegate { Close(); }; Grid.SetColumn(close, 2); header.Children.Add(close);
            SettingsTheme.EnableHeaderDrag(this,header);
            panel.Children.Add(header);
            panel.Children.Add(SettingsTheme.Tabs(false, delegate {}, delegate { pet.OpenConnection(); }));
            var appearanceTitle = Label("宠物形象", 14, Ink, FontWeights.SemiBold);
            appearanceTitle.Margin = new Thickness(0, 0, 0, 8); panel.Children.Add(appearanceTitle);

            var cards = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            personalCard = SkinCard(workspaceRoot, "personal", "嗨嗨", out personalCheck);
            whaleCard = SkinCard(workspaceRoot, "whale", "DSH 鲸鱼", out whaleCheck);
            pandaCard = SkinCard(workspaceRoot, "panda", "团团熊猫", out pandaCheck);
            pandaCard.Click += delegate { pet.SelectSkin("panda"); Refresh(); };
            personalCard.Click += delegate { pet.SelectSkin("personal"); Refresh(); };
            whaleCard.Click += delegate { pet.SelectSkin("whale"); Refresh(); };
            cards.Children.Add(personalCard); Grid.SetColumn(whaleCard, 2); cards.Children.Add(whaleCard); Grid.SetColumn(pandaCard, 4); cards.Children.Add(pandaCard); panel.Children.Add(cards);

            var options = new Border { Background = Brushes.White, BorderBrush = Color("#E9ECE7"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 0, 14, 0) };
            var optionRows = new StackPanel(); options.Child = optionRows;
            var sizeRow = Row("大小", 42);
            var segments = new UniformGrid { Rows = 1, Columns = 3 };
            var segmentBox = new Border { Background = Color("#F0F3EF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Width = 160, Height = 32, Child = segments, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            string[] sizeLabels = { "小", "标准", "大" }; double[] sizes = { .82, 1, 1.18 };
            for (int i = 0; i < 3; i++) {
                int index = i; Button button = PlainButton(sizeLabels[i], "显示大小：" + sizeLabels[i], Double.NaN, 26, 6);
                button.FontSize = 12; button.Click += delegate { pet.SetSize(sizes[index]); Refresh(); };
                sizeButtons[i] = button; segments.Children.Add(button);
            }
            Grid.SetColumn(segmentBox, 1); sizeRow.Children.Add(segmentBox); optionRows.Children.Add(sizeRow);
            optionRows.Children.Add(new Border { Height = 1, Background = Color("#EDF0EB") });
            var motionRow = Row("动画", 40);
            motion = SettingsTheme.Switch("播放动画",pet.MotionEnabled); motion.HorizontalAlignment = HorizontalAlignment.Right;
            motion.Checked += delegate { if (!refreshing) { pet.SetMotion(true); Refresh(); } };
            motion.Unchecked += delegate { if (!refreshing) { pet.SetMotion(false); Refresh(); } };
            Grid.SetColumn(motion, 1); motionRow.Children.Add(motion); optionRows.Children.Add(motionRow); panel.Children.Add(options);
            var memoryButton=SettingsTheme.Button("好友记忆  ›","记忆设置");memoryButton.HorizontalContentAlignment=HorizontalAlignment.Left;memoryButton.Padding=new Thickness(2,0,2,0);memoryButton.Margin=new Thickness(0,6,0,0);memoryButton.Click+=delegate{pet.OpenMemory();};panel.Children.Add(memoryButton);

            var previewHeader = new Grid(); previewHeader.ColumnDefinitions.Add(new ColumnDefinition()); previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previewHeader.Children.Add(Label("动作预览", 13, Ink, FontWeights.Normal));
            previewArrow = Label("⌄", 17, Muted, FontWeights.Normal); Grid.SetColumn(previewArrow, 1); previewHeader.Children.Add(previewArrow);
            var previewToggle = PlainButton(previewHeader, "展开或收起动作预览", Double.NaN, 32, 9);
            previewToggle.HorizontalContentAlignment = HorizontalAlignment.Stretch; previewToggle.Padding = new Thickness(2, 0, 2, 0);
            previewToggle.Margin = new Thickness(0, 6, 0, 0);
            previewToggle.Click += delegate { SetPreviewExpanded(!expanded); }; panel.Children.Add(previewToggle);
            previewBody = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(-3, 2, -3, 0) };
            var previewGrid = new UniformGrid { Rows = 2, Columns = 3 };
            string[] states = { "待机", "思考", "推荐就绪", "填入草稿", "暂停", "连接异常" };
            foreach (PetState state in Enum.GetValues(typeof(PetState))) {
                PetState captured = state;
                var button = PlainButton(states[(int)state], "预览：" + states[(int)state], Double.NaN, 31, 8);
                button.Background = Color("#F0F3EF"); button.FontSize = 12; button.Margin = new Thickness(3);
                button.Click += delegate { PlayPreview(captured); };
                previewGrid.Children.Add(button); previewButtons.Add(button);
            }
            previewBody.Children.Add(previewGrid);
            previewNote = Label("点击动作，在上方形象卡片中播放", 11, Muted, FontWeights.Normal); previewNote.Margin = new Thickness(5, 8, 0, 2);
            previewBody.Children.Add(previewNote); panel.Children.Add(previewBody);

            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            saveStatus = SettingsTheme.Text("更改自动保存", 11, Muted);
            footer.Children.Add(saveStatus);
            var more = PlainButton("···", "更多操作", 32, 26, 8); more.FontSize = 20; more.ToolTip = "更多操作";
            more.Click += delegate { var menu = MoreMenu(); menu.PlacementTarget = more; menu.Placement = PlacementMode.Top; menu.IsOpen = true; };
            Grid.SetColumn(more, 1); footer.Children.Add(more); panel.Children.Add(footer);
            retrySave = SettingsTheme.Button("重试保存", "重试保存外观设置");
            retrySave.HorizontalAlignment = HorizontalAlignment.Left; retrySave.Margin = new Thickness(0, 6, 0, 0);
            retrySave.Click += delegate { pet.RetrySaveAppearance(); Refresh(); };
            panel.Children.Add(retrySave);
            previewTimer.Interval = TimeSpan.FromMilliseconds(33);
            previewTimer.Tick += delegate {
                if (previewClock.Elapsed.TotalSeconds >= 8) { StopActionPreview(); return; }
                thumbnails[previewSkin].ApplyFrame(previewClock.Elapsed.TotalSeconds);
            };
            Closed += delegate { StopActionPreview(); };
            Refresh();
        }

        public void PlayPreview(PetState state)
        {
            StopActionPreview();
            previewState = state; previewSkin = pet.CurrentSkin;
            var thumbnail = thumbnails[previewSkin];
            thumbnail.SetState(state); thumbnail.SetMotion(true); thumbnail.ApplyFrame(0);
            previewClock.Restart(); previewTimer.Start();
            SetPreviewExpanded(true); Refresh();
        }

        private void StopActionPreview()
        {
            previewTimer.Stop(); previewClock.Reset();
            foreach (var thumbnail in thumbnails.Values) {
                thumbnail.SetMotion(false); thumbnail.SetState(PetState.Ready);
            }
            Refresh();
        }

        public void SetPreviewExpanded(bool value)
        {
            if (!value && previewTimer.IsEnabled) StopActionPreview();
            expanded = value; previewBody.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            previewArrow.Text = value ? "⌃" : "⌄";
            // Preserve the opened section when an incoming state notification refreshes labels.
            if (IsVisible) Dispatcher.BeginInvoke(new Action(delegate {
                UpdateLayout();
                Rect work = SystemParameters.WorkArea;
                if (Top + ActualHeight > work.Bottom - 12) Top = Math.Max(work.Top + 12, work.Bottom - ActualHeight - 12);
            }));
        }

        public void Refresh()
        {
            refreshing = true;
            try {
                SelectCard(personalCard, personalCheck, pet.CurrentSkin == "personal");
                SelectCard(whaleCard, whaleCheck, pet.CurrentSkin == "whale");
                SelectCard(pandaCard, pandaCheck, pet.CurrentSkin == "panda");
                int selected = pet.PetScale < .95 ? 0 : pet.PetScale > 1.05 ? 2 : 1;
                for (int i = 0; i < sizeButtons.Length; i++) {
                    sizeButtons[i].Background = i == selected ? Brushes.White : Brushes.Transparent;
                    sizeButtons[i].Foreground = i == selected ? Ink : Muted;
                    sizeButtons[i].FontWeight = i == selected ? FontWeights.SemiBold : FontWeights.Normal;
                }
                motion.IsChecked = pet.MotionEnabled;
                saveStatus.Text = pet.AppearanceError ?? "更改自动保存";
                retrySave.Visibility = pet.CanRetryAppearance ? Visibility.Visible : Visibility.Collapsed;
                if (previewTimer.IsEnabled && previewSkin != pet.CurrentSkin) {
                    thumbnails[previewSkin].SetMotion(false); thumbnails[previewSkin].SetState(PetState.Ready);
                    previewSkin = pet.CurrentSkin;
                    thumbnails[previewSkin].SetState(previewState); thumbnails[previewSkin].SetMotion(true);
                    previewClock.Restart();
                }
                for (int i = 0; i < previewButtons.Count; i++) {
                    bool previewSelected = previewTimer.IsEnabled && (int)previewState == i;
                    previewButtons[i].IsEnabled = true;
                    previewButtons[i].Background = previewSelected ? Color("#DCEBDD") : Color("#F0F3EF");
                    previewButtons[i].FontWeight = previewSelected ? FontWeights.SemiBold : FontWeights.Normal;
                }
                previewNote.Text = previewTimer.IsEnabled
                    ? PreviewNames[(int)previewState] + " · 卡片内播放 8 秒，推荐照常运行"
                    : "点击动作，在上方形象卡片中播放";
            } finally { refreshing = false; }
        }

        private ContextMenu MoreMenu()
        {
            var menu = new ContextMenu { FontFamily = FontFamily, FontSize = 13, Background = Brushes.White, Foreground = Ink, Padding = new Thickness(5) };
            if (pet.IsLive) {
                MenuItem start = MenuItem(pet.IsListening ? "正在等待来信" : "等待新来信", listen);
                start.IsEnabled = canStart != null && canStart(); menu.Items.Add(start);
                menu.Items.Add(MenuItem(pet.BusinessState == PetState.Paused ? "继续推荐" : "暂停推荐", togglePause));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(MenuItem("退出桌宠", quit)); return menu;
        }
        private static MenuItem MenuItem(string text, Action action)
        {
            var item = new MenuItem { Header = text, Padding = new Thickness(12, 7, 12, 7) };
            item.Click += delegate { if (action != null) action(); }; return item;
        }
        private static Grid Row(string text, double height)
        {
            var row = new Grid { Height = height };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Label(text, 13, Ink, FontWeights.Normal)); return row;
        }
        private static TextBlock Label(string text, double size, Brush color, FontWeight weight)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        }
        private Button SkinCard(string root, string skin, string title, out Border check)
        {
            var content = new Grid { Margin = new Thickness(3, 5, 3, 6) };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(94) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            var thumbnail = new PetVisual(root) { Width = 84, Height = 94, HorizontalAlignment = HorizontalAlignment.Center };
            thumbnails.Add(skin, thumbnail);
            bool available = thumbnail.SelectSkin(skin); thumbnail.SetState(PetState.Ready); thumbnail.SetMotion(false);
            thumbnail.Visibility = available ? Visibility.Visible : Visibility.Hidden;
            content.Children.Add(thumbnail);
            var caption = Label(available ? title : title + " · 暂不可用", 12, Ink, FontWeights.Medium);
            caption.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(caption, 1); content.Children.Add(caption);
            var glyph = new Path { Data = Geometry.Parse("M 4,8 L 7,11 L 12,5"), Stroke = Brushes.White, StrokeThickness = 1.6, Width = 16, Height = 16 };
            check = new Border { Width = 18, Height = 18, Background = Accent, CornerRadius = new CornerRadius(9), Child = glyph, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), Visibility = Visibility.Hidden };
            content.Children.Add(check);
            var card = PlainButton(content, "选择" + title, Double.NaN, 130, 12);
            card.BorderThickness = new Thickness(1.5); card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            card.VerticalContentAlignment = VerticalAlignment.Stretch; card.IsEnabled = available;
            if (!available) card.ToolTip = "未找到此形象素材，可选择其他形象。";
            return card;
        }
        private static void SelectCard(Button card, Border check, bool selected)
        {
            card.Background = selected ? Color("#EDF4ED") : Brushes.White;
            card.BorderBrush = selected ? Accent : Color("#E7EAE5");
            check.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
        }
        private static Path Cross() { return new Path { Data = Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"), Stroke = Muted, StrokeThickness = 1.5, Width = 12, Height = 12 }; }
        private static Brush Color(string value) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value); brush.Freeze(); return brush; }

        private static Button PlainButton(object content, string accessibleName, double width, double height, double radius)
        {
            var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Chrome";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true); border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Color("#E6EDE5"), "Chrome")); template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Chrome"));
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1.5), "Chrome")); template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45, "Chrome")); template.Triggers.Add(disabled);
            var button = new Button { Content = content, Width = width, Height = height, Background = Brushes.Transparent, Foreground = Ink,
                BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                FontSize = 13, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand, Template = template, FocusVisualStyle = null };
            AutomationProperties.SetName(button, accessibleName); return button;
        }
        private static ToggleButton MotionSwitch()
        {
            var track = new FrameworkElementFactory(typeof(Border)); track.Name = "Track";
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(11)); track.SetValue(Border.BackgroundProperty, Color("#D8DED7"));
            var knob = new FrameworkElementFactory(typeof(Ellipse)); knob.Name = "Knob";
            knob.SetValue(Ellipse.FillProperty, Brushes.White); knob.SetValue(FrameworkElement.WidthProperty, 16.0); knob.SetValue(FrameworkElement.HeightProperty, 16.0);
            knob.SetValue(FrameworkElement.MarginProperty, new Thickness(3)); knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); track.AppendChild(knob);
            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = track };
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BackgroundProperty, Accent, "Track")); on.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob")); template.Triggers.Add(on);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Color("#93B2A5"), "Track")); focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Track")); template.Triggers.Add(focus);
            var toggle = new ToggleButton { Width = 38, Height = 22, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Template = template, FocusVisualStyle = null };
            AutomationProperties.SetName(toggle, "播放动画"); return toggle;
        }
    }
}

