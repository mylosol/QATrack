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

    /// <summary>True when <paramref name="value"/> is a valid SemVer 2.0 string.</summary>
    public static bool IsValidSemVer(string? value) => value is not null && SemVerPattern().IsMatch(value);

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
