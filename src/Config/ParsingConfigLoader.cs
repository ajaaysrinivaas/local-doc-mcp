using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DocumentRagMcpServer.Models;

namespace DocumentRagMcpServer.Config
{
    /// <summary>
    /// Loads and saves the RAG configuration with organized directory structure:
    /// - Global config: config/global/rag-config.json (pipeline settings)
    /// - Global preprocessing: config/global/preprocessing.json (fallback preprocessing defaults)
    /// - Per-document configs: config/individual/{filename}.json (document-specific settings override global)
    /// 
    /// Precedence: Individual config > Global config
    /// All configs created with defaults on first access.
    /// </summary>
    public class ParsingConfigLoader
    {
        private readonly string _configDir;
        private readonly string _globalDir;
        private readonly string _individualDir;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        private RagConfig? _cached;

        public ParsingConfigLoader(string configDir)
        {
            _configDir = configDir;
            _globalDir = Path.Combine(configDir, "global");
            _individualDir = Path.Combine(configDir, "individual");
            try
            {
                Console.Error.WriteLine($"  [ParsingConfigLoader] Initializing with configDir: {configDir}");
                InitializeDefaults();
                Console.Error.WriteLine($"  [ParsingConfigLoader] Initialization complete");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [ParsingConfigLoader] Error during initialization: {ex.Message}");
                Console.Error.WriteLine($"  [ParsingConfigLoader] Exception type: {ex.GetType().Name}");
                // Continue anyway - we'll create files on demand
            }
        }

        /// <summary>Create default config files if they don't exist.</summary>
        private void InitializeDefaults()
        {
            try
            {
                Directory.CreateDirectory(_globalDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Failed to create global config dir: {ex.Message}");
            }

            try
            {
                Directory.CreateDirectory(_individualDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Failed to create individual config dir: {ex.Message}");
            }

            // Create global rag-config.json if missing
            var ragConfigPath = Path.Combine(_globalDir, "rag-config.json");
            try
            {
                if (!File.Exists(ragConfigPath))
                {
                    var defaultRagConfig = new RagConfig();
                    File.WriteAllText(ragConfigPath, JsonSerializer.Serialize(defaultRagConfig, _jsonOptions));
                    Console.Error.WriteLine($"✓ Created default config: {ragConfigPath}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Failed to create rag-config.json: {ex.Message}");
            }

            // Create global preprocessing.json if missing
            var globalPrepPath = Path.Combine(_globalDir, "preprocessing.json");
            try
            {
                if (!File.Exists(globalPrepPath))
                {
                    var defaultPrep = new { preprocessing = new PreprocessingConfig() };
                    File.WriteAllText(globalPrepPath, JsonSerializer.Serialize(defaultPrep, _jsonOptions));
                    Console.Error.WriteLine($"✓ Created default config: {globalPrepPath}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Failed to create preprocessing.json: {ex.Message}");
            }
        }

        /// <summary>Load global configuration from config/global/rag-config.json.</summary>
        public RagConfig LoadConfig()
        {
            if (_cached != null) return _cached;

            var path = Path.Combine(_globalDir, "rag-config.json");
            if (File.Exists(path))
            {
                try
                {
                    var content = File.ReadAllText(path, System.Text.Encoding.UTF8);

                    // Handle empty file
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _cached = new RagConfig();
                        SaveConfig(_cached);
                        return _cached;
                    }

                    _cached = JsonSerializer.Deserialize<RagConfig>(content, _jsonOptions)
                        ?? new RagConfig();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to load rag-config.json: {ex.Message}");
                    _cached = new RagConfig();
                    SaveConfig(_cached);
                }
            }
            else
            {
                _cached = new RagConfig();
            }

            return _cached;
        }

        /// <summary>Save global configuration to config/global/rag-config.json.</summary>
        public void SaveConfig(RagConfig config)
        {
            Directory.CreateDirectory(_globalDir);
            File.WriteAllText(
                Path.Combine(_globalDir, "rag-config.json"),
                JsonSerializer.Serialize(config, _jsonOptions),
                System.Text.Encoding.UTF8);
            _cached = config;
        }

        /// <summary>Load per-document configuration from config/individual/{filename}.json (precedence over global).</summary>
        public DocumentConfig? LoadDocumentConfig(string filename)
        {
            // Filename is like "icf-english-red-book.txt"
            // Config file is "icf-english-red-book.json" in config/individual/
            var configFilename = Path.GetFileNameWithoutExtension(filename) + ".json";
            var configPath = Path.Combine(_individualDir, configFilename);

            if (!File.Exists(configPath))
                return null;

            try
            {
                var content = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(content))
                    return null;

                return JsonSerializer.Deserialize<DocumentConfig>(content, _jsonOptions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to load {configFilename}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Save per-document configuration to config/individual/{filename}.json.</summary>
        public void SaveDocumentConfig(string filename, DocumentConfig config)
        {
            Directory.CreateDirectory(_individualDir);
            var configFilename = Path.GetFileNameWithoutExtension(filename) + ".json";
            var configPath = Path.Combine(_individualDir, configFilename);
            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(config, _jsonOptions),
                System.Text.Encoding.UTF8);
        }

        /// <summary>Get path to global preprocessing config (for fallback when no per-doc config exists).</summary>
        public string GetGlobalPreprocessingConfigPath() => Path.Combine(_globalDir, "preprocessing.json");

        /// <summary>List all individual document configs that exist.</summary>
        public List<string> GetDocumentConfigNames()
        {
            if (!Directory.Exists(_individualDir))
                return new List<string>();

            return Directory.GetFiles(_individualDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
        }

        /// <summary>Clear the in-memory cache so the next LoadConfig reads from disk.</summary>
        public void Invalidate() => _cached = null;
    }
}

