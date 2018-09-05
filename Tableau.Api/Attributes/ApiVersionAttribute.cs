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

        public Version MinVersion { get; }
    }
}
