using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Conversation.Windowing;

/// <summary>
/// LAPIS 2 (02_Modules/Nexus.Conversation):
/// Controls viewport message windowing (20 messages on initial open) so switching conversations is instantaneous.
/// </summary>
public static class TranscriptPageWindowEngine
{
    public const int PageRows = 20;
    public const int LoadMarginPx = 800;

    public static int OpeningFrom(int count) => Math.Max(0, count - PageRows);

    public static int EarlierFrom(int from) => Math.Max(0, from - PageRows);

    public static bool ShouldDrawEarlier(int from, double scrollTop, double scrollHeight) =>
        from > 0 && scrollHeight > 0 && scrollTop < LoadMarginPx;

    public static IReadOnlyList<T> SliceOpeningWindow<T>(IReadOnlyList<T>? allItems, int pageRows = PageRows)
    {
        if (allItems is null || allItems.Count == 0)
            return Array.Empty<T>();
        int rows = Math.Max(1, pageRows);
        int start = Math.Max(0, allItems.Count - rows);
        return allItems.Skip(start).ToList();
    }
}
