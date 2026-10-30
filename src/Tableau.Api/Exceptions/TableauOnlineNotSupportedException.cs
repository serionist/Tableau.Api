using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Exceptions;

public class TableauOnlineNotSupportedException : Exception
{
    public TableauOnlineNotSupportedException(string endpointName)
    {
        EndpointName = endpointName;
    }

    public string EndpointName { get; }

    public override string Message => $"Endpoint '{EndpointName}' not available in Tableau Online";
}
