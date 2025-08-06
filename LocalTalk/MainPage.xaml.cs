using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;

namespace LocalTalk
{
    /// <summary>
    /// Represents the main page of the LocalTalk Windows Phone application.
    /// This page serves as the primary navigation hub using a Panorama control to display
    /// different sections of the application including file transfer, device discovery, and settings.
    /// </summary>
    public partial class MainPage : PhoneApplicationPage
    {
        /// <summary>
        /// Initializes a new instance of the MainPage class.
        /// Sets up the user interface components and prepares the page for user interaction.
        /// </summary>
        public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called when the page is navigated to.
        /// This method can be used to load data for the ViewModel items or perform page-specific initialization.
        /// </summary>
        /// <param name="e">Navigation event arguments containing information about the navigation operation.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
        }

        /// <summary>
        /// Handles the selection change event for the Panorama control.
        /// Adjusts the background opacity based on the selected panorama item to provide visual feedback.
        /// </summary>
        /// <param name="sender">The Panorama control that triggered the event.</param>
        /// <param name="e">Selection changed event arguments containing information about the selection change.</param>
        private void Panorama_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Panorama p = sender as Panorama;
            PanoramaBackground.Opacity = p.SelectedIndex != 0 ? 0 : 1;
        }
    }
}
