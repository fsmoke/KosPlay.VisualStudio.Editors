using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using ScintillaNET;
using EditorStyle = ScintillaNET.Style;

namespace KosPlay.VisualStudio.Editors
{
    public class CommandEditorWindow : Form
    {
		public string CommandText => editor.Text;
		private readonly IContainer components = new Container();
        private readonly HashSet<string> completionKeywords = new HashSet<string>(StringComparer.Ordinal);
        private readonly Scintilla editor;
        private readonly string defaultWordChars;
        private readonly MacroAwareHighlighter highlighter;
        private readonly Scintilla previewEditor;
        private readonly MacroAwareHighlighter previewHighlighter;
        private const float BaseFontSize = 11f;
        private readonly ToolStripComboBox zoomSelector;
        private double zoomPercent = 100;
        private bool synchronizingZoom;
        private readonly ToolStripComboBox syntaxSelector;
        private readonly ToolStripButton previewToggleButton;
        private readonly ToolStripButton macrosToggleButton;
        private int lastEditorPosition;
        private readonly List<MacroRow> macros;
        private readonly List<string> macroCompletionMatches = new List<string>();
        private bool macroCompletionActive;
        private Timer? previewTimer = null;
        private int[] previewLineMap = new int[0];
        private bool synchronizingPreviewFolds;

        public CommandEditorWindow() : this("CommandEditor", string.Empty, string.Empty, new List<MacroRow>())
        {
        }

        internal CommandEditorWindow(string title, string initialValue, string defaultSyntax, List<MacroRow> macros)
        {
            if (macros == null)
                throw new ArgumentNullException(nameof(macros));

            this.macros = macros;
            AutoScaleMode = AutoScaleMode.Font;
            Text = title ?? string.Empty;
            Icon = (Icon)Properties.Resources.ResourceManager.GetObject("logo_icon");
            ClientSize = new Size(1200, 800);
            MinimumSize = new Size(700, 450);
            StartPosition = FormStartPosition.CenterScreen;

            var toolbar = new ToolStrip
            {
                Name = "editorToolbar",
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden
            };
            syntaxSelector = new ToolStripComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = false,
                Width = 180
            };
            syntaxSelector.Items.AddRange(new object[] { "Batch (.bat, .cmd)", "Bash (.sh)", "Python (.py)", "PowerShell (.ps1)" });
            zoomSelector = new ToolStripComboBox
            {
                Name = "zoomSelector",
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoSize = false,
                Width = 80,
                ToolTipText = "Zoom"
            };
            zoomSelector.Items.AddRange(new object[] { "20%", "50%", "70%", "100%", "150%" });
            toolbar.Items.Add(zoomSelector);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(new ToolStripLabel("Syntax:"));
            toolbar.Items.Add(syntaxSelector);
            toolbar.Items.Add(new ToolStripSeparator());
            var resources = Properties.Resources.ResourceManager;
            previewToggleButton = new ToolStripButton
            {
                Name = "previewToggleButton",
                Text = "Preview",
                ToolTipText = "Show or hide preview",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = (Image)resources.GetObject("editor_split"),
                CheckOnClick = true,
                Checked = true
            };
            macrosToggleButton = new ToolStripButton
            {
                Name = "macrosToggleButton",
                Text = "Macros",
                ToolTipText = "Show or hide macros",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = (Image)resources.GetObject("macro"),
                CheckOnClick = true
            };
            var aboutButton = new ToolStripButton
            {
                Name = "aboutButton",
                Text = "About",
                ToolTipText = "About",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = (Image)resources.GetObject("about"),
                Alignment = ToolStripItemAlignment.Right
            };
            aboutButton.Click += (sender, e) =>
            {
                using (var about = new AboutBox())
                    about.ShowDialog(this);
            };
            toolbar.Items.Add(previewToggleButton);
            toolbar.Items.Add(macrosToggleButton);
            toolbar.Items.Add(aboutButton);

            editor = CreateEditor(false);
            defaultWordChars = editor.WordChars;
            editor.Leave += (sender, e) => lastEditorPosition = editor.CurrentPosition;
            highlighter = new MacroAwareHighlighter(editor);
            components.Add(highlighter);
            previewEditor = CreateEditor(true);
            previewHighlighter = new MacroAwareHighlighter(previewEditor) { CopyFoldLevels = false };
            components.Add(previewHighlighter);
            ConfigureAutoCompletion();
            ConfigureLayout(toolbar, macros);
            ConfigurePreview();
            ConfigureZoom();
            syntaxSelector.SelectedIndexChanged += SyntaxSelectorSelectedIndexChanged;

            int defaultSyntaxIdx;
            switch (defaultSyntax?.Trim().ToLowerInvariant())
            {
                case "bash": defaultSyntaxIdx = 1; break;
                case "python": defaultSyntaxIdx = 2; break;
                case "powershell": defaultSyntaxIdx = 3; break;
                default: defaultSyntaxIdx = 0; break;
            }

