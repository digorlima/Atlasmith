#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Atlasmith.Core;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor
{
    /// <summary>
    /// A janela do "Create Atlas": as mesmas opções de empacotamento do app, sem campos
    /// de caminho nem projetos salvos. Abre gerando com as opções guardadas na planilha
    /// do atlas (ou as padrão); o botão Gerar só é preciso depois de mudar alguma coisa.
    /// </summary>
    public sealed class CreateAtlasWindow : EditorWindow
    {
        private static readonly int[] MaxSizes = { 512, 1024, 2048, 4096, 8192, 16384 };

        [SerializeField] private string _kritaPath = "";
        [SerializeField] private string _outputFolder = "";
        [SerializeField] private string _atlasName = "";
        [SerializeField] private RecipeOrigin _origin;
        [SerializeField] private string _recipeSheet = "";

        [SerializeField] private int _padding = PackSettings.DefaultPadding;
        [SerializeField] private int _borderPadding;
        [SerializeField] private int _extrude;
        [SerializeField] private int _maxSize = PackSettings.DefaultMaxSize;
        [SerializeField] private bool _powerOfTwo = true;
        [SerializeField] private bool _forceSquare;
        [SerializeField] private bool _ignoreHidden = true;

        [SerializeField] private bool _settingsChanged;
        [SerializeField] private bool _showOutlines = true;
        [SerializeField] private bool _settingsOpen = true;
        [SerializeField] private bool _logOpen = true;
        [SerializeField] private List<string> _log = new List<string>();
        [SerializeField] private List<string> _queue = new List<string>();

        [SerializeField] private bool _hasMetrics;
        [SerializeField] private int _metricWidth, _metricHeight, _metricSprites;
        [SerializeField] private double _metricOccupancy, _metricSeconds;
        [SerializeField] private string _status = "";
        [SerializeField] private MessageType _statusType = MessageType.None;

        private AtlasGenerationJob? _job;
        private Vector2 _scroll, _logScroll;
        private Texture2D? _preview;
        private AtlasSheet? _previewSheet;
        private GUIStyle? _metricValue, _metricCaption, _logStyle;

        // ------------------------------------------------------------------
        // Abertura
        // ------------------------------------------------------------------

        /// <summary>Abre a janela para um ou mais arquivos do Krita e, se pedido, já gera.</summary>
        public static CreateAtlasWindow Open(IEnumerable<string> kritaPaths, bool generateNow)
        {
            AtlasmithLanguage.Apply();
            var window = GetWindow<CreateAtlasWindow>();
            window.minSize = new Vector2(380, 520);
            window.Show();

            var paths = kritaPaths.ToList();
            if (paths.Count == 0) return window;

            if (window._job != null && window._job.IsRunning)
            {
                window._queue.AddRange(paths);
                return window;
            }

            window.Target(paths[0]);
            window._queue.AddRange(paths.Skip(1));
            if (generateNow) window.StartGeneration();
            return window;
        }

        private void Target(string kritaPath)
        {
            var recipe = AtlasRecipe.For(kritaPath);
            _kritaPath = kritaPath;
            _outputFolder = recipe.OutputFolder;
            _atlasName = recipe.AtlasName;
            _origin = recipe.Origin;
            _recipeSheet = recipe.RecipeSheetPath ?? "";
            LoadSettings(recipe.Settings, recipe.IgnoreHiddenLayers);
            _settingsChanged = false;
            _hasMetrics = false;
            _status = "";
            _log.Clear();
            _log.Add(DescribeOrigin(recipe));
            LoadPreview(fromExisting: true);
        }

        private void LoadSettings(PackSettings s, bool ignoreHidden)
        {
            _padding = s.Padding;
            _borderPadding = s.BorderPadding;
            _extrude = s.Extrude;
            _maxSize = s.MaxSize;
            _powerOfTwo = s.PowerOfTwo;
            _forceSquare = s.ForceSquare;
            _ignoreHidden = ignoreHidden;
        }

        private PackSettings CurrentSettings() => new PackSettings
        {
            Padding = _padding,
            BorderPadding = _borderPadding,
            Extrude = _extrude,
            MaxSize = _maxSize,
            PowerOfTwo = _powerOfTwo,
            ForceSquare = _forceSquare
        }.Clamped();

        private AtlasRecipe CurrentRecipe() => new AtlasRecipe
        {
            KritaPath = _kritaPath,
            OutputFolder = _outputFolder,
            AtlasName = _atlasName,
            Settings = CurrentSettings(),
            IgnoreHiddenLayers = _ignoreHidden,
            Origin = _origin,
            RecipeSheetPath = _recipeSheet.Length > 0 ? _recipeSheet : null
        };

        private string DescribeOrigin(AtlasRecipe recipe)
        {
            var text = Texts.Current;
            switch (recipe.Origin)
            {
                case RecipeOrigin.SheetForThisFile: return text.SettingsFromSheet(recipe.RecipeSheetPath ?? recipe.SheetPath);
                case RecipeOrigin.ExistingSheet: return text.SettingsFromExistingAtlas(recipe.RecipeSheetPath ?? recipe.SheetPath);
                default: return text.SettingsDefault;
            }
        }

        // ------------------------------------------------------------------
        // Ciclo de vida
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            AtlasmithLanguage.Apply();
            AtlasmithLanguage.Changed += OnLanguageChanged;
            UpdateTitle();
            if (_kritaPath.Length > 0) LoadPreview(fromExisting: !_hasMetrics);
        }

        private void OnDisable()
        {
            AtlasmithLanguage.Changed -= OnLanguageChanged;
            if (_job == null) return;

            // Fechar a janela não pode deixar o AssetDatabase travado.
            _job.Cancel();
            try
            {
                _job.Work?.Wait(10000);
            }
            catch (AggregateException)
            {
            }
            AtlasGenerator.Finish(_job);
            _job = null;
        }

        private void OnLanguageChanged()
        {
            UpdateTitle();
            Repaint();
        }

        private void UpdateTitle()
        {
            titleContent = new GUIContent(Texts.Current.CreateAtlasTitle, AssetPreview.GetMiniTypeThumbnail(typeof(Sprite)));
        }

        private void Update()
        {
            if (_job == null) return;

            foreach (string line in _job.DrainLog()) _log.Add(line);
            if (_job.IsCompleted) Complete();
            Repaint();
        }

        // ------------------------------------------------------------------
        // Geração
        // ------------------------------------------------------------------

        /// <param name="earlierLog">Linhas que continuam no topo do log (quando a geração recomeça depois de um diálogo).</param>
        private void StartGeneration(List<string>? earlierLog = null)
        {
            if (_job != null || _kritaPath.Length == 0) return;
            var text = Texts.Current;
            var recipe = CurrentRecipe();

            bool convert = false;
            if (earlierLog == null)
            {
                if (recipe.HasTexturePackerSheet)
                {
                    if (!EditorUtility.DisplayDialog(text.ConvertFirstTitle, text.ConvertFirstMessage(Path.GetFileName(recipe.TpSheetPath)),
                            text.ConvertFirstOk, text.Cancel))
                    {
                        _log.Add(text.ConvertFirstDeclined);
                        SetStatus(text.ConvertFirstDeclined, MessageType.Warning);
                        return;
                    }
                    convert = true;
                }
                else if (!ConfirmOverwriteOtherSource(recipe))
                {
                    return;
                }
            }

            _log.Clear();
            if (earlierLog != null) _log.AddRange(earlierLog);
            SetStatus("", MessageType.None);
            _job = AtlasGenerator.Start(recipe, convert);
            _log.AddRange(_job.Result.Log);
            if (_job.IsCompleted) Complete();
        }

        /// <summary>
        /// A geração parou porque sairiam sprites do atlas. Pergunta o que fazer; nunca
        /// remove em silêncio. Devolve true se uma geração nova começou.
        /// </summary>
        private bool HandlePendingRemoval(AtlasGenerationResult result)
        {
            var text = Texts.Current;
            var check = result.PendingRemoval!;

            // Sem interface (modo batch), não há a quem perguntar: avisa e não grava.
            if (Application.isBatchMode)
            {
                Debug.LogWarning(text.RemovalSkipped(result.Recipe.PngPath, check));
                SetStatus(text.RemovalCancelled, MessageType.Warning);
                return false;
            }

            string message = text.RemovalMessage(result.Recipe.AtlasName, check);
            int choice; // 0 = incluir ocultas, 1 = cancelar, 2 = remover e gerar
            if (check.CanIncludeHidden)
                choice = EditorUtility.DisplayDialogComplex(text.RemovalTitle, message, text.RemovalIncludeHidden, text.Cancel, text.RemovalGenerateAnyway);
            else
                choice = EditorUtility.DisplayDialog(text.RemovalTitle, message, text.RemovalGenerateAnyway, text.Cancel) ? 2 : 1;

            switch (choice)
            {
                case 0:
                    _ignoreHidden = false;
                    _settingsChanged = true;
                    var earlier = new List<string>(result.Log) { text.RemovalIncludingHidden };
                    StartGeneration(earlier); // com earlierLog, sempre começa (e cuida da fila ao terminar)
                    return true;
                case 2:
                    _job = AtlasGenerator.Resume(result);
                    _log.Clear();
                    _log.AddRange(_job.Result.Log);
                    if (_job.IsCompleted) Complete();
                    return true;
                default:
                    _log.Add(text.RemovalCancelled);
                    SetStatus(text.RemovalCancelled, MessageType.Warning);
                    return false;
            }
        }

        /// <summary>O destino escolhido pode ser o atlas de outro arquivo do Krita.</summary>
        private bool ConfirmOverwriteOtherSource(AtlasRecipe recipe)
        {
            string folderFull = ProjectPaths.ToFull(recipe.OutputFolder);
            var sheet = AtlasSheet.TryRead(ProjectPaths.ToFull(recipe.SheetPath));
            if (sheet?.Source == null || sheet.Source.Path.Length == 0) return true;

            string from = sheet.Source.Resolve(folderFull);
            if (ProjectPaths.SameFile(from, ProjectPaths.ToFull(recipe.KritaPath))) return true;

            var text = Texts.Current;
            return EditorUtility.DisplayDialog(text.CreateAtlasTitle,
                text.ConfirmOtherSource(recipe.AtlasName, ProjectPaths.ToAssetPath(from) ?? from, recipe.KritaPath),
                text.Replace, text.Cancel);
        }

        private void Complete()
        {
            if (_job == null) return;
            var result = AtlasGenerator.Finish(_job);
            _job = null;

            // result.Log tem o log inteiro, na ordem (inclusive o que já apareceu durante a geração).
            _log.Clear();
            _log.AddRange(result.Log);

            var text = Texts.Current;
            if (result.PendingRemoval != null)
            {
                if (HandlePendingRemoval(result)) return;
            }
            else if (result.Succeeded)
            {
                _hasMetrics = true;
                _metricWidth = result.Width;
                _metricHeight = result.Height;
                _metricSprites = result.SpriteCount;
                _metricOccupancy = result.Occupancy;
                _metricSeconds = result.Seconds;
                _settingsChanged = false;
                _origin = RecipeOrigin.SheetForThisFile;
                _recipeSheet = result.Recipe.SheetPath;
                SetStatus(result.Warnings.Count > 0 ? string.Join("\n", result.Warnings) : text.GeneratedOk(result.Recipe.PngPath),
                    result.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
                LoadPreview(fromExisting: false);
            }
            else if (result.Cancelled)
            {
                SetStatus(text.GenerationCancelled, MessageType.Warning);
            }
            else
            {
                SetStatus(result.Error ?? text.GenerationFailedUnknown, MessageType.Error);
            }

            if (_queue.Count > 0)
            {
                string next = _queue[0];
                _queue.RemoveAt(0);
                Target(next);
                StartGeneration();
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        private void LoadPreview(bool fromExisting)
        {
            _preview = null;
            _previewSheet = null;
            if (_outputFolder.Length == 0 || _atlasName.Length == 0) return;

            string png = ProjectPaths.Combine(_outputFolder, _atlasName + ".png");
            _preview = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
            _previewSheet = AtlasSheet.TryRead(ProjectPaths.ToFull(ProjectPaths.Combine(_outputFolder, _atlasName + AtlasSheet.Extension)))
                            ?? TpSheet.TryRead(ProjectPaths.ToFull(ProjectPaths.Combine(_outputFolder, _atlasName + TpSheet.Extension)));

            if (fromExisting && _previewSheet != null && !_hasMetrics)
            {
                _metricWidth = _previewSheet.Width;
                _metricHeight = _previewSheet.Height;
                _metricSprites = _previewSheet.Sprites.Count;
                double area = (double)_previewSheet.Width * _previewSheet.Height;
                _metricOccupancy = area <= 0 ? 0 : _previewSheet.Sprites.Sum(s => s.Rect.Area) / area;
                _metricSeconds = -1;
            }
        }

        // ------------------------------------------------------------------
        // Interface
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            var text = Texts.Current;
            EnsureStyles();
            bool busy = _job != null;

            DrawToolbar(text);

            if (_kritaPath.Length == 0)
            {
                EditorGUILayout.HelpBox(text.NoKritaSelected, MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawPaths(text, busy);
            EditorGUILayout.Space(4);
            DrawSettings(text, busy);
            EditorGUILayout.Space(6);
            DrawActions(text, busy);

            if (_status.Length > 0 && !busy) EditorGUILayout.HelpBox(_status, _statusType);

            EditorGUILayout.Space(6);
            DrawMetrics(text);
            DrawPreview(text);
            EditorGUILayout.Space(4);
            DrawLog(text);

            EditorGUILayout.EndScrollView();
        }

        private void EnsureStyles()
        {
            if (_metricValue != null) return;
            _metricValue = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            _metricCaption = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            _logStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false, fontSize = 11 };
        }

        private void DrawToolbar(Texts text)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string title = _kritaPath.Length > 0 ? Path.GetFileName(_kritaPath) : text.CreateAtlasTitle;
                GUILayout.Label(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                GUILayout.Label(text.LanguageLabel, EditorStyles.miniLabel);
                var options = new[] { text.LanguageAuto, "Português", "English" };
                int current = (int)AtlasmithLanguage.Option;
                int chosen = EditorGUILayout.Popup(current, options, EditorStyles.toolbarPopup, GUILayout.Width(110));
                if (chosen != current) AtlasmithLanguage.Option = (AtlasmithLanguageOption)chosen;
            }
        }

        private void DrawPaths(Texts text, bool busy)
        {
            EditorGUILayout.Space(4);
            bool exists = File.Exists(ProjectPaths.ToFull(_kritaPath));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(text.SourceLabel, GUILayout.Width(70));
                EditorGUILayout.SelectableLabel(_kritaPath, EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (GUILayout.Button(text.Show, EditorStyles.miniButton, GUILayout.Width(60)))
                        EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(_kritaPath));
                }
            }

            string png = ProjectPaths.Combine(_outputFolder, _atlasName + ".png");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(text.OutputLabel, GUILayout.Width(70));
                EditorGUILayout.SelectableLabel(png, EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                using (new EditorGUI.DisabledScope(busy))
                {
                    if (GUILayout.Button(text.Change, EditorStyles.miniButton, GUILayout.Width(60))) ChangeOutput(text);
                }
            }

            if (!exists) EditorGUILayout.HelpBox(text.KritaFileMissing(_kritaPath), MessageType.Error);
            else if (File.Exists(ProjectPaths.ToFull(ProjectPaths.Combine(_outputFolder, _atlasName + TpSheet.Extension))))
                EditorGUILayout.HelpBox(text.TargetUsesTexturePacker, MessageType.Info);
        }

        private void ChangeOutput(Texts text)
        {
            string chosen = EditorUtility.SaveFilePanelInProject(text.ChangeOutputTitle, _atlasName, "png", text.ChangeOutputMessage, _outputFolder);
            if (string.IsNullOrEmpty(chosen)) return;

            _outputFolder = ProjectPaths.FolderOf(chosen);
            _atlasName = Path.GetFileNameWithoutExtension(chosen);
            _settingsChanged = true;
            _hasMetrics = false;
            LoadPreview(fromExisting: true);
        }

        private void DrawSettings(Texts text, bool busy)
        {
            _settingsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_settingsOpen, text.SettingsHeader);
            if (_settingsOpen)
            {
                using (new EditorGUI.DisabledScope(busy))
                {
                    EditorGUI.BeginChangeCheck();
                    _padding = EditorGUILayout.IntSlider(new GUIContent(text.Padding, text.PaddingHint), _padding, 0, PackSettings.MaxPadding);
                    _borderPadding = EditorGUILayout.IntSlider(new GUIContent(text.BorderPadding, text.BorderPaddingHint), _borderPadding, 0, PackSettings.MaxPadding);
                    _extrude = EditorGUILayout.IntSlider(new GUIContent(text.Extrude, text.ExtrudeHint), _extrude, 0, PackSettings.MaxExtrude);

                    int index = Array.IndexOf(MaxSizes, _maxSize);
                    if (index < 0) index = Array.IndexOf(MaxSizes, PackSettings.DefaultMaxSize);
                    var labels = MaxSizes.Select(s => new GUIContent(s.ToString(CultureInfo.InvariantCulture))).ToArray();
                    index = EditorGUILayout.Popup(new GUIContent(text.MaxSize, text.MaxSizeHint), index, labels);
                    _maxSize = MaxSizes[index];

                    _powerOfTwo = EditorGUILayout.Toggle(new GUIContent(text.PowerOfTwo, text.PowerOfTwoHint), _powerOfTwo);
                    _forceSquare = EditorGUILayout.Toggle(new GUIContent(text.Square, text.SquareHint), _forceSquare);
                    _ignoreHidden = EditorGUILayout.Toggle(new GUIContent(text.IgnoreHidden, text.IgnoreHiddenHint), _ignoreHidden);
                    if (EditorGUI.EndChangeCheck()) _settingsChanged = true;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawActions(Texts text, bool busy)
        {
            if (busy && _job != null)
            {
                Rect bar = EditorGUILayout.GetControlRect(false, 22);
                EditorGUI.ProgressBar(bar, _job.Progress, _job.Stage);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(text.Cancel, GUILayout.Width(100))) _job.Cancel();
                }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(text.ResetDefaults), GUILayout.Height(26), GUILayout.Width(150)))
                {
                    LoadSettings(new PackSettings(), true);
                    _settingsChanged = true;
                }

                GUILayout.FlexibleSpace();
                bool exists = File.Exists(ProjectPaths.ToFull(_kritaPath));
                using (new EditorGUI.DisabledScope(!exists))
                {
                    var previous = GUI.backgroundColor;
                    if (_settingsChanged) GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
                    if (GUILayout.Button(new GUIContent(text.Generate, text.GenerateHint), GUILayout.Height(26), GUILayout.MinWidth(140)))
                        StartGeneration();
                    GUI.backgroundColor = previous;
                }
            }

            if (_settingsChanged) EditorGUILayout.LabelField(text.SettingsChangedHint, EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawMetrics(Texts text)
        {
            if (!_hasMetrics && _previewSheet == null) return;

            var c = text.Culture;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                Metric(text.MetricSize, string.Format(c, "{0}×{1}", _metricWidth, _metricHeight));
                Metric(text.MetricOccupancy, string.Format(c, "{0:0.0}%", _metricOccupancy * 100));
                Metric(text.MetricSprites, _metricSprites.ToString(c));
                Metric(text.MetricTime, _metricSeconds < 0 ? "—" : string.Format(c, "{0:0.00} s", _metricSeconds));
            }
        }

        private void Metric(string caption, string value)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label(value, _metricValue);
                GUILayout.Label(caption, _metricCaption);
            }
        }

        private void DrawPreview(Texts text)
        {
            if (_preview == null || _previewSheet == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(text.PreviewHeader, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                _showOutlines = GUILayout.Toggle(_showOutlines, new GUIContent(text.Outlines, text.OutlinesHint), EditorStyles.miniButton, GUILayout.Width(120));
            }

            float width = Mathf.Max(64, position.width - 24);
            float aspect = _previewSheet.Height <= 0 ? 1 : (float)_previewSheet.Height / Mathf.Max(1, _previewSheet.Width);
            float height = Mathf.Clamp(width * aspect, 120, Mathf.Max(160, position.height * 0.6f));
            Rect area = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
            Rect fit = Fit(area, _previewSheet.Width, _previewSheet.Height);

            EditorGUI.DrawTextureTransparent(fit, _preview, ScaleMode.StretchToFill);
            if (!_showOutlines) return;

            float sx = fit.width / Mathf.Max(1, _previewSheet.Width);
            float sy = fit.height / Mathf.Max(1, _previewSheet.Height);
            var outline = EditorGUIUtility.isProSkin ? new Color(0.35f, 0.85f, 1f, 0.9f) : new Color(0.05f, 0.45f, 0.85f, 0.9f);
            string? hovered = null;
            Vector2 mouse = Event.current.mousePosition;

            foreach (var sprite in _previewSheet.Sprites)
            {
                var r = new Rect(fit.x + sprite.Rect.X * sx, fit.y + sprite.Rect.Y * sy, sprite.Rect.W * sx, sprite.Rect.H * sy);
                bool over = r.Contains(mouse);
                if (over) hovered = sprite.Name;
                if (Event.current.type != EventType.Repaint) continue;

                if (over) EditorGUI.DrawRect(r, new Color(outline.r, outline.g, outline.b, 0.18f));
                DrawFrame(r, outline);
            }

            if (hovered != null)
            {
                var label = new GUIContent(hovered);
                Vector2 size = EditorStyles.helpBox.CalcSize(label);
                var tip = new Rect(Mathf.Min(mouse.x + 12, area.xMax - size.x), Mathf.Min(mouse.y + 14, area.yMax - size.y), size.x, size.y);
                GUI.Label(tip, label, EditorStyles.helpBox);
            }
            if (area.Contains(mouse)) Repaint();
        }

        private static Rect Fit(Rect area, int w, int h)
        {
            if (w <= 0 || h <= 0) return area;
            float scale = Mathf.Min(area.width / w, area.height / h);
            float fw = w * scale, fh = h * scale;
            return new Rect(area.x + (area.width - fw) / 2, area.y + (area.height - fh) / 2, fw, fh);
        }

        private static void DrawFrame(Rect r, Color color)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), color);
        }

        private void DrawLog(Texts text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _logOpen = EditorGUILayout.Foldout(_logOpen, text.LogHeader, true);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(text.Copy, EditorStyles.miniButtonLeft, GUILayout.Width(60)))
                    EditorGUIUtility.systemCopyBuffer = string.Join("\n", _log);
                if (GUILayout.Button(text.Clear, EditorStyles.miniButtonRight, GUILayout.Width(60))) _log.Clear();
            }
            if (!_logOpen || _logStyle == null) return;

            string content = string.Join("\n", _log);
            float width = position.width - 30;
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight * 3, _logStyle.CalcHeight(new GUIContent(content), width));
            using (var scope = new EditorGUILayout.ScrollViewScope(_logScroll, EditorStyles.helpBox, GUILayout.MinHeight(80), GUILayout.MaxHeight(260)))
            {
                _logScroll = scope.scrollPosition;
                EditorGUILayout.SelectableLabel(content, _logStyle, GUILayout.Height(height));
            }
        }
    }
}
