using Windows.ApplicationModel;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;


namespace LocalTalk
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await Shared.LocalSendProtocol.Instance.Start();
            
            PackageVersion version = Package.Current.Id.Version;
            Version.Text = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }
}
