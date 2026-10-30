using System;
using System.Net;
using System.Net.Http;
using Tableau.Api.Exceptions;

namespace Tableau.Api
{
    /// <summary>
    /// What the REST client and the Vizportal client share about HTTP, so the two can't drift apart: the handler each
    /// request goes through, the base URL's path, and the exception a redirect throws.
    /// </summary>
    internal static class TableauHttp
    {
        /// <summary>
        /// A handler that validates the server's certificate unless ignoreSslErrors is set, and follows no redirect, so a
        /// request, its body, its token and its cookies go only to the host it was built for.
        /// </summary>
        public static HttpClientHandler CreateHandler(bool ignoreSslErrors, CookieContainer cookies = null)
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            if (cookies != null)
                handler.CookieContainer = cookies;
            if (ignoreSslErrors)
                handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
            return handler;
        }

        /// <summary>The base URL's path, ending in /, so a server behind a path prefix is reached under it.</summary>
        public static string BasePath(Uri baseUrl) =>
            baseUrl.AbsolutePath.EndsWith("/", StringComparison.Ordinal) ? baseUrl.AbsolutePath : baseUrl.AbsolutePath + "/";

        /// <summary>
        /// The exception for a 3xx response, which is never followed. It names the scheme, host and port the redirect
        /// points to, never its user info, path or query.
        /// </summary>
        public static TableauRequestException RedirectException(HttpResponseMessage resp)
        {
            var requestUri = resp.RequestMessage.RequestUri;
            var location = resp.Headers.Location;
            string target = null;
            if (location != null)
            {
                var absolute = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
                target = $"{absolute.Scheme}://{absolute.Host}{(absolute.IsDefaultPort ? "" : $":{absolute.Port}")}";
            }
            return new TableauRequestException(requestUri, resp.StatusCode, null,
                target ?? "an address the response does not name");
        }

        /// <summary>Whether a status is a redirect.</summary>
        public static bool IsRedirect(HttpStatusCode status) => (int)status >= 300 && (int)status < 400;
    }
}
