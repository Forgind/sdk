﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Evaluation;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Test;
using NuGet.Packaging;
using LocalizableStrings = Microsoft.DotNet.Tools.Test.LocalizableStrings;

namespace Microsoft.DotNet.Cli;

internal static class SolutionAndProjectUtility
{
    public static (bool SolutionOrProjectFileFound, string Message) TryGetProjectOrSolutionFilePath(string directory, out string projectOrSolutionFilePath, out bool isSolution)
    {
        projectOrSolutionFilePath = string.Empty;
        isSolution = false;

        if (!Directory.Exists(directory))
        {
            return (false, string.Format(LocalizableStrings.CmdNonExistentDirectoryErrorDescription, directory));
        }

        var solutionPaths = GetSolutionFilePaths(directory);

        // If more than a single sln file is found, an error is thrown since we can't determine which one to choose.
        if (solutionPaths.Length > 1)
        {
            return (false, string.Format(CommonLocalizableStrings.MoreThanOneSolutionInDirectory, directory));
        }

        if (solutionPaths.Length == 1)
        {
            var projectPaths = GetProjectFilePaths(directory);

            if (projectPaths.Length == 0)
            {
                projectOrSolutionFilePath = solutionPaths[0];
                isSolution = true;
                return (true, string.Empty);
            }

            return (false, LocalizableStrings.CmdMultipleProjectOrSolutionFilesErrorDescription);
        }
        else  // If no solutions are found, look for a project file
        {
            string[] projectPaths = GetProjectFilePaths(directory);

            if (projectPaths.Length == 0)
            {
                var solutionFilterPaths = GetSolutionFilterFilePaths(directory);

                if (solutionFilterPaths.Length == 0)
                {
                    return (false, LocalizableStrings.CmdNoProjectOrSolutionFileErrorDescription);
                }

                if (solutionFilterPaths.Length == 1)
                {
                    projectOrSolutionFilePath = solutionFilterPaths[0];
                    isSolution = true;
                    return (true, string.Empty);
                }
                else
                {
                    return (false, LocalizableStrings.CmdMultipleProjectOrSolutionFilesErrorDescription);
                }
            }

            if (projectPaths.Length == 1)
            {
                projectOrSolutionFilePath = projectPaths[0];
                return (true, string.Empty);
            }

            return (false, string.Format(CommonLocalizableStrings.MoreThanOneSolutionInDirectory, directory));
        }
    }

    private static string[] GetSolutionFilePaths(string directory)
    {
        string[] solutionFiles = Directory.GetFiles(directory, CliConstants.SolutionExtensionPattern, SearchOption.TopDirectoryOnly);
        solutionFiles.AddRange(Directory.GetFiles(directory, CliConstants.SolutionXExtensionPattern, SearchOption.TopDirectoryOnly));

        return solutionFiles;
    }

    private static string[] GetSolutionFilterFilePaths(string directory)
    {
        return Directory.GetFiles(directory, CliConstants.SolutionFilterExtensionPattern, SearchOption.TopDirectoryOnly);
    }

    private static string[] GetProjectFilePaths(string directory) => [.. Directory.EnumerateFiles(directory, CliConstants.ProjectExtensionPattern, SearchOption.TopDirectoryOnly).Where(IsProjectFile)];

    private static bool IsProjectFile(string filePath) => CliConstants.ProjectExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    public static string GetRootDirectory(string solutionOrProjectFilePath)
    {
        string fileDirectory = Path.GetDirectoryName(solutionOrProjectFilePath);
        return string.IsNullOrEmpty(fileDirectory) ? Directory.GetCurrentDirectory() : fileDirectory;
    }

