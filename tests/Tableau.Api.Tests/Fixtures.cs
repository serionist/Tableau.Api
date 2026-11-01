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

    /// <summary>A database connection's password, which an update sends in its body alone.</summary>
    public const string ConnectionPassword = "invented-connection-password";

    /// <summary>
    /// The start of every HS256 JWT's header, <c>{"alg":"HS256",</c> in base64url: a log line that holds it holds a JWT,
    /// such as one the client minted for a connected app.
    /// </summary>
    public const string JwtStart = "eyJhbGciOiJIUzI1NiIs";

    /// <summary>Every secret a test hands the library, which no log line may hold.</summary>
    public static readonly string[] Secrets =
        [Password, SessionToken, OtherSessionToken, PatSecret, ConnectionPassword, SessionFixtures.AppSecretValue, SessionFixtures.Jwt, JwtStart];

    /// <summary>Every line the clients Client makes log, across the suite.</summary>
    public static readonly RecordingLogger Log = new();

    /// <summary>
    /// A client for the fake server, logging to <see cref="Log"/>, with a short timeout so a request nobody answers
    /// fails fast.
    /// </summary>
    public static TableauApiClient Client(FakeTableauServer server) =>
        new(server.BaseUrl, Log, ApiVersion) { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Where Vizportal's methods are posted, under the base URL.</summary>
    public const string VizportalPath = "/vizportal/api/web/v1/";

    /// <summary>Vizportal's answer to a call that returns nothing: an empty result.</summary>
    public static FakeResponse VizportalResult() => FakeResponse.Json(200, "{\"result\":{}}");

    /// <summary>The old client's session, as a sign-in gave it, for its tests; 2.0's are in <see cref="SessionFixtures"/>.</summary>
    public static Models.TableauSession Session() =>
        new() { SiteId = SiteId, UserId = UserId, Token = SessionToken, UserName = UserName };
}
