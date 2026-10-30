using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Tableau.Api.Models;

namespace Tableau.Api.Exceptions;

public class TableauRequestException : Exception
{
    public Uri RequestUrl { get; }

    public HttpStatusCode StatusCode { get; set; }

    public bool HasDetails => Details != null;
    public errorType Details { get; }

    // Where a redirect pointed, which the client does not follow: its scheme, host and port only.
    private readonly string _redirectTarget;

    internal TableauRequestException(Uri requesturi, HttpStatusCode code, errorType details)
    {
        RequestUrl = requesturi;
        StatusCode = code;
        Details = details;
    }

    internal TableauRequestException(Uri requesturi, HttpStatusCode code, errorType details, string redirectTarget)
        : this(requesturi, code, details)
    {
        _redirectTarget = redirectTarget;
    }

    /// <summary>
    /// The status code and Tableau's error details; for a redirect, which is never followed, the address it pointed to.
    /// </summary>
    public override String Message
    {
        get
        {
            string ret = "A Tableau Server exception occured. HTTP Status Code: " + ((int)StatusCode);
            if (_redirectTarget != null)
            {
                ret += ", a redirect to " + _redirectTarget + ", which is not followed";
            }
            if (HasDetails)
            {
                ret += String.Format(", Internal Code: {0}, Summary: '{1}', Detailed info: '{2}'", Details.code, Details.summary, Details.detail);
            }
            return ret;
        }
    }

}


