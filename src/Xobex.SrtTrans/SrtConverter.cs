// <copyright file="SrtConverter.cs" company="Dmitry Kolchev">
// Copyright (c) 2026 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Xobex.SrtTrans;

public partial class SrtConverter
{
    private static readonly Regex TimeRegex = GetTimeRegex();

    /// <summary>
    /// Reads an SRT file and parses its structure into a list of subtitle items.
    /// </summary>
    public static async Task<List<SubtitleItem>> ReadSrtAsync(string srtFilePath)
    {
        if (!File.Exists(srtFilePath))
        {
            throw new FileNotFoundException($"Source SRT file not found: {srtFilePath}");
        }

        // Read the whole file and normalize line endings to Unix for stable parsing
        var srtContent = await File.ReadAllTextAsync(srtFilePath, Encoding.UTF8);
        srtContent = srtContent.Replace("\r\n", "\n");
        var subtitles = new List<SubtitleItem>();

        // Split content into blocks separated by blank lines
        var blocks = srtContent.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
            {
                continue;
            }

            if (int.TryParse(lines[0].Trim(), out var index))
            {
                var match = TimeRegex.Match(lines[1]);
                if (match.Success)
                {
                    var textLines = new List<string>();
                    for (var i = 2; i < lines.Length; i++)
                    {
                        textLines.Add(lines[i]);
                    }

                    subtitles.Add(new SubtitleItem
                    {
                        Index = index,
                        StartTime = match.Groups[1].Value,
                        EndTime = match.Groups[2].Value,
                        Text = string.Join("\n", textLines)
                    });
                }
            }
        }
        return subtitles;
    }

    /// <summary>
    /// Verifies that the translated subtitle count and indices match the expected structure.
    /// </summary>
    public static bool Validate(List<SubtitleItem> original, List<SubtitleItem> translated, bool quiet)
    {
        var valid = true;
        if (original.Count != translated.Count)
        {
            Console.Error.WriteLine($"Warning: original items count {original.Count} does not match translated items count {translated.Count}");
            valid = false;
        }
        for (var index = 0; index < translated.Count; ++index)
        {
            if (translated[index].Index != index + 1)
            {
                Console.Error.WriteLine($"Warning: translated item index {translated[index].Index} does not match expected index {index + 1}");
                valid = false;
            }
        }
        return valid;
    }

    /// <summary>
    /// Writes subtitle items to a file, either as .srt text or as indented JSON.
    /// </summary>
    public static async Task WriteSrtAsync(List<SubtitleItem> items, string filePath)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNullOrEmpty(filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".json")
        {
            await File.WriteAllTextAsync(
                filePath,
                JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        else if(extension == ".srt")
        {
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                sb.AppendLine(item.Index.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(CultureInfo.InvariantCulture, $"{item.StartTime} --> {item.EndTime}");
                sb.AppendLine(item.Text);
                sb.AppendLine(); // Blank line separating subtitle blocks
            }
            // Remove trailing line breaks and write a valid SRT file
            var resultSrt = sb.ToString().TrimEnd('\r', '\n') + Environment.NewLine;
            await File.WriteAllTextAsync(filePath, resultSrt, new UTF8Encoding(false));
        }
        else
        {
            throw new ArgumentException($"Unknown file type {filePath}", nameof(filePath));
        }
    }

    [GeneratedRegex(@"(\d{2}:\d{2}:\d{2},\d{3})\s-->\s(\d{2}:\d{2}:\d{2},\d{3})", RegexOptions.Compiled)]
    private static partial Regex GetTimeRegex();
}
