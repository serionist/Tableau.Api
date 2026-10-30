using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Attributes
{
    public class ApiVersionAttribute: Attribute
    {
        public ApiVersionAttribute(Version minVersion)
        {
            MinVersion = minVersion;
        }
        public ApiVersionAttribute(int major, int minor):this(new Version(major, minor)) { }

        public Version MinVersion { get; }
    }
}
