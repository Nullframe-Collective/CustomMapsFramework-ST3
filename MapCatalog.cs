namespace CustomMapsFramework
{
    public static class MapCatalog
    {
        private static HashSet<string> _selection;

        public static int ApiVersion => 1;
        public static bool Enabled { get; set; } = true;
        public static bool IsReady => Core.IsInitialized && Core.Error == null;
        public static IReadOnlyList<MapInfo> Installed => Core.CustomMaps
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        public static IReadOnlyList<MapInfo> Items => Installed
            .Where(item => _selection == null || _selection.Contains(item.Id)).ToArray();

        internal static bool Allows(MapInfo item) => Enabled && item?.IsAvailable == true &&
            (_selection == null || _selection.Contains(item.Id));

        public static IDisposable UseContent(string[] ids)
        {
            if (ids == null || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
                ids.Any(id => !Core.CustomMaps.Any(item => item.Id == id && item.IsAvailable)))
            {
                throw new ArgumentException("The required map packs are unavailable.", nameof(ids));
            }

            HashSet<string> previous = _selection;
            HashSet<string> selected = new(ids, StringComparer.Ordinal);
            _selection = selected;
            return new Selection(previous, selected);
        }

        private sealed class Selection : IDisposable
        {
            private readonly HashSet<string> _previous;
            private HashSet<string> _selected;

            internal Selection(HashSet<string> previous, HashSet<string> selected)
            {
                _previous = previous;
                _selected = selected;
            }

            public void Dispose()
            {
                if (_selected != null && ReferenceEquals(_selection, _selected))
                {
                    _selection = _previous;
                }

                _selected = null;
            }
        }
    }
}
