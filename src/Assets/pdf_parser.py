#!/usr/bin/env python3
"""
PDF to Text Converter - Entry point for Document RAG MCP Server
Converts entire PDF files to text with formatting preservation (bold, italic, headings)
Uses pymupdf (fitz) for low-level font and formatting extraction.
"""

import re
import sys
import fitz  # pymupdf
from pathlib import Path


class PDFConverter:
    def __init__(self, pdf_path: str) -> None:
        self.pdf_path = Path(pdf_path)
        self.pdf = None
        
    def __enter__(self):
        self.pdf = fitz.open(self.pdf_path)
        return self
        
    def __exit__(self, exc_type, exc_val, exc_tb):
        if self.pdf:
            self.pdf.close()
    
    def get_page_count(self) -> int:
        """Get total number of pages in PDF"""
        if not self.pdf:
            with fitz.open(self.pdf_path) as pdf:
                return len(pdf)
        return len(self.pdf)
    
    def _is_bold(self, font_name: str) -> bool:
        """Infer bold from font name"""
        return "bold" in font_name.lower() or "bd" in font_name.lower()
    
    def _is_italic(self, font_name: str) -> bool:
        """Infer italic from font name"""
        return "italic" in font_name.lower() or "it" in font_name.lower() or "oblique" in font_name.lower()
    
    def _infer_heading_level(self, font_size: float, base_size: float = 12.0) -> int:
        """Infer heading level from font size relative to base"""
        if font_size >= base_size * 2.2:  # H1
            return 1
        elif font_size >= base_size * 1.8:  # H2
            return 2
        elif font_size >= base_size * 1.5:  # H3
            return 3
        return 0  # Regular text
    
    def _apply_markdown_formatting(self, text: str, font_name: str, font_size: float, base_size: float) -> str:
        """Apply markdown formatting based on font attributes"""
        if not text.strip():
            return text
        
        heading_level = self._infer_heading_level(font_size, base_size)
        is_bold = self._is_bold(font_name)
        is_italic = self._is_italic(font_name)
        
        # Heading takes precedence
        if heading_level > 0:
            return "#" * heading_level + " " + text
        
        # Apply bold and/or italic
        if is_bold and is_italic:
            return f"***{text}***"
        elif is_bold:
            return f"**{text}**"
        elif is_italic:
            return f"_{text}_"
        
        return text
    
    def convert_to_text(self, start_page: int = 0, end_page: int = None) -> str:
        """
        Convert entire PDF to text with markdown formatting for bold, italic, headings
        
        Args:
            start_page: 0-indexed start page (default: 0)
            end_page: 0-indexed end page (default: last page)
        
        Returns:
            Extracted text with markdown annotations for formatting
        """
        if not self.pdf:
            raise ValueError("PDF not opened. Use 'with' statement")
        
        end_page = end_page or len(self.pdf) - 1
        text_content = []
        
        # Detect base font size from first page (for relative heading detection)
        base_size = self._detect_base_font_size()
        
        for i in range(start_page, min(end_page + 1, len(self.pdf))):
            page = self.pdf[i]
            page_text = self._extract_page_with_formatting(page, base_size)
            
            # Page number highlighted with clear markers
            page_marker = f"\n{'='*80}\nPDF PAGE {i + 1}\n{'='*80}\n"
            text_content.append(page_marker + page_text)
        
        return "\n".join(text_content)
    
    def _detect_base_font_size(self) -> float:
        """Detect base font size from first page (most common body text size)"""
        if not self.pdf or len(self.pdf) == 0:
            return 12.0
        
        page = self.pdf[0]
        font_sizes = []
        
        try:
            blocks = page.get_text("dict")["blocks"]
            for block in blocks:
                if block["type"] == 0:  # Text block
                    for line in block["lines"]:
                        for span in line["spans"]:
                            font_sizes.append(span["size"])
        except Exception as e:
            print(f"Warning: Could not detect base font size: {e}", file=sys.stderr)
            return 12.0
        
        if not font_sizes:
            return 12.0
        
        # Return median size (most common body text)
        font_sizes.sort()
        return font_sizes[len(font_sizes) // 2]
    
    def _extract_page_with_formatting(self, page, base_size: float) -> str:
        """Extract page text with markdown formatting annotations"""
        lines = []

        try:
            blocks = page.get_text("dict")["blocks"]
            for block in blocks:
                if block["type"] == 0:  # Text block
                    for line in block["lines"]:
                        line_parts = []

                        for span in line["spans"]:
                            text = span["text"]
                            # Skip spans that are purely whitespace / layout padding
                            if not text.strip():
                                continue
                            font_name = span["font"]
                            font_size = span["size"]

                            formatted_text = self._apply_markdown_formatting(
                                text.strip(), font_name, font_size, base_size
                            )
                            line_parts.append(formatted_text)

                        assembled = " ".join(line_parts).strip()
                        if assembled:
                            lines.append(assembled)
        except Exception as e:
            print(f"Warning: Error extracting formatting: {e}", file=sys.stderr)
            # Fallback to plain text
            lines = [l.strip() for l in page.get_text().split('\n') if l.strip()]

        return self._clean_page_text("\n".join(lines))

    def _clean_page_text(self, text: str) -> str:
        """
        Post-process extracted page text:
        - Strip trailing/leading whitespace from every line
        - Collapse runs of 2+ blank lines into a single blank line
        - Remove lines that are only whitespace or isolated digits (page numbers)
        """
        lines = text.split('\n')
        cleaned = []
        for line in lines:
            stripped = line.strip()
            # Drop lines that are only digits (stray page-number artefacts)
            if re.fullmatch(r'\d{1,4}', stripped):
                continue
            cleaned.append(stripped)

        # Collapse consecutive blank lines to at most one
        result = []
        prev_blank = False
        for line in cleaned:
            is_blank = (line == '')
            if is_blank and prev_blank:
                continue  # skip extra blank
            result.append(line)
            prev_blank = is_blank

        return '\n'.join(result).strip()


def main():
    """Entry point called by C# wrapper"""
    if len(sys.argv) != 2:
        print("Usage: pdf_parser.py <pdf_file>", file=sys.stderr)
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    
    try:
        with PDFConverter(pdf_path) as converter:
            text = converter.convert_to_text()
            print(text)
            sys.exit(0)
    except FileNotFoundError:
        print(f"Error: PDF file not found: {pdf_path}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"Error: Failed to convert PDF: {str(e)}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
