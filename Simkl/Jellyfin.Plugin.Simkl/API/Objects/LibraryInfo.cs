using System;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    public class LibraryInfo
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string LibraryType { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;
    }
}
