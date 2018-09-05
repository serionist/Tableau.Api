using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Exceptions
{
    public class TableauApiVersionException:Exception
    {
        internal TableauApiVersionException(string endpointName, Version versionRequired, Version currentVersion)
        {
            VersionRequired = versionRequired;
            CurrentVersion = currentVersion;
            EndpointName = endpointName;
        }
        public string EndpointName { get; }
        public Version VersionRequired { get; }
        public Version CurrentVersion { get; }

        public override string Message =>
            $"Current Tableau Api version ({CurrentVersion.ToString(2)}) is too low for endpoint: '{EndpointName}'. Version required: {VersionRequired.ToString(2)}";

    }
}
