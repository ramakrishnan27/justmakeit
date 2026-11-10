using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Platform.Content.Tests;

/// <summary>
/// The dependency rule, which the compiler stopped enforcing when Platform.Auth became a folder of Platform.Core: Core references
/// nothing of ours; the content feature and every channel reference only Core; and none of them uses the Auth folder's
/// namespace. Reads the project files and the sources, so a violation fails here even though it would still compile.
/// </summary>
public sealed class ArchitectureTests
{
    // The feature and channel projects. A folder that is not on disk (a stub someone deleted) has nothing to violate.
    public static TheoryData<string> FeatureProjects() =>
        new() { "Platform.Content", "Platform.Media", "Platform.Email", "Platform.Facebook", "Platform.Instagram", "Platform.YouTube", "Platform.Sms", "Platform.WhatsApp" };

    private static readonly Regex AuthNamespace = new(@"\bPlatform\.Auth\b", RegexOptions.Compiled);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "JustMakeIt.sln")))
            {
                return directory.FullName;
            }
        }

        throw new FileNotFoundException("JustMakeIt.sln was not found above " + AppContext.BaseDirectory);
    }

    private static string[] ProjectReferences(string csproj) =>
        XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(reference => ((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/'))
            .ToArray();

    [Fact]
    public void Core_ReferencesNoProjectOfOurs()
    {
        var core = Path.Combine(RepositoryRoot(), "src", "Platform.Core", "Platform.Core.csproj");

        Assert.Empty(ProjectReferences(core));
    }

    [Theory]
    [MemberData(nameof(FeatureProjects))]
    public void AFeatureOrChannelProject_ReferencesOnlyCore(string project)
    {
        var csproj = Path.Combine(RepositoryRoot(), "src", project, project + ".csproj");
        if (!File.Exists(csproj))
        {
            return;
        }

        Assert.Equal(["../Platform.Core/Platform.Core.csproj"], ProjectReferences(csproj));
    }

    [Theory]
    [MemberData(nameof(FeatureProjects))]
    public void AFeatureOrChannelProject_DoesNotUseTheAuthNamespace(string project)
    {
        var folder = Path.Combine(RepositoryRoot(), "src", project);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // Words in a comment are not a dependency.
                if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) && AuthNamespace.IsMatch(lines[i]))
                {
                    offenders.Add($"{project}/{relative}:{i + 1}");
                }
            }
        }

        Assert.Empty(offenders);
    }
}
