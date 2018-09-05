using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Tableau.Api.Models;

namespace Tableau.Api.Exceptions
{
    public class TableauRequestException : Exception
    {
        public Uri RequestUrl { get; }

        public HttpStatusCode StatusCode { get; set; }

        public bool HasDetails => Details != null;
        public errorType Details { get; }

        internal TableauRequestException(Uri requesturi, HttpStatusCode code, errorType details)
        {
            RequestUrl = requesturi;
            StatusCode = code;
            Details = details;
        }

        public override String Message
        {
            get
            {
                string ret = "A Tableau Server exception occured. HTTP Status Code: " + ((int)StatusCode);
                if (HasDetails)
                {
                    ret += String.Format(", Internal Code: {0}, Summary: '{1}', Detailed info: '{2}'", Details.code, Details.summary, Details.detail);
                }
                return ret;
            }
        }

    }

   
}
