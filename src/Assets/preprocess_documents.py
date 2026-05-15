#!/usr/bin/env python3
"""
Document Preprocessor - Configurable text cleaning pipeline
Strips markdown, normalizes line breaks, removes page markers based on config
"""

import sys
import re
import json
from pathlib import Path


class PreprocessingConfig:
    """Configuration for preprocessing pipeline"""
    
    def __init__(self, config_dict=None):
        if config_dict is None:
            config_dict = {}
        
        # Encoding cleanup (ENABLED BY DEFAULT)
        self.clean_encoding = config_dict.get("cleanEncoding", True)
        
        # Markdown handling (disabled by default - preserve content)
        self.strip_bold = config_dict.get("stripBold", False)
        self.strip_italic = config_dict.get("stripItalic", False)
        self.strip_headers = config_dict.get("stripHeaders", False)
        self.preserve_markdown = config_dict.get("preserveMarkdown", True)  # Default: preserve formatting
        
        # Page markers
        self.remove_page_markers = config_dict.get("removePageMarkers", False)
        
        # Line breaks (disabled by default to preserve structure)
        self.normalize_line_breaks = config_dict.get("normalizeLineBreaks", False)
        self.join_short_lines = config_dict.get("joinShortLines", False)
        
    def should_strip_formatting(self):
        """Check if any formatting stripping is enabled"""
        return not self.preserve_markdown and (self.strip_bold or self.strip_italic or self.strip_headers)


