// <copyright file="Program.cs" company="Dmitry Kolchev">
// Copyright (c) 2026 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.CommandLine;

namespace Xobex.SrtTrans;

internal class Program
{
    private const string QuietOptionName = "--quiet";
    private static ParseResult _parseResult = null!;
    /// <summary>
    /// Application entry point
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private static async Task Main(string[] args)
    {
        var rootCommand = AddCommandlineOptions();
        _parseResult = rootCommand.Parse(args);
        PrintSplash();
        await _parseResult.InvokeAsync();
    }

    private static bool IsQuiet => _parseResult.GetValue<bool>(QuietOptionName);

    /// <summary>
    /// Reads the SRT file and translates it in batches against the local llama.cpp server.
    /// </summary>
    private static async Task TranslateFile(bool useTls, string host, int port, string input, string output, string language, int timeout, int batchSize, bool quiet, bool verbose, CancellationToken cancellation)
    {
        var url = $"{(useTls ? "https" : "http")}://{host}:{port}";

        WriteLine($"Translating {input} to {output} in {language}\n" +
            $"using {url} with timeout {timeout} minutes and batch size {batchSize}...");

        var translator = new LlamaDynamicTranslator($"{url}/v1/chat/completions", timeout);
        var items = await SrtConverter.ReadSrtAsync(input);
        var translated = new List<SubtitleItem>();

        // Process the file in fixed-size batches so progress can be tracked.
        for (var i = 0; ; ++i)
        {
            var offset = i * batchSize;
            var count = Math.Min(batchSize, items.Count - offset);
            if (count > 0)
            {
                WriteLine($"Translating batch {i + 1} of {Math.Ceiling((double)items.Count / batchSize)}...");
                var slice = items.Slice(offset, count);
                var newItems = await translator.TranslateSubtitlesAsync(slice, language, cancellation);
                if (verbose)
                {
                    foreach (var item in newItems)
                    {
                        Console.WriteLine($"{item.Index}\n{item.StartTime} --> {item.EndTime}\n{item.Text}\n");
                    }
                }
                translated.AddRange(newItems);
            }
            else
            {
                break;
            }
        }
        if (!SrtConverter.Validate(items, translated))
        {
            Console.Error.WriteLine("Validation failed.");
        }
        await SrtConverter.WriteSrtAsync(translated, output);
    }

    private static void WriteLine(string text)
    {
        if (!IsQuiet)
        {
            Console.WriteLine(text);
        }
    }

    private static RootCommand AddCommandlineOptions()
    {
        // Input subtitle file (.srt)
        Option<string> inputFileOption = new("--input", ["-i"])
        {
            Description = "Input .srt file",
            Required = true
        };

        // Output file (.srt or .json)
        Option<string> outputFileOption = new("--out", ["-o"])
        {
            Description = "Output .srt or .json file",
            Required = true
        };

        // Target language for translation
        Option<string> langOption = new("--lang", ["-l"])
        {
            Description = "Translation language.",
            DefaultValueFactory = (arg) => "russian"
        };

        // Port of the local llama.cpp server
        Option<int> portOption = new("--port", ["-p"])
        {
            Description = "port where llama.cpp server listen to",
            DefaultValueFactory = (arg) => 8080
        };

        Option<string> hostOption = new("--host", ["-h"])
        {
            Description = "llama.cpp server host name or IP address",
            DefaultValueFactory = (arg) => "localhost"
        };

        Option<bool> tlsOption = new("--tls", ["-s"])
        {
            Description = "Use HTTPS protocol",
            DefaultValueFactory = (arg) => false
        };

        // Timeout for a single llama.cpp server request
        Option<int> timeoutOption = new("--timeout", ["-t"])
        {
            Description = "llama.cpp server request timeout in minutes",
            DefaultValueFactory = (arg) => 15
        };

        // Number of subtitles translated in each batch
        Option<int> batchSizeOption = new("--batch-size", ["-b"])
        {
            Description = "Number of subtitles to translate in each batch",
            DefaultValueFactory = (arg) => 50
        };

        Option<bool> quietOption = new(QuietOptionName, ["-q"])
        {
            Description = "Suppress console information messages output.",
            DefaultValueFactory = (arg) => false
        };

        Option<bool> verboseOption = new("--verbose", ["-v"])
        {
            Description = "Display verbose output",
            DefaultValueFactory = (arg) => false
        };

        var rootCommand = new RootCommand("Subtitle Translator Program");
        rootCommand.Options.Add(inputFileOption);
        rootCommand.Options.Add(outputFileOption);
        rootCommand.Options.Add(langOption);
        rootCommand.Options.Add(tlsOption);
        rootCommand.Options.Add(hostOption);
        rootCommand.Options.Add(portOption);
        rootCommand.Options.Add(timeoutOption);
        rootCommand.Options.Add(batchSizeOption);
        rootCommand.Options.Add(quietOption);
        rootCommand.Options.Add(verboseOption);

        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellation) =>
        {
            await TranslateFile(
                parseResult.GetValue(tlsOption),
                parseResult.GetValue(hostOption)!,
                parseResult.GetValue(portOption),
                parseResult.GetValue(inputFileOption)!,
                parseResult.GetValue(outputFileOption)!,
                parseResult.GetValue(langOption)!,
                parseResult.GetValue(timeoutOption),
                parseResult.GetValue(batchSizeOption),
                parseResult.GetValue(quietOption),
                parseResult.GetValue(verboseOption),
                cancellation);
        });

        return rootCommand;
    }

    private static void PrintSplash()
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        if (Random.Shared.Next(100) % 2 == 0)
        {
            WriteLine(@"
  ____  ____ _____       _____                    _       _             
 / ___||  _ \_   _|     |_   _| __ __ _ _ __  ___| | __ _| |_ ___  _ __ 
 \___ \| |_) || |  _____  | || '__/ _` | '_ \/ __| |/ _` | __/ _ \| '__|
  ___) |  _ < | | |_____| | || | | (_| | | | \__ \ | (_| | || (_) | |   
 |____/|_| \_\|_|         |_||_|  \__,_|_| |_|___/_|\__,_|\__\___/|_|  
");
        }
        else
        {
            WriteLine(@"
   _____ ____  ______  ______                      __      __            
  / ___// __ \/_  __/ /_  __/________ _____  _____/ /___ _/ /_____  _____
  \__ \/ /_/ / / /_____/ / / ___/ __ `/ __ \/ ___/ / __ `/ __/ __ \/ ___/
 ___/ / _, _/ / /_____/ / / /  / /_/ / / / (__  ) / /_/ / /_/ /_/ / /    
/____/_/ |_| /_/     /_/ /_/   \__,_/_/ /_/____/_/\__,_/\__/\____/_/     
");
        }
        Console.ResetColor();
    }
}
