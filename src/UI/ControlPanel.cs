using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentRagMcpServer.Config;
using DocumentRagMcpServer.Interfaces;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.UI
{
    /// <summary>
    /// Thin menu loop. Each menu option is delegated to a dedicated screen class.
    /// </summary>
    public class ControlPanel
    {
        private readonly string _rootPath;
        private readonly ParsingConfigLoader _configLoader;
        private readonly IDocumentRepository? _repository;

        private readonly DocumentsScreen _documents;
        private readonly PageSelectionScreen _pageSelection;
        private readonly ExtractionConfigScreen _extractionConfig;

        public ControlPanel(string rootPath, ParsingConfigLoader configLoader, IDocumentRepository? repository = null)
        {
            _rootPath = rootPath;
            _configLoader = configLoader;
            _repository = repository;

            _documents = new DocumentsScreen(rootPath, configLoader);
            _pageSelection = new PageSelectionScreen(rootPath, configLoader);
            _extractionConfig = new ExtractionConfigScreen(rootPath, configLoader);
        }

        public async Task<bool> RunAsync()
        {
            MenuHelpers.SafeClear();
            PrintHeader();

            if (IsFirstRun()) PrintFirstRunMessage();

            while (true)
            {
                PrintMenu();
                var choice = MenuHelpers.Prompt("> ");

                switch (choice)
                {
                    case "1":
                        _documents.Show();
                        break;
                    case "2":
                        await _pageSelection.ShowAsync();
                        break;
                    case "3":
                        await _extractionConfig.ShowAsync();
                        break;
                    case "4":
                        TogglePreprocessing();
                        break;
                    case "5":
                        await ShowStatisticsAsync();
                        break;
                    case "6":
                        await RebuildIndexAsync();
                        break;
                    case "0":
                        Console.WriteLine("\nStarting MCP server...");
                        return true;
                    default:
                        Console.WriteLine("Invalid choice.");
                        break;
                }

                MenuHelpers.PressAnyKey();
            }
        }

        private bool IsFirstRun()
        {
            var docsPath = Path.Combine(_rootPath, "documents");
            var rawPath = Path.Combine(_rootPath, "raw");
            return !(Directory.Exists(docsPath) && Directory.GetFiles(docsPath, "*.txt").Length > 0)
                && !(Directory.Exists(rawPath) && Directory.GetFiles(rawPath, "*.pdf").Length > 0);
        }

        private void PrintHeader()
        {
            Console.WriteLine("╔════════════════════════════════════╗");
            Console.WriteLine("║   Document RAG MCP — Control Panel ║");
            Console.WriteLine("╚════════════════════════════════════╝");
            Console.WriteLine();
        }

        private void PrintMenu()
        {
            var config = _configLoader.LoadConfig();
            var pipelineStatus = config.EnableUnifiedPipeline ? "unified" : "legacy";
            var prepStatus = config.EnablePreprocessing ? "ON " : "off";

            Console.WriteLine("  1. Documents");
            Console.WriteLine("  2. Page Selection");
            Console.WriteLine("  3. Extraction Config");
            Console.WriteLine($"  4. Pipeline              [{pipelineStatus}] [{prepStatus}]");
            Console.WriteLine("  5. Statistics");
            Console.WriteLine("  6. Rebuild Index");
            Console.WriteLine("  0. Start MCP Server");
            Console.WriteLine();
        }

        private void PrintFirstRunMessage()
        {
            Console.WriteLine("First run — no documents found.\n");
            Console.WriteLine($"  Place .txt files in:  {Path.Combine(_rootPath, "documents")}");
            Console.WriteLine($"  Place PDF files in:   {Path.Combine(_rootPath, "raw")}");
            Console.WriteLine();
            Console.WriteLine("  PDF requirements:   pip install -r requirements.txt");
            Console.WriteLine();
            MenuHelpers.PressAnyKey();
        }

        // ── Preprocessing toggle ──────────────────────────────────────────────

        private class PreprocessingConfigFile
        {
            [System.Text.Json.Serialization.JsonPropertyName("preprocessing")]
            public PreprocessingConfig Preprocessing { get; set; } = new();
        }

        private PreprocessingConfig LoadGlobalPreprocessingConfig()
        {
            var configPath = Path.Combine(_rootPath, "config", "preprocessing.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var content = File.ReadAllText(configPath);
                    var wrapper = System.Text.Json.JsonSerializer.Deserialize<PreprocessingConfigFile>(content);
                    if (wrapper?.Preprocessing != null)
                        return wrapper.Preprocessing;
                }
                catch { }
            }
            return new PreprocessingConfig();
        }

        private void SaveGlobalPreprocessingConfig(PreprocessingConfig config)
        {
            var configDir = Path.Combine(_rootPath, "config");
            Directory.CreateDirectory(configDir);
            var configPath = Path.Combine(configDir, "preprocessing.json");
            var wrapper = new PreprocessingConfigFile { Preprocessing = config };
            var json = System.Text.Json.JsonSerializer.Serialize(wrapper, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }

        private void TogglePreprocessing()
        {
            while (true)
            {
                MenuHelpers.SafeClear();
                Console.WriteLine("═ Pipeline & Preprocessing Configuration ═\n");

                var config = _configLoader.LoadConfig();

                // ── Unified Pipeline Toggle ──────────────────────────────────────────
                var pipelineMode = config.EnableUnifiedPipeline ? "UNIFIED (PDF→txt→norm)" : "LEGACY (two-pass)";
                Console.WriteLine($"Pipeline Mode: {pipelineMode}\n");
                Console.WriteLine("  U. Toggle Pipeline Mode");
                Console.WriteLine("     • UNIFIED: Single-pass PDF→txt→normalization");
                Console.WriteLine("     • LEGACY: Separate PDF parsing + preprocessing\n");

                // ── Preprocessing Toggle ──────────────────────────────────────────────
                var prepStatus = config.EnablePreprocessing ? "ENABLED ✓" : "disabled";
                Console.WriteLine($"Preprocessing: {prepStatus}\n");

                if (config.EnablePreprocessing)
                {
                    var prep = LoadGlobalPreprocessingConfig();

                    Console.WriteLine("  Global Preprocessing Defaults:\n");
                    Console.WriteLine($"  1. Clean Encoding         [{(prep.CleanEncoding ? "ON " : "off")}]");
                    Console.WriteLine($"     Removes mojibake, \\r, control chars, zero-width chars\n");

                    Console.WriteLine($"  2. Preserve Markdown      [{(prep.PreserveMarkdown ? "ON " : "off")}]");
                    Console.WriteLine($"     Keeps **bold**, _italic_, # headers (default: ON)\n");

                    Console.WriteLine($"  3. Strip Headers          [{(prep.StripHeaders ? "ON " : "off")}] (ignored if Preserve Markdown=ON)");
                    Console.WriteLine($"     # Header → Header\n");

                    Console.WriteLine($"  4. Strip Bold             [{(prep.StripBold ? "ON " : "off")}] (ignored if Preserve Markdown=ON)");
                    Console.WriteLine($"     **text** → text\n");

                    Console.WriteLine($"  5. Strip Italic           [{(prep.StripItalic ? "ON " : "off")}] (ignored if Preserve Markdown=ON)");
                    Console.WriteLine($"     _text_ / *text* → text\n");

                    Console.WriteLine($"  6. Remove Page Markers    [{(prep.RemovePageMarkers ? "ON " : "off")}]");
                    Console.WriteLine($"     Removes PDF PAGE N separators\n");

                    Console.WriteLine($"  7. Normalize Line Breaks  [{(prep.NormalizeLineBreaks ? "ON " : "off")}]");
                    Console.WriteLine($"     Intelligently join/preserve breaks\n");

                    Console.WriteLine($"  8. Join Short Lines       [{(prep.JoinShortLines ? "ON " : "off")}]");
                    Console.WriteLine($"     Fixes artificial PDF breaks\n");
                }
                else
                {
                    Console.WriteLine("  When DISABLED (current):");
                    Console.WriteLine("    • Documents indexed directly");
                    Console.WriteLine("    • No preprocessing applied\n");
                }

                Console.WriteLine($"  E. {(config.EnablePreprocessing ? "Disable" : "Enable")} Preprocessing");
                Console.WriteLine("  T. Toggle All Steps");
                Console.WriteLine("  1-8. Toggle individual steps (when enabled)");
                Console.WriteLine("  0. Back");

                var choice = MenuHelpers.Prompt("\n> ");

                switch (choice)
                {
                    case "U":
                        config.EnableUnifiedPipeline = !config.EnableUnifiedPipeline;
                        _configLoader.SaveConfig(config);
                        Console.WriteLine($"\n  ✓ Pipeline mode: {(config.EnableUnifiedPipeline ? "UNIFIED" : "LEGACY")}");
                        Console.WriteLine("    Run Rebuild Index (option 6) to apply changes.");
                        MenuHelpers.PressAnyKey();
                        break;

                    case "E":
                        config.EnablePreprocessing = !config.EnablePreprocessing;
                        _configLoader.SaveConfig(config);
                        Console.WriteLine($"\n  ✓ Preprocessing {(config.EnablePreprocessing ? "enabled" : "disabled")}.");
                        Console.WriteLine("    Run Rebuild Index (option 6) to apply changes.");
                        MenuHelpers.PressAnyKey();
                        break;

                    case "T":
                        if (config.EnablePreprocessing)
                            ToggleAllSteps();
                        else
                            Console.WriteLine("  Enable preprocessing first.");
                        MenuHelpers.PressAnyKey();
                        break;

                    case "1" when config.EnablePreprocessing:
                        ToggleStep("cleanEncoding", "Clean Encoding");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "2" when config.EnablePreprocessing:
                        ToggleStep("preserveMarkdown", "Preserve Markdown");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "3" when config.EnablePreprocessing:
                        ToggleStep("stripHeaders", "Strip Headers");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "4" when config.EnablePreprocessing:
                        ToggleStep("stripBold", "Strip Bold");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "5" when config.EnablePreprocessing:
                        ToggleStep("stripItalic", "Strip Italic");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "6" when config.EnablePreprocessing:
                        ToggleStep("removePageMarkers", "Remove Page Markers");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "7" when config.EnablePreprocessing:
                        ToggleStep("normalizeLineBreaks", "Normalize Line Breaks");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "8" when config.EnablePreprocessing:
                        ToggleStep("joinShortLines", "Join Short Lines");
                        MenuHelpers.PressAnyKey();
                        break;
                    case "0":
                        return;
                    default:
                        Console.WriteLine("  Invalid choice.");
                        MenuHelpers.PressAnyKey();
                        break;
                }
            }
        }

        private void ToggleStep(string stepName, string stepLabel)
        {
            var prepConfig = LoadGlobalPreprocessingConfig();

            // Toggle the step
            switch (stepName)
            {
                case "cleanEncoding": prepConfig.CleanEncoding = !prepConfig.CleanEncoding; break;
                case "preserveMarkdown": prepConfig.PreserveMarkdown = !prepConfig.PreserveMarkdown; break;
                case "stripBold": prepConfig.StripBold = !prepConfig.StripBold; break;
                case "stripItalic": prepConfig.StripItalic = !prepConfig.StripItalic; break;
                case "stripHeaders": prepConfig.StripHeaders = !prepConfig.StripHeaders; break;
                case "removePageMarkers": prepConfig.RemovePageMarkers = !prepConfig.RemovePageMarkers; break;
                case "normalizeLineBreaks": prepConfig.NormalizeLineBreaks = !prepConfig.NormalizeLineBreaks; break;
                case "joinShortLines": prepConfig.JoinShortLines = !prepConfig.JoinShortLines; break;
            }

            SaveGlobalPreprocessingConfig(prepConfig);

            Console.WriteLine($"\n  ✓ {stepLabel} toggled for ALL documents.");
            Console.WriteLine("    Run Rebuild Index (option 6) to apply.");
        }

        private void ToggleAllSteps()
        {
            var prep = LoadGlobalPreprocessingConfig();

            // Check if all steps are currently on
            bool allOn = prep.CleanEncoding && prep.StripHeaders && prep.StripBold &&
                        prep.StripItalic && prep.RemovePageMarkers && prep.NormalizeLineBreaks &&
                        prep.JoinShortLines;

            // Toggle all to opposite state
            prep.CleanEncoding = !allOn;
            prep.StripHeaders = !allOn;
            prep.StripBold = !allOn;
            prep.StripItalic = !allOn;
            prep.RemovePageMarkers = !allOn;
            prep.NormalizeLineBreaks = !allOn;
            prep.JoinShortLines = !allOn;

            SaveGlobalPreprocessingConfig(prep);

            Console.WriteLine($"\n  ✓ All preprocessing steps toggled for ALL documents.");
            Console.WriteLine("    Run Rebuild Index (option 6) to apply.");
        }

        // ── Statistics ────────────────────────────────────────────────────────

        private async Task ShowStatisticsAsync()
        {
            MenuHelpers.SafeClear();
            Console.WriteLine("═ Statistics ═\n");

            if (_repository == null) { Console.WriteLine("Repository not available."); return; }

            try
            {
                var index = await _repository.LoadIndexAsync();
                Console.WriteLine($"  Documents : {index.Files.Count}");
                Console.WriteLine($"  Sections  : {index.Metadata.TotalSections}");
                Console.WriteLine($"  Characters: {index.Metadata.TotalCharacters:N0}");
                Console.WriteLine($"  Avg/Section: {(index.Metadata.TotalSections > 0 ? index.Metadata.TotalCharacters / index.Metadata.TotalSections : 0):N0} chars");
                Console.WriteLine($"  Indexed at: {index.Metadata.ParsedAt}");
                Console.WriteLine();

                var byLevel = new Dictionary<int, int>();
                foreach (var sec in index.Metadata.Sections)
                {
                    if (!byLevel.ContainsKey(sec.HeadingLevel)) byLevel[sec.HeadingLevel] = 0;
                    byLevel[sec.HeadingLevel]++;
                }

                if (byLevel.Count > 0)
                {
                    Console.WriteLine("  Structure:");
                    foreach (var level in byLevel.OrderBy(x => x.Key))
                    {
                        var name = level.Key switch
                        {
                            0 => "Paragraphs",
                            1 => "Top-level headings",
                            2 => "Sub-headings",
                            3 => "Deep headings",
                            _ => $"Level {level.Key}"
                        };
                        Console.WriteLine($"    {name}: {level.Value}");
                    }
                    Console.WriteLine();
                }

                foreach (var f in index.Files)
                    Console.WriteLine($"  • {f.FilePath}  ({f.SectionCount} sections, {f.TotalCharacters:N0} chars)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        // ── Rebuild ───────────────────────────────────────────────────────────

        private async Task RebuildIndexAsync()
        {
            MenuHelpers.SafeClear();
            Console.WriteLine("═ Rebuild Index ═\n");

            if (_repository == null) { Console.WriteLine("Repository not available."); return; }

            try
            {
                Console.WriteLine("Rebuilding index...\n");
                var index = await _repository.RebuildIndexAsync();
                Console.WriteLine($"✓ Index rebuilt");
                Console.WriteLine($"  Documents: {index.Files.Count}");
                Console.WriteLine($"  Sections:  {index.Metadata.TotalSections}");
                Console.WriteLine($"  Characters: {index.Metadata.TotalCharacters:N0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
            }
        }
    }
}
