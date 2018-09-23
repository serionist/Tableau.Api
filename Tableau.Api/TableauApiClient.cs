using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
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
            if (tableauBaseUri == null)
                throw new ArgumentException("Tableau base Uri is required (eg. http://mytableauserver.com)", nameof(tableauBaseUri));
            var version = apiVersion;
            if (string.IsNullOrEmpty(version))
            {
                ApiVersion = new Version(2,4);
                try
                {
                    version = ServerInfo().Result.restApiVersion;
                    Log.LogDebug($"Tableau API version autodetected. Version: {version}");
                    ApiVersion = Version.Parse(version);
                }
                catch (AggregateException e)
                {
                    Log?.LogWarning(e.InnerExceptions.First(), $"Failed to retrieve Tableau API version. This might be because the server is version 2.4 or lower. Assuming 2.0 server version");
                    ApiVersion = new Version(2,0);
                }
            }
            else if (Version.TryParse(apiVersion, out var v))
                if (v < _minVersion)
                    throw new ArgumentOutOfRangeException(nameof(apiVersion),
                        $"Tableau API version must be greater than {_minVersion.ToString(2)}");
                else ApiVersion = v;
            else throw new ArgumentException($"'apiVersion' parameter cannot be converted to Version object", nameof(apiVersion));
            Log = logger;

            Log?.Log(LogLevel.Debug, $"Created TableauApiClient for tableau base url: '{tableauBaseUri}', api version: {apiVersion}");
        }
        /// <inheritdoc />
        /// <summary>
        /// Initializes an instance of the Tableau Api client
        /// </summary>
        /// <param name="tableauBaseUri">The base URL of the Tableau Server, eg: https://my-tableau-server.com</param>
        /// <param name="apiVersion">The Tableau API Version to use</param>
        public TableauApiClient(Uri tableauBaseUri, string apiVersion = "2.0") : this(tableauBaseUri, null, apiVersion) { }
        /// <inheritdoc />
        /// <summary>
        /// Initializes an instance of the Tableau Api client and attempt to automatically discover its version. 
        /// </summary>
        /// <param name="tableauBaseUri">The base URL of the Tableau Server, eg: https://my-tableau-server.com</param>
        /// <param name="logger">The logger object</param>
        public TableauApiClient(Uri tableauBaseUri, ILogger logger):this(tableauBaseUri, logger, null) { }
        /// <inheritdoc />
        /// <summary>
        /// Initializes an instance of the Tableau Api client and attempt to automatically discover its version. 
        /// </summary>
        /// <param name="tableauBaseUri">The base URL of the Tableau Server, eg: https://my-tableau-server.com</param>
        public TableauApiClient(Uri tableauBaseUri) : this(tableauBaseUri, null, null) { }
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

        private HttpClient buildClient(string AuthToken = null)
        {
            var clientOptions = new HttpClientHandler() { AllowAutoRedirect = true };
            if (IgnoreSSLErrors)
                clientOptions.ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true;
            var c = new HttpClient(clientOptions) { Timeout = Timeout };
            c.DefaultRequestHeaders.Remove("Accept");
            c.DefaultRequestHeaders.Add("Accept", "application/xml");
            if (!string.IsNullOrEmpty(AuthToken)) c.DefaultRequestHeaders.TryAddWithoutValidation("X-Tableau-Auth", AuthToken);
            return c;
        }
        private Uri buildUri(string relativePath, params (string, string)[] urlParams)
        {
            var url = $"{BaseUrl.Scheme}://{BaseUrl.Host}:{BaseUrl.Port}/api/{ApiVersion}/{(relativePath.StartsWith("/") ? relativePath.Substring(1) : relativePath)}";
            if (urlParams.Any())
                url += $"?{string.Join("&", urlParams.Select(e => $"{e.Item1}={(e.Item1 == "filter" || e.Item1 == "sort" ? e.Item2 : HttpUtility.UrlEncode(e.Item2))}"))}";
            return new Uri(url);

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

        private async Task<HttpContent> apiRequest(Uri fullUri, HttpMethod method, int expectedStatusCode,
            CancellationToken? cancelToken, TableauSession session = null, HttpContent body = null)
        {

            var state = new object();
            using (var logScope = Log?.BeginScope(state))
                try
                {
                    Log?.Log(LogLevel.Information, $"Sending request to Tableau Server. Method: {method}, Url: '{fullUri}'. Expected response code: {expectedStatusCode}");
                    using (var c = buildClient(session?.Token))
                    {
                        var msg = new HttpRequestMessage(method, fullUri);
                        if (body != null)
                            msg.Content = body;

                        var resp = await c.SendAsync(msg, cancelToken ?? CancellationToken.None);
                        if ((int)resp.StatusCode != expectedStatusCode)
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
      
        private async Task<T> getResponseAsObject<T>(HttpContent content) where T:class
        {
            using (var reader = XmlReader.Create(new StringReader(await content.ReadAsStringAsync())))
                return ((tsResponse) new XmlSerializer(typeof(tsResponse)).Deserialize(reader)).Items.FirstOrDefault(
                    e => e is T) as T;

        }
        private async Task<(T, T2)> getResponseAsObject<T, T2>(HttpContent content) where T : class where T2:class
        {
            using (var reader = XmlReader.Create(new StringReader(await content.ReadAsStringAsync())))
            {
                var resp = ((tsResponse) new XmlSerializer(typeof(tsResponse)).Deserialize(reader));
                return (resp.Items.FirstOrDefault(e=>e is T) as T, resp.Items.FirstOrDefault(e=>e is T2) as T2);
            }

        }

        private StringContent getObjectAsRequestContent(object o)
        {
            var ser = new XmlSerializer(typeof(tsRequest));
            using (var textWriter = new StringWriter())
                using (var xmlWriter = XmlWriter.Create(textWriter))
            {
                ser.Serialize(xmlWriter, new tsRequest(){Item = o});
                var content = new StringContent(textWriter.ToString());
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
                return content;
            }
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
