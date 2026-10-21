using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Atlasmith.Controls;

/// <summary>Campo numérico com botões de incremento, roda do mouse e validação.</summary>
public partial class NumberBox : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(NumberBox),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged,
                CoerceValue));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumberBox), new PropertyMetadata(0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumberBox), new PropertyMetadata(999));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(int), typeof(NumberBox), new PropertyMetadata(1));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public event EventHandler? ValueCommitted;

    public NumberBox()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var box = (NumberBox)d;
        return Math.Clamp((int)baseValue, box.Minimum, box.Maximum);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumberBox)d;
        box.Refresh();
        box.ValueCommitted?.Invoke(box, EventArgs.Empty);
    }

    private void Refresh()
    {
        string text = Value.ToString(CultureInfo.InvariantCulture);
        if (ValueBox.Text != text) ValueBox.Text = text;
    }

    private void Commit()
    {
        if (int.TryParse(ValueBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            Value = parsed;
        Refresh();
    }

    private void OnMinus(object sender, RoutedEventArgs e) => Value -= Step;

    private void OnPlus(object sender, RoutedEventArgs e) => Value += Step;

    private void OnBoxLostFocus(object sender, RoutedEventArgs e) => Commit();

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                Keyboard.ClearFocus();
                e.Handled = true;
                break;
            case Key.Up:
                Value += Step;
                e.Handled = true;
                break;
            case Key.Down:
                Value -= Step;
                e.Handled = true;
                break;
        }
    }

    private void OnBoxWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ValueBox.IsKeyboardFocusWithin) return;
        Value += e.Delta > 0 ? Step : -Step;
        e.Handled = true;
    }
}
