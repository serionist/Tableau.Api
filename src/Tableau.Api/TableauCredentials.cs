using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>
/// What a <see cref="TableauClient"/> signs in with: a personal access token, a connected app's direct trust, a JWT, a
/// unified access token or a password. Make them with the static methods, and give them to
/// <see cref="TableauClient.Create"/>, the client's constructor, <see cref="TableauClientBuilder.UseCredentials(TableauCredentials)"/>,
/// or <see cref="SessionsClient.SignInAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each secret can come from a delegate, which the client calls at each sign-in, so a vault can hand it over when it is
/// needed and rotate it between sign-ins. Nothing here keeps a session: the client signs in when a call needs it.
/// </para>
/// <para>
/// <see cref="ToString"/> names the kind and the user, never a secret. Tableau allows a personal access token one session
/// at a time: a second sign-in with it, from this client or any other process, ends the first. Give each process its own
/// token, or a connected app.
/// </para>
/// </remarks>
public abstract class TableauCredentials
{
    private protected TableauCredentials()
    {
    }

    /// <summary>
    /// A personal access token: its name and its secret. Optionally impersonating another user, by that user's LUID, which
    /// only Tableau Server allows, and only to a server administrator's token.
    /// </summary>
    /// <param name="name">The token's name, which the session's <see cref="TableauSession.UserName"/> takes.</param>
    /// <param name="secret">The token's secret.</param>
    /// <param name="impersonateUserId">The LUID of the user to act as, or null. Refused before sending on Tableau Cloud.</param>
    /// <exception cref="ArgumentException">A name or a secret is empty, or <paramref name="impersonateUserId"/> is no LUID.</exception>
    public static TableauCredentials PersonalAccessToken(string name, string secret, string? impersonateUserId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return PersonalAccessToken(name, Fixed(secret), impersonateUserId);
    }

    /// <summary>A personal access token whose secret <paramref name="secret"/> gives at each sign-in, such as from a vault.</summary>
    /// <param name="name">The token's name, which the session's <see cref="TableauSession.UserName"/> takes.</param>
    /// <param name="secret">Gives the token's secret; called at each sign-in, with the sign-in's token.</param>
    /// <param name="impersonateUserId">The LUID of the user to act as, or null. Refused before sending on Tableau Cloud.</param>
    /// <exception cref="ArgumentException">The name is empty, or <paramref name="impersonateUserId"/> is no LUID.</exception>
    public static TableauCredentials PersonalAccessToken(string name, Func<CancellationToken, ValueTask<string>> secret,
        string? impersonateUserId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(secret);
        return new PersonalAccessTokenCredentials(name, secret, Luid(impersonateUserId, nameof(impersonateUserId)));
    }

