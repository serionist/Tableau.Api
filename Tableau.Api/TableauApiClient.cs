using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using Tableau.Api.Attributes;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;

namespace Tableau.Api
{
    public partial class TableauApiClient
    {
        private static readonly TimeSpan _defaultTimeout = new TimeSpan(0, 2, 0);
        private static readonly Version _minVersion = new Version(2, 0);
        private static ReadOnlyDictionary<string, Version> _minVersionForEndpoints { get; }
        private static string[] _onPremiseOnlyEndpoints { get; }
        /// <summary>
        /// Load Minimum version for endpoints from reflection. Also store OnPremiseOnly endpoints
        /// </summary>
        static TableauApiClient()
        {
            _minVersionForEndpoints = new ReadOnlyDictionary<string, Version>(typeof(TableauApiClient).GetProperties()
                .Select(e => (e.Name, e.GetCustomAttribute<ApiVersionAttribute>())).Where(e => e.Item2 != null)
                .ToDictionary(e => e.Item1, e => e.Item2.MinVersion));
            _onPremiseOnlyEndpoints = typeof(TableauApiClient).GetProperties()
                .Where(e => e.GetCustomAttribute<OnPremiseOnlyAttribute>() != null).Select(e => e.Name).ToArray();
            
        }

        public Uri BaseUrl { get; }
        public Version ApiVersion { get; }
        private ILogger Log { get; }

        /// <summary>
        /// The timeout of each request. Default: 2 minutes
        /// </summary>
        public TimeSpan Timeout { get; set; } = _defaultTimeout;
        public bool IgnoreSSLErrors { get; set; } = false;
        /// <summary>
        /// Initializes an instance of the Tableau Api client
        /// </summary>
        /// <param name="tableauBaseUri">The base URL of the Tableau Server, eg: https://my-tableau-server.com</param>
        /// <param name="logger">The logger object</param>
        /// <param name="apiVersion">The Tableau API Version to use</param>
        public TableauApiClient(Uri tableauBaseUri, ILogger logger, string apiVersion = "2.0")
        {
            BaseUrl = tableauBaseUri;
            Log = logger;
            if (tableauBaseUri == null)
                throw new ArgumentException("Tableau base Uri is required (eg. http://mytableauserver.com)", nameof(tableauBaseUri));
            if (Version.TryParse(apiVersion, out var v))
                if (v < _minVersion)
                    throw new ArgumentOutOfRangeException(nameof(apiVersion),
                        $"Tableau API version must be greater than {_minVersion.ToString(2)}");
                else ApiVersion = v;
            else throw new ArgumentException($"'apiVersion' parameter cannot be converted to Version object", nameof(apiVersion));
          

            Log?.Log(LogLevel.Debug, $"Created TableauApiClient for tableau base url: '{tableauBaseUri}', api version: {apiVersion}");
        }
        /// <inheritdoc />
        /// <summary>
        /// Initializes an instance of the Tableau Api client
        /// </summary>
        /// <param name="tableauBaseUri">The base URL of the Tableau Server, eg: https://my-tableau-server.com</param>
        /// <param name="apiVersion">The Tableau API Version to use</param>
        public TableauApiClient(Uri tableauBaseUri, string apiVersion = "2.0") : this(tableauBaseUri, null, apiVersion) { }
     
        private void checkNullParameters(params (string, object)[] arguments)
        {
            foreach (var a in arguments)
                if (a.Item2 == null || (a.Item2 is string && string.IsNullOrEmpty((string)a.Item2)))
                    throw new ArgumentNullException(a.Item1, $"Argument {a.Item1} cannot be null");
        }

        private void checkEmptyArrays(params (string, Array)[] arguments)
        {
            foreach (var a in arguments)
                if (a.Item2.Length  == 0)
                    throw new ArgumentOutOfRangeException(a.Item1, $"Argument array '{a.Item1}' cannot be empty");
        }
        private void checkParametersBetween(params (string name, int value, int min, int max)[] arguments)
        {
            foreach (var a in arguments)
                if (a.value < a.min || a.value > a.max)
                    throw new ArgumentOutOfRangeException(a.name, $"Argument '{a.name}' must be between {a.min} and {a.max}");
        }
        private void checkEndpointAvailability([CallerMemberName]string methodName = null)
        {
            if (string.IsNullOrEmpty(methodName)) return;
            if (_minVersionForEndpoints.ContainsKey(methodName) && _minVersionForEndpoints[methodName] > ApiVersion)
                throw new TableauApiVersionException(methodName, _minVersionForEndpoints[methodName], ApiVersion);
            if (_onPremiseOnlyEndpoints.Contains(methodName))
                throw new TableauOnlineNotSupportedException(methodName);
        }

