﻿using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using IniParser;
using IniParser.Model;
using Microsoft.Extensions.Logging;
using static System.Environment;

namespace proton_handler;

internal static class ProtonHandler
{
    private const string ProtonPathPattern = "PROTONPATH=(.*)$";
    private const string SteamCompatInstallPathPattern = "STEAM_COMPAT_INSTALL_PATH=(.*)$";
    private const string SteamCompatDataPathPattern = "STEAM_COMPAT_DATA_PATH=(.*)$";
    private const string DotnetRootPattern = "DOTNET_ROOT=(.*)$";
    private const string AppExePattern = "EXE=(.*)$";
    private const string TR = "/usr/bin/tr";
    private const string ProtonVerb = "run";
    private const string ProtonIdentifier = "srt-bwrap";
    
    private static string FirstCaptureGroup(Regex r, StringBuilder s)
    {
        foreach (var line in s.ToString().Split('\n'))
        {
            var m = r.Match(line);
            if (!m.Success) continue;
            {
                return m.Groups[1].Captures[0].ToString();
            }
        }

        return "";
    }
    
    private static async Task<StringBuilder> Environ(Process process)
    {
        var stdOutBuffer = new StringBuilder();
        await using var input = File.OpenRead("/proc/" + process.Id + "/environ");
        await Cli.Wrap(TR)
            .WithArguments(env => env
                .Add("'\\0'")
                .Add("'\n'"))
            .WithStandardInputPipe(PipeSource.FromStream(input))
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();
        
        return stdOutBuffer;
    }
    
    private static async Task<StringBuilder> CmdLine(Process process)
    {
        var stdOutBuffer = new StringBuilder();
        var filePath = "/proc/" + process.Id + "/cmdline";
        await using var input = File.OpenRead(filePath);
        await Cli.Wrap(TR)
            .WithArguments(env => env
                .Add("'\\0'")
                .Add("' '"))
            .WithStandardInputPipe(PipeSource.FromStream(input))
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();
        return stdOutBuffer;
    }

    private static string SteamCompatInstallPath(StringBuilder environ)
    {
        return FirstCaptureGroup(new Regex(SteamCompatInstallPathPattern), environ);
    }
    
    private static string SteamCompatDataPath(StringBuilder environ)
    {
        return FirstCaptureGroup(new Regex(SteamCompatDataPathPattern), environ);
    }

    private static string ProtonPath(StringBuilder environ)
    {
        var protonPath = FirstCaptureGroup(new Regex(ProtonPathPattern), environ);
        return protonPath != "" ? protonPath + "/proton" : "";
    }

    private static string DotnetRoot(StringBuilder environ)
    {
        return FirstCaptureGroup(new Regex(DotnetRootPattern), environ);
    }

    private static string AppExe(StringBuilder environ)
    {
        return FirstCaptureGroup(new Regex(AppExePattern), environ);
    }

    private static string AppArgs(StringBuilder cmdline, string exe)
    {
        return FirstCaptureGroup(new Regex(exe + " (.*)$"), cmdline).Trim();
    }

