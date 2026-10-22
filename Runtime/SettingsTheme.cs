using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WeChatPet {
    internal static class SettingsTheme {
        internal static void EnableHeaderDrag(Window window, FrameworkElement header) {
            var panel=header as Panel;if(panel!=null)panel.Background=Brushes.Transparent;
            // Include the outer padding and header margin, not just the title glyphs.
            window.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
                if(e.ChangedButton!=MouseButton.Left||e.ButtonState!=MouseButtonState.Pressed||!header.IsVisible)return;
                var point=e.GetPosition(window);
                var bottom=header.TranslatePoint(new Point(0,header.ActualHeight+header.Margin.Bottom),window).Y;
                if(point.Y<0||point.Y>bottom)return;
                for(DependencyObject node=e.OriginalSource as DependencyObject;node!=null&&node!=window;) {
                    if(node is ButtonBase||node is TextBoxBase||node is PasswordBox||node is Selector||node is System.Windows.Documents.Hyperlink)return;
                    node=node is Visual||node is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
                }
                try {window.DragMove();e.Handled=true;}catch(InvalidOperationException) { }
            };
        }
        internal static readonly Brush Ink = Brush("#25342E"), Muted = Brush("#626F66"), Accent = Brush("#315D50"), Line = Brush("#E4E9E2"), Paper = Brush("#FAFBF9");
        internal static Brush Brush(string hex) { var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b; }
        internal static TextBlock Text(string text,double size=13,Brush color=null) {return new TextBlock{Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};}
        internal static ToggleButton Switch(string name,bool value) {
            var track=new FrameworkElementFactory(typeof(Border));track.Name="Track";track.SetValue(Border.CornerRadiusProperty,new CornerRadius(11));track.SetValue(Border.BackgroundProperty,Brush("#D8DED7"));
            var knob=new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));knob.Name="Knob";knob.SetValue(System.Windows.Shapes.Ellipse.FillProperty,Brushes.White);knob.SetValue(FrameworkElement.WidthProperty,16.0);knob.SetValue(FrameworkElement.HeightProperty,16.0);knob.SetValue(FrameworkElement.MarginProperty,new Thickness(3));knob.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);track.AppendChild(knob);
            var template=new ControlTemplate(typeof(ToggleButton)){VisualTree=track};
            var on=new Trigger{Property=ToggleButton.IsCheckedProperty,Value=true};on.Setters.Add(new Setter(Border.BackgroundProperty,Accent,"Track"));on.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Right,"Knob"));template.Triggers.Add(on);
            var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Brush("#93B2A5"),"Track"));focus.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(2),"Track"));template.Triggers.Add(focus);
            var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.45));template.Triggers.Add(disabled);
            var toggle=new ToggleButton{Width=38,Height=22,IsChecked=value,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand,Template=template,FocusVisualStyle=null,ToolTip=name};AutomationProperties.SetName(toggle,name);return toggle;
        }
        internal static Border Card(UIElement content) {return new Border{Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(13),Child=content,Margin=new Thickness(0,0,0,10)};}
        internal static Button Button(object content,string name, bool primary=false) {
            var b=new FrameworkElementFactory(typeof(Border));b.Name="Chrome";
            b.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));
            b.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
            b.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
            b.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
            b.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
            var p=new FrameworkElementFactory(typeof(ContentPresenter));
            p.SetValue(ContentPresenter.HorizontalAlignmentProperty,new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            p.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);b.AppendChild(p);
            var template=new ControlTemplate(typeof(Button)){VisualTree=b};
            var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,Brush(primary?"#244A3D":"#EDF3ED"),"Chrome"));template.Triggers.Add(hover);
            var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Chrome"));template.Triggers.Add(focus);
            var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.45,"Chrome"));template.Triggers.Add(disabled);
            var button=new Button{Content=content,Height=32,Padding=new Thickness(12,0,12,0),Background=primary?Accent:Brushes.Transparent,Foreground=primary?Brushes.White:Ink,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(1),Template=template,Cursor=Cursors.Hand,HorizontalContentAlignment=HorizontalAlignment.Center,FocusVisualStyle=null};
            AutomationProperties.SetName(button,name);return button;
        }
        internal static FrameworkElement Tabs(bool model,Action appearance,Action connection) {
            var grid=new UniformGrid{Rows=1,Columns=2};
            string[] labels={"外观","模型连接"};
            for(int i=0;i<2;i++) {int index=i;var b=Button(labels[i],labels[i]);bool selected=model==(i==1);b.Background=selected?Brushes.White:Brushes.Transparent;b.Foreground=selected?Ink:Muted;b.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;b.Height=32;b.Click+=delegate{if(index==0)appearance();else connection();};grid.Children.Add(b);}
            return new Border{Background=Brush("#EFF2ED"),CornerRadius=new CornerRadius(11),Padding=new Thickness(3),Child=grid,Margin=new Thickness(0,0,0,12)};
        }
        internal static DataTemplate ModelTemplate() {
            var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(FrameworkElement.MarginProperty,new Thickness(0,1,0,1));
            var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Name"));name.SetValue(TextBlock.FontSizeProperty,14.0);name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);name.SetValue(TextBlock.ForegroundProperty,Ink);panel.AppendChild(name);
            var hint=new FrameworkElementFactory(typeof(TextBlock));hint.SetBinding(TextBlock.TextProperty,new Binding("Hint"));hint.SetValue(TextBlock.FontSizeProperty,11.0);hint.SetValue(TextBlock.ForegroundProperty,Muted);hint.SetValue(FrameworkElement.MarginProperty,new Thickness(0,4,0,0));hint.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);panel.AppendChild(hint);
            return new DataTemplate{VisualTree=panel};
        }
        internal static ComboBox ModelPicker(FrameworkElement popupHeader=null) {
            // Keep native ComboBox keyboard/accessibility behavior; replace only its chrome.
            var root=new FrameworkElementFactory(typeof(Grid));
            var toggle=new FrameworkElementFactory(typeof(ToggleButton));toggle.SetValue(Control.FocusableProperty,false);toggle.SetBinding(ToggleButton.IsCheckedProperty,new Binding("IsDropDownOpen"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent),Mode=BindingMode.TwoWay});
            var border=new FrameworkElementFactory(typeof(Border));border.Name="PickerBorder";border.SetValue(Border.BackgroundProperty,Brushes.White);border.SetValue(Border.BorderBrushProperty,Line);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));
            var arrow=new FrameworkElementFactory(typeof(TextBlock));arrow.SetValue(TextBlock.TextProperty,"⌄");arrow.SetValue(TextBlock.ForegroundProperty,Muted);arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Right);arrow.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);arrow.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,13,0));border.AppendChild(arrow);
            toggle.SetValue(Control.TemplateProperty,new ControlTemplate(typeof(ToggleButton)){VisualTree=border});root.AppendChild(toggle);
            var selected=new FrameworkElementFactory(typeof(ContentPresenter));selected.SetValue(UIElement.IsHitTestVisibleProperty,false);selected.SetValue(FrameworkElement.MarginProperty,new Thickness(13,8,36,8));selected.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);selected.SetBinding(ContentPresenter.ContentProperty,new Binding("SelectionBoxItem"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});selected.SetBinding(ContentPresenter.ContentTemplateProperty,new Binding("ItemTemplate"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});root.AppendChild(selected);
            var popup=new FrameworkElementFactory(typeof(Popup));popup.Name="PART_Popup";popup.SetValue(Popup.PlacementProperty,PlacementMode.Bottom);popup.SetValue(Popup.AllowsTransparencyProperty,true);popup.SetValue(Popup.FocusableProperty,false);popup.SetBinding(Popup.IsOpenProperty,new Binding("IsDropDownOpen"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
            var box=new FrameworkElementFactory(typeof(Border));box.SetValue(Border.BackgroundProperty,Brushes.White);box.SetValue(Border.CornerRadiusProperty,new CornerRadius(12));box.SetValue(Border.BorderBrushProperty,Line);box.SetValue(Border.BorderThicknessProperty,new Thickness(1));box.SetValue(Border.PaddingProperty,new Thickness(5));box.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,0));box.SetBinding(FrameworkElement.WidthProperty,new Binding("ActualWidth"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
            var body=new FrameworkElementFactory(typeof(DockPanel));
            if(popupHeader!=null){var header=new FrameworkElementFactory(typeof(ContentControl));header.SetValue(DockPanel.DockProperty,Dock.Top);header.SetBinding(ContentControl.ContentProperty,new Binding("Tag"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});body.AppendChild(header);}
            var scroll=new FrameworkElementFactory(typeof(ScrollViewer));scroll.SetValue(FrameworkElement.MaxHeightProperty,popupHeader==null?290.0:250.0);scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto);var items=new FrameworkElementFactory(typeof(ItemsPresenter));scroll.AppendChild(items);body.AppendChild(scroll);box.AppendChild(body);popup.AppendChild(box);root.AppendChild(popup);
            var template=new ControlTemplate(typeof(ComboBox)){VisualTree=root};
            var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.55));template.Triggers.Add(disabled);
            var style=new Style(typeof(ComboBoxItem));
            var rowBorder=new FrameworkElementFactory(typeof(Border));rowBorder.Name="Row";rowBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));rowBorder.SetValue(Border.PaddingProperty,new Thickness(10,9,10,9));rowBorder.SetValue(Border.BackgroundProperty,Brushes.Transparent);
            var row=new FrameworkElementFactory(typeof(DockPanel));var check=new FrameworkElementFactory(typeof(TextBlock));check.Name="Check";check.SetValue(DockPanel.DockProperty,Dock.Right);check.SetValue(TextBlock.TextProperty,"✓");check.SetValue(TextBlock.ForegroundProperty,Accent);check.SetValue(FrameworkElement.WidthProperty,22.0);check.SetValue(UIElement.VisibilityProperty,Visibility.Hidden);check.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);row.AppendChild(check);
            var item=new FrameworkElementFactory(typeof(ContentPresenter));item.SetBinding(ContentPresenter.ContentProperty,new Binding("Content"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});item.SetBinding(ContentPresenter.ContentTemplateProperty,new Binding("ContentTemplate"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});row.AppendChild(item);rowBorder.AppendChild(row);
            var it=new ControlTemplate(typeof(ComboBoxItem)){VisualTree=rowBorder};var chosen=new Trigger{Property=ComboBoxItem.IsSelectedProperty,Value=true};chosen.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible,"Check"));chosen.Setters.Add(new Setter(Border.BackgroundProperty,Brush("#EDF4ED"),"Row"));it.Triggers.Add(chosen);var over=new Trigger{Property=ComboBoxItem.IsHighlightedProperty,Value=true};over.Setters.Add(new Setter(Border.BackgroundProperty,Brush("#F1F5F0"),"Row"));it.Triggers.Add(over);style.Setters.Add(new Setter(Control.TemplateProperty,it));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
            var combo=new ComboBox{Tag=popupHeader,Height=54,Template=template,ItemTemplate=ModelTemplate(),ItemContainerStyle=style,IsEditable=false,IsTextSearchEnabled=true,MaxDropDownHeight=290,HorizontalContentAlignment=HorizontalAlignment.Stretch};TextSearch.SetTextPath(combo,"Name");AutomationProperties.SetName(combo,"回复模型");return combo;
        }
    }
}
