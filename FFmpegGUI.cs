/*
 * FFmpegGUI - Minimalist FFmpeg Tactical Exoskeleton
 * Copyright (C) 2026 Lawyer Daxu
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;

// =====================================================================
// FEATURE: Custom Dark Theme ComboBox Control (Anti-overlap, Centered)
// =====================================================================
class DarkComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1; // Prevent flickering
            return;
        }

        if (m.Msg == WM_PAINT)
        {
            PAINTSTRUCT ps = new PAINTSTRUCT();
            IntPtr hdc = BeginPaint(this.Handle, ref ps);

            using (Graphics g = Graphics.FromHdc(hdc))
            {
                using (SolidBrush bgBrush = new SolidBrush(this.BackColor))
                {
                    g.FillRectangle(bgBrush, 0, 0, this.Width, this.Height);
                }

                int safeButtonWidth = SystemInformation.VerticalScrollBarWidth;
                Rectangle rect = new Rectangle(this.Width - safeButtonWidth, 0, safeButtonWidth, this.Height);
                
                using (SolidBrush btnBrush = new SolidBrush(Color.FromArgb(30, 30, 30)))
                {
                    g.FillRectangle(btnBrush, rect);
                }

                using (Pen borderPen = new Pen(Color.FromArgb(70, 70, 70)))
                {
                    g.DrawLine(borderPen, rect.X, 0, rect.X, this.Height);
                }

                int arrowWidth = 11;
                int arrowHeight = 6;
                int arrowX = rect.X + (rect.Width - arrowWidth) / 2;
                int arrowY = rect.Y + (rect.Height - arrowHeight) / 2;

                Point[] arrow = new Point[] {
                    new Point(arrowX, arrowY),
                    new Point(arrowX + arrowWidth, arrowY),
                    new Point(arrowX + arrowWidth / 2, arrowY + arrowHeight)
                };

                using (SolidBrush arrowBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillPolygon(arrowBrush, arrow);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                }

                using (Pen borderEdgePen = new Pen(Color.FromArgb(70, 70, 70)))
                {
                    g.DrawRectangle(borderEdgePen, 0, 0, this.Width - 1, this.Height - 1);
                }
            }

            EndPaint(this.Handle, ref ps);
            return; // Skip base painting to use custom draw
        }

        base.WndProc(ref m);
    }
}

class FFmpegGUI : Form
{
    // =====================================================================
    // NATIVE API IMPORTS: Theming, UI, and Process Management
    // =====================================================================
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern bool AllowDarkModeForApp(bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern Int32 SendMessage(IntPtr hWnd, int msg, int wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);
    const int CB_SETCUEBANNER = 0x1703;

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    // Form Controls
    DarkComboBox cmbFFmpeg, cmbInput, cmbOutputDir, cmbArgs;
    RichTextBox txtLog;
    Button btnRun, btnCancel, btnLang;
    
    string configFile = "FFmpegGUI.ini";

    Process currentProcess = null; 
    bool isPaused = false; 
    string lastInfoPath = ""; 

    int cmbArgsClickCount = 0;
    DateTime cmbArgsLastClickTime = DateTime.MinValue;

    Queue<string> batchQueue = new Queue<string>();
    int totalBatchFiles = 0;
    int currentBatchIndex = 0;

    private Dictionary<string, string> langStrings = new Dictionary<string, string>();
    // Set default language to English
    private string currentLanguage = "en-US";
    // Polling list of supported five languages
    private string[] supportedLangs = { "zh-CN", "en-US", "ja-JP", "fr-FR", "de-DE" };

    // =====================================================================
    // FEATURE: Main Form Initialization & UI Setup
    // =====================================================================
    public FFmpegGUI()
    {
        PreloadLanguage(); 
        InitLanguage(currentLanguage);

        this.Text = GetStr("AppTitle");
        this.Size = new Size(1100, 720);
        this.MinimumSize = new Size(750, 600); 
        this.BackColor = Color.FromArgb(30, 30, 30);
        this.ForeColor = Color.White;
        this.FormBorderStyle = FormBorderStyle.Sizable; 
        this.MaximizeBox = true;
        this.StartPosition = FormStartPosition.CenterScreen;

        try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        this.MouseClick += new MouseEventHandler(Form_MouseClick);

        cmbFFmpeg = MakeComboBox(20, GetStr("PlaceholderFFmpeg"));
        
        cmbInput = MakeComboBox(80, GetStr("PlaceholderInput"));
        cmbInput.AllowDrop = true;
        cmbInput.DragEnter += new DragEventHandler(cmbInput_DragEnter);
        cmbInput.DragDrop += new DragEventHandler(cmbInput_DragDrop);
        cmbInput.Leave += delegate { 
            if(!string.IsNullOrEmpty(cmbInput.Text)) {
                string firstFile = cmbInput.Text.Split(new char[] { '|' })[0];
                FetchMediaInfo(CleanPath(firstFile)); 
            }
        };

        cmbOutputDir = MakeComboBox(140, GetStr("PlaceholderOutput"));
        cmbArgs = MakeComboBox(200, GetStr("PlaceholderArgs"));

        // FEATURE: Triple-click to Select All in Args ComboBox
        cmbArgs.MouseDown += (s, e) => {
            if (e.Button == MouseButtons.Left) {
                if ((DateTime.Now - cmbArgsLastClickTime).TotalMilliseconds <= SystemInformation.DoubleClickTime) {
                    cmbArgsClickCount++;
                } else {
                    cmbArgsClickCount = 1;
                }
                cmbArgsLastClickTime = DateTime.Now;

                if (cmbArgsClickCount == 3) {
                    cmbArgs.SelectAll();
                    cmbArgsClickCount = 0; 
                }
            }
        };

        LoadConfig();

        if (cmbFFmpeg.Items.Count > 0 && string.IsNullOrEmpty(cmbFFmpeg.Text)) cmbFFmpeg.Text = cmbFFmpeg.Items[0].ToString();
        if (cmbOutputDir.Items.Count > 0 && string.IsNullOrEmpty(cmbOutputDir.Text)) cmbOutputDir.Text = cmbOutputDir.Items[0].ToString();

        btnRun = new Button();
        btnRun.Text = "▶"; 
        btnRun.Size = new Size(160, 50); 
        btnRun.Font = new Font("Segoe UI", 18, FontStyle.Bold); 
        btnRun.UseCompatibleTextRendering = true;
        btnRun.BackColor = Color.FromArgb(38, 79, 120);
        btnRun.ForeColor = Color.FromArgb(200, 200, 200); 
        btnRun.FlatStyle = FlatStyle.Flat;
        btnRun.FlatAppearance.BorderSize = 0;
        btnRun.Click += new EventHandler(BtnRun_Click);
        this.Controls.Add(btnRun);

        btnCancel = new Button();
        btnCancel.Text = "⏹"; 
        btnCancel.Size = new Size(160, 50); 
        btnCancel.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        btnCancel.UseCompatibleTextRendering = true;
        btnCancel.BackColor = Color.FromArgb(139, 45, 45); 
        btnCancel.ForeColor = Color.FromArgb(200, 200, 200); 
        btnCancel.FlatStyle = FlatStyle.Flat;
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Enabled = false; 
        btnCancel.Click += new EventHandler(BtnCancel_Click);
        this.Controls.Add(btnCancel);

        btnLang = new Button();
        btnLang.Text = GetLangButtonText(currentLanguage);
        btnLang.Size = new Size(120, 50); 
        btnLang.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        btnLang.UseCompatibleTextRendering = true;
        btnLang.BackColor = Color.FromArgb(60, 60, 60); 
        btnLang.ForeColor = Color.FromArgb(200, 200, 200); 
        btnLang.FlatStyle = FlatStyle.Flat;
        btnLang.FlatAppearance.BorderSize = 0;
        btnLang.Click += new EventHandler(BtnLang_Click);
        this.Controls.Add(btnLang);

        this.Resize += new EventHandler(Form_Resize);
        Form_Resize(this, EventArgs.Empty); 

        // FEATURE: Dark Mode Log Panel
        Panel pnlLog = new Panel();
        pnlLog.Location = new Point(20, 350);
        pnlLog.Size = new Size(this.ClientSize.Width - 40, this.ClientSize.Height - 370);
        pnlLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        pnlLog.BackColor = Color.FromArgb(70, 70, 70); 
        pnlLog.Padding = new Padding(1); 
        this.Controls.Add(pnlLog);

        txtLog = new RichTextBox();
        txtLog.Dock = DockStyle.Fill;
        txtLog.BorderStyle = BorderStyle.None; 
        txtLog.Font = new Font("Consolas", 11);
        txtLog.BackColor = Color.FromArgb(15, 15, 15);
        txtLog.ForeColor = Color.Gray;
        txtLog.Text = GetStr("LogDefault");
        txtLog.ReadOnly = true;
        pnlLog.Controls.Add(txtLog);

        SetWindowTheme(txtLog.Handle, "DarkMode_Explorer", null);
    }

    // =====================================================================
    // FEATURE: Configuration and Language Preloading
    // =====================================================================
    private void PreloadLanguage()
    {
        try {
            if (File.Exists(configFile)) {
                foreach (string line in File.ReadAllLines(configFile)) {
                    if (line.StartsWith("Language=")) {
                        string savedLang = line.Substring(9).Trim();
                        if (Array.Exists(supportedLangs, l => l.Equals(savedLang, StringComparison.OrdinalIgnoreCase))) {
                            currentLanguage = savedLang;
                        }
                        return; 
                    }
                }
            }
        } catch { }
    }

    private string GetLangButtonText(string langCode)
    {
        string prefix = langCode.Substring(0, 2).ToLower();
        switch (prefix) {
            case "en": return "EN";
            case "ja": return "日本語";
            case "fr": return "FR";
            case "de": return "DE";
            // Removed the space here
            default:   return "中文";
        }
    }

    // =====================================================================
    // FEATURE: Language Switching Event
    // =====================================================================
    private void BtnLang_Click(object sender, EventArgs e)
    {
        string oldLogDefault = GetStr("LogDefault");

        int currentIndex = Array.IndexOf(supportedLangs, currentLanguage);
        if (currentIndex == -1) currentIndex = 0;
        
        currentLanguage = supportedLangs[(currentIndex + 1) % supportedLangs.Length];

        InitLanguage(currentLanguage);
        RefreshUI(oldLogDefault);
        SaveConfig();
    }

    private void RefreshUI(string oldLogDefault)
    {
        this.Text = GetStr("AppTitle");
        btnLang.Text = GetLangButtonText(currentLanguage);

        SendMessage(cmbFFmpeg.Handle, CB_SETCUEBANNER, 0, GetStr("PlaceholderFFmpeg"));
        SendMessage(cmbInput.Handle, CB_SETCUEBANNER, 0, GetStr("PlaceholderInput"));
        SendMessage(cmbOutputDir.Handle, CB_SETCUEBANNER, 0, GetStr("PlaceholderOutput"));
        SendMessage(cmbArgs.Handle, CB_SETCUEBANNER, 0, GetStr("PlaceholderArgs"));

        cmbFFmpeg.Invalidate();
        cmbInput.Invalidate();
        cmbOutputDir.Invalidate();
        cmbArgs.Invalidate();

        if (txtLog.Text == oldLogDefault)
        {
            txtLog.Text = GetStr("LogDefault");
        }
    }

    // =====================================================================
    // FEATURE: Localization Dictionary Setup
    // =====================================================================
    private void InitLanguage(string lang)
    {
        langStrings.Clear();
        string prefix = lang.Substring(0, 2).ToLower();
        
        if (prefix == "en")
        {
            langStrings["AppTitle"] = "Minimalist FFmpeg Tactical Exoskeleton";
            langStrings["PlaceholderFFmpeg"] = "Enter the full path of ffmpeg.exe";
            langStrings["PlaceholderInput"] = "Enter media path, drag & drop multiple files allowed (separated by |)";
            langStrings["PlaceholderOutput"] = "Specify output folder (leave blank to use source directory)";
            langStrings["PlaceholderArgs"] = "Enter ffmpeg arguments, e.g.: -c:v libx264 -crf 23";
            langStrings["LogDefault"] = "Console Output";
            langStrings["AboutTitle"] = "About";
            langStrings["AboutText"] = "Minimalist FFmpeg Tactical Exoskeleton\nBorn for the ultimate pure command-line experience.\n\nVersion: 1.2 (Batch Edition)\nCopyright (C) {0} Lawyer Daxu";
            langStrings["LogMediaInfo"] = "\n--- [INFO] First Media Info ---\n";
            langStrings["MsgInterrupting"] = "\n[!] Sending interrupt signal and clearing task queue, please wait...";
            langStrings["ErrCancelFailed"] = "[ERR] Cancel request failed: ";
            langStrings["MsgPaused"] = "\n[||] Task Paused";
            langStrings["MsgResumed"] = "\n[>] Task Resumed";
            langStrings["ErrPauseFailed"] = "\n[ERR] Pause/Resume failed: ";
            langStrings["ErrNoFFmpeg"] = "[!] Error: Please specify FFmpeg path!";
            langStrings["ErrNoInput"] = "[!] Error: Please input or drag valid source files!";
            langStrings["MsgTaskStart"] = "[>_] Task started...";
            langStrings["MsgBatchStart"] = "[>_] Batch task started, detected {0} files...";
            langStrings["MsgTaskDone"] = "\n" + new string('-', 50) + "\n[OK] Task completed flawlessly!";
            langStrings["MsgAllDone"] = "\n" + new string('-', 50) + "\n[OK] All batch tasks completed flawlessly!";
            langStrings["WarnOutDirNotExist"] = "[!] Warning: Output folder does not exist, using source directory ({0})";
            langStrings["ErrPathParse"] = "[!] Path parsing failed: ";
            langStrings["MsgProcessingFile"] = "\n[>>>] Processing file {0}/{1}...";
            langStrings["MsgInputFile"] = "[IN] Source: ";
            langStrings["MsgSingleOutput"] = "[OUT] Single-track output: ";
            langStrings["MsgManualOutputDetected"] = "[OUT] Smart Mount: Manual output stream detected.";
            langStrings["MsgManualOutputProtected"] = "[OUT] Defense Mechanism: Timestamp injected into manual output to prevent overwriting!";
            langStrings["MsgFileDone"] = "\n[OK] File {0} processed!";
            langStrings["MsgTaskAborted"] = "\n" + new string('-', 50) + "\n[X] Task aborted or manually canceled (Exit Code: {0}).";
            langStrings["ErrRunFailed"] = "\n[ERR] Run failed: ";
            langStrings["MsgSkipFile"] = "\n[!] Skipping this file, trying the next one...";
        }
        else if (prefix == "ja")
        {
            langStrings["AppTitle"] = "極簡 FFmpeg 戦術外骨格";
            langStrings["PlaceholderFFmpeg"] = "ffmpeg.exe の完全なパスを入力";
            langStrings["PlaceholderInput"] = "メディアのパスを入力（複数ファイルのドラッグ＆ドロップ可、| で区切る）";
            langStrings["PlaceholderOutput"] = "出力フォルダを指定（空白の場合は元のディレクトリに出力）";
            langStrings["PlaceholderArgs"] = "ffmpeg のパラメータを入力。例: -c:v libx264 -crf 23";
            langStrings["LogDefault"] = "コンソール出力";
            langStrings["AboutTitle"] = "著作権情報";
            langStrings["AboutText"] = "極簡 FFmpeg 戦術外骨格\n究極で純粋なコマンドライン体験のために。\n\nバージョン: 1.2 (Batch Edition)\nCopyright (C) {0} 大許弁護士";
            langStrings["LogMediaInfo"] = "\n--- [INFO] 最初のメディア情報 ---\n";
            langStrings["MsgInterrupting"] = "\n[!] 強制中断シグナルを送信し、タスクキューをクリアしています。お待ちください...";
            langStrings["ErrCancelFailed"] = "[ERR] 中断要求に失敗しました: ";
            langStrings["MsgPaused"] = "\n[||] タスクが一時停止しました (Paused)";
            langStrings["MsgResumed"] = "\n[>] タスクが再開されました (Resumed)";
            langStrings["ErrPauseFailed"] = "\n[ERR] 一時停止/再開に失敗しました: ";
            langStrings["ErrNoFFmpeg"] = "[!] エラー: FFmpeg のパスを指定してください！";
            langStrings["ErrNoInput"] = "[!] エラー: 有効なソースファイルを入力またはドラッグしてください！";
            langStrings["MsgTaskStart"] = "[>_] タスク開始...";
            langStrings["MsgBatchStart"] = "[>_] バッチタスク開始、{0} 個のファイルを検出しました...";
            langStrings["MsgTaskDone"] = "\n" + new string('-', 50) + "\n[OK] タスクが正常に完了しました！";
            langStrings["MsgAllDone"] = "\n" + new string('-', 50) + "\n[OK] すべてのバッチタスクが正常に完了しました！";
            langStrings["WarnOutDirNotExist"] = "[!] 警告: 指定された出力フォルダが存在しません。元のディレクトリを使用します ({0})";
            langStrings["ErrPathParse"] = "[!] パスの解析に失敗しました: ";
            langStrings["MsgProcessingFile"] = "\n[>>>] {0}/{1} 番目のファイルを処理中...";
            langStrings["MsgInputFile"] = "[IN] ソースファイル: ";
            langStrings["MsgSingleOutput"] = "[OUT] 単一出力: ";
            langStrings["MsgManualOutputDetected"] = "[OUT] スマートマウント: 手動出力ストリームが検出されました。";
            langStrings["MsgManualOutputProtected"] = "[OUT] 防御メカニズム: 上書きを防止するため、手動出力名にタイムスタンプを強制挿入しました！";
            langStrings["MsgFileDone"] = "\n[OK] ファイル {0} の処理が完了しました！";
            langStrings["MsgTaskAborted"] = "\n" + new string('-', 50) + "\n[X] タスクが異常終了または手動でキャンセルされました (終了コード: {0})。";
            langStrings["ErrRunFailed"] = "\n[ERR] 実行に失敗しました: ";
            langStrings["MsgSkipFile"] = "\n[!] このファイルをスキップし、次を試行します...";
        }
        else if (prefix == "fr")
        {
            langStrings["AppTitle"] = "Exosquelette Tactique Minimaliste FFmpeg";
            langStrings["PlaceholderFFmpeg"] = "Entrez le chemin complet de ffmpeg.exe";
            langStrings["PlaceholderInput"] = "Entrez le chemin du média (glisser-déposer multiple autorisé, séparé par |)";
            langStrings["PlaceholderOutput"] = "Spécifiez le dossier de sortie (laisser vide pour utiliser le répertoire source)";
            langStrings["PlaceholderArgs"] = "Entrez les paramètres ffmpeg, ex : -c:v libx264 -crf 23";
            langStrings["LogDefault"] = "Sortie Console";
            langStrings["AboutTitle"] = "À propos";
            langStrings["AboutText"] = "Exosquelette Tactique Minimaliste FFmpeg\nNé pour l'expérience ultime de la ligne de commande pure.\n\nVersion : 1.2 (Batch Edition)\nCopyright (C) {0} Avocat Daxu";
            langStrings["LogMediaInfo"] = "\n--- [INFO] Premières infos média ---\n";
            langStrings["MsgInterrupting"] = "\n[!] Envoi du signal d'interruption et effacement de la file d'attente, veuillez patienter...";
            langStrings["ErrCancelFailed"] = "[ERR] Échec de la demande d'annulation : ";
            langStrings["MsgPaused"] = "\n[||] Tâche en pause";
            langStrings["MsgResumed"] = "\n[>] Tâche reprise";
            langStrings["ErrPauseFailed"] = "\n[ERR] Échec de la pause/reprise : ";
            langStrings["ErrNoFFmpeg"] = "[!] Erreur : Veuillez spécifier le chemin de FFmpeg !";
            langStrings["ErrNoInput"] = "[!] Erreur : Veuillez entrer ou faire glisser des fichiers sources valides !";
            langStrings["MsgTaskStart"] = "[>_] Démarrage de la tâche...";
            langStrings["MsgBatchStart"] = "[>_] Tâche par lots démarrée, {0} fichiers détectés...";
            langStrings["MsgTaskDone"] = "\n" + new string('-', 50) + "\n[OK] Tâche terminée avec succès !";
            langStrings["MsgAllDone"] = "\n" + new string('-', 50) + "\n[OK] Toutes les tâches par lots sont terminées !";
            langStrings["WarnOutDirNotExist"] = "[!] Avertissement : Le dossier de sortie n'existe pas, utilisation du répertoire source ({0})";
            langStrings["ErrPathParse"] = "[!] Échec de l'analyse du chemin : ";
            langStrings["MsgProcessingFile"] = "\n[>>>] Traitement du fichier {0}/{1}...";
            langStrings["MsgInputFile"] = "[IN] Fichier source : ";
            langStrings["MsgSingleOutput"] = "[OUT] Sortie unique : ";
            langStrings["MsgManualOutputDetected"] = "[OUT] Montage intelligent : Flux de sortie manuel détecté.";
            langStrings["MsgManualOutputProtected"] = "[OUT] Mécanisme de défense : Horodatage injecté dans la sortie manuelle pour éviter l'écrasement !";
            langStrings["MsgFileDone"] = "\n[OK] Fichier {0} traité !";
            langStrings["MsgTaskAborted"] = "\n" + new string('-', 50) + "\n[X] Tâche abandonnée ou annulée manuellement (Code de sortie : {0}).";
            langStrings["ErrRunFailed"] = "\n[ERR] Échec de l'exécution : ";
            langStrings["MsgSkipFile"] = "\n[!] Fichier ignoré, essai du suivant...";
        }
        else if (prefix == "de")
        {
            langStrings["AppTitle"] = "Minimalistisches FFmpeg Taktisches Exoskelett";
            langStrings["PlaceholderFFmpeg"] = "Geben Sie den vollständigen Pfad von ffmpeg.exe ein";
            langStrings["PlaceholderInput"] = "Medienpfad eingeben (Drag & Drop für mehrere Dateien erlaubt, getrennt durch |)";
            langStrings["PlaceholderOutput"] = "Ausgabeordner angeben (leer lassen, um Quellverzeichnis zu verwenden)";
            langStrings["PlaceholderArgs"] = "Geben Sie ffmpeg-Parameter ein, z.B.: -c:v libx264 -crf 23";
            langStrings["LogDefault"] = "Konsolenausgabe";
            langStrings["AboutTitle"] = "Über";
            langStrings["AboutText"] = "Minimalistisches FFmpeg Taktisches Exoskelett\nGeboren für das ultimative reine Kommandozeilenerlebnis.\n\nVersion: 1.2 (Batch Edition)\nCopyright (C) {0} Anwalt Daxu";
            langStrings["LogMediaInfo"] = "\n--- [INFO] Erste Medieninfo ---\n";
            langStrings["MsgInterrupting"] = "\n[!] Sende Unterbrechungssignal und lösche Aufgabenwarteschlange, bitte warten...";
            langStrings["ErrCancelFailed"] = "[ERR] Abbruchanforderung fehlgeschlagen: ";
            langStrings["MsgPaused"] = "\n[||] Aufgabe pausiert";
            langStrings["MsgResumed"] = "\n[>] Aufgabe fortgesetzt";
            langStrings["ErrPauseFailed"] = "\n[ERR] Pause/Fortsetzen fehlgeschlagen: ";
            langStrings["ErrNoFFmpeg"] = "[!] Fehler: Bitte FFmpeg-Pfad angeben!";
            langStrings["ErrNoInput"] = "[!] Fehler: Bitte gültige Quelldateien eingeben oder hineinziehen!";
            langStrings["MsgTaskStart"] = "[>_] Aufgabe gestartet...";
            langStrings["MsgBatchStart"] = "[>_] Stapelverarbeitung gestartet, {0} Dateien erkannt...";
            langStrings["MsgTaskDone"] = "\n" + new string('-', 50) + "\n[OK] Aufgabe erfolgreich abgeschlossen!";
            langStrings["MsgAllDone"] = "\n" + new string('-', 50) + "\n[OK] Alle Stapelaufgaben erfolgreich abgeschlossen!";
            langStrings["WarnOutDirNotExist"] = "[!] Warnung: Ausgabeordner existiert nicht, verwende Quellverzeichnis ({0})";
            langStrings["ErrPathParse"] = "[!] Pfadanalyse fehlgeschlagen: ";
            langStrings["MsgProcessingFile"] = "\n[>>>] Verarbeite Datei {0}/{1}...";
            langStrings["MsgInputFile"] = "[IN] Quelldatei: ";
            langStrings["MsgSingleOutput"] = "[OUT] Einzelausgabe: ";
            langStrings["MsgManualOutputDetected"] = "[OUT] Smart Mount: Manueller Ausgabestream erkannt.";
            langStrings["MsgManualOutputProtected"] = "[OUT] Abwehrmechanismus: Zeitstempel in manuelle Ausgabe injiziert, um Überschreiben zu verhindern!";
            langStrings["MsgFileDone"] = "\n[OK] Datei {0} verarbeitet!";
            langStrings["MsgTaskAborted"] = "\n" + new string('-', 50) + "\n[X] Aufgabe abgebrochen oder manuell storniert (Beendigungscode: {0}).";
            langStrings["ErrRunFailed"] = "\n[ERR] Ausführung fehlgeschlagen: ";
            langStrings["MsgSkipFile"] = "\n[!] Überspringe diese Datei, versuche die nächste...";
        }
        else // zh-CN
        {
            langStrings["AppTitle"] = "极简 FFmpeg 战术外骨骼";
            langStrings["PlaceholderFFmpeg"] = "输入 ffmpeg.exe 的完整路径";
            langStrings["PlaceholderInput"] = "输入音视频文件路径，可拖放多个文件 (以 | 符号分隔)";
            langStrings["PlaceholderOutput"] = "指定输出文件夹 (留空则默认输出至各源文件目录)";
            langStrings["PlaceholderArgs"] = "输入 ffmpeg 的参数，例如：-c:v libx264 -crf 23";
            langStrings["LogDefault"] = "控制台输出";
            langStrings["AboutTitle"] = "版权信息";
            langStrings["AboutText"] = "极简 FFmpeg 战术外骨骼\n(Minimalist FFmpeg Tactical Exoskeleton)\n为极致纯粹的命令行体验而生。\n\n版本: 1.2 (Batch Edition)\n版权所有 (C) {0} 大许律师";
            langStrings["LogMediaInfo"] = "\n--- [INFO] 首个媒体信息 (First Media Info) ---\n";
            langStrings["MsgInterrupting"] = "\n[!] 正在发送强行中断信号并清空任务队列，请稍候...";
            langStrings["ErrCancelFailed"] = "[ERR] 中断请求失败: ";
            langStrings["MsgPaused"] = "\n[||] 任务已暂停 (Paused)";
            langStrings["MsgResumed"] = "\n[>] 任务继续 (Resumed)";
            langStrings["ErrPauseFailed"] = "\n[ERR] 暂停/继续失败: ";
            langStrings["ErrNoFFmpeg"] = "[!] 错误: 请指定 FFmpeg 路径！";
            langStrings["ErrNoInput"] = "[!] 错误: 请输入或拖入有效的源文件！";
            langStrings["MsgTaskStart"] = "[>_] 任务启动...";
            langStrings["MsgBatchStart"] = "[>_] 批量任务启动，共检测到 {0} 个文件...";
            langStrings["MsgTaskDone"] = "\n" + new string('-', 50) + "\n[OK] 任务完美结束！";
            langStrings["MsgAllDone"] = "\n" + new string('-', 50) + "\n[OK] 所有批量任务完美结束！";
            langStrings["WarnOutDirNotExist"] = "[!] 警告: 指定的输出文件夹不存在，将输出至源文件目录 ({0})";
            langStrings["ErrPathParse"] = "[!] 路径解析失败: ";
            langStrings["MsgProcessingFile"] = "\n[>>>] 正在处理第 {0}/{1} 个文件...";
            langStrings["MsgInputFile"] = "[IN] 源文件: ";
            langStrings["MsgSingleOutput"] = "[OUT] 单轨输出: ";
            langStrings["MsgManualOutputDetected"] = "[OUT] 智能挂载：检测到您手动设定了输出分流。";
            langStrings["MsgManualOutputProtected"] = "[OUT] 防御机制：已为您手写的输出文件名强制植入时间戳，绝不会发生覆盖！";
            langStrings["MsgFileDone"] = "\n[OK] 文件 {0} 处理完毕！";
            langStrings["MsgTaskAborted"] = "\n" + new string('-', 50) + "\n[X] 任务异常中止或已被手动取消 (退出代码: {0})。已停止后续批处理。";
            langStrings["ErrRunFailed"] = "\n[ERR] 运行失败: ";
            langStrings["MsgSkipFile"] = "\n[!] 跳过此文件，尝试继续下一个...";
        }
    }

    private string GetStr(string key)
    {
        return langStrings.ContainsKey(key) ? langStrings[key] : key;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        this.ActiveControl = null; 
    }

    // =====================================================================
    // FEATURE: Extract Media Information via Background FFmpeg Process
    // =====================================================================
    private void FetchMediaInfo(string inputPath)
    {
        string ffmpeg = CleanPath(cmbFFmpeg.Text);
        if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(inputPath) || inputPath == lastInfoPath) return;
        lastInfoPath = inputPath; 

        string absFFmpeg = ffmpeg.Contains("\\") || ffmpeg.Contains("/") ? Path.GetFullPath(ffmpeg) : ffmpeg;
        string absInput = Path.GetFullPath(inputPath);

        ThreadPool.QueueUserWorkItem(new WaitCallback(delegate(object state) {
            try {
                Process p = new Process();
                p.StartInfo.FileName = absFFmpeg;
                p.StartInfo.Arguments = "-hide_banner -i \"" + absInput + "\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string output = p.StandardError.ReadToEnd();
                p.WaitForExit();

                string[] lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string info = GetStr("LogMediaInfo");
                bool hasInfo = false;
                foreach(string line in lines) {
                    if(line.TrimStart().StartsWith("Duration:") || line.TrimStart().StartsWith("Stream #")) {
                        info += line + "\n";
                        hasInfo = true;
                    }
                }
                if(hasInfo) {
                    info += "------------------------------------\n";
                    Log(info);
                }
            } catch { }
        }));
    }

    // =====================================================================
    // FEATURE: History Management (Load, Save, Add)
    // =====================================================================
    private void LoadHistory(ComboBox cmb, string data)
    {
        cmb.Items.Clear();
        string[] items = data.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items) cmb.Items.Add(item);
    }

    private string SaveHistory(ComboBox cmb)
    {
        string[] items = new string[cmb.Items.Count];
        for (int i = 0; i < cmb.Items.Count; i++) items[i] = cmb.Items[i].ToString();
        return string.Join("|", items);
    }

    private void AddToHistory(ComboBox cmb)
    {
        string text = cmb.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        
        if (cmb.Items.Contains(text)) cmb.Items.Remove(text);
        cmb.Items.Insert(0, text);
        
        if (cmb.Items.Count > 15) cmb.Items.RemoveAt(15);
        
        cmb.Text = text; 
    }

    private void LoadConfig()
    {
        try {
            if (File.Exists(configFile)) {
                string[] lines = File.ReadAllLines(configFile);
                foreach (string line in lines) {
                    if (line.StartsWith("FFmpegHistory=")) LoadHistory(cmbFFmpeg, line.Substring(14));
                    else if (line.StartsWith("InputHistory=")) LoadHistory(cmbInput, line.Substring(13));
                    else if (line.StartsWith("OutputHistory=")) LoadHistory(cmbOutputDir, line.Substring(14));
                    else if (line.StartsWith("ArgsHistory=")) LoadHistory(cmbArgs, line.Substring(12));
                }
            }
        } catch { }
    }

    private void SaveConfig()
    {
        AddToHistory(cmbFFmpeg);
        AddToHistory(cmbInput);
        AddToHistory(cmbOutputDir);
        AddToHistory(cmbArgs);

        try {
            string[] lines = new string[] {
                "Language=" + currentLanguage,
                "FFmpegHistory=" + SaveHistory(cmbFFmpeg),
                "InputHistory=" + SaveHistory(cmbInput),
                "OutputHistory=" + SaveHistory(cmbOutputDir),
                "ArgsHistory=" + SaveHistory(cmbArgs)
            };
            File.WriteAllLines(configFile, lines);
        } catch { }
    }

    // =====================================================================
    // FEATURE: Custom "About" Dialog (Triggered by Right-Click)
    // =====================================================================
    void Form_MouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Form aboutForm = new Form();
            aboutForm.Text = GetStr("AboutTitle");
            aboutForm.BackColor = Color.FromArgb(30, 30, 30); 
            aboutForm.ForeColor = Color.White;
            aboutForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            aboutForm.MaximizeBox = false;
            aboutForm.MinimizeBox = false;
            aboutForm.StartPosition = FormStartPosition.CenterParent;

            IntPtr hwnd = aboutForm.Handle; 
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
            DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, sizeof(int)); 

            PictureBox picBox = new PictureBox();
            picBox.Image = SystemIcons.Information.ToBitmap();
            picBox.Size = new Size(32, 32);
            picBox.Location = new Point(35, 35);
            picBox.SizeMode = PictureBoxSizeMode.StretchImage;
            aboutForm.Controls.Add(picBox);

            Label lbl = new Label();
            lbl.Text = string.Format(GetStr("AboutText"), DateTime.Now.Year);
            lbl.Font = new Font("Microsoft YaHei", 10);
            lbl.TextAlign = ContentAlignment.MiddleLeft; 
            lbl.AutoSize = true; 
            lbl.Location = new Point(85, 25);
            aboutForm.Controls.Add(lbl);

            aboutForm.PerformLayout(); 
            int textActualWidth = lbl.Width;
            int textActualHeight = lbl.Height;

            int formRequiredWidth = lbl.Left + textActualWidth + 40; 
            int formRequiredHeight = lbl.Top + textActualHeight + 90; 
            aboutForm.ClientSize = new Size(Math.Max(formRequiredWidth, 450), Math.Max(formRequiredHeight, 200));

            Button btnOK = new Button();
            btnOK.Text = "OK"; 
            btnOK.Size = new Size(100, 35);
            btnOK.Location = new Point((aboutForm.ClientSize.Width - 100) / 2, lbl.Bottom + 30);
            btnOK.FlatStyle = FlatStyle.Flat;
            btnOK.FlatAppearance.BorderSize = 1;
            btnOK.FlatAppearance.BorderColor = Color.Gray;
            btnOK.BackColor = Color.FromArgb(50, 50, 50); 
            btnOK.DialogResult = DialogResult.OK;
            aboutForm.Controls.Add(btnOK);

            aboutForm.ShowDialog(this);
        }
    }

    // =====================================================================
    // FEATURE: Path Cleaning Utility
    // =====================================================================
    private string CleanPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        path = path.Trim();
        path = path.Trim('\"'); 
        path = path.Trim('\''); 
        
        if (path.EndsWith("\\") && !path.EndsWith(":\\")) 
        {
            path = path.TrimEnd('\\');
        }
        return path;
    }

    // =====================================================================
    // FEATURE: Responsive Layout Resizing
    // =====================================================================
    void Form_Resize(object sender, EventArgs e)
    {
        int spacing = 20;
        int totalWidth = btnRun.Width + spacing + btnCancel.Width + spacing + btnLang.Width;
        int startX = (this.ClientSize.Width - totalWidth) / 2;
        
        btnRun.Left = startX;
        btnCancel.Left = btnRun.Right + spacing;
        btnLang.Left = btnCancel.Right + spacing;
        
        btnRun.Top = 270;
        btnCancel.Top = 270;
        btnLang.Top = 270;
    }

    // =====================================================================
    // FEATURE: UI Factory for Dark Theme ComboBoxes
    // =====================================================================
    private DarkComboBox MakeComboBox(int y, string placeholder)
    {
        DarkComboBox cmb = new DarkComboBox();
        cmb.Location = new Point(20, y);
        cmb.Size = new Size(this.ClientSize.Width - 40, 40); 
        cmb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; 
        cmb.Font = new Font("Consolas", 14); 
        
        cmb.BackColor = Color.FromArgb(15, 15, 15);
        cmb.ForeColor = Color.White;
        
        cmb.FlatStyle = FlatStyle.Flat;
        cmb.DropDownStyle = ComboBoxStyle.DropDown; 
        
        SendMessage(cmb.Handle, CB_SETCUEBANNER, 0, placeholder);
        SetWindowTheme(cmb.Handle, "DarkMode_Explorer", null); 
        
        this.Controls.Add(cmb);
        return cmb;
    }

    // =====================================================================
    // FEATURE: Dark Mode Window Title Bar (DWM API)
    // =====================================================================
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int useImmersiveDarkMode = 1;
        DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
        DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int)); 
    }

    // =====================================================================
    // FEATURE: Drag and Drop Support
    // =====================================================================
    void cmbInput_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    void cmbInput_DragDrop(object sender, DragEventArgs e)
    {
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0) {
            List<string> paths = new List<string>();
            foreach(string f in files) paths.Add(CleanPath(f));
            
            cmbInput.Text = string.Join(" | ", paths);
            FetchMediaInfo(paths[0]); 
        }
    }

    // =====================================================================
    // FEATURE: Thread-safe RichTextBox Logging
    // =====================================================================
    void Log(string msg)
    {
        if (this.InvokeRequired) { this.Invoke(new Action<string>(Log), new object[] { msg }); return; }
        
        if (txtLog.Text == GetStr("LogDefault")) {
            txtLog.Clear();
            txtLog.ForeColor = Color.LightGreen;
        }
        
        txtLog.AppendText(msg + "\n");
        txtLog.ScrollToCaret();
    }

    // =====================================================================
    // FEATURE: Process Status & Cancellation Handling
    // =====================================================================
    private bool IsProcessRunning()
    {
        if (currentProcess == null) return false;
        try { return !currentProcess.HasExited; } catch { return false; }
    }

    void BtnCancel_Click(object sender, EventArgs e)
    {
        if (IsProcessRunning())
        {
            try {
                Log(GetStr("MsgInterrupting"));
                batchQueue.Clear(); 
                if (isPaused) {
                    try { NtResumeProcess(currentProcess.Handle); } catch { }
                }
                currentProcess.Kill(); 
                btnCancel.Enabled = false; 
            } catch (Exception ex) {
                Log(GetStr("ErrCancelFailed") + ex.Message);
            }
        }
    }

    // =====================================================================
    // FEATURE: Process Execution, Pause/Resume & Batch Queue Initialization
    // =====================================================================
    void BtnRun_Click(object sender, EventArgs e)
    {
        if (IsProcessRunning())
        {
            try {
                if (!isPaused) {
                    NtSuspendProcess(currentProcess.Handle); 
                    isPaused = true;
                    btnRun.Text = "▶"; 
                    Log(GetStr("MsgPaused"));
                } else {
                    NtResumeProcess(currentProcess.Handle); 
                    isPaused = false;
                    btnRun.Text = "⏸"; 
                    Log(GetStr("MsgResumed"));
                }
            } catch (Exception ex) {
                Log(GetStr("ErrPauseFailed") + ex.Message);
            }
            return; 
        }

        string ffmpeg = CleanPath(cmbFFmpeg.Text);
        if (string.IsNullOrEmpty(ffmpeg)) { Log(GetStr("ErrNoFFmpeg")); return; }

        string[] inputs = cmbInput.Text.Split(new string[] { "|", " | " }, StringSplitOptions.RemoveEmptyEntries);
        batchQueue.Clear();
        foreach (string rawInput in inputs) {
            string input = CleanPath(rawInput);
            if (!string.IsNullOrEmpty(input) && File.Exists(input)) {
                batchQueue.Enqueue(input);
            }
        }

        totalBatchFiles = batchQueue.Count;
        if (totalBatchFiles == 0) { Log(GetStr("ErrNoInput")); return; }
        currentBatchIndex = 0;

        SaveConfig(); 

        isPaused = false;
        btnRun.Text = "⏸"; 
        btnCancel.Enabled = true; 
        
        txtLog.Clear();
        txtLog.ForeColor = Color.LightGreen;
        
        if (totalBatchFiles == 1) {
            Log(GetStr("MsgTaskStart"));
        } else {
            Log(string.Format(GetStr("MsgBatchStart"), totalBatchFiles));
        }
        
        ProcessNextFile(ffmpeg);
    }

    // =====================================================================
    // FEATURE: Core FFmpeg Command Building and Execution (Batch Loop)
    // =====================================================================
    private void ProcessNextFile(string ffmpeg)
    {
        if (batchQueue.Count == 0)
        {
            this.Invoke(new Action(() => {
                if (totalBatchFiles == 1) {
                    Log(GetStr("MsgTaskDone"));
                } else {
                    Log(GetStr("MsgAllDone"));
                }
                btnRun.Text = "▶"; 
                btnCancel.Enabled = false;
                isPaused = false;
                currentProcess = null; 
            }));
            return;
        }

        string input = batchQueue.Dequeue();
        currentBatchIndex++;
        
        string outDir = CleanPath(cmbOutputDir.Text);
        string finalOutDir = "";
        if (!string.IsNullOrEmpty(outDir)) {
            if (Directory.Exists(outDir)) finalOutDir = outDir;
            else { 
                Log(string.Format(GetStr("WarnOutDirNotExist"), outDir)); 
                finalOutDir = Path.GetDirectoryName(input); 
            }
        } else {
            finalOutDir = Path.GetDirectoryName(input); 
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safeArgs = cmbArgs.Text.Trim();
        
        safeArgs = Regex.Replace(safeArgs, @"^(""[^""]*?ffmpeg(?:\.exe)?""|'[^']*?ffmpeg(?:\.exe)?'|\S*?ffmpeg(?:\.exe)?)\s+", "", RegexOptions.IgnoreCase);
        safeArgs = Regex.Replace(safeArgs, @"(^|\s)-i(?=\s+-|$)", " ");
        safeArgs = safeArgs.Trim();

        bool hasManualOutput = false;

        string exts = @"mp4|mkv|avi|mov|mp3|m4a|wav|aac|flac|ts|flv|webm|gif|m2ts";
        string pattern = @"(?<=^|\s)(\""[^\""]+\.(?:" + exts + @")\""|\'[^\']+\.(?:" + exts + @")\'|[^\s\""\'=]+\.(?:" + exts + @"))(?=\s|$)";

        safeArgs = Regex.Replace(safeArgs, pattern, m => {
            int index = m.Index;
            string before = safeArgs.Substring(0, index).TrimEnd();
            
            if (before.EndsWith("-i")) return m.Value; 

            hasManualOutput = true;
            string file = m.Value;
            bool doubleQuoted = file.StartsWith("\"") && file.EndsWith("\"");
            bool singleQuoted = file.StartsWith("'") && file.EndsWith("'");
            
            string unquoted = file;
            if (doubleQuoted || singleQuoted) unquoted = file.Substring(1, file.Length - 2);

            string ext = Path.GetExtension(unquoted);
            string name = unquoted.Substring(0, unquoted.Length - ext.Length);
            string newName = name + "_" + timestamp + ext;

            if (doubleQuoted) return "\"" + newName + "\"";
            if (singleQuoted) return "'" + newName + "'";
            return newName;
        }, RegexOptions.IgnoreCase);

        string defaultOutputFilePath = "";
        try {
            string fileNameOnly = Path.GetFileNameWithoutExtension(input);
            defaultOutputFilePath = Path.Combine(finalOutDir, fileNameOnly + "_" + timestamp + ".mp4");
        } catch (Exception ex) { 
            Log(GetStr("ErrPathParse") + ex.Message); 
            ProcessNextFile(ffmpeg); 
            return; 
        }

        if (totalBatchFiles > 1) {
            Log(string.Format(GetStr("MsgProcessingFile"), currentBatchIndex, totalBatchFiles));
        }
        
        Log(GetStr("MsgInputFile") + input);

        currentProcess = new Process();
        string absFFmpeg = ffmpeg.Contains("\\") || ffmpeg.Contains("/") ? Path.GetFullPath(ffmpeg) : ffmpeg;
        string absInput = Path.GetFullPath(input);

        currentProcess.StartInfo.FileName = absFFmpeg;
        currentProcess.StartInfo.WorkingDirectory = finalOutDir; 

        string args = "-y -i \"" + absInput + "\" " + safeArgs;
        
        if (!hasManualOutput) {
            args += " \"" + defaultOutputFilePath + "\""; 
            Log(GetStr("MsgSingleOutput") + defaultOutputFilePath);
        } else {
            Log(GetStr("MsgManualOutputDetected"));
            Log(GetStr("MsgManualOutputProtected"));
        }
        
        Log(new string('-', 50));

        currentProcess.StartInfo.Arguments = args;
        currentProcess.StartInfo.UseShellExecute = false;
        currentProcess.StartInfo.RedirectStandardError = true;
        currentProcess.StartInfo.CreateNoWindow = true;

        currentProcess.ErrorDataReceived += new DataReceivedEventHandler(Process_ErrorDataReceived);
        
        Thread monitorThread = new Thread(() => {
            try {
                currentProcess.WaitForExit(); 
                int exitCode = currentProcess.ExitCode;

                this.Invoke(new Action(() => {
                    if (exitCode == 0) {
                        if (totalBatchFiles > 1) {
                            Log(string.Format(GetStr("MsgFileDone"), currentBatchIndex));
                        }
                        ProcessNextFile(ffmpeg); 
                    }
                    else {
                        Log(string.Format(GetStr("MsgTaskAborted"), exitCode));
                        btnRun.Text = "▶"; 
                        btnCancel.Enabled = false;
                        isPaused = false;
                        currentProcess = null; 
                        batchQueue.Clear(); 
                    }
                }));
            } catch { }
        });
        
        try { 
            currentProcess.Start(); 
            currentProcess.BeginErrorReadLine(); 
            monitorThread.IsBackground = true;
            monitorThread.Start(); 
        } 
        catch (Exception ex) { 
            Log(GetStr("ErrRunFailed") + ex.Message); 
            this.Invoke(new Action(() => {
                Log(GetStr("MsgSkipFile"));
                ProcessNextFile(ffmpeg); 
            }));
        }
    }

    void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data)) Log(e.Data);
    }

    [STAThread]
    static void Main() {
        if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
        
        try { SetPreferredAppMode(2); } catch { }
        try { AllowDarkModeForApp(true); } catch { }
        try { FlushMenuThemes(); } catch { }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new FFmpegGUI());
    }
}