using System.Reflection;

namespace AzVmStartStop.Functions;

/// <summary>
/// Exposes build metadata for logging so a log line can be traced back to the
/// exact function version that produced it. The commit SHA is stamped into the
/// assembly's informational version at publish time via
/// <c>-p:SourceRevisionId=&lt;sha&gt;</c> (the .NET SDK appends <c>+&lt;sha&gt;</c>).
/// </summary>
public static class BuildInfo
{
    /// <summary>Short commit SHA of this build, or <c>"unknown"</c> for local/dev builds.</summary>
    public static string CommitSha { get; } = ResolveCommitSha();

    private static string ResolveCommitSha()
    {
        var informationalVersion = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrEmpty(informationalVersion))
        {
            return "unknown";
        }

        // The SDK formats this as "<version>+<SourceRevisionId>".
        var plusIndex = informationalVersion.IndexOf('+');
        if (plusIndex < 0 || plusIndex == informationalVersion.Length - 1)
        {
            return "unknown";
        }

        var sha = informationalVersion[(plusIndex + 1)..];
        return sha.Length > 7 ? sha[..7] : sha;
    }
}
