using System;
using System.Linq;
using System.Reflection;

namespace Tableau.Api.Tests;

/// <summary>
/// Sets the process's local time zone, for a test that must run under one that is not UTC wherever the suite runs.
/// .NET reads no setting for it on Windows, so this sets the zone TimeZoneInfo caches; disposing clears the cache, and
/// the machine's zone comes back. A test that uses it changes the whole process, so it is marked DoNotParallelize.
/// </summary>
internal static class LocalTimeZone
{
    public static IDisposable Set(TimeZoneInfo zone)
    {
        TimeZoneInfo.ClearCachedData();
        var cacheField = typeof(TimeZoneInfo).GetField("s_cachedData", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(TimeZoneInfo), "s_cachedData");
        var cache = cacheField.GetValue(null)!;
        var localField = cache.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(TimeZoneInfo)
                && field.Name.IndexOf("local", StringComparison.OrdinalIgnoreCase) >= 0);
        localField.SetValue(cache, zone);
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => TimeZoneInfo.ClearCachedData();
    }
}
