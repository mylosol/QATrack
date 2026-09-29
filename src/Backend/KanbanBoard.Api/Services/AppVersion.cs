using System.Reflection;
using System.Text.RegularExpressions;

namespace KanbanBoard.Api.Services;

/// <summary>
/// The running application's Semantic Version (https://semver.org).
/// </summary>
/// <remarks>
/// Sourced from the assembly's informational version, which the build derives
/// from the root package.json (see Directory.Build.props) plus the git commit
/// as SemVer build metadata, e.g. <c>1.1.0+1c3f0969b507...</c>.
/// </remarks>
/// <param name="Version">MAJOR.MINOR.PATCH[-prerelease], e.g. "1.1.0".</param>
/// <param name="Commit">Short git commit the build came from, or null.</param>
/// <param name="InformationalVersion">Full SemVer string including build metadata.</param>
public sealed partial record AppVersion(string Version, string? Commit, string InformationalVersion)
{
    /// <summary>Official SemVer 2.0 regex (semver.org), with named groups.</summary>
    [GeneratedRegex(
        @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)" +
        @"(?:-(?<prerelease>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
        @"(?:\+(?<build>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemVerPattern();

    /// <summary>
    /// Canonical build identifier: <c>MAJOR.MINOR.PATCH[-pre]+&lt;7-char commit&gt;</c>, or just
    /// the version when no commit is known. The SPA bakes the same string in at
    /// build time (vite.config.ts); any difference means a different build is deployed.
    /// </summary>
    public string Build => Commit is null ? Version : $"{Version}+{Commit}";

    /// <summary>True when <paramref name="value"/> is a valid SemVer 2.0 string.</summary>
    public static bool IsValidSemVer(string? value) => value is not null && SemVerPattern().IsMatch(value);

    /// <summary>
    /// SemVer precedence of two versions (build metadata ignored): negative when
    /// <paramref name="a"/> is older, 0 when equal, positive when newer. A
    /// pre-release sorts before its release (1.5.0-rc.1 &lt; 1.5.0).
    /// </summary>
    /// <exception cref="FormatException">Either value is not valid SemVer.</exception>
    public static int Compare(string a, string b)
    {
        var x = SemVerPattern().Match(a);
        var y = SemVerPattern().Match(b);
        if (!x.Success || !y.Success)
        {
            throw new FormatException($"'{(x.Success ? b : a)}' is not a valid Semantic Version.");
        }

        foreach (var part in new[] { "major", "minor", "patch" })
        {
            var diff = long.Parse(x.Groups[part].Value).CompareTo(long.Parse(y.Groups[part].Value));
            if (diff != 0)
            {
                return diff;
            }
        }

        var (px, py) = (x.Groups["prerelease"], y.Groups["prerelease"]);
        if (px.Success != py.Success)
        {
            return px.Success ? -1 : 1;
        }

        return px.Success ? ComparePrerelease(px.Value, py.Value) : 0;
    }

    /// <summary>SemVer 2.0 rule 11: dot-separated identifiers, numeric ones compared numerically.</summary>
    private static int ComparePrerelease(string a, string b)
    {
        var xs = a.Split('.');
        var ys = b.Split('.');
        for (var i = 0; i < Math.Min(xs.Length, ys.Length); i++)
        {
            var xNum = long.TryParse(xs[i], out var xn);
            var yNum = long.TryParse(ys[i], out var yn);
            var diff = (xNum, yNum) switch
            {
                (true, true) => xn.CompareTo(yn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(xs[i], ys[i]),
            };
            if (diff != 0)
            {
                return Math.Sign(diff);
            }
        }

        return xs.Length.CompareTo(ys.Length);
    }

    /// <summary>
    /// Parses an informational version such as "1.1.0+abcdef123". The commit is
    /// shortened to 7 characters. Invalid input yields "0.0.0" so the app never
    /// fails to start over version metadata.
    /// </summary>
    public static AppVersion Parse(string? informationalVersion)
    {
        var match = SemVerPattern().Match(informationalVersion ?? string.Empty);
        if (!match.Success)
        {
            return new AppVersion("0.0.0", null, informationalVersion ?? "0.0.0");
        }

        var version = informationalVersion!.Split('+')[0];
        var build = match.Groups["build"].Success ? match.Groups["build"].Value : null;
        var commit = build is null ? null : build.Length > 7 ? build[..7] : build;
        return new AppVersion(version, commit, informationalVersion);
    }

    /// <summary>The version of the running KanbanBoard.Api assembly.</summary>
    public static AppVersion Current { get; } = Parse(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
