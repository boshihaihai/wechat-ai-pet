using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace WeChatPet {
    internal static class CompactMenu {
        internal static void Apply(ContextMenu menu) {
            // These menus contain plain actions, so no icon/checkmark column is needed.
            var resources=(ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='{x:Type ContextMenu}'>
    <Setter Property='FontFamily' Value='Microsoft YaHei UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='#25342E'/>
    <Setter Property='Template'>
      <Setter.Value><ControlTemplate TargetType='{x:Type ContextMenu}'>
        <Border Background='#FAFBF9' BorderBrush='#DCE2DD' BorderThickness='1' CornerRadius='7' Padding='4'>
          <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
        </Border>
      </ControlTemplate></Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='{x:Type MenuItem}'>
    <Setter Property='Padding' Value='12,7'/>
    <Setter Property='Template'>
      <Setter.Value><ControlTemplate TargetType='{x:Type MenuItem}'>
        <Border x:Name='Row' Background='Transparent' Padding='{TemplateBinding Padding}' CornerRadius='4'>
          <ContentPresenter ContentSource='Header' RecognizesAccessKey='True'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='#E8EFEA'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='{x:Type Separator}'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Separator}'>
      <Border Background='#E2E7E3' Height='1' Margin='8,4'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>");
            menu.Resources=resources;
            menu.Style=(Style)resources[typeof(ContextMenu)];
        }
    }
}
