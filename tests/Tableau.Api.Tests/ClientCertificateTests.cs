using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Pipeline;
using Tableau.Api.Tests.Fakes;

namespace Tableau.Api.Tests;

/// <summary>
/// The 2.0 client's connection: the server's certificate validated, one its issuer revoked refused, one whose revocation
/// can't be checked accepted; <see cref="TableauClientOptions.DangerousAcceptAnyServerCertificate"/> turning the check
/// off with a warning; plain http refused unless allowed; and no cookie kept between requests. The revocation rows run
/// against a certificate authority made for the test, its revocation list served by a second fake server, so they show
/// the same on Windows and Linux.
/// </summary>
[TestClass]
public sealed class ClientCertificateTests
{
    [TestMethod]
    public async Task AnUntrustedCertificateIsRefusedAndNothingIsSent()
    {
        using var server = FakeTableauServer.StartTls();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        await using var client = ClientFixtures.Client(server);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Server.GetInfoAsync());

        Assert.IsInstanceOfType<HttpRequestException>(thrown.InnerException);
        Assert.IsInstanceOfType<AuthenticationException>(thrown.InnerException.InnerException);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task TheOptOutConnectsToAnUntrustedCertificateAndWarns()
    {
        using var server = FakeTableauServer.StartTls();
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        var options = ClientFixtures.Options(server);
        options.DangerousAcceptAnyServerCertificate = true;
        var log = new RecordingLogger();
        await using var client = TableauClient.Create(options, log.Factory);

        await client.Server.GetInfoAsync();

        Assert.HasCount(1, server.Requests);
        Assert.Contains("DangerousAcceptAnyServerCertificate is set, so any server can pose as", log.At(LogLevel.Warning).Single());
    }

    [TestMethod]
    public async Task PlainHttpIsRefusedUnlessAllowedAndThenWarned()
    {
        var log = new RecordingLogger();
        var refused = new TableauClientOptions { ServerUrl = new Uri("http://tableau.example.com/") };
        var allowed = new TableauClientOptions { ServerUrl = new Uri("http://tableau.example.com/"), AllowInsecureHttp = true };

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => TableauClient.Create(refused, log.Factory));
        await using var client = TableauClient.Create(allowed, log.Factory);

