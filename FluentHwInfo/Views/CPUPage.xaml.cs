using FluentHwInfo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace FluentHwInfo.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class CPUPage : Page
{
    // this is the field, that we access in XAML with {x:Bind ViewModel.SensorList}
    public CPUViewModel ViewModel { get; }

    public CPUPage()
    {
        this.InitializeComponent();

        // create the viewmodel when the page is loaded
        ViewModel = new CPUViewModel();
    }

    private void PinToWidget_Click(object sender, RoutedEventArgs e)
    {
        var widget = new WidgetWindow();
        widget.Activate();
        // Optional: MainWindow verstecken, damit nur das Widget bleibt
        // App.MainWindow.Hide(); 
    }
}
