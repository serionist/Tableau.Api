using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Tableau.Api.Exceptions;
using Tableau.Api.Models;
using Tableau.Api.VizPortal.Models;

namespace Tableau.Api.VizPortal;

public partial class VizPortalClient
{
    private TableauApiClient _tableauApiClient;

    public VizPortalClient(TableauApiClient tableauApiClient)
    {
        _tableauApiClient = tableauApiClient;
    }

    private HttpClient buildClient(string sessionToken)
    {
        var cookieContainer = new CookieContainer();
        var xrsf = Guid.NewGuid().ToString("N");
        cookieContainer.Add(new Cookie("workgroup_session_id", sessionToken, "/")
        {
            HttpOnly = true,
            Domain = _tableauApiClient.BaseUrl.Host,
            Secure = true
        });
        cookieContainer.Add(new Cookie("XSRF-TOKEN", xrsf, "/")
        {
            HttpOnly = true,
            Domain = _tableauApiClient.BaseUrl.Host,
            Secure = true
        });
        // The same handler as the REST client's: the certificate is validated unless IgnoreSSLErrors is set, and no
        // redirect is followed.
        var clientOptions = TableauHttp.CreateHandler(_tableauApiClient.IgnoreSSLErrors, cookieContainer);
        var baseUrl = _tableauApiClient.BaseUrl;
        var c = new HttpClient(clientOptions)
        {
            BaseAddress = new Uri(
                $"{baseUrl.Scheme}://{baseUrl.Host}:{baseUrl.Port}{TableauHttp.BasePath(baseUrl)}vizportal/api/web/v1/"),
            Timeout = _tableauApiClient.Timeout
        };
        c.DefaultRequestHeaders.Add("X-Xsrf-Token", xrsf);
        return c;
    }

    private async Task<TResult> Request<TParameters, TResult>(TableauSession session, string methodName,
        TParameters parameters)
    {
        var jsonSettings = new JsonSerializer()
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver() { NamingStrategy = new CamelCaseNamingStrategy() }
        };
        jsonSettings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
        JObject p;
        try
        {
            p = JObject.FromObject(parameters, jsonSettings);
        }
        catch (Exception ex)
        {
            _tableauApiClient.Log?.Log(LogLevel.Error, ex, $"Failed to serialize parameters for VizPortal request");
            throw;
        }

        var r = await Request(session, methodName, p);

