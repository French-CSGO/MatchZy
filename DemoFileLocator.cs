namespace MatchZy;

/// <summary>
/// Where a match demo is written and where to look for it again. Kept free of
/// CounterStrikeSharp so it can be unit tested.
/// </summary>
public static class DemoFileLocator
{
    /// <summary>
    /// matchzy_demo_path relative to csgo/, always ending in '/' (or empty for csgo/ itself).
    /// A demo path without a trailing '/' no longer runs into the file name.
    /// </summary>
    public static string NormalizeDemoPath(string? demoPath)
    {
        string p = (demoPath ?? "").Trim().Replace('\\', '/');
        while (p.StartsWith("/")) p = p.Substring(1);
        if (p == "" || p == ".") return "";
        return p.EndsWith("/") ? p : p + "/";
    }

    /// <summary>
    /// The absolute path tv_record is given: &lt;game&gt;/csgo/&lt;demo path&gt;&lt;file name&gt;, with '/'
    /// separators on every OS (Windows accepts them).
    ///
    /// With a relative path, tv_record writes to the first writable Game search path. Metamod puts
    /// csgo/addons/metamod first in gameinfo.gi, so demos landed in csgo/addons/metamod/MatchZy/
    /// while the plugin logged, and looked for them in, csgo/MatchZy/.
    /// </summary>
    public static string TvRecordPath(string gameDirectory, string? demoPath, string fileName)
    {
        string game = (gameDirectory ?? "").Replace('\\', '/').TrimEnd('/');
        return $"{game}/csgo/{NormalizeDemoPath(demoPath)}{fileName}";
    }

    /// <summary>
    /// The tv_record argument for <paramref name="path"/>: quoted only when it contains a space
    /// (a Windows install under "Program Files").
    /// </summary>
    public static string TvRecordArgument(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    /// <summary>
    /// Paths where the demo may be, most likely first: the absolute path given to tv_record, then
    /// csgo/addons/metamod/&lt;relative path&gt; where demos recorded with a relative path by older
    /// builds ended up.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(string gameDirectory, string relativeDemoFile)
    {
        string game = (gameDirectory ?? "").Replace('\\', '/').TrimEnd('/');
        string rel = (relativeDemoFile ?? "").Replace('\\', '/').TrimStart('/');
        return new List<string>
        {
            $"{game}/csgo/{rel}",
            $"{game}/csgo/addons/metamod/{rel}",
        };
    }
}
