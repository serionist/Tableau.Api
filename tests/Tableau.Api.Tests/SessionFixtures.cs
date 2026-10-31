using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>The 2.0 client's sign-ins against the fake server: invented credentials, Tableau's answers, and a call in a session.</summary>
internal static class SessionFixtures
{
    public const string SignInPath = "/api/" + ClientFixtures.Version + "/auth/signin";
    public const string SignOutPath = "/api/" + ClientFixtures.Version + "/auth/signout";
    public const string SwitchSitePath = "/api/" + ClientFixtures.Version + "/auth/switchSite";

    /// <summary>The path of <see cref="Items"/> in the invented site.</summary>
    public const string ItemsPath = "/api/" + ClientFixtures.Version + "/sites/" + Fixtures.SiteId + "/items";

    public const string PatName = "nightly-export";
    public const string AppClientId = "1c9a5c7e-0a1b-4c2d-8e3f-4a5b6c7d8e9f";
    public const string AppSecretId = "invented-secret-id";
    public const string AppSecretValue = "invented-connected-app-secret-value";
    public const string AppUser = "grace";
    public const string ImpersonatedUserId = "3e9c7e90-2c3d-4e4f-a051-6c7d8e9fa0b1";

    /// <summary>A JWT as one minted elsewhere would be, its claims naming the user; invented, its signature too.</summary>
    public static readonly string Jwt = InventedJwt("""{"sub":"hopper@invented.example","aud":"tableau"}""");

    /// <summary>A call in the client's session, in the session's site: what each area's calls will be.</summary>
    public static readonly Endpoint Items = new("Test call in a session", HttpMethod.Get, "{version}/sites/{siteId}/items", new Version(2, 0),
        TableauDeployments.Both, Idempotent: true, TransferKind.None);

    /// <summary>A client of its own, with the credentials given, logging to <see cref="Fixtures.Log"/>, on the clock given.</summary>
    public static TableauClient Client(TableauClientOptions options, TableauCredentials? credentials, TimeProvider? time = null) =>
        TableauClient.CreateWithClock(options, credentials, Fixtures.Log.Factory, time);

    /// <summary>The fixture options, for the invented site.</summary>
    public static TableauClientOptions Options(FakeTableauServer server, string? version = ClientFixtures.Version)
    {
        var options = ClientFixtures.Options(server, version);
        options.Site = Fixtures.SiteContentUrl;
        return options;
    }

    public static TableauCredentials Pat(string? impersonateUserId = null) =>
        TableauCredentials.PersonalAccessToken(PatName, Fixtures.PatSecret, impersonateUserId);

    public static TableauCredentials Password(string? impersonateUserId = null) =>
        TableauCredentials.Password(Fixtures.UserName, Fixtures.Password, impersonateUserId);

    public static TableauCredentials DirectTrust() =>
        TableauCredentials.ConnectedAppDirectTrust(AppClientId, AppSecretId, AppSecretValue, AppUser,
            ["tableau:content:read", "tableau:projects:*"]);

    /// <summary>Tableau's answer to a sign-in in the invented site, with the token given.</summary>
    public static FakeResponse SignedIn(string token = Fixtures.SessionToken, string? estimatedTimeToExpiration = null)
    {
        var expiry = estimatedTimeToExpiration is null ? "" : $",\"estimatedTimeToExpiration\":\"{estimatedTimeToExpiration}\"";
        return FakeResponse.Json(200, $$$"""
            {"credentials":{"site":{"id":"{{{Fixtures.SiteId}}}","contentUrl":"{{{Fixtures.SiteContentUrl}}}"},"user":{"id":"{{{Fixtures.UserId}}}"},"token":"{{{token}}}"{{{expiry}}}}}
            """);
    }

    /// <summary>Tableau's answer to a sign-in it refused.</summary>
    public static FakeResponse Refused() =>
        FakeResponse.Json(401, """{"error":{"summary":"Signin Error","detail":"Error signing in to Tableau Server","code":"401001"}}""");

    /// <summary>Tableau's answer to a call whose session ended.</summary>
    public static FakeResponse Unauthorized() =>
        FakeResponse.Json(401, """{"error":{"summary":"Unauthorized Access","detail":"Invalid authentication credentials were provided.","code":"401002"}}""");

    /// <summary>Answers sign-ins with each token in turn, the last again after that, and counts them.</summary>
    public static Counter MapSignIns(FakeTableauServer server, params string[] tokens)
    {
        var counter = new Counter();
        server.Map("POST", SignInPath, _ => SignedIn(tokens[Math.Min(counter.Next() - 1, tokens.Length - 1)]));
        return counter;
    }

    /// <summary>The requests to a path, in order.</summary>
    public static RecordedRequest[] To(FakeTableauServer server, string path) =>
        [.. server.Requests.Where(request => request.Path == path)];

    /// <summary>A delegate that gives a secret and counts its calls.</summary>
    public static Func<CancellationToken, ValueTask<string>> Counted(string secret, Counter counter) =>
        _ =>
        {
            counter.Next();
            return ValueTask.FromResult(secret);
        };

    private static string InventedJwt(string claims) =>
        string.Join('.', Base64Url("""{"alg":"HS256","typ":"JWT"}"""), Base64Url(claims), Base64Url("invented-signature"));

    private static string Base64Url(string text) =>
        System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    /// <summary>A count that threads can add to.</summary>
    internal sealed class Counter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public int Next() => Interlocked.Increment(ref _count);
    }
}
