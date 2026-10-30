using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace GameBoost.Controls;

/// <summary>Beschriftung + Slider + Wertanzeige in einer Zeile.</summary>
public sealed class SliderRow : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SliderRow),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SliderRow)d).UpdateValueText()));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SliderRow), new PropertyMetadata(100.0));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(SliderRow), new PropertyMetadata(" px", (d, _) => ((SliderRow)d).UpdateValueText()));

    private readonly TextBlock _valueText;

    public SliderRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(Label)) { Source = this });
        label.SetResourceReference(TextBlock.ForegroundProperty, "B.TextDim");
        grid.Children.Add(label);

        var slider = new Slider
        {
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });
        slider.SetBinding(RangeBase.MinimumProperty, new Binding(nameof(Minimum)) { Source = this });
        slider.SetBinding(RangeBase.MaximumProperty, new Binding(nameof(Maximum)) { Source = this });
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);

        _valueText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(_valueText, 2);
        grid.Children.Add(_valueText);

        Content = grid;
        UpdateValueText();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    private void UpdateValueText()
    {
        if (_valueText == null) return;
        _valueText.Text = Value.ToString("0", CultureInfo.CurrentCulture) + Suffix;
    }
}
