using Windows.ApplicationModel;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;


namespace LocalTalkUWP
{
    /// <summary>
    /// Represents the main page of the LocalTalk UWP application.
    /// This page serves as the primary entry point for the Universal Windows Platform version
    /// of LocalTalk, providing file transfer capabilities and device discovery functionality.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Initializes a new instance of the MainPage class.
        /// Sets up the user interface components for the UWP application.
        /// </summary>
        public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called when the page is navigated to.
        /// Initializes the LocalSend protocol service and displays the current application version.
        /// </summary>
        /// <param name="e">Navigation event arguments containing information about the navigation operation.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await Shared.LocalSendProtocol.Instance.Start();

            PackageVersion version = Package.Current.Id.Version;
            Version.Text = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }
}
