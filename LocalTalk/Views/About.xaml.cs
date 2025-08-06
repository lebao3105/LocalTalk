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
    /// Represents the About page user control that displays application information, version details, and developer credits.
    /// This control provides users with information about the LocalTalk application and its creator.
    /// </summary>
    public partial class About : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the About user control.
        /// Loads the application version from the WMAppManifest.xml file and displays it in the UI.
        /// </summary>
        public About()
        {
            InitializeComponent();
            Version.Text = System.Xml.Linq.XDocument.Load("WMAppManifest.xml")
                .Root.Element("App").Attribute("Version").Value;
        }
    }
}
