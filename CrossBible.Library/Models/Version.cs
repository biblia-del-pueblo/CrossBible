using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CrossBible.Library
{
    public class AppVersion
    {
        // The version of the program that runs. An Android app has no entry assembly (it starts in Java), and the library has the version of Directory.Build.props as well.
        public System.Version Version => (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly).GetName().Version ?? new Version(1,0);
    }
}
