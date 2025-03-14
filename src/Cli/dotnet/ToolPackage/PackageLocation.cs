﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.EnvironmentAbstractions;

namespace Microsoft.DotNet.Cli.ToolPackage;

internal class PackageLocation
{
    public PackageLocation(
        FilePath? nugetConfig = null,
        DirectoryPath? rootConfigDirectory = null,
        string[] additionalFeeds = null,
        string[] sourceFeedOverrides = null)
    {
        NugetConfig = nugetConfig;
        RootConfigDirectory = rootConfigDirectory;
        AdditionalFeeds = additionalFeeds ?? Array.Empty<string>();
        SourceFeedOverrides = sourceFeedOverrides ?? Array.Empty<string>();
    }

    public FilePath? NugetConfig { get; }
    public DirectoryPath? RootConfigDirectory { get; }
    public string[] AdditionalFeeds { get; }
    public string[] SourceFeedOverrides { get; }
}
