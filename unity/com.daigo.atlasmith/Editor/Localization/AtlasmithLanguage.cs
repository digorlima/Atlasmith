#nullable enable

using System;
using System.Collections.Generic;
using Atlasmith.Core;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor
{
    public enum AtlasmithLanguageOption
    {
        /// <summary>Português se o sistema estiver em português; inglês nos outros casos.</summary>
        Auto,
        Portuguese,
        English
    }

    /// <summary>
    /// O idioma do Atlasmith no editor. Fica nas EditorPrefs (vale para todos os projetos
    /// desta máquina) e também vale para as mensagens do núcleo (CoreText).
    /// </summary>
    public static class AtlasmithLanguage
    {
        public const string PrefKey = "Atlasmith.Language";

        public static event Action? Changed;

        public static AtlasmithLanguageOption Option
        {
            get
            {
                int value = EditorPrefs.GetInt(PrefKey, (int)AtlasmithLanguageOption.Auto);
                return Enum.IsDefined(typeof(AtlasmithLanguageOption), value) ? (AtlasmithLanguageOption)value : AtlasmithLanguageOption.Auto;
            }
            set
            {
                if (value == Option) return;
                EditorPrefs.SetInt(PrefKey, (int)value);
                Apply();
                Changed?.Invoke();
            }
        }

        public static TextLanguage Resolved
        {
            get
            {
                switch (Option)
                {
                    case AtlasmithLanguageOption.Portuguese: return TextLanguage.Portuguese;
                    case AtlasmithLanguageOption.English: return TextLanguage.English;
                    default: return Application.systemLanguage == SystemLanguage.Portuguese ? TextLanguage.Portuguese : TextLanguage.English;
                }
            }
        }

        /// <summary>Põe a interface e o núcleo no idioma escolhido.</summary>
        public static void Apply()
        {
            var language = Resolved;
            Texts.Use(language);
            if (CoreText.Current.Language != language) CoreText.Use(language);
        }

        [SettingsProvider]
        private static SettingsProvider CreatePreferences() =>
            new SettingsProvider("Preferences/Atlasmith", SettingsScope.User)
            {
                label = "Atlasmith",
                guiHandler = _ =>
                {
                    var text = Texts.Current;
                    EditorGUILayout.Space(6);
                    var options = new[] { text.LanguageAuto, "Português", "English" };
                    int current = (int)Option;
                    int chosen = EditorGUILayout.Popup(new GUIContent(text.LanguageLabel, text.LanguageHint), current, options);
                    if (chosen != current) Option = (AtlasmithLanguageOption)chosen;
                },
                keywords = new HashSet<string>(new[] { "Atlasmith", "Language", "Idioma", "Krita", "Atlas" })
            };

        [MenuItem("Tools/Atlasmith/Preferences…", false, 200)]
        private static void OpenPreferences() => SettingsService.OpenUserPreferences("Preferences/Atlasmith");
    }
}
