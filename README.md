# SrtTranslator

A .NET console tool that translates `.srt` subtitle files using a local
[llama.cpp](https://github.com/ggerganov/llama.cpp) server.

The tool reads an SRT file, batches the subtitles, sends each batch to the
llama.cpp OpenAI-compatible chat completions endpoint, and writes the result
back as SRT (or JSON). It keeps the original subtitle indices and timing, so
the structure of the file is preserved.

## Features

- Translates subtitles via a local llama.cpp server (OpenAI-compatible API).
- Progress feedback per batch.
- Retries with backoff when the model returns an invalid/empty response.
- Falls back to the original text if a batch fails to translate.
- Token-aware internal batching for very large inputs.
- SRT output preserves format; JSON output is an indented serialization of the
  parsed items.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/) to build and run.
- A running [llama.cpp server](https://github.com/ggerganov/llama.cpp)
  exposing the `/v1/chat/completions` endpoint (e.g. started with the
  OpenAI-compatible server example).

## Downloading and running llama-server

Download the prebuilt `llama-server` binaries from
<https://llama-cpp.com/download/>. Extract the archive to a folder of your
choice and make sure a GGUF model file (e.g. a Gemma 4 quantized model) is
available.

Start the server from PowerShell:

```pwsh
.\llama-server -m ".\gemma-4-12B-it-GGUF\gemma-4-12B-it-QAT-Q4_0.gguf" -c 40000 -b 2048 --flash-attn on -ngl 99 --port 8087 --reasoning off
```

Run the tool pointing it at that port:

```bash
dotnet run -- -i subtitles_en.srt -o subtitles_ru.srt -l russian --port 8087
```

### Key server flags

| Flag | Meaning |
|------|---------|
| `-m` | Path to the GGUF model file. |
| `-c` | Context size in tokens. |
| `-b` | Batch size. |
| `--flash-attn on` | Enable Flash Attention. |
| `-ngl` | Number of layers to offload to the GPU. |
| `--port` | Port the server listens on (pass the same value to `--port`). |
| `--reasoning off` | Disable reasoning output.

## Usage

Build the tool:

```bash
dotnet build
```

Run it:

```bash
dotnet run -- --input input.srt --output output.srt --lang russian
```

## Command-line options

| Option | Aliases | Default | Description |
|--------|---------|---------|-------------|
| `--input` | `-i` | *(required)* | Input `.srt` file. |
| `--output` | `-o` | *(required)* | Output file, `.srt` or `.json`. |
| `--lang` | `-l` | `russian` | Target language for translation. |
| `--port` | `-p` | `8080` | Local port of the llama.cpp server. |
| `--timeout` | `-t` | `15` | llama.cpp request timeout, in minutes. |
| `--batch-size` | `-b` | `50` | Number of subtitles translated per batch. |

## Example

Start a llama.cpp server (approximate, adjust to your model):

```pwsh
.\llama-server -m ".\gemma-4-12B-it-GGUF\gemma-4-12B-it-QAT-Q4_0.gguf" -c 40000 -b 2048 --flash-attn on -ngl 99 --port 8087 --reasoning off
```

Translate subtitles into Russian:

```bash
dotnet run -- -i subtitles_en.srt -o subtitles_ru.srt -l russian
```

Output to JSON:

```bash
dotnet run -- -i subtitles_en.srt -o subtitles_ru.json -l russian --batch-size 100
```

## How it works

1. `SrtConverter.ReadSrtAsync` parses the input SRT into a list of
   `SubtitleItem` (index, start time, end time, text).
2. `Program.TranslateFile` iterates over the file in fixed-size batches
   (`--batch-size`, default 50) so progress can be tracked.
3. `LlamaDynamicTranslator.TranslateSubtitlesAsync` sends each batch to the
   server. Internally, if a batch would exceed the context limit
   (`MaxContextTokens = 20000`), it is split further by an estimated token
   count.
4. The model is asked to keep the `[index]` markers and the internal line
   breaks (`[BR]`).
5. `ParseLlamaResponse` maps the model output back to subtitle texts.
6. `SrtConverter.Validate` checks that the count and indices are intact
   (warning only).
7. `SrtConverter.WriteSrtAsync` writes the result as SRT or JSON.

## Project structure

| File | Purpose |
|------|---------|
| `Program.cs` | CLI entry point and batching loop. |
| `SrtConverter.cs` | SRT parsing, validation, and writing. |
| `LlamaDynamicTranslator.cs` | llama.cpp API call, retry, and response parsing. |
| `SubtitleItem.cs` | Subtitle data model. |
