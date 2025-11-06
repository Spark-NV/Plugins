using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Tvdb.SeasonClient
{
    public interface IExtendedSeasonClient
    {
        System.Threading.Tasks.Task<Response99> GetSeasonExtendedWithTranslationsAsync(double id, System.Threading.CancellationToken cancellationToken = default);
    }
}
