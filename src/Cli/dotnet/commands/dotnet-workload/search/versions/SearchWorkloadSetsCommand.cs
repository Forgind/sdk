// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Completions;
using System.Text.Json;
using Microsoft.Deployment.DotNet.Releases;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.NuGetPackageDownloader;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Workloads.Workload.Install;
using Microsoft.NET.Sdk.WorkloadManifestReader;
using Microsoft.TemplateEngine.Cli.Commands;

namespace Microsoft.DotNet.Workloads.Workload.Search.Versions;

internal class SearchWorkloadSetsCommand : WorkloadCommandBase
{
    internal readonly int NumberOfWorkloadSetsToTake;
    private readonly SearchWorkloadSetsFormat _workloadSetOutputFormat;
    internal readonly IWorkloadManifestInstaller Installer;
    internal readonly ReleaseVersion SdkVersion;
    private readonly IWorkloadResolver _workloadResolver;

    private static readonly ManifestId WorkloadPackageIdBase = new("Microsoft.NET.Workloads");

    public SearchWorkloadSetsCommand(ParseResult result, IWorkloadResolverFactory workloadResolverFactory = null, IReporter reporter = null) : base(result, reporter: reporter)
    {
        NumberOfWorkloadSetsToTake = result.HasOption(SearchWorkloadSetsParser.TakeOption) ? result.GetValue(SearchWorkloadSetsParser.TakeOption) : 5;
        _workloadSetOutputFormat = result.GetValue(SearchWorkloadSetsParser.FormatOption);
        workloadResolverFactory = workloadResolverFactory ?? new WorkloadResolverFactory();
        var creationResult = workloadResolverFactory.Create();

        SdkVersion = creationResult.SdkVersion;
        _workloadResolver = creationResult.WorkloadResolver;

        Installer = WorkloadInstallerFactory.GetWorkloadInstaller(
                reporter,
                new SdkFeatureBand(SdkVersion),
                _workloadResolver,
                Verbosity,
                creationResult.UserProfileDir,
                !SignCheck.IsDotNetSigned(),
                restoreActionConfig: new RestoreActionConfig(result.HasOption(SharedOptions.InteractiveOption)),
                elevationRequired: false,
                shouldLog: false);
    }

    protected override void ShowHelpOrErrorIfAppropriate(ParseResult parseResult)
    {

    }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        var featureBand = new SdkFeatureBand(SdkVersion);
        var packageId = Installer.GetManifestPackageId(WorkloadPackageIdBase, featureBand);
        var versions = (await PackageDownloader.GetLatestPackageVersions(packageId, NumberOfWorkloadSetsToTake, packageSourceLocation: null, includePreview: !string.IsNullOrWhiteSpace(SdkVersion.Prerelease)).ConfigureAwait(false))
            .Select(version => WorkloadManifestUpdater.WorkloadSetPackageVersionToWorkloadSetVersion(featureBand, version.Version.ToString()));
        if (_workloadSetOutputFormat == SearchWorkloadSetsFormat.json)
        {
            Reporter.WriteLine(JsonSerializer.Serialize(versions.Select(version => version.ToDictionary(_ => "workloadVersion", v => v))));
        }
        else
        {
            Reporter.WriteLine(string.Join('\n', versions));
        }

        return 0;
    }

    public override int Execute() => Execute(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Used for CLI completions to get the workload set versions.
    /// </summary>
    /// <returns></returns>
    public static async Task<IEnumerable<string>> GetWorkloadSetVersions(CompletionContext ctx)
    {
        var inner = new SearchWorkloadSetsCommand(ctx.ParseResult);
        var featureBand = new SdkFeatureBand(inner.SdkVersion);
        var packageId = inner.Installer.GetManifestPackageId(WorkloadPackageIdBase, featureBand);
        var versions = (await inner.PackageDownloader.GetLatestPackageVersions(packageId, inner.NumberOfWorkloadSetsToTake, packageSourceLocation: null, includePreview: !string.IsNullOrWhiteSpace(inner.SdkVersion.Prerelease)).ConfigureAwait(false))
            .Select(version => WorkloadManifestUpdater.WorkloadSetPackageVersionToWorkloadSetVersion(featureBand, version.Version.ToString()));
        return versions;
    }
}
