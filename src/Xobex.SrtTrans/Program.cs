// <copyright file="Program.cs" company="Dmitry Kolchev">
// Copyright (c) 2026 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.CommandLine;

namespace Xobex.SrtTrans;

internal class Program
{

    private static async Task Main(string[] args)
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
            DefaultValueFactory =  (arg)=> "russian",
            Required = false
        };

        // Port of the local llama.cpp server
        Option<int> portOption = new("--port", ["-p"])
        {
            Description = "localhost port of llama.cpp server",
            DefaultValueFactory = (arg) => 8080,
            Required = false
        };

        // Timeout for a single llama.cpp server request
        Option<int> timeoutOption = new("--timeout", ["-t"])
        {
            Description = "llama.cpp server request timeout in minutes",
            DefaultValueFactory = (arg) => 15,
            Required = false
        };

        // Number of subtitles translated in each batch
        Option<int> batchSizeOption = new("--batch-size", ["-b"])
        {
            Description = "Number of subtitles to translate in each batch",
            DefaultValueFactory = (arg) => 50,
            Required = false
        };

        RootCommand rootCommand = new("SRT Translator");

        rootCommand.Options.Add(inputFileOption);
        rootCommand.Options.Add(outputFileOption);
        rootCommand.Options.Add(langOption);
        rootCommand.Options.Add(portOption);
        rootCommand.Options.Add(timeoutOption);
        rootCommand.Options.Add(batchSizeOption);

        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellation) =>
        {
            await TranslateFile(
                parseResult.GetValue(portOption),
                parseResult.GetValue(inputFileOption)!,
                parseResult.GetValue(outputFileOption)!,
                parseResult.GetValue(langOption)!,
                parseResult.GetValue(timeoutOption),
                parseResult.GetValue(batchSizeOption),
                cancellation);
        });
        var parseResult = rootCommand.Parse(args);
        await parseResult.InvokeAsync();
    }

    /// <summary>
    /// Reads the SRT file and translates it in batches against the local llama.cpp server.
    /// </summary>
    public static async Task TranslateFile(int port, string input, string output, string language, int timeout, int batchSize, CancellationToken cancellation)
    {
        Console.Error.WriteLine($"Translating {input} to {output} in {language}\n" +
            $"using localhost:{port} with timeout {timeout} minutes and batch size {batchSize}...");

        var translator = new LlamaDynamicTranslator($"http://localhost:{port}/v1/chat/completions", timeout);
        var items = await SrtConverter.ReadSrtAsync(input);
        var translated = new List<SubtitleItem>();

        // Process the file in fixed-size batches so progress can be tracked.
        for (var i = 0; ; ++i)
        {
            var offset = i * batchSize;
            var count = Math.Min(batchSize, items.Count - offset);
            if (count > 0)
            {
                Console.WriteLine($"Translating batch {i + 1} of {Math.Ceiling((double)items.Count / batchSize)}...");
                var slice = items.Slice(offset, count);
                var newItems = await translator.TranslateSubtitlesAsync(slice, language, cancellation);

                var startIndex = translated.Count;
                translated.AddRange(newItems);
            }
            else
            {
                break;
            }
        }
        SrtConverter.Validate(items, translated);
        await SrtConverter.WriteSrtAsync(translated, output);
    }
}
