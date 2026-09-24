// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Support/LumNumeric.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace lumina.wpf
{
    /// <summary>
    /// Campo numérico con límites, paso y decimales (0 = enteros).
    /// El texto SIEMPRE se re-normaliza al salir del campo (tolerante a lo
    /// que el usuario escriba; nunca lanza).
    /// </summary>
    public partial class LumNumeric : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(LumNumeric),
                new FrameworkPropertyMetadata(0.0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValueChanged));

        public double Min { get; set; }
        public double Max { get; set; } = 100;
        public double Step { get; set; } = 1;
        public int DecimalPlaces { get; set; }
        /// <summary>Evita el eco infinito texto→valor→texto.</summary>
        private bool _updating;

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        /// <summary>Valor entero (comodidad para puertos/índices).</summary>
        public int IntValue { get { return (int)Math.Round(Value); } }

        public event EventHandler ValueChangedByUser;

        public LumNumeric()
        {
            InitializeComponent();
            txt.Text = "0";
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            LumNumeric n = (LumNumeric)d;
            if (n._updating) return;
            n._updating = true;
            try { n.txt.Text = n.Format((double)e.NewValue); }
            finally { n._updating = false; }
        }

        private string Format(double v)
        {
            v = Clamp(v);
            if (DecimalPlaces <= 0) return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
            return v.ToString("F" + DecimalPlaces.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
        }

        private double Clamp(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return Min;
            if (v < Min) return Min;
            if (v > Max) return Max;
            return v;
        }

        private void OnTxtChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            double v;
            if (double.TryParse(txt.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                || double.TryParse(txt.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            {
                _updating = true;
                try { Value = Clamp(v); }
                finally { _updating = false; }
            }
        }

        private void OnTxtLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Normalize();
        }

        private void OnTxtKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Normalize();
                e.Handled = true;
            }
        }

        private void Normalize()
        {
            _updating = true;
            try
            {
                double v;
                if (!(double.TryParse(txt.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                      || double.TryParse(txt.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out v)))
                    v = Value;
                Value = Clamp(v);
                txt.Text = Format(Value);
            }
            finally { _updating = false; }
        }

        private void OnUpClick(object sender, RoutedEventArgs e)
        {
            _updating = true;
            try { Value = Clamp(Value + Step); txt.Text = Format(Value); }
            finally { _updating = false; }
            EventHandler h = ValueChangedByUser;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void OnDownClick(object sender, RoutedEventArgs e)
        {
            _updating = true;
            try { Value = Clamp(Value - Step); txt.Text = Format(Value); }
            finally { _updating = false; }
            EventHandler h = ValueChangedByUser;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
