using System;
using System.Globalization;
using System.Linq;

using Jellyfin.Extensions;

using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

using Tvdb.Sdk;

namespace Jellyfin.Plugin.Tvdb;

public static class TvdbSdkExtensions
{
    private static string[]? FallbackLanguages => TvdbPlugin.Instance?.Configuration.FallbackLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? GetTranslatedNamedOrDefault(this TranslationExtended? translations, string? language)
    {
        return translations?
            .NameTranslations?
            .FirstOrDefault(translation => IsMatch(translation.Language, language) && translation.IsAlias != true)?
            .Name
            ?? FallbackLanguages?
            .Select(lang => translations?
                .NameTranslations?
                .FirstOrDefault(translation => IsMatch(translation.Language, lang) && translation.IsAlias != true)?
                .Name)
            .FirstOrDefault(name => name != null);
    }

    public static string? GetTranslatedNamedOrDefaultIgnoreAliasProperty(this TranslationExtended? translations, string? language)
    {
        return translations?
            .NameTranslations?
            .FirstOrDefault(translation => IsMatch(translation.Language, language))?
            .Name
            ?? FallbackLanguages?
            .Select(lang => translations?
                .NameTranslations?
                .FirstOrDefault(translation => IsMatch(translation.Language, lang))?
                .Name)
            .FirstOrDefault(name => name != null);
    }

    public static string? GetTranslatedNamedOrDefault(this TranslationSimple? translations, string? language)
    {
        return translations?
            .FirstOrDefault(translation => IsMatch(translation.Key, language))
            .Value
            ?? FallbackLanguages?
            .Select(lang => translations?
                .FirstOrDefault(translation => IsMatch(translation.Key, lang))
                .Value)
            .FirstOrDefault(name => name != null);
    }

    public static string? GetTranslatedOverviewOrDefault(this TranslationExtended? translations, string? language)
    {
        return translations?
            .OverviewTranslations?
            .FirstOrDefault(translation => IsMatch(translation.Language, language))?
            .Overview
            ?? FallbackLanguages?
            .Select(lang => translations?
                .OverviewTranslations?
                .FirstOrDefault(translation => IsMatch(translation.Language, lang))?
            .Overview)
            .FirstOrDefault(overview => overview != null);
    }

    private static bool IsMatch(this string translation, string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        var mappedlanguage = language?.ToLowerInvariant() switch
        {
            "zh-tw" => "zhtw",
            "pt-br" => "pt",
            "pt-pt" => "por",
            _ => null,
        };

        if (mappedlanguage is not null)
        {
            return translation.Equals(mappedlanguage, StringComparison.OrdinalIgnoreCase);
        }

        return TvdbCultureInfo.GetCultureInfo(language!)?
            .ThreeLetterISOLanguageNames?
            .Contains(translation, StringComparer.OrdinalIgnoreCase)
            ?? false;
    }

    private static string? NormalizeToJellyfin(this Language? language)
    {
        return language?.Id?.ToLowerInvariant() switch
        {
            "zhtw" => "zh-TW",
            "pt" => "pt-BR",
            "por" => "pt-PT",
            var tvdbLang when tvdbLang is { } => TvdbCultureInfo.GetCultureInfo(tvdbLang)?.TwoLetterISOLanguageName,
            _ => null,
        };
    }

    public static ImageType? GetImageType(this ArtworkType? artworkType)
    {
        return artworkType?.Name?.ToLowerInvariant() switch
        {
            "poster" => ImageType.Primary,
            "banner" => ImageType.Banner,
            "background" => ImageType.Backdrop,
            "clearlogo" => ImageType.Logo,
            "clearart" => ImageType.Art,
            _ => null,
        };
    }

    public static RemoteImageInfo? CreateImageInfo(this EpisodeExtendedRecord episodeRecord, string providerName)
    {
        if (string.IsNullOrEmpty(episodeRecord.Image))
        {
            return null;
        }

        return new RemoteImageInfo
        {
            ProviderName = providerName,
            Url = episodeRecord.Image,
            Type = ImageType.Primary
        };
    }

    public static RemoteImageInfo? CreateImageInfo(this ArtworkExtendedRecord artworkRecord, string providerName, ImageType? type, Language? language)
    {
        return CreateRemoteImageInfo(
            artworkRecord.Image,
            artworkRecord.Thumbnail,
            (artworkRecord.Width, artworkRecord.Height),
            providerName,
            type,
            language);
    }

    public static RemoteImageInfo? CreateImageInfo(this ArtworkBaseRecord artworkRecord, string providerName, ImageType? type, Language? language)
    {
        return CreateRemoteImageInfo(
            artworkRecord.Image,
            artworkRecord.Thumbnail,
            (artworkRecord.Width, artworkRecord.Height),
            providerName,
            type,
            language);
    }

    private static RemoteImageInfo? CreateRemoteImageInfo(string imageUrl, string thumbnailUrl, (long? Width, long? Height) imageDimension, string providerName, ImageType? type, Language? language)
    {
        if (type is null)
        {
            return null;
        }

        return new RemoteImageInfo
        {
            RatingType = RatingType.Score,
            Url = imageUrl,
            Width = Convert.ToInt32(imageDimension.Width, CultureInfo.InvariantCulture),
            Height = Convert.ToInt32(imageDimension.Height, CultureInfo.InvariantCulture),
            Type = type.Value,
            Language = language.NormalizeToJellyfin()?.ToLowerInvariant(),
            ProviderName = providerName,
            ThumbnailUrl = thumbnailUrl
        };
    }
}
