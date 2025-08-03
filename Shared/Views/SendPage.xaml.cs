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

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Shared.Views
{
    public sealed partial class SendPage : UserControl
    {
        public SendPage()
        {
            this.InitializeComponent();
            Loaded += SendPage_Loaded;
        }

        private void SendPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.TargetList.ItemsSource = LocalSendProtocol.Devices;
        }
    }
}