    public static IEnumerable<TestModule> GetProjectProperties(string projectFilePath, IDictionary<string, string> globalProperties, ProjectCollection projectCollection)
    {
        var projects = new List<TestModule>();

        var globalPropertiesWithoutTargetFramework = new Dictionary<string, string>(globalProperties);
        globalPropertiesWithoutTargetFramework.Remove(ProjectProperties.TargetFramework);

        var project = projectCollection.LoadProject(projectFilePath, globalPropertiesWithoutTargetFramework, null);

        // Check if TargetFramework is specified in global properties
        if (globalProperties.TryGetValue(ProjectProperties.TargetFramework, out string targetFramework))
        {
            Logger.LogTrace(() => $"Loaded project '{Path.GetFileName(projectFilePath)}' with global property TargetFramework '{targetFramework}'.");

            if (IsValidTargetFramework(project, targetFramework))
            {
                Logger.LogTrace(() => $"Project '{Path.GetFileName(projectFilePath)}' with TargetFramework '{targetFramework}': before re-evaluation '{ProjectProperties.IsTestingPlatformApplication}' is '{project.GetPropertyValue(ProjectProperties.IsTestingPlatformApplication)}'.");

                project.SetProperty(ProjectProperties.TargetFramework, targetFramework);
                project.ReevaluateIfNecessary();

                Logger.LogTrace(() => $"Project '{Path.GetFileName(projectFilePath)}' with TargetFramework '{targetFramework}': after re-evaluation '{ProjectProperties.IsTestingPlatformApplication}' is '{project.GetPropertyValue(ProjectProperties.IsTestingPlatformApplication)}'.");

                if (GetModuleFromProject(project) is { } module)
                {
                    projects.Add(module);
                }
            }
            else
            {
                // TODO: When can this happen? Should we explicitly error?
                Logger.LogTrace(() => $"Project '{Path.GetFileName(projectFilePath)}' with TargetFramework '{targetFramework}' was considered invalid.");
            }
        }
        else
        {
            string targetFrameworks = project.GetPropertyValue(ProjectProperties.TargetFrameworks);

            if (string.IsNullOrEmpty(targetFrameworks))
            {
                Logger.LogTrace(() => $"Loaded project '{Path.GetFileName(projectFilePath)}' has '{ProjectProperties.IsTestingPlatformApplication}' = '{project.GetPropertyValue(ProjectProperties.IsTestingPlatformApplication)}'.");

                if (GetModuleFromProject(project) is { } module)
                {
                    projects.Add(module);
                }
            }
            else
            {
                Logger.LogTrace(() => $"Loaded project '{Path.GetFileName(projectFilePath)}' has '{ProjectProperties.IsTestingPlatformApplication}' = '{project.GetPropertyValue(ProjectProperties.IsTestingPlatformApplication)}' (TFMs: '{targetFrameworks}').");

                var frameworks = targetFrameworks.Split(CliConstants.SemiColon, StringSplitOptions.RemoveEmptyEntries);
                foreach (var framework in frameworks)
                {
                    project.SetProperty(ProjectProperties.TargetFramework, framework);
                    project.ReevaluateIfNecessary();

                    Logger.LogTrace(() => $"Loaded project '{Path.GetFileName(projectFilePath)}' has '{ProjectProperties.IsTestingPlatformApplication}' = '{project.GetPropertyValue(ProjectProperties.IsTestingPlatformApplication)}' (TFM: '{framework}').");

                    if (GetModuleFromProject(project) is { } module)
                    {
                        projects.Add(module);
                    }
                }
            }
        }

        return projects;
    }


    private static bool IsValidTargetFramework(Project project, string targetFramework)
    {
        string targetFrameworks = project.GetPropertyValue(ProjectProperties.TargetFrameworks);
        if (string.IsNullOrEmpty(targetFrameworks))
        {
            return project.GetPropertyValue(ProjectProperties.TargetFramework) == targetFramework;
        }

        var frameworks = targetFrameworks.Split(CliConstants.SemiColon, StringSplitOptions.RemoveEmptyEntries);
        return frameworks.Contains(targetFramework);
    }

    private static TestModule? GetModuleFromProject(Project project)
    {
        _ = bool.TryParse(project.GetPropertyValue(ProjectProperties.IsTestProject), out bool isTestProject);
        _ = bool.TryParse(project.GetPropertyValue(ProjectProperties.IsTestingPlatformApplication), out bool isTestingPlatformApplication);

        if (!isTestProject && !isTestingPlatformApplication)
        {
            return null;
        }

        string targetFramework = project.GetPropertyValue(ProjectProperties.TargetFramework);

        string executablePath = GetExecutablePath(project);
        string targetPath = !string.IsNullOrEmpty(executablePath) ? executablePath : project.GetPropertyValue(ProjectProperties.TargetPath);
        string projectFullPath = project.GetPropertyValue(ProjectProperties.ProjectFullPath);
        string runSettingsFilePath = project.GetPropertyValue(ProjectProperties.RunSettingsFilePath);

        return new TestModule(targetPath, PathUtility.FixFilePath(projectFullPath), targetFramework, runSettingsFilePath, isTestingPlatformApplication, isTestProject);
    }

    private static string GetExecutablePath(Project project)
    {
        _ = bool.TryParse(project.GetPropertyValue(ProjectProperties.IsExecutable), out bool isExecutable);
        _ = bool.TryParse(project.GetPropertyValue(ProjectProperties.UseAppHost), out bool useAppHost);

        string targetFrameworkIdentifier = project.GetPropertyValue(ProjectProperties.TargetFrameworkIdentifier);

        if (targetFrameworkIdentifier.Equals(CliConstants.NetCoreIdentifier, StringComparison.OrdinalIgnoreCase) &&
           isExecutable &&
           useAppHost)
        {
            string targetDir = project.GetPropertyValue(ProjectProperties.TargetDir);
            string assemblyName = project.GetPropertyValue(ProjectProperties.AssemblyName);
            string nativeExecutableExtension = project.GetPropertyValue(ProjectProperties.NativeExecutableExtension);

            string executablePath = $"{targetDir}{assemblyName}{nativeExecutableExtension}";

            if (File.Exists(executablePath))
            {
                return executablePath;
            }
        }

        return string.Empty;
    }

}
