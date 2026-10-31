using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Tableau.Api.Pipeline;

/// <summary>
/// The handler a client's requests go through when the library builds it: from <c>TableauClient.Create</c> and from
/// <c>AddTableauClient</c>'s named HttpClient.
/// </summary>
internal static class TableauHandler
{
    /// <summary>How long a pooled connection lives, so a client that runs for days still follows the server's DNS.</summary>
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A handler that follows no redirect, since the pipeline follows them itself and only to the same origin; keeps no
    /// cookies, so nothing one response sets rides on another request or another session's; and validates the server's
    /// certificate, revoked ones refused, unless the options turn the check off.
    /// </summary>
    public static SocketsHttpHandler Create(TableauClientOptions options) => Create(options, CertificatePolicy());

    /// <summary>The same, with the certificate policy given, which the tests point at a certificate authority of their own.</summary>
    [SuppressMessage("Security", "CA5359:Do Not Disable Certificate Validation", Justification = "Only when the caller sets "
        + "DangerousAcceptAnyServerCertificate, which asks for exactly this; the client logs a warning when it is built.")]
    public static SocketsHttpHandler Create(TableauClientOptions options, X509ChainPolicy policy)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = PooledConnectionLifetime,
            SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy },
        };
        if (options.DangerousAcceptAnyServerCertificate)
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        return handler;
    }

    /// <summary>
    /// The machine's trusted roots, with revocation checked online: a certificate its issuer has revoked is refused.
    /// When no answer comes, from a revocation list or a responder the network can't reach, the status is unknown and
    /// tolerated, as a browser does, so a server on a closed network stays reachable.
    /// </summary>
    public static X509ChainPolicy CertificatePolicy() => new()
    {
        RevocationMode = X509RevocationMode.Online,
        RevocationFlag = X509RevocationFlag.ExcludeRoot,
        VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown
            | X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown
            | X509VerificationFlags.IgnoreCtlSignerRevocationUnknown
            | X509VerificationFlags.IgnoreRootRevocationUnknown,
    };
}
