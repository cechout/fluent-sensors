using FluentHwInfo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentHwInfo.Views
{
    public sealed partial class SensorCardControl : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(SensorRowViewModel),
                typeof(SensorCardControl),
                new PropertyMetadata(null, OnViewModelChanged));

        public SensorRowViewModel ViewModel
        {
            get => (SensorRowViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SensorCardControl control)
            {
                // we force the ui to update the bindings when the view model changes
                // this could fix the bug where ui does not render after long sessions main window closed
                control.Bindings.Update();
            }
        }

        public SensorCardControl()
        {
            this.InitializeComponent();
        }
    }
}