class Preprocessor:
    def __init__(self, config: PreprocessingConfig = None):
        self.config = config or PreprocessingConfig()
    
    def process(self, content: str) -> str:
        """Apply preprocessing pipeline based on config"""
        # Order matters: encoding first, then markers, then formatting, then line breaks
        
        if self.config.clean_encoding:
            content = self._clean_encoding(content)
        
        if self.config.remove_page_markers:
            content = self._remove_page_markers(content)
        
        if self.config.should_strip_formatting():
            if self.config.strip_headers:
                content = self._strip_headers(content)
            if self.config.strip_bold:
                content = self._strip_bold(content)
            if self.config.strip_italic:
                content = self._strip_italic(content)
        
        if self.config.normalize_line_breaks:
            content = self._normalize_line_breaks(content)
        
        if self.config.join_short_lines:
            content = self._join_short_lines(content)
        
        return content
    
    @staticmethod
    def _clean_encoding(content: str) -> str:
        """
        Clean encoding artifacts and invalid Unicode sequences.
        - Removes stray \r (carriage returns) - primary culprit
        - Removes control characters except newline/tab
        - Fixes mojibake from encoding/decoding errors
        - Normalizes whitespace (tabs to spaces, multiple spaces to single)
        - Removes zero-width and invisible characters
        - Cleans up repeated escaped sequences
        """
        import unicodedata
        
        # 1. CRITICAL: Remove all carriage returns (\r) which appear as literal chars
        content = content.replace('\r', '')
        content = content.replace('\\r', '')  # Also remove escaped versions
        
        # 2. Remove escaped newlines that became literal text
        content = content.replace('\\n', ' ')  # Escaped \n becomes space, not newline
        
        # 3. Remove common control characters (except newline, tab)
        # This filters out category 'C' (control) chars
        content = ''.join(
            ch for ch in content 
            if unicodedata.category(ch)[0] != 'C' or ch in '\n\t'
        )
        
        # 4. Remove zero-width and invisible characters
        zero_width_chars = [
            '\u200b',  # Zero-width space
            '\u200c',  # Zero-width non-joiner
            '\u200d',  # Zero-width joiner
            '\ufeff',  # Zero-width no-break space (BOM)
            '\xad',    # Soft hyphen
            '\u2060',  # Word joiner
            '\u2061',  # Function application
        ]
        for char in zero_width_chars:
            content = content.replace(char, '')
        
        # 5. Clean up mojibake patterns (encoding errors from PDF extraction)
        # Common UTF-8 sequences misinterpreted as Latin-1
        mojibake_replacements = {
            '\u0393\u00C7\u00FF': "'",     # ΓÇ¥ → ' (right single quote)
            '\u0393\u00C7\u00D6': '"',     # ΓÇÖ → " (left double quote)
            '\u0393\u00C7\u00D7': '"',     # ΓÇ× → " (right double quote)
            '\u0393\u00C7\u00A3': '«',     # ΓÇ£ → « (left guillemet)
            '\u0393\u00C7\u00A4': '»',     # ΓÇ¤ → » (right guillemet)
            '\u0393\u0082': '',            # Γ‚ → remove (control char)
            '\u00E2\u0082': '',            # â‚ → remove (control sequence)
            'ΓÇô': '-',                     # em-dash mojibake
            'ΓÇ£': '«',                     # left guillemet
            'ΓÇ¤': '»',                     # right guillemet
        }
        for mojibake, replacement in mojibake_replacements.items():
            content = content.replace(mojibake, replacement)
        
        # 6. Remove remaining corrupted character sequences
        # Patterns like ├£, ├╝, etc. from broken UTF-8
        content = re.sub(r'[├┤┬┴─│┐┌└┘]', '', content)  # Box drawing chars (encoding errors)
        content = re.sub(r'[\u0080-\u009F]', '', content)  # C1 control characters
        
        # 7. Clean repeated whitespace and line breaks
        # Multiple spaces to single space
        content = re.sub(r' +', ' ', content)
        # Multiple newlines to double newline (preserve paragraph breaks)
        content = re.sub(r'\n{3,}', '\n\n', content)
        # Tabs to spaces
        content = re.sub(r'\t', ' ', content)
        
        # 8. Strip trailing whitespace from each line
        lines = content.split('\n')
        lines = [line.rstrip() for line in lines]
        content = '\n'.join(lines)
        
        return content
    
    @staticmethod
    def _remove_page_markers(content: str) -> str:
        """Remove PDF PAGE N markers (80 char separator lines)"""
        # Remove pattern: ====...==== / PDF PAGE N / ====...====
        pattern = r'\n={40,}\s*\nPDF PAGE \d+\s*\n={40,}\s*\n'
        return re.sub(pattern, '\n', content)
    
    @staticmethod
    def _strip_headers(content: str) -> str:
        """Remove markdown headers (# ## ###) but keep text"""
        # Replace "# Header" with "Header"
        content = re.sub(r'^#+\s+', '', content, flags=re.MULTILINE)
        return content
    
    @staticmethod
    def _strip_bold(content: str) -> str:
        """Remove markdown bold (**text**) -> text"""
        return re.sub(r'\*\*([^*]+)\*\*', r'\1', content)
    
    @staticmethod
    def _strip_italic(content: str) -> str:
        """Remove markdown italic (_text_ or *text*) -> text"""
        content = re.sub(r'_([^_]+)_', r'\1', content)
        content = re.sub(r'(?<!\*)\*([^*]+)\*(?!\*)', r'\1', content)  # Single * only
        return content
    
    @staticmethod
    def _normalize_line_breaks(content: str) -> str:
        """
        Intelligent line break normalization.
        Keeps newlines after sentence endings (.!?;—–…)
        Joins mid-sentence lines with space.
        Preserves paragraph breaks (\n\n).
        """
        lines = content.split('\n')
        result = []
        
        for i, line in enumerate(lines):
            stripped = line.rstrip()
            
            if not stripped:
                # Preserve blank lines (paragraphs)
                result.append('')
                continue
            
            # If previous line ended with sentence-ending punctuation or special markers
            if i > 0 and result:
                prev = result[-1].rstrip()
                if prev and (prev[-1] in '.!?;—–…' or 
                           prev.startswith('#') or 
                           re.search(r'\d+\.\d+\s*$', prev)):
                    # Previous line ended a sentence/heading/section
                    result.append(stripped)
                else:
                    # Mid-sentence: join with space
                    result[-1] = (result[-1] + ' ' + stripped).strip()
            else:
                result.append(stripped)
        
        # Rebuild with original paragraph breaks
        return '\n'.join(result)
    
    @staticmethod
    def _join_short_lines(content: str) -> str:
        """
        Join lines shorter than threshold with next line.
        Useful for poorly formatted PDFs with artificial line breaks.
        """
        min_length = 80  # Lines shorter than this might be artificial breaks
        lines = content.split('\n')
        result = []
        i = 0
        
        while i < len(lines):
            line = lines[i].rstrip()
            
            # If this line is short and there's a next line, try to join
            if i < len(lines) - 1 and len(line) < min_length and line:
                next_line = lines[i + 1].lstrip()
                # Join if next line doesn't look like a new section
                if next_line and not re.match(r'^[#\d]|^PDF PAGE', next_line):
                    result.append((line + ' ' + next_line).strip())
                    i += 2
                    continue
            
            result.append(line)
            i += 1
        
        return '\n'.join(result)


