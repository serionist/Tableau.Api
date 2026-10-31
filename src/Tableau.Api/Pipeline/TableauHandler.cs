using System;
using System.Collections.Generic;
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
    /// certificate, revoked ones refused, by the library's chain policy or one the caller's settings gave it. No
    /// certificate callback of the caller's turns the check off; only <see cref="TableauClientOptions.DangerousAcceptAnyServerCertificate"/>
    /// does, though a chain policy the caller set is used as set, and its flags can loosen the check.
    /// </summary>
    /// <param name="options">The client's options.</param>
    /// <param name="configure">The caller's own settings, from <c>ConfigurePrimaryHandler</c>, in the order given.</param>
    public static SocketsHttpHandler Create(TableauClientOptions options, IEnumerable<Action<SocketsHttpHandler>>? configure = null) =>
        Create(options, CertificatePolicy(), configure);

    /// <summary>
    /// The same, with the certificate policy given, which the tests point at a certificate authority of their own. The
    /// handler starts with the library's settings; the caller's actions run on it; then the library's rules are set
    /// again, whatever the actions did: no redirects, no cookies, and the certificate check the options ask for, with the
    /// policy given where the actions left none. The rules are set on a copy of the TLS options the handler holds alone:
    /// options the actions gave it may be shared, with another client's handler or an app's own, which the rules of this
    /// client must not change.
    /// </summary>
    [SuppressMessage("Security", "CA5359:Do Not Disable Certificate Validation", Justification = "Only when the caller sets "
        + "DangerousAcceptAnyServerCertificate, which asks for exactly this; the client logs a warning when it is built.")]
    public static SocketsHttpHandler Create(TableauClientOptions options, X509ChainPolicy policy,
        IEnumerable<Action<SocketsHttpHandler>>? configure = null)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = PooledConnectionLifetime,
            SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy },
        };
        try
        {
            foreach (var action in configure ?? [])
                action(handler);
            handler.AllowAutoRedirect = false;
            handler.UseCookies = false;
            var ssl = Copy(handler.SslOptions);
            ssl.CertificateChainPolicy ??= policy;
            ssl.RemoteCertificateValidationCallback = options.DangerousAcceptAnyServerCertificate
                ? static (_, _, _, _) => true
                : null;
            handler.SslOptions = ssl;
            return handler;
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A copy of TLS options, member by member, as the runtime's own internal clone copies them: the list of protocols a
    /// list of its own, the rest as they are. A test counts the members, so a .NET release that adds one fails it.
    /// </summary>
    public static SslClientAuthenticationOptions Copy(SslClientAuthenticationOptions source)
    {
        var copy = new SslClientAuthenticationOptions
        {
            AllowRenegotiation = source.AllowRenegotiation,
            AllowTlsResume = source.AllowTlsResume,
            ApplicationProtocols = source.ApplicationProtocols is null ? null : [.. source.ApplicationProtocols],
            CertificateChainPolicy = source.CertificateChainPolicy,
            CertificateRevocationCheckMode = source.CertificateRevocationCheckMode,
            CipherSuitesPolicy = source.CipherSuitesPolicy,
            ClientCertificateContext = source.ClientCertificateContext,
            ClientCertificates = source.ClientCertificates,
            EnabledSslProtocols = source.EnabledSslProtocols,
            EncryptionPolicy = source.EncryptionPolicy,
            LocalCertificateSelectionCallback = source.LocalCertificateSelectionCallback,
            RemoteCertificateValidationCallback = source.RemoteCertificateValidationCallback,
            TargetHost = source.TargetHost,
        };
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            copy.AllowRsaPssPadding = source.AllowRsaPssPadding;
            copy.AllowRsaPkcs1Padding = source.AllowRsaPkcs1Padding;
        }
        return copy;
    }
    /// <summary>
    /// How long a new connection waits for a revocation list or a responder's answer. One that is reachable answers in
    /// well under a second; one a firewall drops silently would hold every new connection for the platform's own
    /// default, so the wait is bounded here, far inside <see cref="TableauClientOptions.RequestTimeout"/>'s two minutes.
    /// The pool keeps a connection for five minutes, so the wait comes at most that often.
    /// </summary>
    public static readonly TimeSpan RevocationTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The machine's trusted roots, with revocation checked online: a certificate its issuer has revoked is refused.
    /// When no answer comes in <see cref="RevocationTimeout"/>, from a revocation list or a responder the network can't
    /// reach, the status is unknown and tolerated, as a browser does, so a server on a closed network stays reachable.
    /// </summary>
    public static X509ChainPolicy CertificatePolicy() => new()
    {
        RevocationMode = X509RevocationMode.Online,
        UrlRetrievalTimeout = RevocationTimeout,
        RevocationFlag = X509RevocationFlag.ExcludeRoot,
        VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown
            | X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown
            | X509VerificationFlags.IgnoreCtlSignerRevocationUnknown
            | X509VerificationFlags.IgnoreRootRevocationUnknown,
    };
}
