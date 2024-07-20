using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using NukeBuildHelpers;
using NukeBuildHelpers.Entry;
using NukeBuildHelpers.Entry.Extensions;
using NukeBuildHelpers.RunContext.Extensions;
using NukeBuildHelpers.RunContext.Interfaces;
using NukeBuildHelpers.Runner.Abstraction;
using System;
using System.Text.RegularExpressions;

class Build : BaseNukeBuildHelpers
{
    public static int Main () => Execute<Build>(x => x.Version);

    public override string[] EnvironmentBranches { get; } = ["main", "prerelease"];

    public override string MainEnvironmentBranch { get; } = "main";

    private static readonly string[] osMatrix = ["windows"];
    private static readonly string[] archMatrix = ["x64", "arm64"];

    AbsolutePath GetOutAsset(string os, string arch)
    {
        string name = $"winsw_{os.ToLowerInvariant()}_{arch.ToLowerInvariant()}";
        if (os == "linux")
        {
            return OutputDirectory / (name + ".tar.gz");
        }
        else if (os == "windows")
        {
            return OutputDirectory / (name + ".zip");
        }
        else
        {
            throw new NotSupportedException();
        }
    }

    BuildEntry BuildEdgeGridBinaries => _ => _
        .AppId("winsw")
        .Matrix(osMatrix, (definitionOs, os) =>
        {
            var osPascal = Regex.Replace(os, @"\b\p{Ll}", match => match.Value.ToUpper());
            definitionOs.RunnerOS(os switch
            {
                "linux" => RunnerOS.Ubuntu2204,
                "windows" => RunnerOS.Windows2022,
                _ => throw new NotSupportedException()
            });
            definitionOs.Matrix(archMatrix, (definitionArch, arch) =>
            {
                string runtime = $"{os.ToLowerInvariant()}-{arch.ToLowerInvariant()}";
                definitionArch.WorkflowId($"build_{os}_{arch}");
                definitionArch.DisplayName($"[Build] {osPascal}{arch.ToUpperInvariant()}");
                definitionArch.ReleaseAsset(context => [GetOutAsset(os, arch)]);
                definitionArch.Execute(context =>
                {
                    var outAsset = GetOutAsset(os, arch);
                    var archivePath = outAsset.Parent / outAsset.NameWithoutExtension;
                    var outPath = archivePath / outAsset.NameWithoutExtension;
                    var proj = RootDirectory / "src" / "WinSW" / "WinSW.csproj";
                    DotNetTasks.DotNetBuild(_ => _
                        .SetProjectFile(proj)
                        .SetConfiguration("Release"));
                    DotNetTasks.DotNetPublish(_ => _
                        .SetProject(proj)
                        .SetConfiguration("Release")
                        .EnableSelfContained()
                        .SetFramework(runtime switch
                        {
                            "linux-x64" => "net8.0-linux",
                            "linux-arm64" => "net8.0-linux",
                            "windows-x64" => "net8.0-windows",
                            "windows-arm64" => "net8.0-windows",
                            _ => throw new NotImplementedException()
                        })
                        .SetRuntime(runtime switch
                        {
                            "linux-x64" => "linux-x64",
                            "linux-arm64" => "linux-arm64",
                            "windows-x64" => "win-x64",
                            "windows-arm64" => "win-arm64",
                            _ => throw new NotImplementedException()
                        })
                        .EnablePublishSingleFile()
                        .SetOutput(outPath));
                    if (os == "linux")
                    {
                        archivePath.TarGZipTo(outAsset);
                    }
                    else if (os == "windows")
                    {
                        archivePath.ZipTo(outAsset);
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                });
            });
        });
}
