using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Tvdb;

internal static class CollectionExtensions
{
    public static ICollection<T> AddIfNotNull<T>(this ICollection<T> collection, T? item)
    {
        if (item != null)
        {
            collection.Add(item);
        }

        return collection;
    }

    internal static IEnumerable<RemoteImageInfo> OrderByLanguageDescending(this IEnumerable<RemoteImageInfo> remoteImageInfos, string requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage))
        {
            requestedLanguage = "en";
        }

        var isRequestedLanguageEn = string.Equals(requestedLanguage, "en", StringComparison.OrdinalIgnoreCase);

        return remoteImageInfos.OrderByDescending(i =>
        {
            if (string.Equals(requestedLanguage, i.Language, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (!isRequestedLanguageEn && string.Equals(i.Language, "en", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.IsNullOrEmpty(i.Language))
            {
                return 0;
            }

            return 1;
        });
    }
}
