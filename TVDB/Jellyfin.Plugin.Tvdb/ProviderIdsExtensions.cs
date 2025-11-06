using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Tvdb;

internal static class ProviderIdsExtensions
{
    internal static bool IsSupported(this IHasProviderIds? item)
    {
        return HasProviderId(item, MetadataProvider.Tvdb)
               || HasProviderId(item, MetadataProvider.Imdb)
               || HasProviderId(item, MetadataProvider.Zap2It);
    }

    internal static bool IsSupported(this Dictionary<string, string> item)
    {
        return (item.TryGetValue(MetadataProvider.Tvdb.ToString(), out var tvdbId) && !string.IsNullOrEmpty(tvdbId))
               || (item.TryGetValue(MetadataProvider.Imdb.ToString(), out var imdbId) && !string.IsNullOrEmpty(imdbId))
               || (item.TryGetValue(MetadataProvider.Zap2It.ToString(), out var zap2ItId) && !string.IsNullOrEmpty(zap2ItId));
    }

    public static int GetTvdbId(this IHasProviderIds item)
        => Convert.ToInt32(item.GetProviderId(TvdbPlugin.ProviderId), CultureInfo.InvariantCulture);

    public static void SetTvdbId(this IHasProviderIds item, long? value)
        => SetTvdbId(item, value.HasValue && value > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : null);

    public static bool SetTvdbId(this IHasProviderIds item, string? value)
        => SetProviderIdIfHasValue(item, TvdbPlugin.ProviderId, value);

    public static bool SetProviderIdIfHasValue(this IHasProviderIds item, MetadataProvider provider, string? value)
        => SetProviderIdIfHasValue(item, provider.ToString(), value);

    public static bool SetProviderIdIfHasValue(this IHasProviderIds item, string name, string? value)
    {
        if (!HasValue(value))
        {
            return false;
        }

        item.SetProviderId(name, value);
        return true;
    }

    public static bool HasTvdbId(this IHasProviderIds? item)
        => HasTvdbId(item, out var value);

    public static bool HasTvdbId(this IHasProviderIds? item, out string? value)
        => HasProviderId(item, TvdbPlugin.ProviderId, out value);

    public static bool HasProviderId(this IHasProviderIds? item, MetadataProvider provider)
        => HasProviderId(item, provider, out var value);

    public static bool HasProviderId(this IHasProviderIds? item, MetadataProvider provider, out string? value)
        => HasProviderId(item, provider.ToString(), out value);

    public static bool HasProviderId(this IHasProviderIds? item, string name)
        => HasProviderId(item, name, out var value);

    public static bool HasProviderId(this IHasProviderIds? item, string name, out string? value)
    {
        value = null;
        var result = item is { }
            && item.TryGetProviderId(name, out value)
            && HasValue(value);

        value = result ? value : null;
        return result;
    }

    private static bool HasValue([NotNullWhen(true)] string? value)
        => !string.IsNullOrWhiteSpace(value);
}
