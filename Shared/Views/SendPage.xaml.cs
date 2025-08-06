using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace LocalTalk.Shared.Views
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
        }
    }
}