        Assert.Contains("ServerUrl is plain http, which sends passwords and tokens unencrypted", thrown.Message);
        Assert.Contains("AllowInsecureHttp is set, so passwords and tokens go to http://tableau.example.com/ unencrypted",
            log.At(LogLevel.Warning).Single());
    }

    [TestMethod]
    public async Task ACookieOneResponseSetsIsNotSentOnTheNext()
    {
        using var server = FakeTableauServer.Start();
        server.Map("GET", ClientFixtures.ServerInfoPath,
            ClientFixtures.ServerInfo().WithHeader("Set-Cookie", "workgroup_session_id=invented-cookie; Path=/"));
        await using var client = ClientFixtures.Client(server);

        await client.Server.GetInfoAsync();
        await client.Server.GetInfoAsync();

        Assert.IsTrue(server.Requests.All(request => request.Header("Cookie") is null));
    }

    [TestMethod]
    public void TheHandlerFollowsNoRedirectKeepsNoCookiesAndChecksRevocation()
    {
        using var handler = TableauHandler.Create(new TableauClientOptions { ServerUrl = new Uri("https://tableau.example.com/") });

        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.UseCookies);
        Assert.IsNull(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.AreEqual(X509RevocationMode.Online, handler.SslOptions.CertificateChainPolicy!.RevocationMode);
        Assert.AreEqual(X509ChainTrustMode.System, handler.SslOptions.CertificateChainPolicy.TrustMode);
        Assert.AreEqual(TableauHandler.RevocationTimeout, handler.SslOptions.CertificateChainPolicy.UrlRetrievalTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(10), TableauHandler.RevocationTimeout);
        Assert.AreEqual(TableauHandler.PooledConnectionLifetime, handler.PooledConnectionLifetime);
    }

    // The serial's first byte at 0x80 and above is where a serial read as signed turns negative, which a revocation list
    // built from the serial's bytes rather than the certificate would then name wrongly.
    [TestMethod]
    [DataRow((byte)0x01)]
    [DataRow((byte)0x7F)]
    [DataRow((byte)0x80)]
    [DataRow((byte)0xFF)]
    public async Task ARevokedCertificateIsRefused(byte firstSerialByte)
    {
        using var authority = new TestAuthority();
        using var crl = FakeTableauServer.Start();
        var crlUrl = TestAuthority.CrlUrl(crl);
        using var leaf = authority.Leaf(crlUrl, firstSerialByte);
        authority.ServeCrl(crl, crlUrl, revoked: leaf);
        using var server = FakeTableauServer.StartTls(leaf);
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        using var handler = authority.Handler(server);
        using var http = new HttpClient(handler, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server), Fixtures.Log.Factory);

        var thrown = await ClientFixtures.FailsAsync(TableauErrorKind.Http, () => client.Server.GetInfoAsync());

        Assert.IsInstanceOfType<AuthenticationException>(thrown.InnerException?.InnerException);
        Assert.IsEmpty(server.Requests);
        Assert.IsNotEmpty(crl.Requests, "the revocation list was never asked for");
    }

    [TestMethod]
    [DataRow((byte)0x01)]
    [DataRow((byte)0x80)]
    public async Task ACertificateItsAuthorityListsAsGoodIsAccepted(byte firstSerialByte)
    {
        using var authority = new TestAuthority();
        using var crl = FakeTableauServer.Start();
        var crlUrl = TestAuthority.CrlUrl(crl);
        using var leaf = authority.Leaf(crlUrl, firstSerialByte);
        authority.ServeCrl(crl, crlUrl, revoked: null);
        using var server = FakeTableauServer.StartTls(leaf);
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        using var handler = authority.Handler(server);
        using var http = new HttpClient(handler, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server), Fixtures.Log.Factory);

        await client.Server.GetInfoAsync();

        Assert.HasCount(1, server.Requests);
        Assert.IsNotEmpty(crl.Requests, "the revocation list was never asked for");
    }

    [TestMethod]
    public async Task ACertificateWhoseRevocationListCantBeReachedIsAccepted()
    {
        using var authority = new TestAuthority();
        using var server = FakeTableauServer.StartTls(authority.Leaf($"http://127.0.0.1:{ClientFixtures.ClosedPort()}/{Guid.NewGuid():N}.crl", 0x01));
        server.Map("GET", ClientFixtures.ServerInfoPath, ClientFixtures.ServerInfo());
        using var handler = authority.Handler(server);
        using var http = new HttpClient(handler, disposeHandler: false);
        await using var client = new TableauClient(http, ClientFixtures.Options(server), Fixtures.Log.Factory);

        await client.Server.GetInfoAsync();

        Assert.HasCount(1, server.Requests);
    }

    /// <summary>A certificate authority for one test: its root, a leaf for 127.0.0.1, and its revocation list.</summary>
    internal sealed class TestAuthority : IDisposable
    {
        private readonly RSA _key = RSA.Create(2048);
        private readonly X509Certificate2 _root;

        public TestAuthority()
        {
            var request = new CertificateRequest("CN=Tableau.Api tests authority", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            var now = DateTimeOffset.UtcNow;
            _root = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(1));
        }

        /// <summary>The authority's own certificate, which a client trusts to trust its leaves.</summary>
        public X509Certificate2 Root => _root;

        /// <summary>
        /// Where server will serve the authority's revocation list: a path no other run has used, so no cache of a list
        /// from an earlier run answers for this one.
        /// </summary>
        public static string CrlUrl(FakeTableauServer server) => new Uri(server.BaseUrl, $"/{Guid.NewGuid():N}.crl").ToString();

        /// <summary>
        /// Serves the authority's revocation list at crlUrl, naming the certificate revoked, if one is given, by its own
        /// serial number.
        /// </summary>
        public void ServeCrl(FakeTableauServer server, string crlUrl, X509Certificate2? revoked)
        {
            var builder = new CertificateRevocationListBuilder();
            if (revoked is not null)
                builder.AddEntry(revoked, DateTimeOffset.UtcNow.AddMinutes(-1));
            var now = DateTimeOffset.UtcNow;
            var crl = builder.Build(_root, BigInteger.One, now.AddHours(1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, now.AddMinutes(-1));
            server.Map("GET", new Uri(crlUrl).AbsolutePath, FakeResponse.Bytes(200, "application/pkix-crl", crl));
        }

        /// <summary>
        /// A certificate for 127.0.0.1 that names crlUrl as where its revocation is listed, its serial eight bytes that
        /// start with firstSerialByte.
        /// </summary>
        public X509Certificate2 Leaf(string crlUrl, byte firstSerialByte)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_root, true, false));
            request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([crlUrl]));
            var serial = RandomNumberGenerator.GetBytes(8);
            serial[0] = firstSerialByte;
            var now = DateTimeOffset.UtcNow;
            using var issued = request.Create(_root, now.AddMinutes(-5), now.AddMinutes(50), serial);
            using var withKey = issued.CopyWithPrivateKey(key);
            // Windows' TLS stack can't use a key that lives only in memory, so the certificate goes through a PFX.
            return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
        }

        /// <summary>The library's handler, trusting this authority alone, with revocation checked as the library checks it.</summary>
        public SocketsHttpHandler Handler(FakeTableauServer server)
        {
            var policy = TableauHandler.CertificatePolicy();
            policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            policy.CustomTrustStore.Add(_root);
            return TableauHandler.Create(ClientFixtures.Options(server), policy);
        }

        public void Dispose()
        {
            _root.Dispose();
            _key.Dispose();
        }
    }
}