    /// <summary>
    /// A connected app's direct trust: the client mints a JWT at each sign-in, signed with the secret's value by
    /// HMAC-SHA256, valid for five minutes, for the user named.
    /// </summary>
    /// <remarks>
    /// The JWT's header holds <c>alg</c> HS256, <c>kid</c> (the secret's id) and <c>iss</c> (the app's client id); its
    /// claims are <c>iss</c>, <c>exp</c>, a new <c>jti</c> each time, <c>aud</c> "tableau", <c>sub</c> and <c>scp</c>,
    /// the scopes as a list. Tableau refuses a JWT more than ten minutes from expiring, so the client's clock may be five
    /// minutes off the server's either way. Signing in and out needs no scope from Tableau 2023.2; each call the session
    /// makes needs its own, such as <c>tableau:content:read</c> to read projects and workbooks.
    /// </remarks>
    /// <param name="clientId">The connected app's client id.</param>
    /// <param name="secretId">The id of the app's secret.</param>
    /// <param name="secretValue">The secret's value, the key the JWT is signed with.</param>
    /// <param name="userName">
    /// The user the session acts as, the JWT's <c>sub</c>: the user's name on Tableau Server, the email address on Tableau
    /// Cloud.
    /// </param>
    /// <param name="scopes">The scopes the session's calls need, the JWT's <c>scp</c>.</param>
    /// <exception cref="ArgumentException">A value is empty, or a scope is.</exception>
    public static TableauCredentials ConnectedAppDirectTrust(string clientId, string secretId, string secretValue, string userName,
        IEnumerable<string> scopes)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretValue);
        return ConnectedAppDirectTrust(clientId, secretId, Fixed(secretValue), userName, scopes);
    }

    /// <summary>A connected app's direct trust whose secret value <paramref name="secretValue"/> gives at each sign-in.</summary>
    /// <inheritdoc cref="ConnectedAppDirectTrust(string, string, string, string, IEnumerable{string})" path="/remarks"/>
    /// <param name="clientId">The connected app's client id.</param>
    /// <param name="secretId">The id of the app's secret.</param>
    /// <param name="secretValue">Gives the secret's value; called at each sign-in, with the sign-in's token.</param>
    /// <param name="userName">
    /// The user the session acts as, the JWT's <c>sub</c>: the user's name on Tableau Server, the email address on Tableau
    /// Cloud.
    /// </param>
    /// <param name="scopes">The scopes the session's calls need, the JWT's <c>scp</c>.</param>
    /// <exception cref="ArgumentException">A value is empty, or a scope is.</exception>
    public static TableauCredentials ConnectedAppDirectTrust(string clientId, string secretId,
        Func<CancellationToken, ValueTask<string>> secretValue, string userName, IEnumerable<string> scopes)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentException.ThrowIfNullOrEmpty(secretId);
        ArgumentNullException.ThrowIfNull(secretValue);
        ArgumentException.ThrowIfNullOrEmpty(userName);
        ArgumentNullException.ThrowIfNull(scopes);
        string[] list = [.. scopes];
        if (list.Any(string.IsNullOrEmpty))
            throw new ArgumentException("A scope is null or empty.", nameof(scopes));
        return new DirectTrustCredentials(clientId, secretId, secretValue, userName, list);
    }

    /// <summary>
    /// A JWT minted elsewhere, such as by a connected app's OAuth 2.0 trust or an identity provider, which
    /// <paramref name="token"/> gives at each sign-in. A JWT expires within minutes, so it is asked for each time.
    /// </summary>
    /// <param name="token">Gives the JWT; called at each sign-in, with the sign-in's token.</param>
    public static TableauCredentials Jwt(Func<CancellationToken, ValueTask<string>> token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new JwtCredentials("Jwt", token, Endpoints.SignInWithJwt, unifiedAccessToken: false);
    }

    /// <summary>
    /// A unified access token's JWT, which <paramref name="token"/> gives at each sign-in: Tableau Cloud only, from REST API
    /// 3.27, signed in with <c>isUat</c>. Refused before sending on Tableau Server or below 3.27.
    /// </summary>
    /// <param name="token">Gives the JWT; called at each sign-in, with the sign-in's token.</param>
    public static TableauCredentials UnifiedAccessToken(Func<CancellationToken, ValueTask<string>> token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new JwtCredentials("UnifiedAccessToken", token, Endpoints.SignInWithUnifiedAccessToken, unifiedAccessToken: true);
    }

    /// <summary>
    /// A user's name and password. Optionally impersonating another user, by that user's LUID, which only Tableau Server
    /// allows, and only to a server administrator. Tableau Cloud takes no password when multi-factor authentication is on.
    /// </summary>
    /// <param name="name">The user's name.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="impersonateUserId">The LUID of the user to act as, or null. Refused before sending on Tableau Cloud.</param>
    /// <exception cref="ArgumentException">A name or a password is empty, or <paramref name="impersonateUserId"/> is no LUID.</exception>
    public static TableauCredentials Password(string name, string password, string? impersonateUserId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return Password(name, Fixed(password), impersonateUserId);
    }

    /// <summary>A user's name and a password <paramref name="password"/> gives at each sign-in, such as from a vault.</summary>
    /// <param name="name">The user's name.</param>
    /// <param name="password">Gives the password; called at each sign-in, with the sign-in's token.</param>
    /// <param name="impersonateUserId">The LUID of the user to act as, or null. Refused before sending on Tableau Cloud.</param>
    /// <exception cref="ArgumentException">The name is empty, or <paramref name="impersonateUserId"/> is no LUID.</exception>
    public static TableauCredentials Password(string name, Func<CancellationToken, ValueTask<string>> password,
        string? impersonateUserId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(password);
        return new PasswordCredentials(name, password, Luid(impersonateUserId, nameof(impersonateUserId)));
    }

    /// <summary>The kind and the user, such as <c>PersonalAccessToken (nightly-export)</c>; never a secret.</summary>
    public override string ToString() => Shown is null ? Kind : $"{Kind} ({Shown})";

    /// <summary>The kind, as a log line and <see cref="ToString"/> name it.</summary>
    internal abstract string Kind { get; }

    /// <summary>Who <see cref="ToString"/> names: the user, or the token's name; never a secret.</summary>
    private protected abstract string? Shown { get; }

    /// <summary>The sign-in these credentials make, whose gates refuse a kind the server lacks before any secret is read.</summary>
    internal abstract Endpoint Endpoint { get; }

    /// <summary>
    /// The sign-in's body and the name the session takes, the secret read from its delegate. Throws
    /// <see cref="TableauException"/> (<see cref="TableauErrorKind.Authentication"/>) for a delegate that gave nothing.
    /// </summary>
    internal abstract ValueTask<(Credentials Body, string? UserName)> SignInAsync(string site, TimeProvider time,
        CancellationToken cancellationToken);

    private static Func<CancellationToken, ValueTask<string>> Fixed(string secret) => _ => ValueTask.FromResult(secret);

    private static string? Luid(string? value, string name)
    {
        if (value is null) return null;
        if (!(Guid.TryParseExact(value, "D", out var luid) && string.Equals(luid.ToString("D"), value, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"'{value}' is no user's LUID: give one, such as 7c1d2e3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f, or null.", name);
        return value;
    }

    /// <summary>A secret a delegate gave, which must be text; the kind names what it is in the exception.</summary>
    private protected static async ValueTask<string> ReadAsync(Func<CancellationToken, ValueTask<string>> secret, string what,
        CancellationToken cancellationToken)
    {
        string? value;
        try
        {
            value = await secret(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new TableauException(TableauErrorKind.Authentication,
                $"The delegate that gives the {what} failed, so the client couldn't sign in: {e.GetType().Name}.", e);
        }
        return string.IsNullOrEmpty(value)
            ? throw new TableauException(TableauErrorKind.Authentication,
                $"The delegate that gives the {what} gave none, so the client couldn't sign in.")
            : value;
    }

    private static User? AsUser(string? id) => id is null ? null : new User { Id = id };

    private sealed class PersonalAccessTokenCredentials(string name, Func<CancellationToken, ValueTask<string>> secret,
        string? impersonateUserId)
        : TableauCredentials
    {
        internal override string Kind => "PersonalAccessToken";

        private protected override string? Shown => impersonateUserId is null ? name : $"{name}, as {impersonateUserId}";

        internal override Endpoint Endpoint => impersonateUserId is null
            ? Endpoints.SignInWithPersonalAccessToken
            : Endpoints.SignInWithPersonalAccessTokenImpersonating;

        internal override async ValueTask<(Credentials Body, string? UserName)> SignInAsync(string site, TimeProvider time,
            CancellationToken cancellationToken)
        {
            var value = await ReadAsync(secret, "personal access token's secret", cancellationToken).ConfigureAwait(false);
            return (new Credentials
            {
                PersonalAccessTokenName = name,
                PersonalAccessTokenSecret = value,
                Site = new Site { ContentUrl = site },
                User = AsUser(impersonateUserId),
            }, name);
        }
    }

    private sealed class PasswordCredentials(string name, Func<CancellationToken, ValueTask<string>> password, string? impersonateUserId)
        : TableauCredentials
    {
        internal override string Kind => "Password";

        private protected override string? Shown => impersonateUserId is null ? name : $"{name}, as {impersonateUserId}";

        internal override Endpoint Endpoint => impersonateUserId is null ? Endpoints.SignIn : Endpoints.SignInImpersonating;

        internal override async ValueTask<(Credentials Body, string? UserName)> SignInAsync(string site, TimeProvider time,
            CancellationToken cancellationToken)
        {
            var value = await ReadAsync(password, "password", cancellationToken).ConfigureAwait(false);
            return (new Credentials
            {
                Name = name,
                Password = value,
                Site = new Site { ContentUrl = site },
                User = AsUser(impersonateUserId),
            }, name);
        }
    }

    private sealed class DirectTrustCredentials(string clientId, string secretId, Func<CancellationToken, ValueTask<string>> secretValue,
        string userName, string[] scopes)
        : TableauCredentials
    {
        internal override string Kind => "ConnectedAppDirectTrust";

        private protected override string? Shown => userName;

        internal override Endpoint Endpoint => Endpoints.SignInWithJwt;

        internal override async ValueTask<(Credentials Body, string? UserName)> SignInAsync(string site, TimeProvider time,
            CancellationToken cancellationToken)
        {
            var key = await ReadAsync(secretValue, "connected app's secret value", cancellationToken).ConfigureAwait(false);
            var jwt = ConnectedAppJwt.Mint(clientId, secretId, key, userName, scopes, time.GetUtcNow());
            return (new Credentials { Jwt = jwt, Site = new Site { ContentUrl = site } }, userName);
        }
    }

    private sealed class JwtCredentials(string kind, Func<CancellationToken, ValueTask<string>> token, Endpoint endpoint,
        bool unifiedAccessToken)
        : TableauCredentials
    {
        internal override string Kind => kind;

        private protected override string? Shown => null;

        internal override Endpoint Endpoint => endpoint;

        internal override async ValueTask<(Credentials Body, string? UserName)> SignInAsync(string site, TimeProvider time,
            CancellationToken cancellationToken)
        {
            var jwt = await ReadAsync(token, unifiedAccessToken ? "unified access token" : "JWT", cancellationToken)
                .ConfigureAwait(false);
            return (new Credentials
            {
                Jwt = jwt,
                IsUat = unifiedAccessToken ? true : null,
                Site = new Site { ContentUrl = site },
            }, ConnectedAppJwt.Subject(jwt));
        }
    }
}

/// <summary>A connected app's direct-trust JWT, minted with the BCL's HMAC-SHA256: no JWT package.</summary>
internal static class ConnectedAppJwt
{
    /// <summary>
    /// How long a minted JWT is valid. Tableau refuses one valid for more than ten minutes, so five leaves the client's
    /// clock five minutes either side of the server's.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>The JWT: its header, its claims and its HS256 signature, each base64url without padding.</summary>
    public static string Mint(string clientId, string secretId, string secretValue, string userName, IReadOnlyList<string> scopes,
        DateTimeOffset now)
    {
        var header = Json(writer =>
        {
            writer.WriteString("alg", "HS256");
            writer.WriteString("typ", "JWT");
            writer.WriteString("kid", secretId);
            writer.WriteString("iss", clientId);
        });
        var claims = Json(writer =>
        {
            writer.WriteString("iss", clientId);
            writer.WriteNumber("exp", (now + Lifetime).ToUnixTimeSeconds());
            writer.WriteString("jti", Guid.NewGuid().ToString("D"));
            writer.WriteString("aud", "tableau");
            writer.WriteString("sub", userName);
            writer.WriteStartArray("scp");
            foreach (var scope in scopes)
                writer.WriteStringValue(scope);
            writer.WriteEndArray();
        });
        var signed = Base64Url.EncodeToString(header) + "." + Base64Url.EncodeToString(claims);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretValue), Encoding.ASCII.GetBytes(signed));
        return signed + "." + Base64Url.EncodeToString(signature);
    }

    /// <summary>
    /// A JWT's <c>sub</c> claim, read without checking the signature, which is the server's to check; null when the
    /// token is no JWT whose claims say one.
    /// </summary>
    public static string? Subject(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return claims.RootElement.ValueKind == JsonValueKind.Object
                && claims.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String
                    ? sub.GetString()
                    : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
