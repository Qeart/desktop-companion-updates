using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Aurum;

internal static class MapFilterAppearance
{
    public static Style Style(bool tile)
    {
        var template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
              <Border x:Name="Body" CornerRadius="8" BorderThickness="1" BorderBrush="#343A42" Background="#11161C" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
                <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Body" Property="BorderBrush" Value="#B58A42"/><Setter Property="Foreground" Value="#FFE0A1"/>
                  <Setter TargetName="Body" Property="Background"><Setter.Value><LinearGradientBrush StartPoint="0,0" EndPoint="1,1"><GradientStop Color="#3C3020" Offset="0"/><GradientStop Color="#211D17" Offset="1"/></LinearGradientBrush></Setter.Value></Setter>
                </Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Body" Property="BorderBrush" Value="#E6BE6A"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Body" Property="BorderBrush" Value="#FFF1CE"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Body" Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(169, 176, 187))));
        style.Setters.Add(new Setter(Control.FontSizeProperty, tile ? 12d : 11d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(tile ? 10 : 11, tile ? 11 : 8, tile ? 10 : 11, tile ? 11 : 8)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, tile ? 44d : 32d));
        return style;
    }
    public static FrameworkElement TileContent(string genus, TextBlock count)
    {
        var icons = new Dictionary<string, (string Shape, string Color)> {
            ["cogni"] = ("M5,14 L5,10 C1,9 2,2 8,2 C13,2 15,6 12,9 L12,12 L9,12 L9,14 M6,5 L10,5 M6,8 L9,8", "#9DBBE2"),
            ["fera"] = ("M4,8 C1,4 4,2 5,5 M7,5 C5,0 9,0 9,5 M11,6 C11,1 15,3 13,7 M5,10 C2,13 4,15 7,14 C11,16 14,13 11,10 C10,7 7,7 5,10", "#DCAC87"),
            ["natura"] = ("M2,14 C1,6 6,2 14,2 C14,10 10,15 2,14 M2,14 L11,5 M5,11 L5,7 M8,8 L12,8", "#8FBF9B"),
            ["varian"] = ("M8,1 L14,6 L8,15 L2,6 Z M2,6 L14,6 M5,6 L8,15 L11,6 L8,1 Z", "#BCA2E4"),
            ["special"] = ("M8,1 L10,6 L15,6 L11,9 L13,15 L8,12 L3,15 L5,9 L1,6 L6,6 Z", "#E6BE6A")
        };
        var icon = icons[genus];
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var shape = new System.Windows.Shapes.Path { Data = Geometry.Parse(icon.Shape), Stroke = (Brush)new BrushConverter().ConvertFromString(icon.Color)!, StrokeThickness = 1.2, Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(shape);
        var label = new TextBlock { Text = char.ToUpperInvariant(genus[0]) + genus[1..], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 4, 0) };
        Grid.SetColumn(label, 1); grid.Children.Add(label);
        count.FontSize = 10; count.Foreground = new SolidColorBrush(Color.FromRgb(205, 186, 148)); count.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2), Child = count };
        Grid.SetColumn(badge, 2); grid.Children.Add(badge); return grid;
    }
}
