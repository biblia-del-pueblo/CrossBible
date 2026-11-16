using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CrossBible.Library
{
    public class AppVersion
    {
        public System.Version Version => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1,0);
    }
}