    private static async Task Main(string[] args)
    {
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
        ILogger logger = factory.CreateLogger("proton-app-handler");

        if (!File.Exists(TR))
        {
            logger.LogError("Cannot find executable '/usr/bin/tr'.");
            Exit(1);
        }

        if (args.Length < 2)
        {
            logger.LogWarning("Not enough arguments. Received: " + args.Length +
                              ". Expected: At least 2. Must pass an app exe name (eg, MO2.exe) and any arguments to pass to the app (eg, a URL).");
            Exit(0);
        }
        var home = GetEnvironmentVariable("HOME");
        var configFile = home + "/.config/proton-handler/config.ini";
        FileIniDataParser parser = new();
        IniData config = parser.ReadFile(configFile);

        string app = "", appArgs = "", proton = "", steamDir = "", prefixDir = "", dotnetRoot = "";

        var appExe = args[0];
        var handlerArgsArray = args.Skip(1).ToArray();
        var handlerArgsString = string.Join(' ', handlerArgsArray).Trim();
        
        logger.LogInformation("Searching for '{app}'", appExe);
        var processes = Process.GetProcessesByName(ProtonIdentifier);
        logger.LogInformation("Found {numberOf} processes matching {identifier}", processes.Length, ProtonIdentifier);
        
        /*
        // Uncomment if you need a list of all processes to figure out how C# is identifying things.
        var localAll = Process.GetProcesses();
        logger.LogInformation("Found {numberOf} processes in total", localAll.Length);
        foreach (var process in localAll)
        {
            logger.LogInformation("Process Name: {name}, PID: {id}", process.ProcessName, process.Id);
        }
        */
        
        // Ensure that the "srt-bwrap" process we select is the right one.
        var appRegex = new Regex(appExe);

        foreach (var process in processes)
        {
            var environ = await Environ(process);
            var cmdline = await CmdLine(process);

            logger.LogInformation("Process Name: {name}, PID: {id}", process.ProcessName, process.Id);

            var match = appRegex.Match(cmdline.ToString());
            if (!match.Success) continue;
            {
                logger.LogInformation("Match Found!");

                app = AppExe(environ);
                appArgs = AppArgs(cmdline, app);
                proton = ProtonPath(environ);
                steamDir = SteamCompatInstallPath(environ);
                prefixDir = SteamCompatDataPath(environ);
                dotnetRoot = DotnetRoot(environ);

                if (prefixDir != "")
                {
                    logger.LogInformation("Found running process. Writing to config file.");
                    config[appExe]["APP"] = app;
                    config[appExe]["ARGS"] = appArgs;
                    config[appExe]["PROTON"] = proton;
                    config[appExe]["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamDir;
                    config[appExe]["STEAM_COMPAT_DATA_PATH"] = prefixDir;
                    config[appExe]["DOTNET_ROOT"] = dotnetRoot;
                    parser.WriteFile(configFile, config);
                }
                else
                {
                    logger.LogError("Could not determine Proton's current prefix.");
                    Exit(1);
                }

                break;
            }
        }
        
        if (prefixDir == "" && config[appExe]["STEAM_COMPAT_DATA_PATH"] != null)
        {
            logger.LogInformation("Could not find running process, but stored config exists.");
            app = config[appExe]["APP"];
            appArgs = config[appExe]["ARGS"];
            proton = config[appExe]["PROTON"];
            steamDir = config[appExe]["STEAM_COMPAT_CLIENT_INSTALL_PATH"];
            prefixDir = config[appExe]["STEAM_COMPAT_DATA_PATH"];
            dotnetRoot = config[appExe]["DOTNET_ROOT"];
        }
        else if (prefixDir == "")
        {
            logger.LogError("Could not find a running process, and no stored config exists.");
            Exit(1);
        }
        
        logger.LogInformation($"Dotnet: {dotnetRoot}\nSteamDir: {steamDir}\nPrefix: {prefixDir}\nProton: {proton}\nApp: {app}\nArgs: {appArgs}\nLink: {handlerArgsString}");
        logger.LogInformation(
            $"DOTNET_ROOT={dotnetRoot} STEAM_COMPAT_CLIENT_INSTALL_PATH=\"{steamDir}\" STEAM_COMPAT_DATA_PATH=\"{prefixDir}\" \"{proton}\" run \"{app}\" \"{appArgs}\" \"{handlerArgsString}\"");

        // There has to be a better way to do this.
        // Maybe I can override the Add method.
        Command handler;
        if (appArgs != null && appArgs.Length > 0)
        {
            handler = Cli.Wrap(proton)
                .WithArguments(a => a
                    .Add(ProtonVerb)
                    .Add(app)
                    .Add(appArgs)
                    .Add(handlerArgsArray));
        }
        else
        {
            handler = Cli.Wrap(proton)
                .WithArguments(a => a
                    .Add(ProtonVerb)
                    .Add(app)
                    .Add(handlerArgsArray));
        }

        var result = await handler
            .WithEnvironmentVariables(e => e
                .Set("DOTNET_ROOT", dotnetRoot)
                .Set("STEAM_COMPAT_CLIENT_INSTALL_PATH", steamDir)
                .Set("STEAM_COMPAT_DATA_PATH", prefixDir))
            .ExecuteAsync();
        logger.LogInformation("Exit Code: {code}", result.ExitCode);
    }
}