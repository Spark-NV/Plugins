using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Tvdb.Sdk;

namespace Jellyfin.Plugin.Tvdb
{
    public static class TvdbUtils
    {
        public const string TvdbBaseUrl = "https://www.thetvdb.com/";

        private static bool FallbackToOriginalLanguage => TvdbPlugin.Instance?.Configuration.FallbackToOriginalLanguage ?? false;

        public static IEnumerable<DayOfWeek> GetAirDays(SeriesAirsDays seriesAirsDays)
        {
            if (seriesAirsDays.Sunday.GetValueOrDefault())
            {
                yield return DayOfWeek.Sunday;
            }

            if (seriesAirsDays.Monday.GetValueOrDefault())
            {
                yield return DayOfWeek.Monday;
            }

            if (seriesAirsDays.Tuesday.GetValueOrDefault())
            {
                yield return DayOfWeek.Tuesday;
            }

            if (seriesAirsDays.Wednesday.GetValueOrDefault())
            {
                yield return DayOfWeek.Wednesday;
            }

            if (seriesAirsDays.Thursday.GetValueOrDefault())
            {
                yield return DayOfWeek.Thursday;
            }

            if (seriesAirsDays.Friday.GetValueOrDefault())
            {
                yield return DayOfWeek.Friday;
            }

            if (seriesAirsDays.Saturday.GetValueOrDefault())
            {
                yield return DayOfWeek.Saturday;
            }
        }

        public static string? ReturnOriginalLanguageOrDefault(string? text)
        {
            return FallbackToOriginalLanguage ? text : null;
        }

        public static string GetComparableName(string name)
        {
            name = name.ToLowerInvariant();
            name = name.Normalize(NormalizationForm.FormC);
            name = name.Replace(", the", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("the ", " ", StringComparison.OrdinalIgnoreCase)
                .Replace(" the ", " ", StringComparison.OrdinalIgnoreCase);
            name = name.Replace("&", " and ", StringComparison.OrdinalIgnoreCase);
            name = Regex.Replace(name, @"[\p{Lm}\p{Mn}]", string.Empty);
            name = Regex.Replace(name, @"[\W\p{Pc}]+", " ");
            return name.Trim();
        }
    }
}