            syntaxSelector.SelectedIndex = defaultSyntaxIdx;
            editor.Text = initialValue ?? string.Empty;
            editor.EmptyUndoBuffer();
            editor.SetSavePoint();
            UpdatePreview();
            UpdateLineNumberWidth();
            Shown += (sender, e) => editor.Focus();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Enter &&
                zoomSelector != null && zoomSelector.ComboBox.ContainsFocus)
            {
                zoomSelector.ComboBox.DroppedDown = false;
                ApplyZoomFromSelector();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private void ConfigureZoom()
        {
            zoomSelector.SelectedIndexChanged += (sender, e) =>
            {
                if (!synchronizingZoom && zoomSelector.SelectedIndex >= 0)
                    ApplyZoomFromSelector();
            };
            zoomSelector.ComboBox.Leave += (sender, e) => ApplyZoomFromSelector();
            zoomSelector.ComboBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    zoomSelector.ComboBox.DroppedDown = false;
                    ApplyZoomFromSelector();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    zoomSelector.Text = FormatZoomPercent();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    editor.Focus();
                }
            };
            editor.ZoomChanged += EditorZoomChanged;
            previewEditor.ZoomChanged += EditorZoomChanged;
            ApplyZoom(100);
        }

        private string FormatZoomPercent()
        {
            return zoomPercent.ToString("0.#", CultureInfo.CurrentCulture) + "%";
        }

        private void ApplyZoomFromSelector()
        {
            if (synchronizingZoom)
                return;
            string text = zoomSelector.Text.Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1).Trim();
            double percent;
            if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out percent) ||
                 double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent)) &&
                !double.IsNaN(percent) && !double.IsInfinity(percent) && percent > 0)
                ApplyZoom(percent);
            else
                zoomSelector.Text = FormatZoomPercent();
        }

        private void EditorZoomChanged(object sender, EventArgs e)
        {
            if (synchronizingZoom)
                return;
            var control = (Scintilla)sender;
            // Scintilla's native wheel zoom adds font points, not percentages.
            double fontSize = control.Styles[EditorStyle.Default].SizeF + control.Zoom;
            ApplyZoom(fontSize * 100 / BaseFontSize);
        }

        private void ApplyZoom(double percent)
        {
            if (synchronizingZoom)
                return;
            synchronizingZoom = true;
            try
            {
                zoomPercent = Math.Max(1, Math.Min(1000, percent));
                float size = (float)(BaseFontSize * zoomPercent / 100);
                foreach (Scintilla control in new[] { editor, previewEditor })
                {
                    // Rebase native zoom to support exact percentage presets such as 20%.
                    control.Zoom = 0;
                    for (int style = 0; style < control.Styles.Count; style++)
                        control.Styles[style].SizeF = size;
                }
                zoomSelector.Text = FormatZoomPercent();
                UpdateLineNumberWidth();
                SynchronizePreviewScroll();
            }
            finally
            {
                synchronizingZoom = false;
            }
        }

        private Scintilla CreateEditor(bool readOnly)
        {
            var control = new Scintilla
            {
                Name = readOnly ? "previewEditor" : "sourceEditor",
                Dock = DockStyle.Fill,
                TabWidth = 4,
                UseTabs = false,
                WrapMode = WrapMode.None,
                ReadOnly = readOnly
            };
            control.Margins[0].Type = MarginType.Number;
            control.Margins[1].Width = 0;
            ConfigureFolding(control);
            if (readOnly)
            {
                control.Margins[2].Sensitive = false;
                control.AutomaticFold = AutomaticFold.None;
            }
            control.TextChanged += EditorTextChanged;
            return control;
        }

        private void ConfigureLayout(ToolStrip toolbar, List<MacroRow> macros)
        {
            var mainSplit = new SplitContainer
            {
                Name = "mainSplit",
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2,
                Size = ClientSize,
                SplitterWidth = 9,
                SplitterDistance = 850,
                Panel1MinSize = 350,
                Panel2MinSize = 220,
                TabIndex = 0
            };
            var editorSplit = new SplitContainer
            {
                Name = "editorSplit",
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Size = new Size(850, 770),
                SplitterWidth = 9,
                SplitterDistance = 460,
                Panel1MinSize = 100,
                Panel2MinSize = 100,
                TabIndex = 1
            };
            editorSplit.Panel1.Controls.Add(editor);
            editorSplit.Panel2.Controls.Add(previewEditor);
            int previewPanelHeight = editorSplit.Panel2.Height;
            previewToggleButton.CheckedChanged += (sender, e) =>
            {
                if (previewToggleButton.Checked)
                {
                    editorSplit.Panel2Collapsed = false;
                    int maximum = editorSplit.ClientSize.Height - editorSplit.SplitterWidth - editorSplit.Panel2MinSize;
                    int desired = editorSplit.ClientSize.Height - editorSplit.SplitterWidth - previewPanelHeight;
                    editorSplit.SplitterDistance = Math.Max(editorSplit.Panel1MinSize, Math.Min(maximum, desired));
                    SynchronizePreviewFolds(true);
                    SynchronizePreviewScroll();
                }
                else
                {
                    previewPanelHeight = editorSplit.Panel2.Height;
                    if (previewEditor.ContainsFocus)
                        editor.Focus();
                    editorSplit.Panel2Collapsed = true;
                }
            };
            mainSplit.Panel1.Padding = new Padding(5);
            mainSplit.Panel1.Controls.Add(editorSplit);
            mainSplit.Panel1.Controls.Add(toolbar);

            var rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(5),
                TabIndex = 0
            };
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var nameInput = new TextBox
            {
                Name = "nameInput",
                Dock = DockStyle.Fill,
                Multiline = false,
                Margin = new Padding(0, 0, 0, 5),
                TabIndex = 0
            };
            var valuesGrid = new DataGridView
            {
                Name = "valuesGrid",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoGenerateColumns = false,
                BindingContext = new BindingContext(),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = true,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BackgroundColor = SystemColors.Window,
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                TabIndex = 1
            };
            valuesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "Name",
                DataPropertyName = nameof(MacroRow.Name),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            valuesGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Value",
                HeaderText = "Value",
                DataPropertyName = nameof(MacroRow.Value),
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            valuesGrid.RowTemplate.Resizable = DataGridViewTriState.False;
            valuesGrid.DataSource = macros;
            ConfigureMacroFilter(nameInput, valuesGrid, macros);
            rightLayout.Controls.Add(nameInput, 0, 0);
            rightLayout.Controls.Add(valuesGrid, 0, 1);
            mainSplit.Panel2.Controls.Add(rightLayout);

            var buttonBox = new FlowLayoutPanel
            {
                Name = "buttonBox",
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(5),
                TabIndex = 1
            };
            var macrosButton = new Button
            {
                Name = "macrosButton",
                Text = "Macros>>",
                AutoSize = true,
                MinimumSize = new Size(95, 28),
                TabIndex = 1
            };
            var insertButton = new Button
            {
                Name = "insertButton",
                Text = "Insert",
                AutoSize = true,
                MinimumSize = new Size(95, 28),
                Visible = false,
                TabIndex = 0
            };
            insertButton.Click += (sender, e) =>
                InsertMacro(valuesGrid.CurrentRow?.DataBoundItem as MacroRow);
            valuesGrid.CellDoubleClick += (sender, e) =>
            {
                if (e.RowIndex >= 0)
                    InsertMacro(valuesGrid.Rows[e.RowIndex].DataBoundItem as MacroRow);
            };
            int macrosPanelWidth = mainSplit.Panel2.Width;
            macrosToggleButton.CheckedChanged += (sender, e) =>
            {
                bool showMacros = macrosToggleButton.Checked;
                if (showMacros)
                {
                    mainSplit.Panel2Collapsed = false;
                    int maximum = mainSplit.ClientSize.Width - mainSplit.SplitterWidth - mainSplit.Panel2MinSize;
                    int desired = mainSplit.ClientSize.Width - mainSplit.SplitterWidth - macrosPanelWidth;
                    mainSplit.SplitterDistance = Math.Max(mainSplit.Panel1MinSize, Math.Min(maximum, desired));
                }
                else
                {
                    macrosPanelWidth = mainSplit.Panel2.Width;
                    editor.Focus();
                    mainSplit.Panel2Collapsed = true;
                }
                insertButton.Visible = showMacros;
                macrosButton.Text = showMacros ? "Macros<<" : "Macros>>";
            };
            macrosButton.Click += (sender, e) => macrosToggleButton.Checked = !macrosToggleButton.Checked;
            buttonBox.Controls.Add(macrosButton);
            buttonBox.Controls.Add(insertButton);
            mainSplit.Panel2Collapsed = true;
            Controls.Add(mainSplit);
            Controls.Add(buttonBox);
            Controls.Add(CreateDialogButtonBox());
        }

        private void InsertMacro(MacroRow? macro)
        {
            if (macro == null || string.IsNullOrEmpty(macro.Name))
                return;

            int position = editor.ContainsFocus ? editor.CurrentPosition : lastEditorPosition;
            position = Math.Max(0, Math.Min(position, editor.TextLength));
            editor.AutoCCancel();
            editor.InsertText(position, macro.Name);
            lastEditorPosition = position + macro.Name.Length;
            editor.GotoPosition(lastEditorPosition);
            editor.Focus();
        }

        private void ConfigureMacroFilter(TextBox filterInput, DataGridView grid, List<MacroRow> macros)
        {
            var filterTimer = new Timer(components) { Interval = 1000 };
            filterInput.TextChanged += (sender, e) =>
            {
                // Restart the delay on every insertion or deletion.
                filterTimer.Stop();
                filterTimer.Start();
            };
            filterTimer.Tick += (sender, e) =>
            {
                filterTimer.Stop();
                string filter = filterInput.Text;
                grid.DataSource = filter.Length == 0
                    ? macros
                    : macros.FindAll(macro =>
                        (macro.Name ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (macro.Value ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            };
        }

        private Control CreateDialogButtonBox()
        {
            var dialogButtonBox = new FlowLayoutPanel
            {
                Name = "dialogButtonBox",
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(5),
                TabIndex = 2
            };
            var okButton = new Button
            {
                Name = "okButton",
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                MinimumSize = new Size(95, 28),
                TabIndex = 0
            };
            var cancelButton = new Button
            {
                Name = "cancelButton",
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                MinimumSize = new Size(95, 28),
                TabIndex = 1
            };
            // Close also when this form is launched with Application.Run rather than ShowDialog.
            okButton.Click += (sender, e) => Close();
            cancelButton.Click += (sender, e) => Close();
            AcceptButton = okButton;
            CancelButton = cancelButton;
            dialogButtonBox.Controls.Add(cancelButton);
            dialogButtonBox.Controls.Add(okButton);
            return dialogButtonBox;
        }

        private void ConfigureAutoCompletion()
        {
            editor.AutoCOrder = Order.PerformSort;
            editor.AutoCSeparator = ' ';
            editor.AutoCMaxHeight = 10;
            editor.AutoCChooseSingle = false;
            editor.AutoCDropRestOfWord = true;
            editor.ClearCmdKey(Keys.Control | Keys.Space);
            editor.CharAdded += EditorCharAdded;
            editor.KeyDown += EditorKeyDown;
            editor.KeyUp += EditorKeyUp;
            editor.AutoCSelection += EditorAutoCSelection;
        }

        private void SetKeywords(MacroAwareHighlighter syntaxHighlighter, int set, string keywords)
        {
            syntaxHighlighter.SetKeywords(set, keywords);
            foreach (string keyword in keywords.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                completionKeywords.Add(keyword);
        }

        private void EditorCharAdded(object sender, CharAddedEventArgs e)
        {
            ShowAutoCompletion(false);
        }

        private void EditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && editor.AutoCActive)
            {
                editor.AutoCCancel();
                ResetMacroCompletion();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyData == (Keys.Control | Keys.Space))
            {
                ShowAutoCompletion(true);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ShowAutoCompletion(bool manual)
        {
            if (TryShowMacroCompletion())
                return;

            int position = editor.CurrentPosition;
            int start = editor.WordStartPosition(position, true);
            int length = position - start;
            if (!manual && length < 2)
                return;

            string prefix = editor.GetTextRange(start, length);
            var comparison = editor.AutoCIgnoreCase
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var matches = new List<string>();
            foreach (string keyword in completionKeywords)
            {
                if (keyword.StartsWith(prefix, comparison))
                    matches.Add(keyword);
            }

            if (matches.Count == 0)
                editor.AutoCCancel();
            else
                editor.AutoCShow(length, string.Join(" ", matches));
        }

        private void EditorKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete ||
                (macroCompletionActive && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                    e.KeyCode == Keys.Home || e.KeyCode == Keys.End)))
                TryShowMacroCompletion();
        }

        private bool TryGetMacroContext(out int start, out string filter)
        {
            var line = editor.Lines[editor.CurrentLine];
            string beforeCaret = editor.GetTextRange(line.Position, editor.CurrentPosition - line.Position);
            int opening = beforeCaret.LastIndexOf("$(", StringComparison.Ordinal);
            if (opening >= 0)
            {
                filter = beforeCaret.Substring(opening + 2);
                if (filter.IndexOf('(') < 0 && filter.IndexOf(')') < 0)
                {
                    start = line.Position + opening;
                    return true;
                }
            }
            start = -1;
            filter = string.Empty;
            return false;
        }

        private bool TryShowMacroCompletion()
        {
            int start;
            string filter;
            if (!TryGetMacroContext(out start, out filter))
            {
                if (macroCompletionActive)
                {
                    editor.AutoCCancel();
                    ResetMacroCompletion();
                }
                return false;
            }

            macroCompletionActive = true;
            macroCompletionMatches.Clear();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MacroRow macro in macros)
            {
                if (!string.IsNullOrEmpty(macro.Name) &&
					macro.Name.Substring(2).StartsWith(filter, StringComparison.OrdinalIgnoreCase) && names.Add(macro.Name))
					//macro.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 && names.Add(macro.Name))
					macroCompletionMatches.Add(macro.Name);
            }
            macroCompletionMatches.Sort(StringComparer.OrdinalIgnoreCase);
            editor.AutoCIgnoreCase = true;
            editor.AutoCAutoHide = false;
            editor.AutoCDropRestOfWord = false;
            editor.AutoCSeparator = '\n';
            if (macroCompletionMatches.Count == 0)
            {
                editor.AutoCCancel();
                return true;
            }

            // Native completion matches prefixes. Filter by substring ourselves and
            // replace the entire macro in AutoCSelection instead of using its prefix logic.
            editor.AutoCShow(0, string.Join("\n", macroCompletionMatches));
            editor.AutoCSelect(macroCompletionMatches[0]);
            return true;
        }

        private void ResetMacroCompletion()
        {
            macroCompletionActive = false;
            macroCompletionMatches.Clear();
            editor.AutoCIgnoreCase = syntaxSelector.SelectedIndex == 0 || syntaxSelector.SelectedIndex == 3;
            editor.AutoCAutoHide = true;
            editor.AutoCDropRestOfWord = true;
            editor.AutoCSeparator = ' ';
        }

        private void EditorAutoCSelection(object sender, AutoCSelectionEventArgs e)
        {
            if (!macroCompletionActive)
                return;

            int start;
            string filter;
            editor.AutoCCancel();
            if (!TryGetMacroContext(out start, out filter))
            {
                ResetMacroCompletion();
                return;
            }

            int position = editor.CurrentPosition;
            var line = editor.Lines[editor.CurrentLine];
            string afterCaret = editor.GetTextRange(position, line.EndPosition - position);
            int closing = afterCaret.IndexOf(')');
            int nestedOpening = afterCaret.IndexOf('(');
            int end = closing >= 0 && (nestedOpening < 0 || closing < nestedOpening)
                ? position + closing + 1 : position;
            string name = e.Text;
            ResetMacroCompletion();
            editor.BeginUndoAction();
            try
            {
                editor.SetSelection(end, start);
                editor.ReplaceSelection(name);
            }
            finally
            {
                editor.EndUndoAction();
            }
            lastEditorPosition = start + name.Length;
            editor.GotoPosition(lastEditorPosition);
        }

        private static void ConfigureFolding(Scintilla target)
        {
            target.Margins[2].Type = MarginType.Symbol;
            target.Margins[2].Mask = Marker.MaskFolders;
            target.Margins[2].Sensitive = true;
            target.Margins[2].Width = 20;
            target.AutomaticFold = AutomaticFold.Show | AutomaticFold.Click | AutomaticFold.Change;

            target.Markers[Marker.Folder].Symbol = MarkerSymbol.BoxPlus;
            target.Markers[Marker.FolderOpen].Symbol = MarkerSymbol.BoxMinus;
            target.Markers[Marker.FolderEnd].Symbol = MarkerSymbol.BoxPlusConnected;
            target.Markers[Marker.FolderOpenMid].Symbol = MarkerSymbol.BoxMinusConnected;
            target.Markers[Marker.FolderMidTail].Symbol = MarkerSymbol.TCorner;
            target.Markers[Marker.FolderSub].Symbol = MarkerSymbol.VLine;
            target.Markers[Marker.FolderTail].Symbol = MarkerSymbol.LCorner;

            for (int marker = Marker.FolderEnd; marker <= Marker.FolderOpen; marker++)
            {
                target.Markers[marker].SetForeColor(Color.White);
                target.Markers[marker].SetBackColor(Color.Gray);
            }
        }

        private void EditorFoldMarginClick(object sender, MarginClickEventArgs e)
        {
            if (e.Margin != 2)
                return;
            var line = editor.Lines[editor.LineFromPosition(e.Position)];
            if ((line.FoldLevelFlags & FoldLevelFlags.Header) == 0)
                return;

            // Handle the click explicitly: native fold toggles do not always emit UpdateUI.
            line.ToggleFold();
            SynchronizePreviewFolds();
            SynchronizePreviewScroll();
        }

        private void ConfigurePreview()
        {
            editor.AutomaticFold &= ~AutomaticFold.Click;
            editor.MarginClick += EditorFoldMarginClick;
            previewTimer = new Timer(components) { Interval = 1000 };
            previewTimer.Tick += (sender, e) => UpdatePreview();
            editor.UpdateUI += (sender, e) =>
            {
                SynchronizePreviewFolds();
                if ((e.Change & (UpdateChange.VScroll | UpdateChange.HScroll)) != 0)
                    SynchronizePreviewScroll();
            };
            previewEditor.SizeChanged += (sender, e) => SynchronizePreviewScroll();
            previewEditor.UpdateUI += (sender, e) => SynchronizePreviewFolds();
        }

        private void UpdatePreview()
        {
            previewTimer?.Stop();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (MacroRow macro in macros)
            {
                if (!string.IsNullOrEmpty(macro.Name) && !values.ContainsKey(macro.Name))
                    values.Add(macro.Name, macro.Value ?? string.Empty);
            }

            // Keep the source-line positions while expanding, including multi-line values.
            var sourceLinePositions = new List<int>();
            var expanded = new StringBuilder();
            for (int i = 0; i < editor.Lines.Count; i++)
            {
                sourceLinePositions.Add(expanded.Length);
                string line = editor.Lines[i].Text;
                expanded.Append(MacroAwareHighlighter.MacroPattern.Replace(line, match =>
                {
                    string value;
                    return match.Value != "$()" && values.TryGetValue(match.Value, out value)
                        ? value : string.Empty;
                }));
            }
            string preview = expanded.ToString();
            if (previewEditor.Text != preview)
            {
                previewEditor.ReadOnly = false;
                try
                {
                    previewEditor.Text = preview;
                    previewEditor.EmptyUndoBuffer();
                    previewEditor.SetSavePoint();
                }
                finally
                {
                    previewEditor.ReadOnly = true;
                }
            }
            ApplyPreviewFolding(sourceLinePositions, preview);
            SynchronizePreviewScroll();
        }

        private void ApplyPreviewFolding(List<int> sourceLinePositions, string preview)
        {
            var previewLines = new int[sourceLinePositions.Count];
            for (int i = 0; i < previewLines.Length; i++)
            {
                int position = sourceLinePositions[i];
                // A replacement may join CR and LF at a source-line boundary.
                if (position > 0 && position < preview.Length &&
                    preview[position - 1] == '\r' && preview[position] == '\n')
                    position++;
                previewLines[i] = previewEditor.LineFromPosition(position);
            }
            for (int sourceLine = 0; sourceLine < previewLines.Length; sourceLine++)
            {
                int end = sourceLine + 1 < previewLines.Length
                    ? previewLines[sourceLine + 1] : previewEditor.Lines.Count;
                int level = editor.Lines[sourceLine].FoldLevel | (int)editor.Lines[sourceLine].FoldLevelFlags;
                for (int line = previewLines[sourceLine]; line < end; line++)
                {
                    // Only the corresponding source line can introduce a fold header.
                    int mappedLevel = line == previewLines[sourceLine]
                        ? level : level & ~(int)FoldLevelFlags.Header;
                    previewEditor.DirectMessage(SciApi.SCI_SETFOLDLEVEL, new IntPtr(line), new IntPtr(mappedLevel));
                }
            }
            previewLineMap = previewLines;
            SynchronizePreviewFolds(true);
        }

        private void SynchronizePreviewFolds(bool force = false)
        {
            if (synchronizingPreviewFolds || previewLineMap.Length != editor.Lines.Count)
                return;

            synchronizingPreviewFolds = true;
            try
            {
                for (int sourceLine = 0; sourceLine < previewLineMap.Length; sourceLine++)
                {
                    int start = previewLineMap[sourceLine];
                    int end = sourceLine + 1 < previewLineMap.Length
                        ? previewLineMap[sourceLine + 1] : previewEditor.Lines.Count;
                    if (start >= end || end > previewEditor.Lines.Count)
                        continue;

                    var source = editor.Lines[sourceLine];
                    var target = previewEditor.Lines[start];
                    if ((source.FoldLevelFlags & FoldLevelFlags.Header) != 0 &&
                        (target.FoldLevelFlags & FoldLevelFlags.Header) != 0 &&
                        target.Expanded != source.Expanded)
                        target.Expanded = source.Expanded;

                    // Every line generated by one source line shares its visibility.
                    // This also preserves collapsed children when a parent is reopened.
                    bool visible = source.Visible;
                    if (force || target.Visible != visible || previewEditor.Lines[end - 1].Visible != visible)
                    {
                        if (visible)
                            previewEditor.ShowLines(start, end - 1);
                        else
                            previewEditor.HideLines(start, end - 1);
                    }
                }
            }
            finally
            {
                synchronizingPreviewFolds = false;
            }
        }

        private void SynchronizePreviewScroll()
        {
            // Permit the same horizontal offset even when substitutions shorten lines.
            previewEditor.ScrollWidth = Math.Max(previewEditor.ScrollWidth, editor.ScrollWidth);
            previewEditor.FirstVisibleLine = editor.FirstVisibleLine;
            previewEditor.XOffset = editor.XOffset;
        }

        private void EditorTextChanged(object sender, EventArgs e)
        {
            UpdateLineNumberWidth((Scintilla)sender);
            if (sender == editor && previewTimer != null)
            {
                previewTimer.Stop();
                previewTimer.Start();
            }
        }

        private void UpdateLineNumberWidth()
        {
            UpdateLineNumberWidth(editor);
            UpdateLineNumberWidth(previewEditor);
        }

        private static void UpdateLineNumberWidth(Scintilla target)
        {
            int digits = Math.Max(3, target.Lines.Count.ToString().Length);
            target.Margins[0].Width = target.TextWidth(EditorStyle.LineNumber, new string('9', digits)) + 12;
        }

        private void SyntaxSelectorSelectedIndexChanged(object sender, EventArgs e)
        {
            editor.AutoCCancel();
            ResetMacroCompletion();
            completionKeywords.Clear();
            editor.AutoCIgnoreCase = syntaxSelector.SelectedIndex == 0 || syntaxSelector.SelectedIndex == 3;
            ApplySyntax(editor, highlighter);
            ApplySyntax(previewEditor, previewHighlighter);
            UpdatePreview();
        }

        private void ApplySyntax(Scintilla target, MacroAwareHighlighter syntaxHighlighter)
        {
            // Reveal the previous lexer's folded lines before rebuilding the structure.
            target.FoldAll(FoldAction.Expand);
            target.StyleResetDefault();
            target.Styles[EditorStyle.Default].Font = "Consolas";
            target.Styles[EditorStyle.Default].SizeF = (float)(BaseFontSize * zoomPercent / 100);
            target.Styles[EditorStyle.Default].ForeColor = Color.FromArgb(35, 35, 35);
            target.Styles[EditorStyle.Default].BackColor = target == previewEditor ? SystemColors.Control : Color.White;
            target.StyleClearAll();
            target.Styles[EditorStyle.LineNumber].ForeColor = Color.Gray;
            target.Styles[EditorStyle.LineNumber].BackColor = target == previewEditor
                ? SystemColors.Control : Color.FromArgb(245, 245, 245);

            // PowerShell cmdlet names include hyphens; keep them in one completion word.
            target.WordChars = defaultWordChars + (syntaxSelector.SelectedIndex == 3 ? "-" : string.Empty);

            switch (syntaxSelector.SelectedIndex)
            {
                case 0:
                    syntaxHighlighter.LexerName = "batch";
                    SetKeywords(syntaxHighlighter, 0, "assoc break call cd chcp chdir cls color copy date del dir " +
                        "echo else endlocal erase exit for ftype goto if md mkdir mklink move path pause " +
                        "popd prompt pushd rd rem ren rename rmdir set setlocal shift start time title " +
                        "type ver verify vol defined errorlevel exist not in do enabledelayedexpansion");
                    SetKeywords(syntaxHighlighter, 1, "");
                    SetStyle(target, EditorStyle.Batch.Comment, Color.ForestGreen);
                    SetStyle(target, EditorStyle.Batch.Word, Color.Blue, true);
                    SetStyle(target, EditorStyle.Batch.Label, Color.DarkMagenta);
                    SetStyle(target, EditorStyle.Batch.Hide, Color.Gray);
                    SetStyle(target, EditorStyle.Batch.Command, Color.Teal);
                    SetStyle(target, EditorStyle.Batch.Identifier, Color.DarkViolet);
                    SetStyle(target, EditorStyle.Batch.Operator, Color.DarkSlateGray);
                    break;
                case 1:
                    syntaxHighlighter.LexerName = "bash";
                    SetKeywords(syntaxHighlighter, 0, "if then else elif fi for while until in do done case esac " +
                        "function select time coproc break continue return exit export readonly local " +
                        "declare typeset unset shift source alias unalias set trap eval exec read " +
                        "echo printf cd pwd test true false");
                    SetKeywords(syntaxHighlighter, 1, "");
                    SetStyle(target, LexApi.SCE_SH_COMMENTLINE, Color.ForestGreen);
                    SetStyle(target, LexApi.SCE_SH_NUMBER, Color.DarkOrange);
                    SetStyle(target, LexApi.SCE_SH_WORD, Color.Blue, true);
                    SetStyle(target, LexApi.SCE_SH_STRING, Color.Brown);
                    SetStyle(target, LexApi.SCE_SH_CHARACTER, Color.Brown);
                    SetStyle(target, LexApi.SCE_SH_OPERATOR, Color.DarkSlateGray);
                    SetStyle(target, LexApi.SCE_SH_SCALAR, Color.DarkViolet);
                    SetStyle(target, LexApi.SCE_SH_PARAM, Color.DarkViolet);
                    SetStyle(target, LexApi.SCE_SH_BACKTICKS, Color.Teal);
                    SetStyle(target, LexApi.SCE_SH_HERE_DELIM, Color.Brown);
                    SetStyle(target, LexApi.SCE_SH_HERE_Q, Color.Brown);
                    break;
                case 2:
                    syntaxHighlighter.LexerName = "python";
                    SetKeywords(syntaxHighlighter, 0, "False None True and as assert async await break class " +
                        "continue def del elif else except finally for from global if import in is " +
                        "lambda nonlocal not or pass raise return try while with yield");
                    SetKeywords(syntaxHighlighter, 1, "abs all any bool bytes dict enumerate filter float int " +
                        "len list map max min object open print range set sorted str sum super tuple zip");
                    SetStyle(target, EditorStyle.Python.CommentLine, Color.ForestGreen);
                    SetStyle(target, EditorStyle.Python.CommentBlock, Color.ForestGreen);
                    SetStyle(target, EditorStyle.Python.Number, Color.DarkOrange);
                    SetStyle(target, EditorStyle.Python.String, Color.Brown);
                    SetStyle(target, EditorStyle.Python.Character, Color.Brown);
                    SetStyle(target, EditorStyle.Python.Triple, Color.Brown);
                    SetStyle(target, EditorStyle.Python.TripleDouble, Color.Brown);
                    SetStyle(target, EditorStyle.Python.Word, Color.Blue, true);
                    SetStyle(target, EditorStyle.Python.Word2, Color.Teal);
                    SetStyle(target, EditorStyle.Python.ClassName, Color.DarkCyan, true);
                    SetStyle(target, EditorStyle.Python.DefName, Color.DarkCyan);
                    SetStyle(target, EditorStyle.Python.Decorator, Color.DarkViolet);
                    SetStyle(target, EditorStyle.Python.Operator, Color.DarkSlateGray);
                    break;
                case 3:
                    syntaxHighlighter.LexerName = "powershell";
                    SetKeywords(syntaxHighlighter, 0, "begin break catch class clean configuration continue data " +
                        "define do dynamicparam else elseif end enum exit filter finally for foreach from function " +
                        "if in inlinescript parallel param process return sequence static switch throw trap try " +
                        "until using var while workflow");
                    SetKeywords(syntaxHighlighter, 1, "add-content clear-content copy-item export-csv foreach-object " +
                        "format-list format-table get-childitem get-command get-content get-date get-help " +
                        "get-item get-location get-member get-process get-service get-variable import-csv " +
                        "import-module invoke-command join-path measure-object new-item out-file out-null " +
                        "read-host remove-item resolve-path select-object set-content set-location " +
                        "set-variable sort-object split-path start-process stop-process test-path " +
                        "where-object write-debug write-error write-host write-information write-output " +
                        "write-progress write-verbose write-warning");
                    SetKeywords(syntaxHighlighter, 2, "cat cd cls copy cp del dir echo erase fl foreach ft " +
                        "gc gci gcm gi gl gm gps group gv ls measure mkdir move mv popd ps pushd pwd " +
                        "ren rm rmdir select set sl sort split-path tee type where");
                    SetKeywords(syntaxHighlighter, 3, "");
                    SetKeywords(syntaxHighlighter, 4, "");
                    SetKeywords(syntaxHighlighter, 5, "synopsis description parameter example inputs outputs " +
                        "notes link component role functionality forwardhelptargetname forwardhelpcategory " +
                        "remotehelprunspace externalhelp");
                    SetStyle(target, LexApi.SCE_POWERSHELL_COMMENT, Color.ForestGreen);
                    SetStyle(target, LexApi.SCE_POWERSHELL_COMMENTSTREAM, Color.ForestGreen);
                    SetStyle(target, LexApi.SCE_POWERSHELL_COMMENTDOCKEYWORD, Color.ForestGreen, true);
                    SetStyle(target, LexApi.SCE_POWERSHELL_STRING, Color.Brown);
                    SetStyle(target, LexApi.SCE_POWERSHELL_CHARACTER, Color.Brown);
                    SetStyle(target, LexApi.SCE_POWERSHELL_HERE_STRING, Color.Brown);
                    SetStyle(target, LexApi.SCE_POWERSHELL_HERE_CHARACTER, Color.Brown);
                    SetStyle(target, LexApi.SCE_POWERSHELL_NUMBER, Color.DarkOrange);
                    SetStyle(target, LexApi.SCE_POWERSHELL_VARIABLE, Color.DarkViolet);
                    SetStyle(target, LexApi.SCE_POWERSHELL_OPERATOR, Color.DarkSlateGray);
                    SetStyle(target, LexApi.SCE_POWERSHELL_KEYWORD, Color.Blue, true);
                    SetStyle(target, LexApi.SCE_POWERSHELL_CMDLET, Color.Teal);
                    SetStyle(target, LexApi.SCE_POWERSHELL_ALIAS, Color.Teal);
                    SetStyle(target, LexApi.SCE_POWERSHELL_FUNCTION, Color.DarkCyan);
                    SetStyle(target, LexApi.SCE_POWERSHELL_USER1, Color.DarkCyan);
                    break;
            }

            syntaxHighlighter.SetProperty("fold", "1");
            syntaxHighlighter.SetProperty("fold.compact", "1");
            syntaxHighlighter.SetProperty("fold.comment", "1");
            syntaxHighlighter.SetProperty("fold.quotes.python", "1");

            SetStyle(target, MacroAwareHighlighter.MacroStyle, Color.DarkMagenta, true);
            target.Styles[MacroAwareHighlighter.MacroStyle].BackColor = target == previewEditor
                ? SystemColors.Control : Color.FromArgb(255, 245, 210);
            syntaxHighlighter.RefreshHighlighting();
            UpdateLineNumberWidth(target);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                components.Dispose();
            base.Dispose(disposing);
        }

        private static void SetStyle(Scintilla target, int index, Color color, bool bold = false)
        {
            target.Styles[index].ForeColor = color;
            target.Styles[index].Bold = bold;
        }
    }
}