        private HttpClient buildClient()
        {
            // A redirect is never followed, so a request, its body and its token go only to the host it was built for.
            var clientOptions = new HttpClientHandler() { AllowAutoRedirect = false };
            if (IgnoreSSLErrors)
                clientOptions.ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true;
            var c = new HttpClient(clientOptions) { Timeout = Timeout };
            c.DefaultRequestHeaders.Remove("Accept");
            c.DefaultRequestHeaders.Add("Accept", "application/xml");
            return c;
        }

        /// <summary>
        /// The request URI: the base URL with its path, the API version, the path with its values escaped, and the
        /// query with each key and value escaped. Throws ArgumentException, before anything is sent, when this
        /// platform's URI parsing would send another path than the one written.
        /// </summary>
        private Uri buildUri(RequestPath relativePath, params (string, string)[] urlParams)
        {
            var basePath = BaseUrl.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? BaseUrl.AbsolutePath
                : BaseUrl.AbsolutePath + "/";
            var path = $"{basePath}api/{ApiVersion}/{relativePath.ToEscapedPath()}";
            var url = new StringBuilder($"{BaseUrl.Scheme}://{BaseUrl.Host}:{BaseUrl.Port}{path}");
            for (var i = 0; i < urlParams.Length; i++)
            {
                url.Append(i == 0 ? '?' : '&');
                UriEscaping.AppendQueryPart(url, urlParams[i].Item1 ?? "");
                url.Append('=');
                appendQueryValue(url, urlParams[i].Item1, urlParams[i].Item2);
            }
            var uri = new Uri(url.ToString());
            // Some .NET Framework targets' URI parsing changes a path after it is escaped, so the path it would send is
            // checked against the one written.
            if (!string.Equals(uri.AbsolutePath, path, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"The request path '{path}' would be sent as '{uri.AbsolutePath}' by this platform's URI parsing, so it "
                    + "is not sent. A value or the base URL's path holds characters that this .NET Framework target "
                    + "rewrites.");
            return uri;
        }

        // A filter or sort expression keeps its separators, : and ,, and the parts between them are escaped.
        private static void appendQueryValue(StringBuilder url, string key, string value)
        {
            if (value == null) return;
            if (key != "filter" && key != "sort")
            {
                UriEscaping.AppendQueryPart(url, value);
                return;
            }
            var start = 0;
            for (var i = 0; i <= value.Length; i++)
            {
                if (i < value.Length && value[i] != ':' && value[i] != ',') continue;
                UriEscaping.AppendQueryPart(url, value.Substring(start, i - start));
                if (i < value.Length) url.Append(value[i]);
                start = i + 1;
            }
        }

      
        private async Task<TableauRequestException> buildException(HttpResponseMessage resp)
        {
            errorType det = null;
            try
            {
                det = await getResponseAsObject<errorType>(resp.Content);
            }
            catch (Exception ex)
            {
                Log?.Log(LogLevel.Warning, ex, "Failed to read Tableau Server error details");
            }
            return new TableauRequestException(resp.RequestMessage.RequestUri, resp.StatusCode, det);
        }

        // Names the scheme, host and port the redirect points to, never its user info, path or query.
        private static TableauRequestException buildRedirectException(HttpResponseMessage resp)
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

        private async Task<HttpContent> apiRequest(Uri fullUri, HttpMethod method, int expectedStatusCode,
            CancellationToken? cancelToken, TableauSession session = null, HttpContent body = null)
        {

            var state = new object();
            using (var logScope = Log?.BeginScope(state))
                try
                {
                    Log?.Log(LogLevel.Information, $"Sending request to Tableau Server. Method: {method}, Url: '{fullUri}'. Expected response code: {expectedStatusCode}");
                    using (var c = buildClient())
                    {
                        var msg = new HttpRequestMessage(method, fullUri);
                        // On the request, not the client's default headers, so the token goes with this request only.
                        if (!string.IsNullOrEmpty(session?.Token))
                            msg.Headers.TryAddWithoutValidation("X-Tableau-Auth", session.Token);
                        if (body != null)
                            msg.Content = body;

                        var resp = await c.SendAsync(msg, cancelToken ?? CancellationToken.None);
                        var status = (int)resp.StatusCode;
                        if (status >= 300 && status < 400)
                            throw buildRedirectException(resp);
                        // Tableau answers some calls with another 2xx than the call names, such as 201 for a schedule
                        // created where 1.2.5 named 200.
                        if (status < 200 || status >= 300)
                            throw await buildException(resp);
                        Log?.Log(LogLevel.Information, "Request successful");
                        return resp.Content;
                    }
                }
                catch (TableauRequestException e)
                {
                    Log?.Log(LogLevel.Error, e, $"Tableau Server returned an error at request. {(e.HasDetails ? $"Tableau Server server code: {e.Details.code}" : "No details available")}");
                    throw;
                }
                catch (HttpRequestException e)
                {
                    Log?.Log(LogLevel.Error, e, $"A network error occured while communicating with Tableau Server. Internal error message: {e.Message}");
                    throw;
                }
                catch (Exception e)
                {
                    Log?.Log(LogLevel.Error, e, $"An unhandled error occured while communicating with Tableau Server. Internal error message: {e.Message}");
                    throw;
                }
        }

        private async Task<string> _readResponseString(HttpContent content)
        {
            var contentString = await content.ReadAsStringAsync();
            //replace old schema to new schema in response to allow deserialize on older APIs
            return contentString.Replace("http://tableausoftware.com/api", "http://tableau.com/api");
        }
        private async Task<T> getResponseAsObject<T>(HttpContent content) where T:class
        {
           
            using (var reader = XmlReader.Create(new StringReader(await _readResponseString(content))))
                return ((tsResponse) new XmlSerializer(typeof(tsResponse)).Deserialize(reader)).Items.FirstOrDefault(
                    e => e is T) as T;

        }
        private async Task<(T, T2)> getResponseAsObject<T, T2>(HttpContent content) where T : class where T2:class
        {
            using (var reader = XmlReader.Create(new StringReader(await _readResponseString(content))))
            {
                var resp = ((tsResponse) new XmlSerializer(typeof(tsResponse)).Deserialize(reader));
                return (resp.Items.FirstOrDefault(e=>e is T) as T, resp.Items.FirstOrDefault(e=>e is T2) as T2);
            }

        }

        private StringContent getObjectAsRequestContent(object o) => xmlRequestContent(serializeRequest(o));

        private static string serializeRequest(object o)
        {
            var ser = new XmlSerializer(typeof(tsRequest));
            using (var textWriter = new StringWriter())
                using (var xmlWriter = XmlWriter.Create(textWriter))
            {
                ser.Serialize(xmlWriter, new tsRequest(){Item = o});
                var xml = textWriter.ToString();
                //remove first (XML schema) tag for compatibility with old versions of API
                xml = xml.Substring(xml.IndexOf("?>") +2);
                // remove XML definitions from tsRequest
                return Regex.Replace(xml, @"(?<=<tsRequest )[^>]+", "");
            }
        }

        private static StringContent xmlRequestContent(string xml)
        {
            var content = new StringContent(xml);
            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
            return content;
        }

        private MultipartContent prepareFileUploadContent(StringContent xmlContent, Stream strm)
        {
            var ret = new MultipartContent();
            xmlContent.Headers.TryAddWithoutValidation("Content-Disposition", "name=\"request_payload\"");
            ret.Add(xmlContent);
            if (strm != null)
            {
                var fileContentPart = new StreamContent(strm);
                fileContentPart.Headers.TryAddWithoutValidation("Content-Disposition", $"name=\"tableau_file\"; filename=\"FILE-NAME\"");
                fileContentPart.Headers.Remove("Content-Type");
                fileContentPart.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
                ret.Add(fileContentPart);
            }

            return ret;
        }
    }
}
