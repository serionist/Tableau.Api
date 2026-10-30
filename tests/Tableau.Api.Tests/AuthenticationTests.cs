using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

[TestClass]
public sealed class AuthenticationTests
{
    [TestMethod]
    public async Task SignInPostsTheCredentialsAndReturnsTheSession()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);

        var session = await Fixtures.SignInAsync(server, client);

        var request = server.Requests.Single();
        Assert.AreEqual($"POST /api/{Fixtures.ApiVersion}/auth/signin HTTP/1.1", request.RequestLine);
        Assert.AreEqual("application/xml", request.Header("Content-Type"));
        Assert.IsNull(request.Header("X-Tableau-Auth"));
        var credentials = XElement.Parse(request.BodyText).Elements()
            .Single(element => element.Name.LocalName == "credentials");
        Assert.AreEqual(Fixtures.UserName, (string?)credentials.Attribute("name"));
        Assert.AreEqual(Fixtures.Password, (string?)credentials.Attribute("password"));
        var site = credentials.Elements().Single(element => element.Name.LocalName == "site");
        Assert.AreEqual(Fixtures.SiteContentUrl, (string?)site.Attribute("contentUrl"));

        Assert.AreEqual(Fixtures.SessionToken, session.Token);
        Assert.AreEqual(Fixtures.SiteId, session.SiteId);
        Assert.AreEqual(Fixtures.UserId, session.UserId);
    }

    [TestMethod]
    public async Task SignOutPostsWithTheSessionToken()
    {
        using var server = FakeTableauServer.Start();
        var client = Fixtures.Client(server);
        var session = await Fixtures.SignInAsync(server, client);
        server.Map("POST", $"/api/{Fixtures.ApiVersion}/auth/signout", FakeResponse.Empty(204));

        await client.SignOut(session);

        var request = server.Requests[^1];
        Assert.AreEqual($"POST /api/{Fixtures.ApiVersion}/auth/signout HTTP/1.1", request.RequestLine);
        Assert.AreEqual(Fixtures.SessionToken, request.Header("X-Tableau-Auth"));
    }
}
