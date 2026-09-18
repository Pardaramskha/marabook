using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Marabook.Model
{
    /// <summary>Import et export du dictionnaire personnel (18/09) — sans
    /// WPF, testable en console (C11). Deux formes : le JSON de Marabook
    /// (entrées complètes, natures et définitions) et la LISTE DE MOTS, un
    /// mot par ligne, le plus petit dénominateur commun des dictionnaires
    /// personnels (Word .dic, LibreOffice, Hunspell, Scrivener, Antidote
    /// exporté en texte). À l'import, les lignes d'en-tête connues et les
    /// drapeaux d'affixes sont ignorés ; un mot déjà présent est gardé tel
    /// quel, jamais écrasé.</summary>
    public static class LexiconExchange
    {
        public const string ExportFilter =
            "Dictionnaire Marabook (*.json)|*.json|Liste de mots (*.txt)|*.txt|"
            + "Dictionnaire personnel Word (*.dic)|*.dic";
        public const string ImportFilter =
            "Dictionnaires (*.json;*.txt;*.dic)|*.json;*.txt;*.dic|Tous les fichiers (*.*)|*.*";

        // ------------------------------------------------------------ export

        public static string ToJson(List<LexiconEntry> entries)
        {
            var root = new Dictionary<string, object>();
            root["marabook-lexicon"] = 1;
            root["entries"] = LexiconEntry.ToJsonList(entries);
            return Json.Write(root);
        }

        /// <summary>Un mot par ligne, tri français, sans doublon.</summary>
        public static string ToWordList(List<LexiconEntry> entries)
        {
            var words = new List<string>();
            foreach (var entry in entries)
            {
                var word = (entry.Word ?? "").Trim();
                if (word.Length > 0 && !words.Contains(word)) words.Add(word);
            }
            words.Sort(StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), true));
            return string.Join("\r\n", words.ToArray()) + (words.Count > 0 ? "\r\n" : "");
        }

        /// <summary>Écrit selon l'extension : .json (Marabook, UTF-8), .dic
        /// (liste UTF-16 avec BOM, ce que Word attend d'un dictionnaire
        /// personnel), sinon liste de mots UTF-8.</summary>
        public static void Export(List<LexiconEntry> entries, string path)
        {
            var extension = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (extension == ".json")
                File.WriteAllText(path, ToJson(entries), new UTF8Encoding(false));
            else if (extension == ".dic")
                File.WriteAllText(path, ToWordList(entries), Encoding.Unicode);
            else
                File.WriteAllText(path, ToWordList(entries), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------ import

        /// <summary>Lit un fichier de dictionnaire : BOM honoré (UTF-8,
        /// UTF-16), sinon UTF-8 strict, sinon Windows-1252 (les vieux .dic).</summary>
        public static List<LexiconEntry> Read(string path)
        {
            return Parse(ReadText(File.ReadAllBytes(path)));
        }

        public static string ReadText(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(1252).GetString(bytes);
            }
        }

        /// <summary>Le contenu d'un fichier de dictionnaire : le JSON de
        /// Marabook s'il en a la forme, sinon une liste de mots.</summary>
        public static List<LexiconEntry> Parse(string content)
        {
            var text = (content ?? "").TrimStart('﻿', ' ', '\t', '\r', '\n');
            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                var root = Json.AsObject(Json.Parse(text));
                if (root != null && Json.Field(root, "entries") != null)
                    return LexiconEntry.FromJsonList(Json.AsList(Json.Field(root, "entries")));
                // Un JSON qui n'est pas le nôtre : rien d'importable.
                return new List<LexiconEntry>();
            }
            return ParseWordList(text);
        }

        public static List<LexiconEntry> ParseWordList(string text)
        {
            var entries = new List<LexiconEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var first = true;
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n', '\r'))
            {
                var line = raw.Trim();
                var isFirst = first;
                first = false;
                if (IsNoise(line, isFirst)) continue;
                var word = CleanWord(line);
                if (word.Length == 0 || !seen.Add(word)) continue;
                entries.Add(LexiconEntry.Simple(word));
            }
            return entries;
        }

        /// <summary>Les lignes qui ne sont pas des mots : vides, commentaires,
        /// sections « [Words] » (Scrivener), en-têtes LibreOffice
        /// (« OOoUserDict1 », « lang: fr », « type: positive », « --- »),
        /// et le compte d'entrées en tête d'un .dic Hunspell.</summary>
        public static bool IsNoise(string line, bool firstLine)
        {
            if (line.Length == 0) return true;
            if (line[0] == '#' || line[0] == ';' || line[0] == '[') return true;
            if (line.StartsWith("OOoUserDict", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith("lang:", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith("type:", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith("---", StringComparison.Ordinal)) return true;
            if (firstLine)
            {
                var digits = true;
                foreach (var c in line) if (!char.IsDigit(c)) { digits = false; break; }
                if (digits) return true;
            }
            return false;
        }

        /// <summary>« mot/S » (drapeaux Hunspell) → « mot » ; « mot=Mot »
        /// (LibreOffice, remplacement) → « mot » ; une valeur « clé=valeur »
        /// d'un .ini garde la clé.</summary>
        public static string CleanWord(string line)
        {
            var word = line;
            var slash = word.IndexOf('/');
            if (slash > 0) word = word.Substring(0, slash);
            var equal = word.IndexOf('=');
            if (equal > 0) word = word.Substring(0, equal);
            var tab = word.IndexOf('\t');
            if (tab > 0) word = word.Substring(0, tab);
            return word.Trim();
        }

        /// <summary>Verse les entrées lues dans un dictionnaire : les mots
        /// nouveaux s'ajoutent, les mots déjà là restent. Rend le nombre ajouté.</summary>
        public static int Import(List<LexiconEntry> into, List<LexiconEntry> imported)
        {
            var added = 0;
            foreach (var entry in imported)
            {
                var word = (entry.Word ?? "").Trim();
                if (word.Length == 0 || LexiconEntry.Find(into, word) != null) continue;
                var clone = entry.Clone();
                clone.Word = word;
                into.Add(clone);
                added++;
            }
            return added;
        }
    }
}
