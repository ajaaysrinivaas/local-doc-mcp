# Document RAG MCP Server

**Give any LLM (Claude, ChatGPT, etc.) intelligent access to your local documents.**

A portable, self-contained MCP server that exposes your PDFs and text files as searchable tools for language models. Ask your LLM to find specific information, compare sources, ground reasoning in your materials, or extract structured data—all without uploading files to the cloud.

Perfect for:
- **Educational materials** — Ask Claude questions about textbooks, papers, or study guides
- **Data extraction** — Parse documents into CSVs or structured formats (e.g., extract ICF codes from reference materials)
- **Research & analysis** — Compare information across multiple sources, identify patterns
- **Grounding LLM reasoning** — Use your codebase, documentation, or domain knowledge to inform AI responses
- **Privacy-first workflows** — Keep sensitive documents local; they never leave your machine

---

## How It Works

1. **Point it at your documents** (PDFs, .txt, etc.)
2. **Run the server** (standalone executable, no dependencies)
3. **Configure your LLM** to use the MCP server as a tool
4. **Ask your LLM to search and retrieve** — It handles the indexing and ranking

The server builds a BM25 search index and exposes it via the Model Context Protocol, so any MCP-compatible client (Claude, local models with MCP support, custom integrations) can use it.

---

## Quick Start

### 1. Get the executable

