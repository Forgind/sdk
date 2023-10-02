// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using static Microsoft.DotNet.Cli.Parser;

namespace Microsoft.DotNet.Cli
{
    public static class ParseResultExtensions
    {
        public static void ShowHelp(this ParseResult parseResult)
        {
            DotnetHelpBuilder.Instance.Value.Write(parseResult.CommandResult.Command);
        }

        public static void ShowHelpOrErrorIfAppropriate(this ParseResult parseResult)
        {
            if (parseResult.Errors.Any())
            {
                var unrecognizedTokenErrors = parseResult.Errors.Where(error =>
                    error.Message.Contains(Parser.Instance.Configuration.Resources.UnrecognizedCommandOrArgument(string.Empty).Replace("'", string.Empty)));
                if (parseResult.CommandResult.Command.TreatUnmatchedTokensAsErrors ||
                    parseResult.Errors.Except(unrecognizedTokenErrors).Any())
                {
                    throw new CommandParsingException(
                        message: string.Join(Environment.NewLine,
                                             parseResult.Errors.Select(e => e.Message)), 
                        parseResult: parseResult);
                }
            }
            else if (parseResult.HasOption("--help"))
            {
                parseResult.ShowHelp();
                throw new HelpException(string.Empty);
            }
        }

        public static string RootSubCommandResult(this ParseResult parseResult)
        {
            return parseResult.RootCommandResult.Children?
                .Select(child => GetSymbolResultValue(parseResult, child))
                .FirstOrDefault(subcommand => !string.IsNullOrEmpty(subcommand)) ?? string.Empty;
        }

        public static bool IsDotnetBuiltInCommand(this ParseResult parseResult)
        {
            return string.IsNullOrEmpty(parseResult.RootSubCommandResult()) || BuiltInCommandsCatalog.Commands.ContainsKey(parseResult.RootSubCommandResult());
        }

        public static bool IsTopLevelDotnetCommand(this ParseResult parseResult)
        {
            return parseResult.CommandResult.Command.Equals(RootCommand) && string.IsNullOrEmpty(parseResult.RootSubCommandResult());
        }

        public static string[] GetSubArguments(this string[] args)
        {
            var subargs = args.ToList();

            // Don't remove any arguments that are being passed to the app in dotnet run
            var dashDashIndex = subargs.IndexOf("--");
            var runArgs = dashDashIndex > -1 ? subargs.GetRange(dashDashIndex, subargs.Count() - dashDashIndex) : new List<string>(0);
            subargs = dashDashIndex > -1 ? subargs.GetRange(0, dashDashIndex) : subargs;

            return subargs
                .SkipWhile(arg => DiagOption.Aliases.Contains(arg) || arg.Equals("dotnet"))
                .Skip(1) // remove top level command (ex build or publish)
                .Concat(runArgs)
                .ToArray();
        }

        public static bool DiagOptionPrecedesSubcommand(this string[] args, string subCommand)
        {
            if (string.IsNullOrEmpty(subCommand))
            {
                return true;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(subCommand))
                {
                    return false;
                }
                else if (DiagOption.Aliases.Contains(args[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetSymbolResultValue(ParseResult parseResult, SymbolResult symbolResult)
        {
            if (symbolResult.Token() == null)
            {
                return parseResult.FindResultFor(Parser.DotnetSubCommand)?.GetValueOrDefault<string>();
            }
            else if (symbolResult.Token().Type.Equals(TokenType.Command))
            {
                return symbolResult.Symbol.Name;
            }
            else if (symbolResult.Token().Type.Equals(TokenType.Argument))
            {
                return symbolResult.Token().Value;
            }
            else
            {
                return string.Empty;
            }
        }

        public static bool BothArchAndOsOptionsSpecified(this ParseResult parseResult) =>
            parseResult.HasOption(CommonOptions.ArchitectureOption().Aliases.First()) && 
            parseResult.HasOption(CommonOptions.OperatingSystemOption().Aliases.First());

        internal static string GetCommandLineRuntimeIdentifier(this ParseResult parseResult)
        {
            return parseResult.HasOption(RunCommandParser.RuntimeOption) ?
                parseResult.ValueForOption<string>(RunCommandParser.RuntimeOption) :
                parseResult.HasOption(CommonOptions.OperatingSystemOption().Aliases.First()) || parseResult.HasOption(CommonOptions.ArchitectureOption().Aliases.First()) ?
                CommonOptions.ResolveRidShorthandOptionsToRuntimeIdentifier(
                    parseResult.ValueForOption<string>(CommonOptions.OperatingSystemOption().Aliases.First()),
                    parseResult.ValueForOption<string>(CommonOptions.ArchitectureOption().Aliases.First())) :
                null;
        }

        public static bool UsingRunCommandShorthandProjectOption(this ParseResult parseResult)
        {
            if (parseResult.HasOption(RunCommandParser.PropertyOption) && parseResult.ValueForOption(RunCommandParser.PropertyOption).Any())
            {
                var projVals = parseResult.GetRunCommandShorthandProjectValues();
                if (projVals.Any())
                {
                    if (projVals.Count() != 1 || parseResult.HasOption(RunCommandParser.ProjectOption))
                    {
                        throw new GracefulException(Tools.Run.LocalizableStrings.OnlyOneProjectAllowed);
                    }
                    return true;
                }
            }
            return false;
        }

        public static IEnumerable<string> GetRunCommandShorthandProjectValues(this ParseResult parseResult)
        {
            var properties = GetRunPropertyOptions(parseResult, true);
            return properties.Where(property => !property.Contains("="));
        }

        public static IEnumerable<string> GetRunCommandPropertyValues(this ParseResult parseResult)
        {
            var shorthandProperties = GetRunPropertyOptions(parseResult, true)
                .Where(property => property.Contains("="));
            var longhandProperties = GetRunPropertyOptions(parseResult, false);
            return longhandProperties.Concat(shorthandProperties);
        }

        private static IEnumerable<string> GetRunPropertyOptions(ParseResult parseResult, bool shorthand)
        {
            var optionString = shorthand ? "-p" : "--property";
            var options = parseResult.CommandResult.Children.Where(c => c.Token().Type.Equals(TokenType.Option));
            var propertyOptions = options.Where(o => o.Token().Value.Equals(optionString));
            var propertyValues = propertyOptions.SelectMany(o => o.Children.SelectMany(c => c.Tokens.Select(t=> t.Value))).ToArray();
            return propertyValues;
        }
    }
}
