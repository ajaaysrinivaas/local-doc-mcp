using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentRagMcpServer.Config;
using DocumentRagMcpServer.Models;
using DocumentRagMcpServer.Parsers;

namespace DocumentRagMcpServer.Services
{
    /// <summary>
    /// Unified pipeline service: PDF → txt → normalization in a single pass.
    /// 
    /// Flow:
    ///   1. Parse all raw files (PDF, TXT, MD, HTML) to /documents/ as .txt
    ///   2. Apply preprocessing (cleaning + markdown preservation) per-file using config
    ///   3. All files ready for indexing
    /// 
    /// Key Features:
    ///   - Per-document preprocessing configuration support
    ///   - Defaults: cleanEncoding=true, preserveMarkdown=true (on all files)
    ///   - Single-pass execution for efficiency
    /// </summary>
    public class UnifiedPreprocessingPipeline
    {
        private readonly string _rawPath;
        private readonly string _documentsPath;
        private readonly string _configPath;
        private readonly string? _pythonExe;

        private static readonly System.Text.RegularExpressions.Regex _htmlScriptStyleRegex = new(
            @"<(script|style)[^>]*>.*?</(script|style)>",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex _htmlTagRegex = new(@"<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _multiSpaceTabRegex = new(@"[ \t]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _tripleNewlineRegex = new(@"\n{3,}", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public UnifiedPreprocessingPipeline(string dataPath)
        {
            _rawPath = Path.Combine(dataPath, "raw");
            _documentsPath = Path.Combine(dataPath, "documents");
            _configPath = Path.Combine(dataPath, "config");
            _pythonExe = GetPythonExecutable();

            Directory.CreateDirectory(_documentsPath);
        }

        /// <summary>
        /// Execute the unified pipeline: parse all raw files, then preprocess in-place.
        /// Returns tuple of (filesProcessed, filesPreprocessed, errors).
        /// </summary>
        public async Task<(int filesProcessed, int filesPreprocessed, List<(string file, string error)> errors)> ExecuteAsync(
            RagConfig ragConfig,
            ParsingConfigLoader? configLoader = null)
        {
            var errors = new List<(string, string)>();
            int filesProcessed = 0;
            int filesPreprocessed = 0;

            configLoader ??= new ParsingConfigLoader(_configPath);

            Console.Error.WriteLine("  ▶ Unified Pipeline: Parse + Preprocess");

            // ── Stage 1: Parse raw files → /documents/ ──────────────────────────────
            Console.Error.WriteLine("    [1/2] Parsing raw files...");
            (filesProcessed, var parseErrors) = await ParseRawFilesAsync();
            errors.AddRange(parseErrors);

            // ── Stage 2: Preprocess all .txt files ──────────────────────────────────
            if (ragConfig.EnableUnifiedPipeline)
            {
                Console.Error.WriteLine("    [2/2] Applying preprocessing + normalization...");
                (filesPreprocessed, var prepErrors) = await PreprocessAllFilesAsync(ragConfig, configLoader);
                errors.AddRange(prepErrors);
            }
            else
            {
                Console.Error.WriteLine("    [2/2] Skipped (EnableUnifiedPipeline=false)");
            }

            Console.Error.WriteLine($"  ✓ Unified Pipeline: {filesProcessed} parsed, {filesPreprocessed} preprocessed");
            return (filesProcessed, filesPreprocessed, errors);
        }

        /// <summary>Parse all raw files to /documents/ as .txt.</summary>
        private async Task<(int count, List<(string file, string error)> errors)> ParseRawFilesAsync()
        {
            var errors = new List<(string, string)>();
            int count = 0;

            if (!Directory.Exists(_rawPath))
                return (0, errors);

            // Parse PDFs using Python script
            var pythonParserPath = Path.Combine(_rawPath, "pdf_parser.py");
            if (File.Exists(pythonParserPath))
            {
                var pdfParser = new PythonPdfParser(pythonParserPath, _rawPath, _documentsPath);
                var (successCount, failureCount, pdfErrors) = await pdfParser.ConvertAllPdfsAsync();
                count += successCount;
                if (failureCount > 0)
                {
                    foreach (var (file, error) in pdfErrors)
                        errors.Add((file, error));
                }
            }

            // Copy/convert other text formats
            foreach (var rawFile in Directory.GetFiles(_rawPath).OrderBy(f => f))
            {
                var ext = Path.GetExtension(rawFile).ToLowerInvariant();
                var destPath = Path.Combine(_documentsPath, Path.GetFileNameWithoutExtension(rawFile) + ".txt");

                // Skip if already processed
                if (File.Exists(destPath) &&
                    File.GetLastWriteTimeUtc(destPath) >= File.GetLastWriteTimeUtc(rawFile))
                    continue;

                try
                {
                    switch (ext)
                    {
                        case ".txt":
                        case ".md":
                            File.Copy(rawFile, destPath, overwrite: true);
                            count++;
                            break;
                        case ".html":
                        case ".htm":
                            var html = await File.ReadAllTextAsync(rawFile, System.Text.Encoding.UTF8);
                            var stripped = StripHtml(html);
                            await File.WriteAllTextAsync(destPath, stripped, System.Text.Encoding.UTF8);
                            count++;
                            break;
                        case ".pdf":
                            break; // Already handled by PythonPdfParser
                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add((Path.GetFileName(rawFile), ex.Message));
                }
            }

            return (count, errors);
        }

        /// <summary>Preprocess all .txt files in /documents/ using per-doc or global config.</summary>
        private async Task<(int count, List<(string file, string error)> errors)> PreprocessAllFilesAsync(
            RagConfig ragConfig,
            ParsingConfigLoader configLoader)
        {
            var errors = new List<(string, string)>();
            int count = 0;

            var preprocessorPath = Path.Combine(_rawPath, "preprocess_documents.py");
            if (!File.Exists(preprocessorPath))
            {
                return (0, new List<(string, string)> { ("preprocessor", $"Not found at {preprocessorPath}") });
            }

            var globalConfigPath = !string.IsNullOrEmpty(ragConfig.PreprocessingConfigPath)
                ? ragConfig.PreprocessingConfigPath
                : configLoader.GetGlobalPreprocessingConfigPath();

            foreach (var txtFile in Directory.GetFiles(_documentsPath, "*.txt").OrderBy(f => f))
            {
                var filename = Path.GetFileName(txtFile);
                var docConfig = configLoader.LoadDocumentConfig(filename);

                try
                {
                    // Build preprocessing config for this file
                    PreprocessingConfig preprocessingConfig;

                    if (docConfig?.Preprocessing != null)
                    {
                        // Use per-document config
                        preprocessingConfig = docConfig.Preprocessing;
                    }
                    else if (File.Exists(globalConfigPath))
                    {
                        // Try global config
                        var globalJson = await File.ReadAllTextAsync(globalConfigPath, System.Text.Encoding.UTF8);
                        var globalWrapper = System.Text.Json.JsonSerializer.Deserialize<PreprocessingConfigFile>(globalJson);
                        preprocessingConfig = globalWrapper?.Preprocessing ?? new PreprocessingConfig();
                    }
                    else
                    {
                        // Use defaults (cleanEncoding=true, preserveMarkdown=true)
                        preprocessingConfig = new PreprocessingConfig();
                    }

                    // Write preprocessing config to temp file
                    var tempConfigPath = Path.Combine(Path.GetTempPath(), $"prep_{Path.GetFileNameWithoutExtension(txtFile)}.json");
                    await File.WriteAllTextAsync(tempConfigPath,
                        System.Text.Json.JsonSerializer.Serialize(new { preprocessing = preprocessingConfig }, _jsonOptions),
                        System.Text.Encoding.UTF8);

                    try
                    {
                        await RunPreprocessorAsync(preprocessorPath, txtFile, txtFile, tempConfigPath);

                        // Verify file was properly cleaned
                        var fileContent = await File.ReadAllTextAsync(txtFile, System.Text.Encoding.UTF8);
                        var hasCarriageReturns = fileContent.Contains('\r');
                        var hasMojibake = fileContent.Contains('Γ') || fileContent.Contains('£') || fileContent.Contains('├');

                        if (hasCarriageReturns || hasMojibake)
                        {
                            Console.Error.WriteLine($"      ⚠ {filename}: Detected issues after preprocessing (CR: {hasCarriageReturns}, mojibake: {hasMojibake})");
                        }

                        count++;
                    }
                    finally
                    {
                        try { File.Delete(tempConfigPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add((filename, ex.Message));
                }
            }

            return (count, errors);
        }

        /// <summary>Run the Python preprocessor script on a file.</summary>
        private async Task RunPreprocessorAsync(string scriptPath, string inputPath, string outputPath, string? configPath = null)
        {
            try
            {
                var arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"";
                if (!string.IsNullOrEmpty(configPath))
                    arguments += $" \"{configPath}\"";

                if (string.IsNullOrWhiteSpace(_pythonExe))
                    throw new InvalidOperationException("Python not found in PATH. Install Python 3 and make sure 'python' or 'py' is available on the command line.");

                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExe,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start preprocessor");

                // Start both reads concurrently before waiting — prevents OS buffer deadlock
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var output = await outputTask;
                var errors = await errorTask;

                // Log output for debugging
                if (!string.IsNullOrWhiteSpace(output))
                {
                    foreach (var line in output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                        Console.Error.WriteLine($"      {line}");
                }

                if (process.ExitCode != 0)
                {
                    if (!string.IsNullOrWhiteSpace(errors))
                    {
                        Console.Error.WriteLine($"      ⚠ {Path.GetFileName(inputPath)}: {errors}");
                    }
                    else
                    {
                        Console.Error.WriteLine($"      ⚠ {Path.GetFileName(inputPath)}: Exit code {process.ExitCode}");
                    }
                }
                else if (string.IsNullOrWhiteSpace(output))
                {
                    // Silent success - still logged as processed
                    Console.Error.WriteLine($"      ✓ {Path.GetFileName(inputPath)}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Preprocessing failed: {ex.Message}", ex);
            }
        }

        private static string StripHtml(string html)
        {
            var stripped = _htmlScriptStyleRegex.Replace(html, "");
            stripped = _htmlTagRegex.Replace(stripped, " ");
            stripped = stripped.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                               .Replace("&nbsp;", " ").Replace("&quot;", "\"").Replace("&#39;", "'");
            stripped = _multiSpaceTabRegex.Replace(stripped, " ");
            stripped = _tripleNewlineRegex.Replace(stripped, "\n\n");
            return stripped.Trim();
        }

        private static string? GetPythonExecutable()
        {
            // Try each candidate by running it directly — works in venv, PowerShell, CMD, and any shell
            // Avoids 'where'/'which' which are unreliable (PowerShell aliases, venv, etc.)
            foreach (var cmd in new[] { "python3", "python", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p?.WaitForExit(3000) == true && p.ExitCode == 0) return cmd;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Wrapper class for deserializing preprocessing config from JSON files.</summary>
        private class PreprocessingConfigFile
        {
            public PreprocessingConfig Preprocessing { get; set; } = new();
        }
    }
}
