using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace DeskPill
{
    // Scoped to Agents: existing pages retain their own controls and focus behavior.
    static class AgentControls
    {
        public static ComboBox Picker()
        {
            var control=new ComboBox{FontSize=12,Foreground=Ui.White,Background=Ui.Card,BorderBrush=Ui.Line,Padding=new Thickness(9,3,26,3)};
            control.Resources["AgentSurface"]=Ui.Card;control.Resources["AgentText"]=Ui.White;control.Resources["AgentLine"]=Ui.Line;control.Resources["AgentAccent"]=Ui.Green;
            control.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBox'>
<Grid><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7'/>
<ContentPresenter Margin='{TemplateBinding Padding}' VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' IsHitTestVisible='False'/>
<ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' Background='Transparent'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Grid Background='Transparent'><TextBlock Text='⌄' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,9,3' Foreground='{DynamicResource AgentText}'/></Grid></ControlTemplate></ToggleButton.Template></ToggleButton>
<Popup Name='PART_Popup' AllowsTransparency='True' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' Focusable='False'><Border Background='{DynamicResource AgentSurface}' BorderBrush='{DynamicResource AgentLine}' BorderThickness='1' CornerRadius='7' Padding='4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'><ScrollViewer MaxHeight='220' CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup>
</Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter Property='BorderBrush' Value='{DynamicResource AgentAccent}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            control.ItemContainerStyle=(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBoxItem'><Setter Property='Foreground' Value='{DynamicResource AgentText}'/><Setter Property='Padding' Value='8,6'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border Name='item' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='item' Property='BorderBrush' Value='{DynamicResource AgentAccent}'/><Setter TargetName='item' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='item' Property='Background' Value='{DynamicResource AgentLine}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            System.Windows.Automation.AutomationProperties.SetName(control,"Agent connection");return control;
        }
    }
}
