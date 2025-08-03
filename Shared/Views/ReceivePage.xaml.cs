using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

#if WINDOWS_PHONE
using System.Windows;
using System.Windows.Controls;
#else
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
#endif

namespace Shared.Views
{
    public sealed partial class ReceivePage : UserControl
    {
        public ReceivePage()
        {
            this.InitializeComponent();
            Loaded += ReceivePage_Loaded;
        }

        private void ReceivePage_Loaded(object sender, RoutedEventArgs e)
        {
            this.DeviceName.Text = Settings.DeviceName;
        }
    }
}
