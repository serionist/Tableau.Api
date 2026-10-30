using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Fakes;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Tableau.Api.Tests;

/// <summary>The invented site, user and session every test signs in with, and the client they drive.</summary>
internal static class Fixtures
{
    public const string ApiVersion = "3.4";
    public const string UserName = "ada";
    public const string Password = "invented-password";
    public const string SiteContentUrl = "finance";
    public const string SiteId = "0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f";
    public const string UserId = "7c1d2e3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f";
    public const string SessionToken = "invented-session-token";
    public const string OtherSessionToken = "another-invented-token";
    public const string PatSecret = "invented-pat-secret";

    /// <summary>Every secret a test hands the library, which no log line may hold.</summary>
    public static readonly string[] Secrets = [Password, SessionToken, OtherSessionToken, PatSecret];

    /// <summary>Every line the clients Client makes log, across the suite.</summary>
    public static readonly RecordingLogger Log = new();

    /// <summary>
    /// A client for the fake server, logging to <see cref="Log"/>, with a short timeout so a request nobody answers
    /// fails fast.
    /// </summary>
    public static TableauApiClient Client(FakeTableauServer server) =>
        new(server.BaseUrl, Log, ApiVersion) { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Tableau's answer to a sign-in: the session token, the site's id and the user's id.</summary>
    public static FakeResponse SignedIn() =>
        FakeResponse.Xml(200, $"<credentials token=\"{SessionToken}\">"
            + $"<site id=\"{SiteId}\" contentUrl=\"{SiteContentUrl}\"/><user id=\"{UserId}\"/></credentials>");

    /// <summary>The session a sign-in returns, for tests whose server need not see the sign-in.</summary>
    public static TableauSession Session() =>
        new() { SiteId = SiteId, UserId = UserId, Token = SessionToken, UserName = UserName };

    /// <summary>Map the sign-in on the server and sign in; the tests of other calls start here.</summary>
    public static async Task<TableauSession> SignInAsync(FakeTableauServer server, TableauApiClient client)
    {
        server.Map("POST", $"/api/{ApiVersion}/auth/signin", SignedIn());
        return await client.SignIn(UserName, Password, SiteContentUrl);
    }
}
