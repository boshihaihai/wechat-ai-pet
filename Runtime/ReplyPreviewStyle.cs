using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace WeChatPet.Runtime {
    internal static class ReplyPreviewStyle {
        internal static void Apply(ScrollViewer viewer) {
            // Scoped to reply previews: neutral thumb, small arrows, no painted track.
            // The hit area remains wider than the thumb for mouse dragging.
            var resources=(ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style x:Key='PreviewScrollArrow' TargetType='{x:Type RepeatButton}'>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='IsTabStop' Value='False'/>
  <Setter Property='Foreground' Value='#B3B3B3'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='{x:Type RepeatButton}'>
     <Border Background='Transparent'>
      <Path Data='{TemplateBinding Content}' Fill='{TemplateBinding Foreground}'
       Width='6' Height='4' Stretch='Fill' HorizontalAlignment='Center' VerticalAlignment='Center'/>
     </Border>
     <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#909090'/></Trigger>
      <Trigger Property='IsPressed' Value='True'><Setter Property='Foreground' Value='#777777'/></Trigger>
     </ControlTemplate.Triggers>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
 <Style TargetType='{x:Type ScrollBar}'>
  <Setter Property='Width' Value='9'/>
  <Setter Property='MinWidth' Value='9'/>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='IsTabStop' Value='False'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='{x:Type ScrollBar}'>
     <Grid Background='Transparent'>
      <Grid.RowDefinitions><RowDefinition Height='10'/><RowDefinition Height='*'/><RowDefinition Height='10'/></Grid.RowDefinitions>
      <RepeatButton Grid.Row='0' Style='{StaticResource PreviewScrollArrow}'
       Command='{x:Static ScrollBar.LineUpCommand}'>
       <RepeatButton.Content><PathGeometry Figures='M 0,4 L 3,0 6,4 Z'/></RepeatButton.Content>
      </RepeatButton>
      <Track x:Name='PART_Track' Grid.Row='1' Orientation='Vertical' IsDirectionReversed='True'
       Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
       Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'
       ViewportSize='{TemplateBinding ViewportSize}'>
       <Track.DecreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False' IsTabStop='False'>
         <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton>
       </Track.DecreaseRepeatButton>
       <Track.Thumb>
        <Thumb Focusable='False' IsTabStop='False' MinHeight='24'>
         <Thumb.Template>
          <ControlTemplate TargetType='{x:Type Thumb}'>
           <Border Background='Transparent'>
            <Border x:Name='Grip' Width='5' HorizontalAlignment='Center' Background='#B3B3B3' CornerRadius='2.5'/>
           </Border>
           <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Background' Value='#969696'/></Trigger>
            <Trigger Property='IsDragging' Value='True'><Setter TargetName='Grip' Property='Background' Value='#777777'/></Trigger>
           </ControlTemplate.Triggers>
          </ControlTemplate>
         </Thumb.Template>
        </Thumb>
       </Track.Thumb>
       <Track.IncreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False' IsTabStop='False'>
         <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton>
       </Track.IncreaseRepeatButton>
      </Track>
      <RepeatButton Grid.Row='2' Style='{StaticResource PreviewScrollArrow}'
       Command='{x:Static ScrollBar.LineDownCommand}'>
       <RepeatButton.Content><PathGeometry Figures='M 0,0 L 6,0 3,4 Z'/></RepeatButton.Content>
      </RepeatButton>
     </Grid>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
</ResourceDictionary>");
            viewer.Resources.MergedDictionaries.Add(resources);
        }
    }
}