def load_config(config_path: str = None) -> PreprocessingConfig:
    """Load preprocessing config from JSON file, or use defaults"""
    if config_path and Path(config_path).exists():
        try:
            with open(config_path) as f:
                data = json.load(f)
                return PreprocessingConfig(data.get("preprocessing", {}))
        except Exception as e:
            print(f"Warning: Could not load config from {config_path}: {e}", file=sys.stderr)
    
    return PreprocessingConfig()


def process_file(input_path: str, output_path: str, config: PreprocessingConfig):
    """Process single file with preprocessing pipeline"""
    try:
        # Explicitly read as UTF-8 with error handling
        with open(input_path, 'r', encoding='utf-8-sig') as f:
            content = f.read()
        
        # Log initial state for debugging
        has_carriage_return = '\r' in content
        has_mojibake = any(char in content for char in ['Γ', '£', '├', '┤'])
        
        preprocessor = Preprocessor(config)
        processed = preprocessor.process(content)
        
        # Verify cleaning was effective
        cleaned_carriage = '\r' not in processed and '\\r' not in processed
        cleaned_mojibake = not any(char in processed for char in ['Γ', '£', '├', '┤'])
        
        # Explicitly write as UTF-8 without BOM
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write(processed)
        
        # Report state changes for debugging
        status = []
        if has_carriage_return and cleaned_carriage:
            status.append("cleaned carriage returns")
        if has_mojibake and cleaned_mojibake:
            status.append("removed mojibake")
        
        status_msg = f" ({', '.join(status)})" if status else ""
        print(f"✓ {Path(input_path).name} → {Path(output_path).name} ({len(processed)} chars){status_msg}")
        return True
    except Exception as e:
        print(f"✗ Failed to process {input_path}: {e}", file=sys.stderr)
        return False


def main():
    """Entry point.

    Single-file mode:  preprocess_documents.py <input.txt> <output.txt> [config_file]
    Directory mode:    preprocess_documents.py <input_dir>  <output_dir>  [config_file]
    """
    if len(sys.argv) < 3:
        print("Usage: preprocess_documents.py <input_file_or_dir> <output_file_or_dir> [config_file]", file=sys.stderr)
        sys.exit(1)

    input_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])
    config_path = sys.argv[3] if len(sys.argv) > 3 else None

    config = load_config(config_path)

    # ── Single-file mode ────────────────────────────────────────────────────
    if input_path.is_file():
        output_path.parent.mkdir(parents=True, exist_ok=True)
        success = process_file(str(input_path), str(output_path), config)
        sys.exit(0 if success else 1)

    # ── Directory mode ──────────────────────────────────────────────────────
    input_dir = input_path
    output_dir = output_path

    if not input_dir.exists():
        print(f"Error: Input directory not found: {input_dir}", file=sys.stderr)
        sys.exit(1)

    output_dir.mkdir(parents=True, exist_ok=True)

    txt_files = list(input_dir.glob("*.txt"))
    if not txt_files:
        print(f"Warning: No .txt files found in {input_dir}", file=sys.stderr)
        sys.exit(0)
    
    print(f"Processing {len(txt_files)} files with config:")
    print(f"  - cleanEncoding: {config.clean_encoding}")
    print(f"  - stripBold: {config.strip_bold}")
    print(f"  - stripItalic: {config.strip_italic}")
    print(f"  - stripHeaders: {config.strip_headers}")
    print(f"  - removePageMarkers: {config.remove_page_markers}")
    print(f"  - normalizeLineBreaks: {config.normalize_line_breaks}")
    print(f"  - joinShortLines: {config.join_short_lines}")
    print()
    
    success_count = 0
    for input_file in sorted(txt_files):
        output_file = output_dir / input_file.name
        if process_file(str(input_file), str(output_file), config):
            success_count += 1
    
    print(f"\nCompleted: {success_count}/{len(txt_files)} files processed")
    sys.exit(0 if success_count == len(txt_files) else 1)


if __name__ == "__main__":
    main()