        try
        {
            return r.ToObject<TResult>(jsonSettings);
        }
        catch (Exception ex)
        {
            _tableauApiClient.Log?.Log(LogLevel.Error, ex, $"Failed to serialize output for VizPortal request");
            throw;
        }
    }
    private async Task<JObject> Request(TableauSession session, string methodName, JObject parameters)
    {
        // Disposed after each request, as the REST client's is, so its connection doesn't wait for the finalizer.
        using var c = buildClient(session.Token);
        // The method name ends the path, escaped and checked as a value in a REST path is: the public paging
        // methods take it from the caller. It is appended to the base address, never resolved against it, so no
        // name can reach another path or host.
        RequestPath method = $"{methodName}";
        var uri = new Uri(c.BaseAddress.AbsoluteUri + method.ToEscapedPath());

        try
        {
            _tableauApiClient.Log?.Log(LogLevel.Information, $"Sending request to Tableau VizPortal. Method: {methodName}");
            var reqStr = new JObject(new JProperty("method", methodName), new JProperty("params", parameters))
                .ToString(
                    Formatting.Indented);
            var resp = await c.PostAsync(uri,
                new StringContent(reqStr
                    , Encoding.UTF8, "application/json"));
            if (TableauHttp.IsRedirect(resp.StatusCode))
                throw TableauHttp.RedirectException(resp);
            if ((int)resp.StatusCode != 200)
                throw await buildException(resp);
            _tableauApiClient.Log?.Log(LogLevel.Information, "Request successful");
            var content = await resp.Content.ReadAsStringAsync();
            var jo = JObject.Parse(content);
            if (!jo.ContainsKey("result") || !(jo["result"] is JObject result))
                throw new TableauRequestException(resp.RequestMessage.RequestUri, resp.StatusCode,
                    new errorType() { code = "100", summary = "VizPortal response invalid", detail = "No 'result' property provided in response" });
            if (result.ContainsKey("errors"))
                if ((result["errors"] is JArray errArr) && errArr.First != null && errArr.First is JObject errObj)
                {
                    var code = errObj.Value<int>("code").ToString(CultureInfo.InvariantCulture);
                    throw new TableauRequestException(resp.RequestMessage.RequestUri, resp.StatusCode,
                        new errorType()
                        {
                            code = code,
                            summary = $"Tableau VizPortal API threw code {code}",
                            detail = "No further information is provided by VizPortal"
                        });
                }
                else throw new TableauRequestException(resp.RequestMessage.RequestUri, resp.StatusCode,
                    new errorType()
                    {
                        code = "100",
                        summary = $"Tableau VizPortal API threw an unknown error",
                        detail = "No further information is provided by VizPortal"
                    });
            return result;

        }
        catch (TableauRequestException e)
        {
            _tableauApiClient.Log?.Log(LogLevel.Error, e, $"Tableau VizPortal returned an error at request. {(e.HasDetails ? $"Error code: {e.Details.code}" : "No details available")}");
            throw;
        }
        catch (HttpRequestException e)
        {
            _tableauApiClient.Log?.Log(LogLevel.Error, e, $"A network error occured while communicating with Tableau Server. Internal error message: {e.Message}");
            throw;
        }
        catch (Exception e)
        {
            _tableauApiClient.Log?.Log(LogLevel.Error, e, $"An unhandled error occured while communicating with Tableau Server. Internal error message: {e.Message}");
            throw;
        }


    }

    public async IAsyncEnumerable<TResult> PaginateRequest<TResult, TPageResponse, TParameters>(
        TableauSession session, string methodName,
        TParameters parameters, Func<TPageResponse, TResult[]> getResults) where TPageResponse : QueryResponseModel
        where TParameters : QueryModel
    {
        int startIndex = 0;
        int perPage = 100;
        while (true)
        {
            parameters.Page = new QueryModel.PageModel()
            {
                StartIndex = startIndex,
                MaxItems = perPage
            };
            var pageResult = await Request<TParameters, TPageResponse>(session, methodName, parameters);
            var any = false;
            foreach (var res in getResults(pageResult) ?? Array.Empty<TResult>())
            {
                yield return res;
                any = true;
            }

            if (!pageResult.MoreItems || !any)
                break;
            startIndex += perPage;
        }
    }

    public async IAsyncEnumerable<TResult> NextPageTokenRequest<TResult, TPageResponse, TParameters>(
        TableauSession session, string methodName,
        TParameters parameters, Func<TPageResponse, TResult[]> getResults) where TPageResponse : QueryResponseWithTokenModel
        where TParameters : QueryModel
    {
        string token = null;
        int size = 100;
        while (true)
        {
            parameters.PageWithToken = new QueryModel.PageWithTokenModel()
            {
                Size = size,
                Token = token
            };
            var pageResult = await Request<TParameters, TPageResponse>(session, methodName, parameters);
            var any = false;
            foreach (var res in getResults(pageResult) ?? Array.Empty<TResult>())
            {
                yield return res;
                any = true;
            }

            if (!pageResult.MoreItems || !any)
                break;
            token = pageResult.NextPageToken;
        }
    }

    private async Task<TableauRequestException> buildException(HttpResponseMessage resp)
    {
        errorType det = null;
        try
        {
            var jo = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var c = jo["result"]["errors"][0].Value<int>("code").ToString(CultureInfo.InvariantCulture);
            det = new errorType()
            {
                code = c,
                summary = $"Tableau VizPortal API threw code {c}",
                detail = "No further information is provided by VizPortal"

            };
        }
        catch (Exception ex)
        {
            _tableauApiClient.Log?.Log(LogLevel.Warning, ex, "Failed to read Tableau Server error details");
        }
        return new TableauRequestException(resp.RequestMessage.RequestUri, resp.StatusCode, det);
    }
}
