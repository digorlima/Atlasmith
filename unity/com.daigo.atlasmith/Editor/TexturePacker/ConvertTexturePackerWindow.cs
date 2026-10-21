#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Atlasmith.Core;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor
{
    /// <summary>Lista os .tpsheet do projeto e converte os marcados para .atlasmith.json.</summary>
    public sealed class ConvertTexturePackerWindow : EditorWindow
    {
        public const string ToolsItem = "Tools/Atlasmith/Convert TexturePacker Sheets…";
        public const string ContextItem = "Assets/Convert to Atlasmith";

        private List<TpSheetCandidate> _candidates = new List<TpSheetCandidate>();
        private readonly HashSet<string> _checked = new HashSet<string>(StringComparer.Ordinal);
        private List<TpConversionResult> _results = new List<TpConversionResult>();
        private Vector2 _listScroll, _resultScroll;

        [MenuItem(ToolsItem, false, 100)]
        public static void OpenWindow()
        {
            AtlasmithLanguage.Apply();
            var window = GetWindow<ConvertTexturePackerWindow>();
            window.minSize = new Vector2(460, 360);
            window.Reload(checkAll: true);
            window.Show();
        }

        [MenuItem(ContextItem, false, CreateAtlasMenu.CreateAtlasPriority + 1)]
        private static void ConvertSelected()
        {
            AtlasmithLanguage.Apply();
            var text = Texts.Current;
            var paths = SelectedTpSheets();
            if (paths.Count == 0) return;

            if (!EditorUtility.DisplayDialog(text.ConvertTitle, text.ConvertConfirm(paths.Count), text.ConvertButton, text.Cancel)) return;

            var results = RunWithProgress(paths);
            var window = GetWindow<ConvertTexturePackerWindow>();
            window.minSize = new Vector2(460, 360);
            window.Reload(checkAll: false);
            window._results = results;
            window.Show();
        }

        [MenuItem(ContextItem, true)]
        private static bool ConvertSelectedValidate() => SelectedTpSheets().Count > 0;

        private static List<string> SelectedTpSheets() =>
            Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && TpSheet.IsSheetFile(p))
                .Distinct()
                .ToList();

        private static List<TpConversionResult> RunWithProgress(List<string> paths)
        {
            var text = Texts.Current;
            try
            {
                var results = TpSheetConverter.ConvertAll(paths, (i, total, path) =>
                    EditorUtility.DisplayProgressBar(text.ConvertTitle, Path.GetFileName(path), total == 0 ? 1 : (float)i / total));
                LogSummary(results);
                return results;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void LogSummary(List<TpConversionResult> results)
        {
            var text = Texts.Current;
            foreach (var r in results)
            {
                string body = text.ConvertResultLine(r) + "\n" + string.Join("\n", r.Messages.Concat(r.Differences.Take(20)));
                if (r.Status == TpConversionStatus.Converted) Debug.Log("Atlasmith: " + body);
                else if (r.Status == TpConversionStatus.Skipped) Debug.LogWarning("Atlasmith: " + body);
                else Debug.LogError("Atlasmith: " + body);
            }
        }

        private void OnEnable()
        {
            AtlasmithLanguage.Changed += OnLanguageChanged;
            titleContent = new GUIContent(Texts.Current.ConvertTitle);
            if (_candidates.Count == 0) Reload(checkAll: true);
        }

        private void OnDisable() => AtlasmithLanguage.Changed -= OnLanguageChanged;

        private void OnLanguageChanged()
        {
            titleContent = new GUIContent(Texts.Current.ConvertTitle);
            Reload(checkAll: false);
            Repaint();
        }

        private void OnFocus() => Reload(checkAll: false);

        private void Reload(bool checkAll)
        {
            _candidates = TpSheetConverter.FindCandidates();
            if (checkAll)
            {
                _checked.Clear();
                foreach (var c in _candidates.Where(c => c.CanConvert)) _checked.Add(c.TpSheetPath);
            }
            _checked.RemoveWhere(p => !_candidates.Any(c => c.TpSheetPath == p && c.CanConvert));
        }

        private void OnGUI()
        {
            var text = Texts.Current;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(text.Refresh, EditorStyles.toolbarButton, GUILayout.Width(80))) Reload(checkAll: false);
                if (GUILayout.Button(text.SelectAll, EditorStyles.toolbarButton, GUILayout.Width(90)))
                    foreach (var c in _candidates.Where(c => c.CanConvert)) _checked.Add(c.TpSheetPath);
                if (GUILayout.Button(text.SelectNone, EditorStyles.toolbarButton, GUILayout.Width(90))) _checked.Clear();
                GUILayout.FlexibleSpace();

                GUILayout.Label(text.LanguageLabel, EditorStyles.miniLabel);
                var options = new[] { text.LanguageAuto, "Português", "English" };
                int current = (int)AtlasmithLanguage.Option;
                int chosen = EditorGUILayout.Popup(current, options, EditorStyles.toolbarPopup, GUILayout.Width(110));
                if (chosen != current) AtlasmithLanguage.Option = (AtlasmithLanguageOption)chosen;
            }

            EditorGUILayout.HelpBox(text.ConvertIntro, MessageType.None);

            if (_candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(text.ConvertNothingFound, MessageType.Info);
            }
            else
            {
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(Mathf.Max(160, position.height * 0.5f)));
                foreach (var c in _candidates) DrawCandidate(text, c);
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_checked.Count == 0))
                {
                    if (GUILayout.Button(text.ConvertSelectedButton(_checked.Count), GUILayout.Height(26), GUILayout.MinWidth(180)))
                    {
                        var paths = _candidates.Where(c => _checked.Contains(c.TpSheetPath)).Select(c => c.TpSheetPath).ToList();
                        if (EditorUtility.DisplayDialog(text.ConvertTitle, text.ConvertConfirm(paths.Count), text.ConvertButton, text.Cancel))
                        {
                            _results = RunWithProgress(paths);
                            Reload(checkAll: false);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            if (_results.Count > 0) DrawResults(text);
        }

        private void DrawCandidate(Texts text, TpSheetCandidate c)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!c.CanConvert))
                {
                    bool on = _checked.Contains(c.TpSheetPath);
                    bool now = EditorGUILayout.ToggleLeft(c.TpSheetPath, on);
                    if (now != on)
                    {
                        if (now) _checked.Add(c.TpSheetPath);
                        else _checked.Remove(c.TpSheetPath);
                    }
                }
                GUILayout.Label(c.CanConvert ? text.SpriteCount(c.SpriteCount) : c.Problem, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            }
        }

        private void DrawResults(Texts text)
        {
            EditorGUILayout.Space(6);
            GUILayout.Label(text.ResultsHeader, EditorStyles.boldLabel);

            int ok = _results.Count(r => r.Status == TpConversionStatus.Converted);
            var type = ok == _results.Count ? MessageType.Info : _results.Any(r => r.Status == TpConversionStatus.Failed) ? MessageType.Error : MessageType.Warning;
            EditorGUILayout.HelpBox(text.ConvertSummary(ok, _results.Count), type);

            _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll);
            foreach (var r in _results)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(text.ConvertResultLine(r), EditorStyles.boldLabel);
                    foreach (string m in r.Messages) EditorGUILayout.LabelField(m, EditorStyles.wordWrappedMiniLabel);
                    foreach (string d in r.Differences.Take(12)) EditorGUILayout.LabelField("• " + d, EditorStyles.wordWrappedMiniLabel);
                    if (r.Differences.Count > 12) EditorGUILayout.LabelField(text.MoreDifferences(r.Differences.Count - 12), EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            string? backup = _results.Select(r => r.BackupFolder).FirstOrDefault(b => b.Length > 0 && Directory.Exists(b));
            if (backup != null && GUILayout.Button(text.OpenBackupFolder)) EditorUtility.RevealInFinder(backup);
        }
    }
}
