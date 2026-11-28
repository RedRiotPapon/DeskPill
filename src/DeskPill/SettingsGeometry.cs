using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskPill
{
    public sealed partial class SettingsWindow
    {
        ComboBox geometryScaleChoice;
        Slider geometryScaleSlider;
        bool geometrySyncing;
        void SetGeometryScaleChoice(double value)
        {
            if(geometryScaleChoice==null)return;
            geometrySyncing=true;
            var values=new[]{1.0,1.15,1.3,1.5,value}.Distinct().Select(v=>(v*100).ToString("0",CultureInfo.InvariantCulture)+"%").ToArray();
            geometryScaleChoice.ItemsSource=values;
            geometryScaleChoice.SelectedItem=(value*100).ToString("0",CultureInfo.InvariantCulture)+"%";
            geometrySyncing=false;
        }
        StackPanel SizeAndShape()
        {
            var p=Page("Size & shape","Keep it subtle or give it more room. Preview changes on the notch; Save keeps them and Cancel restores your saved fit.");
            Label(p,"Corner shape");
            var shapes=new System.Collections.Generic.List<RadioButton>();
            var shapeGrid=new System.Windows.Controls.Primitives.UniformGrid {Columns=3,Margin=new Thickness(0,0,0,10)};p.Children.Add(shapeGrid);
            foreach(string name in new[]{"Original","Rounded","Soft square"}){
                string shape=name;
                var row=new StackPanel();
                var outline=new System.Windows.Shapes.Path {Data=IslandMotion.CustomizedOutline(150,30,true,shape),Fill=Ui.White,Width=76,Height=22,Stretch=Stretch.Fill,Margin=new Thickness(0,0,0,8)};
                row.Children.Add(outline);row.Children.Add(Ui.Text(shape,13,Ui.White));
                var radio=new RadioButton {Content=row,GroupName="NotchShape",Tag=shape,IsChecked=draft.NotchShape==shape,Padding=new Thickness(10),Margin=new Thickness(0,3,4,3),HorizontalContentAlignment=HorizontalAlignment.Stretch};
                System.Windows.Automation.AutomationProperties.SetName(radio,shape+" notch shape");
                radio.Checked+=delegate{draft.NotchShape=shape;this.previewPreferences(draft,false);};
                shapes.Add(radio);shapeGrid.Children.Add(radio);
            }

            Label(p,"Overall size · 80–160%");
            var value=Ui.Text((draft.Scale*100).ToString("0",CultureInfo.InvariantCulture)+"%",13,Ui.White);
            p.Children.Add(value);
            geometryScaleSlider=new Slider {Minimum=80,Maximum=160,Value=draft.Scale*100,TickFrequency=5,IsSnapToTickEnabled=true,SmallChange=5,LargeChange=10,Margin=new Thickness(0,6,0,10),Template=Ui.SliderTemplate()};
            System.Windows.Automation.AutomationProperties.SetName(geometryScaleSlider,"Overall notch size");
            geometryScaleSlider.ValueChanged+=delegate {
                draft.Scale=geometryScaleSlider.Value/100;value.Text=geometryScaleSlider.Value.ToString("0",CultureInfo.InvariantCulture)+"%";
                SetGeometryScaleChoice(draft.Scale);this.previewPreferences(draft,false);
            };
            p.Children.Add(geometryScaleSlider);
            Note(p,"Text and expanded pages scale together. Windows display scaling is applied automatically.");
            var width=PreferenceSlider(p,"Idle notch width","CompactWidth",150,240," px",delegate{this.previewPreferences(draft,false);});
            Note(p,"Widen the clock and music bar without stretching its content. Expanded pages keep their layout, and overlap hiding stays tiny.");
            Label(p,"Placement");
            var placement=new ComboBox {ItemsSource=new[]{"Classic","Floating"},SelectedItem=draft.Style,MinHeight=32};
            System.Windows.Automation.AutomationProperties.SetName(placement,"Notch placement");
            placement.SelectionChanged+=delegate{draft.Style=Convert.ToString(placement.SelectedItem);this.previewPreferences(draft,false);};
            p.Children.Add(placement);
            Note(p,"Classic joins the top bezel. Floating leaves a small gap above the island.");
            var reset=Ui.Button("Reset size & shape",delegate {
                geometryScaleSlider.Value=100;width.Value=150;placement.SelectedItem="Classic";
                shapes.First(r=>Convert.ToString(r.Tag)=="Original").IsChecked=true;
                this.previewPreferences(draft,false);
            });
            reset.Margin=new Thickness(0,8,0,0);p.Children.Add(reset);
            return p;
        }
    }
}
