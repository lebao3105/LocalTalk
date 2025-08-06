using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;

namespace LocalTalk.Views
{
    /// <summary>
    /// Represents the Settings page user control that provides configuration options for the LocalTalk application.
    /// This control allows users to customize application behavior including language preferences,
    /// receive settings, send settings, and other application-specific configurations.
    /// </summary>
    public partial class Settings : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the Settings user control.
        /// Sets up the user interface components for the settings page.
        /// </summary>
        public Settings()
        {
            InitializeComponent();
        }
    }
}
