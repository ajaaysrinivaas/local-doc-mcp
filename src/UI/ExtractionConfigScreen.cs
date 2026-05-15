using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentRagMcpServer.Config;
using DocumentRagMcpServer.Models;
using DocumentRagMcpServer.Parsers;

namespace DocumentRagMcpServer.UI
{
    /// <summary>
    /// Extraction configuration screen.
    /// Two strategies: Bold headings (common in books/manuals) and Pattern-based (keywords or regex).
    /// </summary>
    public class ExtractionConfigScreen
    {
        private readonly string _rootPath;
        private readonly ParsingConfigLoader _configLoader;

        public ExtractionConfigScreen(string rootPath, ParsingConfigLoader configLoader)
        {
            _rootPath = rootPath;
            _configLoader = configLoader;
        }

        public async Task ShowAsync()
        {
            MenuHelpers.SafeClear();
            Console.WriteLine("═ Extraction Config ═\n");

            var docsDir = Path.Combine(_rootPath, "documents");
            if (!Directory.Exists(docsDir))
            {
                Console.WriteLine("No documents directory found.");
                return;
            }

            var docFiles = Directory.GetFiles(docsDir, "*.txt")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(f => f)
                .ToList();

            if (docFiles.Count == 0)
            {
                Console.WriteLine("No documents found in documents/ directory.");
                return;
            }

            for (int i = 0; i < docFiles.Count; i++)
                Console.WriteLine($"  {i + 1}. {docFiles[i]}");

            Console.WriteLine();
            var docChoice = MenuHelpers.Prompt("Select document: ").Trim();
            if (!int.TryParse(docChoice, out int idx) || idx < 1 || idx > docFiles.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            await ConfigureDocumentAsync(docFiles[idx - 1]);
        }

        private async Task ConfigureDocumentAsync(string docName)
        {
            var configPath = Path.Combine(_rootPath, "config", $"{docName}.json");
            var config = File.Exists(configPath)
                ? JsonSerializer.Deserialize<DocumentConfig>(File.ReadAllText(configPath),
                      new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                  ?? new DocumentConfig()
                : new DocumentConfig();

            bool modified = false;

            while (true)
            {
                MenuHelpers.SafeClear();
                PrintMenu(docName, config);

                var choice = MenuHelpers.Prompt("> ").Trim();

                switch (choice)
                {
                    case "1":
                        config.ExtractByBold = !config.ExtractByBold;
                        modified = true;
                        Console.WriteLine($"  Bold extraction: {(config.ExtractByBold ? "ON" : "off")}");
                        MenuHelpers.PressAnyKey();
                        break;

                    case "2":
                        if (EditPatterns(config)) modified = true;
                        break;

                    case "3":
                        if (EditSectionSize(config)) modified = true;
                        break;

                    case "4":
                        await PreviewAsync(docName, config);
                        break;

                    case "5":
                        SaveConfig(docName, config);
                        modified = false;
                        Console.WriteLine("  ✓ Saved.");
                        MenuHelpers.PressAnyKey();
                        break;

                    case "0":
                        if (modified)
                        {
                            var r = MenuHelpers.Prompt("Unsaved changes — save? (y/n): ").ToLower();
                            if (r == "y") SaveConfig(docName, config);
                        }
                        return;

                    default:
                        Console.WriteLine("Invalid choice.");
                        MenuHelpers.PressAnyKey();
                        break;
                }
            }
        }

        private void PrintMenu(string docName, DocumentConfig config)
        {
            var boldStatus = config.ExtractByBold ? "ON " : "off";
            var patStatus = config.ExtractByPatterns ? "ON " : "off";
            var patCount = config.CustomHeadingPatterns?.Count ?? 0;

            Console.WriteLine($"═ Extraction: {docName} ═\n");
            Console.WriteLine($"  1. Bold headings          [{boldStatus}]  (** text ** markers)");
            Console.WriteLine($"  2. Patterns / keywords    [{patStatus}]  ({patCount} pattern(s) configured)");
            Console.WriteLine($"  3. Section size                    ({config.MaxSectionSize} chars/section limit)");
            Console.WriteLine($"  4. Preview sections");
            Console.WriteLine($"  5. Save");
            Console.WriteLine($"  0. Back");
            Console.WriteLine();
        }

        // ── Bold toggle is a single keypress — handled inline above ──────────

        // ── Patterns ─────────────────────────────────────────────────────────

        private bool EditPatterns(DocumentConfig config)
        {
            bool modified = false;

            while (true)
            {
                MenuHelpers.SafeClear();
                Console.WriteLine("═ Patterns / Keywords ═\n");
                Console.WriteLine("  Each entry is tried as a regex first, then as a plain keyword.");
                Console.WriteLine("  Examples:  Introduction    |  (?i)^chapter\\s+\\d+  |  1\\.\\d+\n");

                var patterns = config.CustomHeadingPatterns ?? new List<string>();

                if (patterns.Count == 0)
                    Console.WriteLine("  (none)\n");
                else
                    for (int i = 0; i < patterns.Count; i++)
                        Console.WriteLine($"  {i + 1}. {patterns[i]}");

                Console.WriteLine();

                var enabled = config.ExtractByPatterns;
                Console.WriteLine($"  [a] Add pattern");
                Console.WriteLine($"  [r] Remove pattern by number");
                Console.WriteLine($"  [t] Toggle patterns {(enabled ? "OFF" : "ON")} (currently {(enabled ? "ON" : "off")})");
                Console.WriteLine($"  [0] Back");
                Console.WriteLine();

                var choice = MenuHelpers.Prompt("> ").Trim().ToLower();

                switch (choice)
                {
                    case "a":
                        var entry = MenuHelpers.Prompt("Pattern (keyword or regex): ").Trim();
                        if (string.IsNullOrWhiteSpace(entry)) break;
                        ValidatePattern(entry);
                        config.CustomHeadingPatterns ??= new List<string>();
                        config.CustomHeadingPatterns.Add(entry);
                        modified = true;
                        break;

                    case "r":
                        if (patterns.Count == 0) { Console.WriteLine("No patterns to remove."); MenuHelpers.PressAnyKey(); break; }
                        var numStr = MenuHelpers.Prompt("Remove number: ").Trim();
                        if (int.TryParse(numStr, out int n) && n >= 1 && n <= patterns.Count)
                        {
                            patterns.RemoveAt(n - 1);
                            config.CustomHeadingPatterns = patterns;
                            modified = true;
                            Console.WriteLine("  ✓ Removed.");
                        }
                        else Console.WriteLine("  Invalid number.");
                        MenuHelpers.PressAnyKey();
                        break;

                    case "t":
                        config.ExtractByPatterns = !config.ExtractByPatterns;
                        modified = true;
                        Console.WriteLine($"  Patterns: {(config.ExtractByPatterns ? "ON" : "off")}");
                        MenuHelpers.PressAnyKey();
                        break;

                    case "0":
                        return modified;

                    default:
                        break;
                }
            }
        }

        private static void ValidatePattern(string pattern)
        {
            try
            {
                _ = new Regex(pattern);
                Console.WriteLine("  ✓ Valid regex.");
            }
            catch
            {
                Console.WriteLine("  ⚠ Not a valid regex — will be used as a plain keyword.");
            }
        }

        // ── Section size ──────────────────────────────────────────────────────

        private bool EditSectionSize(DocumentConfig config)
        {
            MenuHelpers.SafeClear();
            Console.WriteLine("═ Section Size ═\n");
            Console.WriteLine($"  Current: {config.MaxSectionSize} max chars/section");
            Console.WriteLine("  (Sections exceeding this limit will be split at sentence boundaries)");
            Console.WriteLine();
            var input = MenuHelpers.Prompt("New max size (or Enter to keep): ").Trim();
            if (int.TryParse(input, out int v) && v >= 1000)
            {
                config.MaxSectionSize = v;
                Console.WriteLine($"  ✓ Set to {v}.");
                MenuHelpers.PressAnyKey();
                return true;
            }
            if (v > 0 && v < 1000)
            {
                Console.WriteLine($"  ⚠ Minimum recommended size is 1000.");
                MenuHelpers.PressAnyKey();
            }
            return false;
        }

        // ── Preview ───────────────────────────────────────────────────────────

        private async Task PreviewAsync(string docName, DocumentConfig config)
        {
            MenuHelpers.SafeClear();
            Console.WriteLine("═ Preview ═\n");

            var docPath = Path.Combine(_rootPath, "documents", $"{docName}.txt");
            if (!File.Exists(docPath)) { Console.WriteLine("Document not found."); MenuHelpers.PressAnyKey(); return; }

            try
            {
                var content = File.ReadAllText(docPath);
                var parser = new GenericDocumentParser();
                var sections = parser.ExtractSections(content, config);

                if (sections.Count == 0)
                {
                    Console.WriteLine("No sections extracted with current config.");
                }
                else
                {
                    Console.WriteLine($"Extracted {sections.Count} sections (showing first 10):\n");
                    foreach (var sec in sections.Take(10))
                    {
                        var preview = sec.Content.Length > 120
                            ? sec.Content[..120].Replace("\n", " ") + "…"
                            : sec.Content.Replace("\n", " ");
                        Console.WriteLine($"  [{sec.HeadingLevel}] {sec.Title}");
                        Console.WriteLine($"      {preview}");
                        if (sec.PageNumber > 0) Console.WriteLine($"      page: {sec.PageNumber}");
                        Console.WriteLine();
                    }

                    if (sections.Count > 10) Console.WriteLine($"  … and {sections.Count - 10} more");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            await Task.CompletedTask;
            MenuHelpers.PressAnyKey();
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private void SaveConfig(string docName, DocumentConfig config)
        {
            var configDir = Path.Combine(_rootPath, "config");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(
                Path.Combine(configDir, $"{docName}.json"),
                JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));
        }
    }
}
