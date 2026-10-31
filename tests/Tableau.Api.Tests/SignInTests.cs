using System;
using System.Buffers.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// What each kind of credentials sends to Sign In, what comes back as a <see cref="TableauSession"/>, what is refused
/// before anything is sent, and that a sign-in's failure carries none of Tableau's answer.
/// </summary>
[TestClass]
public sealed class SignInTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task APersonalAccessTokenSendsItsNameAndSecretAndTheSessionComesBack()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn(estimatedTimeToExpiration: "363:10:35"));
        var time = new FakeTimeProvider(_now);
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null, time);

        var session = await client.Sessions.SignInAsync(SessionFixtures.Pat());

        var request = server.Requests.Single();
        Assert.AreEqual($"POST {SessionFixtures.SignInPath} HTTP/1.1", request.RequestLine);
        Assert.IsNull(request.Header("X-Tableau-Auth"));
        Assert.AreEqual("application/json", request.Header("Content-Type"));
        Assert.IsNull(JsonEquivalence.Difference(
            """{"credentials":{"site":{"contentUrl":"finance"},"personalAccessTokenName":"nightly-export","personalAccessTokenSecret":"invented-pat-secret"}}""",
            request.BodyText));
        Assert.AreEqual(Fixtures.SiteId, session.SiteId);
        Assert.AreEqual(Fixtures.SiteContentUrl, session.SiteContentUrl);
        Assert.AreEqual(Fixtures.UserId, session.UserId);
        Assert.AreEqual(SessionFixtures.PatName, session.UserName);
        Assert.AreEqual(_now + TimeSpan.FromMinutes(240), session.ExpiresAt);
        Assert.AreEqual(TableauDeployment.Server, session.Deployment);
        Assert.AreEqual(new Version(3, 25), session.ApiVersion);
        Assert.AreEqual(Fixtures.SessionToken, session.Token);
    }

    [TestMethod]
    public async Task APasswordSendsTheUsersNameAndPassword()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var session = await client.Sessions.SignInAsync(SessionFixtures.Password());

        Assert.IsNull(JsonEquivalence.Difference(
            """{"credentials":{"site":{"contentUrl":"finance"},"name":"ada","password":"invented-password"}}""",
            server.Requests.Single().BodyText));
        Assert.AreEqual(Fixtures.UserName, session.UserName);
    }

    [TestMethod]
    public async Task ImpersonationSendsTheUserOnTableauServer()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        await client.Sessions.SignInAsync(SessionFixtures.Password(SessionFixtures.ImpersonatedUserId));
        await client.Sessions.SignInAsync(SessionFixtures.Pat(SessionFixtures.ImpersonatedUserId));

        Assert.IsNull(JsonEquivalence.Difference(
            $$$"""{"credentials":{"site":{"contentUrl":"finance"},"user":{"id":"{{{SessionFixtures.ImpersonatedUserId}}}"},"name":"ada","password":"invented-password"}}""",
            server.Requests[0].BodyText));
        Assert.IsNull(JsonEquivalence.Difference(
            $$$"""{"credentials":{"site":{"contentUrl":"finance"},"user":{"id":"{{{SessionFixtures.ImpersonatedUserId}}}"},"personalAccessTokenName":"nightly-export","personalAccessTokenSecret":"invented-pat-secret"}}""",
            server.Requests[1].BodyText));
    }

    [TestMethod]
    public async Task ImpersonationIsRefusedOnTableauCloudBeforeTheSecretIsReadOrAnythingSent()
    {
        using var server = FakeTableauServer.Start();
        var options = SessionFixtures.Options(server);
        options.Deployment = TableauDeployment.Cloud;
        await using var client = SessionFixtures.Client(options, null);
        var read = new SessionFixtures.Counter();

        foreach (var credentials in new[]
                 {
                     TableauCredentials.PersonalAccessToken(SessionFixtures.PatName, SessionFixtures.Counted(Fixtures.PatSecret, read), SessionFixtures.ImpersonatedUserId),
                     TableauCredentials.Password(Fixtures.UserName, SessionFixtures.Counted(Fixtures.Password, read), SessionFixtures.ImpersonatedUserId),
                 })
        {
            var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedDeployment, () => client.Sessions.SignInAsync(credentials));
            Assert.Contains("impersonating a user doesn't exist on Tableau Cloud", thrown.Message);
        }

        Assert.IsEmpty(server.Requests);
        Assert.AreEqual(0, read.Count);
    }

    [TestMethod]
    public async Task ADirectTrustJwtIsMintedSignedAndNewAtEachSignIn()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        var time = new FakeTimeProvider(_now);
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null, time);

        var session = await client.Sessions.SignInAsync(SessionFixtures.DirectTrust());
        await client.Sessions.SignInAsync(SessionFixtures.DirectTrust());

        var jwts = server.Requests.Select(request =>
        {
            var credentials = JsonDocument.Parse(request.BodyText).RootElement.GetProperty("credentials");
            Assert.IsFalse(credentials.TryGetProperty("isUat", out _), "a connected app's JWT is no unified access token");
            Assert.AreEqual(Fixtures.SiteContentUrl, credentials.GetProperty("site").GetProperty("contentUrl").GetString());
            return credentials.GetProperty("jwt").GetString()!;
        }).ToList();
        var parts = jwts[0].Split('.');
        Assert.HasCount(3, parts);
        var header = Decode(parts[0]);
        Assert.AreEqual("HS256", header.GetProperty("alg").GetString());
        Assert.AreEqual("JWT", header.GetProperty("typ").GetString());
        Assert.AreEqual(SessionFixtures.AppSecretId, header.GetProperty("kid").GetString());
        Assert.AreEqual(SessionFixtures.AppClientId, header.GetProperty("iss").GetString());
        var claims = Decode(parts[1]);
        Assert.AreEqual(SessionFixtures.AppClientId, claims.GetProperty("iss").GetString());
        Assert.AreEqual("tableau", claims.GetProperty("aud").GetString());
        Assert.AreEqual(SessionFixtures.AppUser, claims.GetProperty("sub").GetString());
        CollectionAssert.AreEqual(new[] { "tableau:content:read", "tableau:projects:*" },
            claims.GetProperty("scp").EnumerateArray().Select(scope => scope.GetString()).ToArray());
        var expires = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
        Assert.IsGreaterThan(_now, expires);
        Assert.IsLessThanOrEqualTo(_now + TimeSpan.FromMinutes(10), expires);
        Assert.IsTrue(Guid.TryParseExact(claims.GetProperty("jti").GetString(), "D", out _));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SessionFixtures.AppSecretValue), Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
        CollectionAssert.AreEqual(signature, Base64Url.DecodeFromChars(parts[2]), "the signature is HS256 over the header and claims, by the secret's value");
        Assert.AreNotEqual(claims.GetProperty("jti").GetString(), Decode(jwts[1].Split('.')[1]).GetProperty("jti").GetString(), "a new jti each time");
        Assert.AreEqual(SessionFixtures.AppUser, session.UserName);
    }

    [TestMethod]
    public async Task AJwtFromElsewhereIsAskedForAtEachSignInAndNamesItsSubject()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);
        var asked = new SessionFixtures.Counter();
        var credentials = TableauCredentials.Jwt(SessionFixtures.Counted(SessionFixtures.Jwt, asked));

        var session = await client.Sessions.SignInAsync(credentials);
        await client.Sessions.SignInAsync(credentials);

        Assert.IsNull(JsonEquivalence.Difference(
            $$$"""{"credentials":{"site":{"contentUrl":"finance"},"jwt":"{{{SessionFixtures.Jwt}}}"}}""", server.Requests[0].BodyText));
        Assert.AreEqual(2, asked.Count);
        Assert.AreEqual("hopper@invented.example", session.UserName);
    }

    [TestMethod]
    public async Task AJwtThatIsNoJwtSignsInWithNoUserName()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.SignedIn());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        foreach (var token in new[] { "opaque", "a.!!!.c", "a." + Base64Url.EncodeToString("[1]"u8) + ".c", "a." + Base64Url.EncodeToString("{\"sub\":1}"u8) + ".c" })
        {
            var session = await client.Sessions.SignInAsync(TableauCredentials.Jwt(_ => ValueTask.FromResult(token)));
            Assert.IsNull(session.UserName, token);
        }
    }

    [TestMethod]
    public async Task AUnifiedAccessTokenSignsInWithIsUatOnTableauCloudFromThreeTwentySeven()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", "/api/3.27/auth/signin", SessionFixtures.SignedIn());
        var options = SessionFixtures.Options(server, "3.27");
        options.Deployment = TableauDeployment.Cloud;
        await using var client = SessionFixtures.Client(options, null, new FakeTimeProvider(_now));

        var session = await client.Sessions.SignInAsync(TableauCredentials.UnifiedAccessToken(_ => ValueTask.FromResult(SessionFixtures.Jwt)));

        Assert.IsNull(JsonEquivalence.Difference(
            $$$"""{"credentials":{"site":{"contentUrl":"finance"},"isUat":true,"jwt":"{{{SessionFixtures.Jwt}}}"}}""", server.Requests.Single().BodyText));
        Assert.AreEqual(TableauDeployment.Cloud, session.Deployment);
        Assert.AreEqual(_now + TimeSpan.FromMinutes(120), session.ExpiresAt, "a Tableau Cloud session lasts 120 minutes");
    }

    [TestMethod]
    public async Task AUnifiedAccessTokenIsRefusedOnTableauServerAndBelowThreeTwentySevenBeforeItIsAskedFor()
    {
        using var server = FakeTableauServer.Start();
        var asked = new SessionFixtures.Counter();
        var credentials = TableauCredentials.UnifiedAccessToken(SessionFixtures.Counted(SessionFixtures.Jwt, asked));
        await using var onServer = SessionFixtures.Client(SessionFixtures.Options(server, "3.27"), null);
        var cloud = SessionFixtures.Options(server, "3.26");
        cloud.Deployment = TableauDeployment.Cloud;
        await using var tooOld = SessionFixtures.Client(cloud, null);

        await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedDeployment, () => onServer.Sessions.SignInAsync(credentials));
        await ClientFixtures.FailsAsync(TableauErrorKind.UnsupportedApiVersion, () => tooOld.Sessions.SignInAsync(credentials));

        Assert.IsEmpty(server.Requests);
        Assert.AreEqual(0, asked.Count);
    }

    [TestMethod]
    public async Task ARefusedSignInIsAuthenticationWithTableausErrorAndNoBody()
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, SessionFixtures.Refused());
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => client.Sessions.SignInAsync(SessionFixtures.Password()));

        Assert.AreEqual("401001", thrown.ErrorCode);
        Assert.AreEqual("Signin Error", thrown.Summary);
        Assert.AreEqual(SessionFixtures.SignInPath, thrown.RequestPath);
        Assert.IsNull(thrown.ResponseBody, "a sign-in's exception keeps none of the answer");
    }

    [TestMethod]
    [DataRow("<html>invented-session-token</html>")]
    [DataRow("""{"credentials":"invented-session-token"}""")]
    [DataRow("""{"site":{"id":"invented-session-token"}}""")]
    public async Task ASignInAnswerTheClientCantReadIsAnInvalidResponseWithNoBody(string answer)
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, FakeResponse.Content(200, "application/json", answer));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => client.Sessions.SignInAsync(SessionFixtures.Pat()));

        Assert.IsNull(thrown.ResponseBody);
        Assert.DoesNotContain(Fixtures.SessionToken, thrown.ToString());
    }

    [TestMethod]
    [DataRow("""{"credentials":{"site":{"id":"0f4b9e1c-3a52-4d6e-9c8b-1a2b3c4d5e6f"},"user":{"id":"u"}}}""", "token")]
    [DataRow("""{"credentials":{"token":"t","user":{"id":"u"}}}""", "site id")]
    [DataRow("""{"credentials":{"token":"t","site":{"id":"s"}}}""", "user id")]
    public async Task CredentialsWithoutATokenASiteOrAUserAreAnInvalidResponse(string answer, string missing)
    {
        using var server = FakeTableauServer.Start();
        server.Map("POST", SessionFixtures.SignInPath, FakeResponse.Json(200, answer));
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.InvalidResponse, () => client.Sessions.SignInAsync(SessionFixtures.Pat()));

        Assert.Contains($"no {missing}", thrown.Message);
    }

    [TestMethod]
    public async Task ASecretsDelegateThatFailsOrGivesNothingIsAuthenticationAndNothingIsSent()
    {
        using var server = FakeTableauServer.Start();
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);

        var failed = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => client.Sessions.SignInAsync(
            TableauCredentials.PersonalAccessToken(SessionFixtures.PatName, _ => throw new InvalidOperationException("The invented vault is closed."))));
        var empty = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => client.Sessions.SignInAsync(
            TableauCredentials.ConnectedAppDirectTrust(SessionFixtures.AppClientId, SessionFixtures.AppSecretId, _ => ValueTask.FromResult(""),
                SessionFixtures.AppUser, [])));
        var none = await ClientFixtures.FailsAsync(TableauErrorKind.Authentication, () => client.Sessions.SignInAsync(
            TableauCredentials.Jwt(_ => ValueTask.FromResult<string>(null!))));

        Assert.IsInstanceOfType<InvalidOperationException>(failed.InnerException);
        Assert.Contains("personal access token's secret failed", failed.Message);
        Assert.Contains("connected app's secret value gave none", empty.Message);
        Assert.Contains("JWT gave none", none.Message);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task TheCallersCancellationInASecretsDelegateStaysACancellation()
    {
        using var server = FakeTableauServer.Start();
        await using var client = SessionFixtures.Client(SessionFixtures.Options(server), null);
        using var cancelled = new CancellationTokenSource();
        var credentials = TableauCredentials.Password(Fixtures.UserName, async token =>
        {
            await cancelled.CancelAsync();
            token.ThrowIfCancellationRequested();
            return Fixtures.Password;
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.Sessions.SignInAsync(credentials, cancellationToken: cancelled.Token));

        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public void CredentialsRefuseWhatCantSignIn()
    {
        Func<CancellationToken, ValueTask<string>> secret = _ => ValueTask.FromResult("s");
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.PersonalAccessToken("", "s"));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.PersonalAccessToken("n", ""));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauCredentials.PersonalAccessToken("n", (string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauCredentials.PersonalAccessToken("n", (Func<CancellationToken, ValueTask<string>>)null!));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.PersonalAccessToken("n", "s", "not-a-luid"));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.Password("n", "p", " " + SessionFixtures.ImpersonatedUserId));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.Password("", secret));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.Password("n", ""));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.ConnectedAppDirectTrust("", "k", "v", "u", []));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.ConnectedAppDirectTrust("c", "", "v", "u", []));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.ConnectedAppDirectTrust("c", "k", "", "u", []));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.ConnectedAppDirectTrust("c", "k", "v", "", []));
        Assert.ThrowsExactly<ArgumentException>(() => TableauCredentials.ConnectedAppDirectTrust("c", "k", "v", "u", ["tableau:content:read", ""]));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauCredentials.ConnectedAppDirectTrust("c", "k", "v", "u", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauCredentials.ConnectedAppDirectTrust("c", "k", (Func<CancellationToken, ValueTask<string>>)null!, "u", []));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauCredentials.Jwt(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TableauCredentials.UnifiedAccessToken(null!));
        Assert.IsNotNull(TableauCredentials.PersonalAccessToken("n", "s", SessionFixtures.ImpersonatedUserId.ToUpperInvariant()));
    }

    [TestMethod]
    public void CredentialsNameTheirKindAndUserAndNoSecret()
    {
        var shown = new[]
        {
            SessionFixtures.Pat().ToString(),
            SessionFixtures.Pat(SessionFixtures.ImpersonatedUserId).ToString(),
            SessionFixtures.Password().ToString(),
            SessionFixtures.Password(SessionFixtures.ImpersonatedUserId).ToString(),
            SessionFixtures.DirectTrust().ToString(),
            TableauCredentials.Jwt(_ => ValueTask.FromResult(SessionFixtures.Jwt)).ToString(),
            TableauCredentials.UnifiedAccessToken(_ => ValueTask.FromResult(SessionFixtures.Jwt)).ToString(),
        };

        CollectionAssert.AreEqual(new[]
        {
            "PersonalAccessToken (nightly-export)",
            $"PersonalAccessToken (nightly-export, as {SessionFixtures.ImpersonatedUserId})",
            "Password (ada)",
            $"Password (ada, as {SessionFixtures.ImpersonatedUserId})",
            "ConnectedAppDirectTrust (grace)",
            "Jwt",
            "UnifiedAccessToken",
        }, shown);
        Assert.IsFalse(shown.Any(text => Fixtures.Secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal))));
    }

    [TestMethod]
    public void ASessionPrintsEveryMemberButItsToken()
    {
        var session = new TableauSession
        {
            SiteId = Fixtures.SiteId,
            SiteContentUrl = Fixtures.SiteContentUrl,
            UserId = Fixtures.UserId,
            UserName = Fixtures.UserName,
            ExpiresAt = _now,
            Deployment = TableauDeployment.Server,
            ApiVersion = new Version(3, 25),
            Token = Fixtures.SessionToken,
        };

        var text = session.ToString();

        Assert.AreEqual($"TableauSession {{ SiteId = {Fixtures.SiteId}, SiteContentUrl = finance, UserId = {Fixtures.UserId}, UserName = ada, "
            + "ExpiresAt = 2026-10-31T12:00:00.0000000+00:00, Deployment = Server, ApiVersion = 3.25 }", text);
        Assert.DoesNotContain(Fixtures.SessionToken, text);
        Assert.IsFalse(typeof(TableauSession).GetProperties().Any(property => property.Name == "Token"), "the token is no public member");
        Assert.AreEqual(session, session with { });
        Assert.AreNotEqual(session, session with { Token = Fixtures.OtherSessionToken });
    }

    [TestMethod]
    [DataRow("363:10:35", 363, 10, 35)]
    [DataRow("0:01:00", 0, 1, 0)]
    [DataRow("2:12:30", 2, 12, 30)]
    public void APersonalAccessTokensLifetimeReadsAsDaysHoursAndMinutes(string text, int days, int hours, int minutes) =>
        Assert.AreEqual(new TimeSpan(days, hours, minutes, 0), TableauSessions.TokenLifetime(text));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("240")]
    [DataRow("1:2")]
    [DataRow("1:24:00")]
    [DataRow("1:00:60")]
    [DataRow("a:00:00")]
    [DataRow("-1:00:00")]
    [DataRow("1:00:00:00")]
    [DataRow("99999999:00:00")]
    public void TextInAnotherShapeIsNoLifetime(string? text) => Assert.IsNull(TableauSessions.TokenLifetime(text));

    [TestMethod]
    public void ASessionEndsAfterItsDeploymentsLifetimeOrItsTokensIfSooner()
    {
        Assert.AreEqual(_now + TimeSpan.FromMinutes(240), TableauSessions.ExpiresAt(_now, TableauDeployment.Server, null));
        Assert.AreEqual(_now + TimeSpan.FromMinutes(120), TableauSessions.ExpiresAt(_now, TableauDeployment.Cloud, null));
        Assert.AreEqual(_now + TimeSpan.FromMinutes(240), TableauSessions.ExpiresAt(_now, TableauDeployment.Server, "363:10:35"));
        Assert.AreEqual(_now + TimeSpan.FromMinutes(60), TableauSessions.ExpiresAt(_now, TableauDeployment.Server, "0:01:00"));
        Assert.AreEqual(_now + TimeSpan.FromMinutes(120), TableauSessions.ExpiresAt(_now, TableauDeployment.Cloud, "0:03:00"));
    }

    private static JsonElement Decode(string part) => JsonDocument.Parse(Base64Url.DecodeFromChars(part)).RootElement;
}