Download the latest release from GitHub (or [build from source](#building)).

**Windows:** `DocumentRagMcpServer.exe` (portable, no installation needed)  
**Linux/WSL:** `DocumentRagMcpServer` (or build with `dotnet publish`)

### 2. (Optional) Install Python for PDF support

If you're working with PDFs, ensure Python is installed and install the parsing dependencies:

```bash
pip install -r requirements.txt
```

**Dependencies:**
- `PyMuPDF` — Extract text from PDFs
- `click`, `colorama`, `pathlib2` — CLI and formatting utilities

On Windows, make sure the `python` or `py` command works in a normal terminal (any Python version will do). If you see a Microsoft Store prompt, disable the `python.exe`/`py.exe` app execution alias in Settings → Apps → Advanced app settings.

If you only have `.txt` or `.md` files, Python is not required.

### 3. Add your documents

Place files in your data directory:

```
{data-path}/
├── raw/              ← Drop PDFs and other source files here
├── documents/        ← Auto-generated .txt files (or add .txt directly)
└── config/
    ├── global/
    │   ├── rag-config.json       ← Pipeline settings (auto-created)
    │   └── preprocessing.json   ← Default preprocessing (auto-created)
    └── individual/
        └── {filename}.json      ← Per-document settings (optional)
```

### 4. Run the server

```bash
# Windows
DocumentRagMcpServer.exe --data-path C:\path\to\your\data

# Linux/WSL
./DocumentRagMcpServer --data-path /path/to/your/data
```

First run shows an interactive setup UI (build index, preview settings). Subsequent runs start the server directly. To skip the UI:

```bash
DocumentRagMcpServer.exe --data-path C:\path\to\your\data --server
```

### 5. Configure your LLM

**Find your MCP config file:**
- **Claude Desktop (macOS):** `~/Library/Application\ Support/Claude/claude_desktop_config.json`
- **Claude Desktop (Windows):** `%APPDATA%\Claude\claude_desktop_config.json`

**Edit the config** and replace the paths in this template:

```json
{
  "servers": {
    "document-rag": {
      "type": "stdio",
      "command": "C:\\path\\to\\DocumentRagMcpServer.exe",
      "args": ["--data-path", "C:\\path\\to\\your\\data"]
    }
  }
}
```

**Replace:**
- `C:\path\to\DocumentRagMcpServer.exe` → Actual path to your .exe
- `C:\path\to\your\data` → Directory where you store documents (the one with `/raw/`, `/documents/`, `/config/`)

**Restart Claude Desktop.** The server will now appear as available tools: `search`, `get_section`, `get_page`, `list_files`.

---

## Examples

**Extract structured data from documents:**
> "Parse all ICF codes from the document. Return as CSV with Code, Description, and Category."

**Compare across materials:**
> "Search for 'functioning' in both documents. Summarize how they define it differently."

**Ground reasoning:**
> "Based on the codebase documentation, how should I implement this feature?"

**Find specific information:**
> "What page discusses the biopsychosocial model? Read the section and explain it."

---

## Tools Available to Your LLM

The server exposes four tools that your LLM can call:

### `search` — Find information

```
query (required): Keywords or phrase to search for
file_id (optional): Limit search to a specific document
limit (optional): Max results (1–20, default 5)
```

Returns top-ranked sections with previews, BM25 scores, and page numbers.

### `list_files` — See what's available

Lists all indexed documents with metadata. Use the returned `file_id` to scope searches.

### `get_section` — Read full content

```
section_id (required): From search results
include_context (optional): Include adjacent sections (true/false)
```

Returns the complete section with hierarchy information and adjacent sections if requested.

### `get_page` — Retrieve by page number

```
page_number (required): The page to retrieve
file_id (optional): Limit to a specific document
```

Useful when you know a section is on a specific page.

---

## Directory Structure

When you run the server for the first time, it automatically creates this structure in your data directory:

```
{data-path}/
├── raw/                      ← Your source files (PDFs, docs, etc.)
├── documents/                ← Parsed .txt files (auto-generated from raw/)
├── config/
│   ├── global/
│   │   ├── rag-config.json   ← Pipeline settings (auto-created)
│   │   └── preprocessing.json ← Default preprocessing (auto-created)
│   └── individual/
│       ├── {filename}.json   ← Per-document overrides (optional)
│       │                        Examples: document1.json, myfile.pdf.json
│       └── ...
├── .index/
│   └── document_index.json   ← BM25 search index (auto-generated)
└── logs/
    └── *.log                 ← Server logs (optional)
```

### Workflow

1. **Add documents** → Drop PDFs/docs in `/raw/`
2. **Run server** → First run: interactive setup (indexes everything)
3. **Subsequent runs** → Skips re-indexing unless files changed (smart caching)
4. **(Optional) Customize** → Create `/config/individual/{filename}.json` per document

### File Handling

- **PDFs in `/raw/`** → Auto-converted to `.txt` in `/documents/` via embedded Python parser
- **Existing `.txt` or `.md` in `/documents/`** → Used directly (no conversion needed)
- **Parsed content** → Indexed by `/config/` rules (bold headings, regex patterns, or fixed-size chunks)

---

## Configuration Files (Optional)

The server works out-of-the-box with sensible defaults. Customize behavior with optional JSON configs:

### Global settings (`config/global/rag-config.json`)

```json
{
  "enableUnifiedPipeline": true,
  "preprocessingConfigPath": null
}
```

### Per-document settings (`config/individual/{filename}.json`)

Override defaults for specific files:

```json
{
  "pageRange": { "start": 1, "end": 100 },
  "extractByBold": false,
  "extractByPatterns": false,
  "customHeadingPatterns": ["\\d+\\.\\d+\\s+"],
  "maxSectionSize": 1500,
  "deduplicateSections": true,
  "preprocessing": {
    "cleanEncoding": true,
    "preserveMarkdown": true
  }
}
```

These are auto-created on first run with defaults. Most users don't need to edit them.

---

## Section Extraction

Three strategies are tried in order, stopping at the first that produces sections:

1. **Bold** (`extractByBold: true`) — `**Heading**` lines define sections
2. **Pattern** (`extractByPatterns: true`) — Custom regex or keyword patterns
3. **Fixed-size** (always available) — Paragraph-boundary chunks at `maxSectionSize`

Sections are post-processed:
- Split at sentence boundaries if over `maxSectionSize` characters
- Deduplicated by title (when `deduplicateSections: true`)
- Page numbers assigned from `PDF PAGE N` markers

---

## Search Algorithm

BM25 with layered bonuses:

- **BM25 body score** — TF × IDF, length-normalised (k1=1.5, b=0.75)
- **Title match** — 3× IDF bonus per matching term
- **Exact phrase** — 2× bonus for all terms appearing consecutively
- **Keyword list** — 0.5× bonus per term found in section keywords

Result cache: 50 queries (FIFO eviction).

---

## CLI Arguments

| Argument | Description |
|----------|-------------|
| `--data-path`, `--root`, `--config-path` | Root directory for documents, config, and index |
| `--server`, `--no-setup` | Skip interactive setup UI |
| `--force-rebuild`, `--rebuild` | Force full index rebuild |

---

## Architecture

```
stdin/stdout (JSON-RPC 2.0)
    ↓
McpServer
    ↓
McpMessageHandler  (search, get_section, get_page, list_files)
    ├── SearchService         — BM25 ranking + result cache
    └── DocumentRepository    — index lifecycle + page retrieval
            ↓
    UnifiedPreprocessingPipeline
        ├── PythonPdfParser   — PDF → .txt via embedded pdf_parser.py
        └── preprocess_documents.py — encoding cleanup + markdown preservation
            ↓
    ParserFactory → GenericDocumentParser
        ├── BoldExtractionStrategy
        ├── PatternExtractionStrategy
        └── FixedSizeExtractionStrategy
            ↓
    DocumentIndex (.index/document_index.json)
```

---

## Privacy & Security

✅ **Documents never leave your machine.** The server runs locally; no data is uploaded or sent anywhere.

✅ **MCP communication is local only.** Your LLM client (Claude Desktop, local model, etc.) communicates with the server over stdin/stdout on your machine.

✅ **No network exposure by default.** The server doesn't listen on the network; it only communicates with your local LLM client.

For enterprise/sensitive use: You control the entire process. Review the source, build it yourself, and audit the binary.

---

## Building from Source

### Prerequisites

- **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Python 3.8+** (only if building PDF support)

### Build steps

```bash
# Clone and navigate
git clone https://github.com/ajaaysrinivaas/local-doc-mcp.git
cd local-doc-mcp

# Restore dependencies
dotnet restore src/

# Build
dotnet build src/ -c Release

# Run (from src directory)
cd src
dotnet run --configuration Release -- --data-path /path/to/data
```

### Publish as self-contained executable

```bash
# Windows (x64)
dotnet publish src/ -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish

# Linux (x64)
dotnet publish src/ -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish

# macOS (x64)
dotnet publish src/ -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true -o ./publish
```

Executable will be in `./publish/`

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

Permission is granted to use, modify, and distribute this software freely, including for commercial purposes. Attribution is appreciated but not required.
