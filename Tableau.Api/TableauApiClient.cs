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
        private static readonly TimeSpan _defaultTimeout = new TimeSpan(2, 0, 0);
        private static readonly Version _minVersion = new Version(2, 0);
        private static readonly Version _maxVersion = new Version(3, 1);
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

        
        public TimeSpan Timeout { get; set; } = _defaultTimeout;
        public bool IgnoreSSLErrors { get; set; } = false;
        public TableauApiClient(Uri tableauBaseUri, ILogger logger, string apiVersion = "2.0")
        {
            BaseUrl = tableauBaseUri;
            if (Version.TryParse(apiVersion, out var v))
                ApiVersion = v < _minVersion ? _minVersion : v;
            else throw new ArgumentException($"'apiVersion' parameter cannot be converted to Version object");
            Log = logger;

            Log?.Log(LogLevel.Debug, $"Created TableauApiClient for tableau base url: '{tableauBaseUri}', api version: {apiVersion}");
        }

        public TableauApiClient(Uri tableauBaseUri, string apiVersion = "2.0") : this(tableauBaseUri, null, apiVersion) { }

        private void checkNullParameters(params (string, object)[] arguments)
        {
            foreach (var a in arguments)
                if (a.Item2 == null || (a.Item2 is string && string.IsNullOrEmpty((string)a.Item2)))
                    throw new ArgumentNullException(a.Item1, $"Argument {a.Item1} cannot be null");
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
            CancellationToken cancelToken, TableauSession session = null, HttpContent body = null)
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

                        var resp = await c.SendAsync(msg, cancelToken);
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
        private async Task<HttpContent> apiRequest<T>(Uri fullUri, HttpMethod method, int expectedStatusCode, CancellationToken cancelToken, TableauSession session, T body)
        {
           
            var Content = new StringContent(getObjectAsRequestXml(body));
            Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
            return await apiRequest(fullUri, method, expectedStatusCode, cancelToken, session, Content);
        }
        private async Task<T> getResponseAsObject<T>(HttpContent content) where T:class
        {
            var xdoc = XElement.Parse(await content.ReadAsStringAsync());
            var serializer = new XmlSerializer(typeof(tsResponse<T>));
            using (var reader = xdoc.CreateReader())
                return ((tsResponse<T>) serializer.Deserialize(reader)).Item1;
        }

        private string getObjectAsRequestXml(object o)
        {
            var ser = new XmlSerializer(typeof(tsRequest));
            using (var textWriter = new StringWriter())
                using (var xmlWriter = XmlWriter.Create(textWriter))
            {
                ser.Serialize(xmlWriter, new tsRequest(){Item = o});
                return textWriter.ToString();
            }
        }

        private MultipartContent prepareFileUploadContent(XElement xmlContent, string fileDisposition, string fileName,
            Stream strm)
        {
            var ret = new MultipartContent();
            var xmlContentPart = new StringContent(xmlContent?.ToString() ?? "", Encoding.UTF8, "text/xml");
            xmlContentPart.Headers.TryAddWithoutValidation("Content-Disposition", "name=\"request_payload\"");
            ret.Add(xmlContentPart);
            if (strm != null)
            {
                var fileContentPart = new StreamContent(strm);
                fileContentPart.Headers.TryAddWithoutValidation("Content-Disposition", $"name=\"{fileDisposition}\"; filename=\"{fileName}\"");
                fileContentPart.Headers.Remove("Content-Type");
                fileContentPart.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
                ret.Add(fileContentPart);
            }

            return ret;
        }
    }
}
