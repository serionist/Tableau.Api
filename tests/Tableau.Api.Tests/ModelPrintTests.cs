using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// A model's ToString, which a log line or a debugger shows, never holds a password, a token or a secret: each secret
/// member prints as *** when set, and every other member prints as a record prints it.
/// </summary>
[TestClass]
public sealed class ModelPrintTests
{
    // The secrets eng/generate-models.cs lists, by the C# names it gives them.
    private static readonly (Type Type, string Member)[] _secrets =
    [
        (typeof(ApiToken), nameof(ApiToken.Secret)),
        (typeof(ConnectedApplicationSecret), nameof(ConnectedApplicationSecret.Value)),
        (typeof(Connection), nameof(Connection.Password)),
        (typeof(ConnectionCredentials), nameof(ConnectionCredentials.Password)),
        (typeof(Credentials), nameof(Credentials.Jwt)),
        (typeof(Credentials), nameof(Credentials.Password)),
        (typeof(Credentials), nameof(Credentials.PersonalAccessTokenSecret)),
        (typeof(Credentials), nameof(Credentials.Token)),
        (typeof(PersonalAccessTokenWithSecret), nameof(PersonalAccessTokenWithSecret.Secret)),
        (typeof(ServerApiToken), nameof(ServerApiToken.ApiToken)),
        (typeof(ServiceToken), nameof(ServiceToken.TokenSecret)),
        (typeof(SiteOidcConfiguration), nameof(SiteOidcConfiguration.ClientSecret)),
        (typeof(User), nameof(User.Password)),
    ];

    // Members whose names sound like a secret but that hold none: names, ids, flags and where a token comes from.
    private static readonly HashSet<string> _notSecrets = new(StringComparer.Ordinal)
    {
        "CreateServerScimConfigResponse.ApiTokenId", "Credentials.PersonalAccessTokenName", "PersonalAccessToken.TokenGuid",
        "PersonalAccessToken.TokenName", "PersonalAccessTokenWithSecret.TokenGuid",
        "PersonalAccessTokenWithSecret.TokenName", "SalesforceAppList.NextPageToken", "ServiceToken.TokenGuid",
        "ServiceToken.TokenName", "SiteOidcConfiguration.TokenEndpoint",
    };

    [TestMethod]
    public void EverySecretPrintsAsStarsAndTheOtherMembersAsTheyAre()
    {
        var failures = new List<string>();
        foreach (var (type, member) in _secrets)
        {
            var record = Activator.CreateInstance(type)!;
            foreach (var property in type.GetProperties().Where(property => property.PropertyType == typeof(string)))
                property.SetValue(record, property.Name == member ? "invented-secret-value" : $"plain-{property.Name}");

            var printed = record.ToString()!;

            if (printed.Contains("invented-secret-value", StringComparison.Ordinal)) failures.Add($"{type.Name}.{member} prints its value");
            if (!printed.Contains($"{member} = ***", StringComparison.Ordinal)) failures.Add($"{type.Name}.{member} doesn't print ***");
            if (!printed.Contains("= plain-", StringComparison.Ordinal)) failures.Add($"{type.Name} hides its other members: {printed}");
            var unset = Activator.CreateInstance(type)!.ToString()!;
            if (!unset.Contains($"{member} = ,", StringComparison.Ordinal) && !unset.Contains($"{member} =  }}", StringComparison.Ordinal))
                failures.Add($"{type.Name}.{member} prints something when unset: {unset}");
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [TestMethod]
    public void AnUnsetSecretPrintsLikeAnyUnsetMember()
    {
        Assert.AreEqual(
            "Credentials { Site = , User = , EstimatedTimeToExpiration = , IsUat = , Jwt = , Name = ada, Password = , "
            + "PersonalAccessTokenName = , PersonalAccessTokenSecret = , Token =  }",
            new Credentials { Name = "ada" }.ToString());
    }

    [TestMethod]
    public void ARecordWithoutSecretsPrintsAsRecordsDo()
    {
        Assert.AreEqual("Tag { Label = R&D }", new Tag { Label = "R&D" }.ToString());
        Assert.AreEqual("Pagination { PageNumber = 2, PageSize = 100, TotalAvailable =  }",
            new Pagination { PageNumber = 2, PageSize = 100 }.ToString());
    }

    [TestMethod]
    public void ASecretStaysHiddenInsideTheRecordThatHoldsIt()
    {
        var request = new TsRequest { Credentials = new Credentials { Name = "ada", Password = Fixtures.Password } };

        var printed = request.ToString();

        Assert.DoesNotContain(Fixtures.Password, printed);
        Assert.Contains("Password = ***", printed);
    }

    // A new schema's secret, or a member renamed, reaches this test before it reaches a log: a string member named like a
    // secret must be on one list or the other.
    [TestMethod]
    public void EveryMemberNamedLikeASecretIsListed()
    {
        var listed = new HashSet<string>(_secrets.Select(secret => $"{secret.Type.Name}.{secret.Member}"), StringComparer.Ordinal);
        var named = typeof(Credentials).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(Credentials).Namespace)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(string) && property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
                .Where(property => new[] { "Password", "Secret", "Token", "Jwt" }.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(property => $"{type.Name}.{property.Name}"))
            .Order(StringComparer.Ordinal).ToList();

        var unlisted = named.Where(name => !listed.Contains(name) && !_notSecrets.Contains(name)).ToList();

        Assert.IsEmpty(unlisted, $"Mask it in eng/generate-models.cs's _secrets and list it here, or list it as no secret: {string.Join(", ", unlisted)}");
        Assert.IsEmpty(_notSecrets.Where(name => !named.Contains(name)).ToList(), "Take the members that are gone off the list.");
    }
}
