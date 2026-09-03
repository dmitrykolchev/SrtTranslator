// <copyright file="LlamaDynamicTranslator.cs" company="Dmitry Kolchev">
// Copyright (c) 2026 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Xobex.SrtTrans;

public partial class LlamaDynamicTranslator
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;

    // Reserved for the system prompt and the model response; the rest is available for the context
    private const int MaxContextTokens = 90000;

    public LlamaDynamicTranslator(string apiUrl, int timeout)
    {
        // Increase the timeout because processing and generating large contexts takes time
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(int.Max(1, timeout)) };
        _apiUrl = apiUrl;
    }

    /// <summary>
    /// Translates the provided subtitles, batching them by estimated token count so
    /// that each request stays within the server context limit.
    /// </summary>
    public async Task<List<SubtitleItem>> TranslateSubtitlesAsync(List<SubtitleItem> originalSubtitles, string targetLanguage, CancellationToken cancellation)
    {
        var translatedSubtitles = new List<SubtitleItem>();
        var currentBatch = new List<SubtitleItem>();
        var currentBatchEstimatedTokens = 0;

        for (var i = 0; i < originalSubtitles.Count && !cancellation.IsCancellationRequested; i++)
        {
            var item = originalSubtitles[i];
            var lineFormat = $"[{item.Index}] {item.Text.Replace("\n", " [BR] ")}\n";

            // Rough token estimate: ~1.5 tokens per character for Cyrillic/special chars in Gemma
            var estimatedTokens = (int)(lineFormat.Length * 1.5);

            if (currentBatchEstimatedTokens + estimatedTokens > MaxContextTokens && currentBatch.Count > 0)
            {
                // The batch is full, send it for translation
                var translatedChunk = await ProcessBatchWithRetryAsync(currentBatch, targetLanguage, cancellation);
                translatedSubtitles.AddRange(translatedChunk);

                currentBatch.Clear();
                currentBatchEstimatedTokens = 0;
            }

            currentBatch.Add(item);
            currentBatchEstimatedTokens += estimatedTokens;
        }

        // Translate the remainder of the batch
        if (currentBatch.Count > 0)
        {
            var translatedChunk = await ProcessBatchWithRetryAsync(currentBatch, targetLanguage, cancellation);
            translatedSubtitles.AddRange(translatedChunk);
        }

        return translatedSubtitles;
    }

    private async Task<List<SubtitleItem>> ProcessBatchWithRetryAsync(List<SubtitleItem> batch, string targetLanguage, CancellationToken cancellation)
    {
        var sbPrompt = new StringBuilder();
        foreach (var item in batch)
        {
            var cleanText = item.Text.Replace("\n", " [BR] ");
            sbPrompt.AppendLine(CultureInfo.InvariantCulture, $"[{item.Index}] {cleanText}");
        }

        var retryCount = 3;
        while (retryCount > 0 && !cancellation.IsCancellationRequested)
        {
            var translatedBlock = await SendToLlamaAsync(sbPrompt.ToString(), targetLanguage, cancellation);
            var translatedDict = ParseLlamaResponse(translatedBlock);

            // Validate the response: retry if the structure is completely broken (empty response)
            if (translatedDict.Count == 0 && batch.Count > 0)
            {
                retryCount--;
                Console.Error.WriteLine($"[Warning] The server returned an invalid format. Retrying ({3 - retryCount}/3)...");
                await Task.Delay(2000, cancellation);
                continue;
            }

            var chunkResult = new List<SubtitleItem>();
            foreach (var item in batch)
            {
                var translatedText = translatedDict.TryGetValue(item.Index, out var text) ? text : item.Text;
                translatedText = translatedText.Replace(" [BR] ", "\n").Replace("[BR]", "\n").Trim();

                chunkResult.Add(new SubtitleItem
                {
                    Index = item.Index,
                    StartTime = item.StartTime,
                    EndTime = item.EndTime,
                    Text = translatedText
                });
            }
            return chunkResult;
        }

        // If all attempts are exhausted, fall back to the original text to avoid breaking the pipeline
        Console.Error.WriteLine("[Error] Failed to translate the batch. Returning the original text for this block.");
        return batch;
    }

    /// <summary>
    /// Sends the subtitle block to the llama.cpp server for translation.
    /// Returns the raw text of the model response, or an empty string on transport/parse errors.
    /// </summary>
    private async Task<string> SendToLlamaAsync(string textToTranslate, string targetLanguage, CancellationToken cancellation)
    {
        var requestBody = new
        {
            messages = new[]
            {
                new { role = "system", content = $"You are a professional subtitle translator. Translate the block into {targetLanguage}. Rules:\n1. Keep indices like '[1]'\n2. Keep internal line breaks as '[BR]'\n3. Do not add introductory words, notes or markdown fences. Output raw text ONLY." },
                new { role = "user", content = textToTranslate }
            },
            temperature = 1, // Gemma4 requires temp=1 (maximum creativity)
            top_p = 0.9,
            stream = false
        };

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_apiUrl, content, cancellation);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync(cancellation);
            using var doc = JsonDocument.Parse(responseString);

            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw; // Do not swallow our own cancellation
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[HTTP Error] {ex.Message}");
            return string.Empty;
        }
        catch (TaskCanceledException ex)
        {
            Console.Error.WriteLine($"[Timeout Error] {ex.Message}");
            return string.Empty;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[JSON Error] {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Parses the model output, mapping each "[index] text" line to its translated text.
    /// Lines that do not match do not form entries.
    /// </summary>
    private static Dictionary<int, string> ParseLlamaResponse(string llamaOutput)
    {
        var result = new Dictionary<int, string>();
        var lines = llamaOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Regex for reliable extraction of indices in the [123] format
            var match = NumberRegex().Match(line);
            if (match.Success)
            {
                var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var text = match.Groups[2].Value.Trim();
                result[index] = text;
            }
        }
        return result;
    }

    [GeneratedRegex(@"^\[(\d+)\](.*)")]
    private static partial Regex NumberRegex();
}
