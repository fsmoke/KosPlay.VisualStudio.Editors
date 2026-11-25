using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ScintillaNET;

namespace KosPlay.VisualStudio.Editors
{
    // Uses an existing Lexilla lexer on a masked copy of the document.
    // Only styles and fold levels are copied back; the editor's text is never replaced.
    internal sealed class MacroAwareHighlighter : Component
    {
        internal const int MacroStyle = 64;
        internal static readonly Regex MacroPattern = new Regex(@"\$\([^()\r\n]*\)", RegexOptions.Compiled);
        private static readonly Regex MacroOnlyLinePattern = new Regex(
            @"\A[^\S\r\n]*(?:\$\([^()\r\n]*\)[^\S\r\n]*)+\z",
            RegexOptions.Compiled);
        internal bool CopyFoldLevels { get; set; } = true;
        private readonly Scintilla editor;
        private readonly Scintilla lexerEditor;
        private bool updating;
        private bool dirty = true;

        internal MacroAwareHighlighter(Scintilla editor)
        {
            this.editor = editor;
            lexerEditor = new Scintilla { Visible = false };
            lexerEditor.LexerName = "null";
            editor.LexerName = string.Empty;
            editor.TextChanged += EditorTextChanged;
            editor.StyleNeeded += EditorStyleNeeded;
        }

        internal string LexerName
        {
            get { return lexerEditor.LexerName; }
            set
            {
                lexerEditor.LexerName = value;
                dirty = true;
            }
        }

        internal void SetKeywords(int set, string keywords)
        {
            lexerEditor.SetKeywords(set, keywords);
            dirty = true;
        }

        internal void SetProperty(string name, string value)
        {
            lexerEditor.SetProperty(name, value);
            dirty = true;
        }

        private void EditorTextChanged(object sender, EventArgs e)
        {
            dirty = true;
            RefreshHighlighting();
        }

        private void EditorStyleNeeded(object sender, StyleNeededEventArgs e)
        {
            if (dirty)
                RefreshHighlighting();
        }

        internal void RefreshHighlighting()
        {
            if (updating || editor.IsDisposed)
                return;

            updating = true;
            try
            {
                string text = editor.Text;
                MatchCollection macros = MacroPattern.Matches(text);
                char[] masked = text.ToCharArray();
                foreach (Match macro in macros)
                {
                    // Identifier characters cannot open strings, comments, substitutions or folds.
                    // Keep UTF-16 offsets and line breaks aligned with the original document.
                    for (int i = macro.Index; i < macro.Index + macro.Length; i++)
                        masked[i] = 'x';
                }

                var excludedFoldLines = new bool[editor.Lines.Count];
                for (int line = 0; line < editor.Lines.Count; line++)
                {
                    int start = editor.Lines[line].Position;
                    int end = editor.Lines[line].EndPosition;
                    while (end > start && (text[end - 1] == '\r' || text[end - 1] == '\n'))
                        end--;
                    if (!MacroOnlyLinePattern.IsMatch(text.Substring(start, end - start)))
                        continue;
                    excludedFoldLines[line] = true;
                    for (int i = start; i < end; i++)
                        masked[i] = ' ';
                }

                lexerEditor.Text = new string(masked);
                lexerEditor.EmptyUndoBuffer();
                lexerEditor.ClearDocumentStyle();
                lexerEditor.Colorize(0, lexerEditor.TextLength);

                int[] styles = new int[text.Length];
                for (int i = 0; i < styles.Length; i++)
                    styles[i] = lexerEditor.GetStyleAt(i);
                foreach (Match macro in macros)
                {
                    for (int i = macro.Index; i < macro.Index + macro.Length; i++)
                        styles[i] = MacroStyle;
                }

                editor.StartStyling(0);
                for (int start = 0; start < styles.Length;)
                {
                    int end = start + 1;
                    while (end < styles.Length && styles[end] == styles[start])
                        end++;
                    editor.SetStyling(end - start, styles[start]);
                    start = end;
                }

                for (int i = 0; CopyFoldLevels && i < editor.Lines.Count; i++)
                {
                    // Set both level and flags in one operation, so Scintilla does not see
                    // an intermediate fold tree and expand unrelated collapsed blocks.
                    int level = lexerEditor.Lines[i].FoldLevel | (int)lexerEditor.Lines[i].FoldLevelFlags;
                    if (excludedFoldLines[i])
                        level &= ~(int)FoldLevelFlags.Header;
                    editor.DirectMessage(SciApi.SCI_SETFOLDLEVEL, new IntPtr(i), new IntPtr(level));
                }
                dirty = false;
            }
            finally
            {
                updating = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                editor.TextChanged -= EditorTextChanged;
                editor.StyleNeeded -= EditorStyleNeeded;
                lexerEditor.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
