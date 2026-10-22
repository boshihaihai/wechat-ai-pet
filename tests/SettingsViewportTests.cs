using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using WeChatPet;
using WeChatPet.Runtime;

// Offline layout only. No window is shown, runtime is created, or account is read.
public static class SettingsViewportTests {
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject {
        if(root is T)yield return (T)root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Visuals<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    static bool VisibleWithin(FrameworkElement element,FrameworkElement ancestor) {
        for(DependencyObject node=element;node!=null;node=VisualTreeHelper.GetParent(node)) {
            var item=node as UIElement;if(item!=null&&item.Visibility!=Visibility.Visible)return false;
            if(node==ancestor)return true;
        }
        return false;
    }
    static FrameworkElement Detach(Window window) {
        var root=(FrameworkElement)window.Content;
        root.SetValue(TextElement.FontFamilyProperty,window.FontFamily);
        root.SetValue(TextElement.FontSizeProperty,window.FontSize);
        root.UseLayoutRounding=window.UseLayoutRounding;
        window.Content=null;
        return root;
    }
    static void Layout(FrameworkElement root,double height) {
        for(int pass=0;pass<4;pass++) {
            root.Measure(new Size(430,height));
            root.Arrange(new Rect(0,0,430,height));root.UpdateLayout();
        }
    }
    static void HorizontalBounds(FrameworkElement surface,string name) {
        var controls=Visuals<FrameworkElement>(surface).Where(e=>
            (e is ButtonBase||e is ComboBox||e is PasswordBox||e is TextBlock)&&
            e.ActualWidth>0&&e.ActualHeight>0&&VisibleWithin(e,surface)).ToList();
        Check(controls.Count>8,name+": real labels and controls are laid out");
        foreach(var control in controls) {
            var bounds=control.TransformToAncestor(surface).TransformBounds(new Rect(control.RenderSize));
            Check(bounds.Left>=-.5&&bounds.Right<=surface.ActualWidth+.5,
                name+": content stays inside surface: "+control.GetType().Name+" "+(control is TextBlock?((TextBlock)control).Text:""));
            var label=control as TextBlock;
            if(label!=null&&label.Text.Length>0&&label.TextWrapping==TextWrapping.NoWrap&&label.TextTrimming==TextTrimming.None) {
                var text=new FormattedText(label.Text,CultureInfo.InvariantCulture,label.FlowDirection,
                    new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,label.Foreground,VisualTreeHelper.GetDpi(label).PixelsPerDip);
                Check(text.Width<=label.ActualWidth+1,name+": untrimmed single-line label remains readable: "+label.Text);
            }
        }
    }
    static void CheckViewport(Window window,FrameworkElement surface,string name) {
        var root=Detach(window);var scroll=Visuals<ScrollViewer>(root).First();
        Layout(root,1600);
        Check(scroll.ComputedVerticalScrollBarVisibility!=Visibility.Visible,name+": tall viewport does not need a scroll bar");
        Check(Math.Abs(surface.ActualWidth-418)<.5,name+": normal surface keeps its 418 DIP width");
        HorizontalBounds(surface,name+" normal");
        foreach(double height in new[]{360.0,260.0}) {
            Layout(root,height);
            Check(scroll.ComputedVerticalScrollBarVisibility==Visibility.Visible,name+": short viewport has vertical scrolling");
            Check(scroll.ViewportWidth>300&&scroll.ViewportWidth<418,name+": scroll bar reserves horizontal space");
            Check(Math.Abs(surface.ActualWidth-scroll.ViewportWidth)<.5,name+": surface shrinks to the available viewport");
            Check(scroll.ScrollableWidth<.5,name+": no hidden horizontal overflow");
            HorizontalBounds(surface,name+" short "+height);
            scroll.ScrollToEnd();Layout(root,height);
            Check(scroll.VerticalOffset>0&&Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<1,name+": bottom controls remain reachable by scrolling");
            scroll.ScrollToHome();Layout(root,height);
            Check(scroll.VerticalOffset<.5,name+": scrolling returns to the header");
        }
        Layout(root,1600);
        Check(scroll.ComputedVerticalScrollBarVisibility!=Visibility.Visible&&Math.Abs(surface.ActualWidth-418)<.5,name+": expanding viewport restores the normal layout");
    }
    static ModelConnectionWindow Model(string provider) {
        var config=new RuntimeSettings{Provider=provider,Model=provider=="chatgpt"?"gpt-test":"deepseek-flash",Validated=true};
        var constructor=typeof(ModelConnectionWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(RuntimeSettings)},null);
        return (ModelConnectionWindow)constructor.Invoke(new object[]{config});
    }
    [STAThread] public static int Main(string[] args) {
        string testBase=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        string settings=Path.Combine(testBase,"viewport-settings-"+Guid.NewGuid().ToString("N")+".json");
        string assets=args.Length>0?Path.GetFullPath(args[0]):Path.GetFullPath(Path.Combine(testBase,".."));
        PetWindow pet=null;
        try {
            foreach(string provider in new[]{"chatgpt","deepseek-account","deepseek-api"}) {
                var model=Model(provider);
                try {
                    if(provider=="deepseek-api") {
                        var change=(Button)typeof(ModelConnectionWindow).GetField("changeKey",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(model);
                        change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    CheckViewport(model,model.PreviewSurface,"model "+provider);
                }finally{model.Close();}
            }
            pet=new PetWindow(assets,settings);
            foreach(bool expanded in new[]{false,true}) {
                var appearance=new PetSettingsWindow(pet,assets,null,null,null,null);
                try {appearance.SetPreviewExpanded(expanded);CheckViewport(appearance,appearance.PreviewSurface,"appearance "+expanded);}
                finally{appearance.Close();}
            }
            Console.WriteLine("settings viewport layout checks passed: "+checks);return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally {
            if(pet!=null)pet.CloseForExit();
            if(File.Exists(settings))File.Delete(settings);
        }
    }
}

