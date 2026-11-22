using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.Persistence
{
    /// <summary>LES MODÈLES DE PROJET (2.0.0, 21/11/2026, Rémi) : « Enregistrer
    /// en tant que modèle » garde le CADRE d'un projet — la structure de ses
    /// Écrits (livres, parties, dossiers, écrits et leurs titres, gabarits et
    /// formats des livres, pages d'éditeur), ses styles, sa mise en page, ses
    /// modèles et ses catégories de fiches, ses natures de relation — et rien
    /// de ce qui y est écrit : ni le texte des écrits, ni leurs synopsis, notes,
    /// états et fiches de scène, ni les fiches, la recherche, les plans, les
    /// frises, les cartes, le dictionnaire, le journal, les instantanés.
    ///
    /// Un modèle dit ce qu'un projet CONTIENT au départ ; il ne dit rien de
    /// l'interface, qui reste l'affaire des Profils. Les modèles vivent à
    /// côté des réglages, sur la machine : un fichier .plot ordinaire par
    /// modèle, que l'on peut copier ou envoyer.</summary>
    public static class ProjectTemplates
    {
        public sealed class Entry
        {
            public string Name;
            public string Path;
            public DateTime SavedAt;
        }

        /// <summary>Sonde et tests : un autre dossier que celui des réglages.</summary>
        public static string FolderOverride;

        public static string Folder()
        {
            return FolderOverride ?? Path.Combine(AppSettings.SettingsFolder(), "modeles");
        }

        public static List<Entry> All()
        {
            var entries = new List<Entry>();
            try
            {
                var folder = Folder();
                if (!Directory.Exists(folder)) return entries;
                foreach (var path in Directory.GetFiles(folder, "*" + PlotFile.Extension))
                    entries.Add(new Entry { Name = Path.GetFileNameWithoutExtension(path), Path = path, SavedAt = File.GetLastWriteTime(path) });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            entries.Sort(delegate(Entry a, Entry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return entries;
        }

        /// <summary>Un nom de modèle devient un nom de fichier : rogné, sans caractère interdit.</summary>
        public static string CleanName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in (name ?? "").Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            var clean = sb.ToString().Trim().TrimEnd('.');
            return clean.Length > 80 ? clean.Substring(0, 80).Trim() : clean;
        }

        public static string PathFor(string name)
        {
            return Path.Combine(Folder(), CleanName(name) + PlotFile.Extension);
        }

        public static bool Exists(string name)
        {
            var clean = CleanName(name);
            return clean.Length > 0 && File.Exists(PathFor(clean));
        }

        /// <summary>Le cadre du projet, vidé de ce qui y est écrit. Le projet d'origine n'est pas touché.</summary>
        public static Project Skeleton(Project source) { return Skeleton(source, false); }

        /// <summary>Le cadre du projet : styles, mise en page, couleurs, modèles et
        /// catégories de fiches, relations — et, à la demande, le dictionnaire du
        /// projet. RIEN de ce qui est écrit ne suit (21/11, Rémi) : ni écrits ni
        /// leurs titres, ni parties, ni nom de livre. S'il y avait un livre, le
        /// projet né du modèle a un « Nouveau livre » à son format, avec ses gabarits.</summary>
        public static Project Skeleton(Project source, bool keepLexicon)
        {
            // Une copie profonde par le format lui-même (textes seuls : ni images, ni fichiers, ni instantanés).
            var temp = Path.Combine(Path.GetTempPath(), "marabook-modele-" + Guid.NewGuid().ToString("N") + PlotFile.Extension);
            string created = source.CreatedAt, modified = source.ModifiedAt, saveId = source.SaveId;
            Project copy;
            try
            {
                PlotFile.Save(source, temp, true);
                copy = PlotFile.Load(temp);
            }
            finally
            {
                source.CreatedAt = created; // Prepare les avait datés : cet enregistrement n'en est pas un
                source.ModifiedAt = modified;
                source.SaveId = saveId;
                foreach (var leftover in new[] { temp, temp + ".tmp", temp + ".bak" })
                    try { if (File.Exists(leftover)) File.Delete(leftover); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            // On part d'un projet NEUF et on n'y porte que le cadre : rien d'autre ne peut suivre par oubli.
            var fresh = Project.CreateNew();
            fresh.Styles = copy.Styles;
            fresh.GlobalStylesStamp = copy.GlobalStylesStamp;
            fresh.Page = copy.Page;
            fresh.CustomColors = copy.CustomColors;
            fresh.HyphenExceptions = copy.HyphenExceptions;
            fresh.Templates = copy.Templates;
            fresh.SheetCategories = copy.SheetCategories;
            fresh.RelationKinds = copy.RelationKinds;
            fresh.RelationSchema = copy.RelationSchema;
            if (keepLexicon) fresh.Lexicon = copy.Lexicon;
            var from = copy.Category(Project.KeyWritings);
            var to = fresh.Category(Project.KeyWritings);
            var model = from == null ? null : FirstBook(from);
            if (model != null && to != null)
            {
                // Un livre neuf, au format du premier livre du projet, avec ses gabarits de pages.
                var book = new BinderItem { Kind = ItemKind.Book, Title = NewBookTitle, Book = new BookInfo() };
                if (model.Book != null)
                {
                    book.Book.Template = model.Book.Template;
                    book.Book.BleedMm = model.Book.BleedMm;
                }
                foreach (var child in new List<BinderItem>(model.Children))
                {
                    if (child.Kind != ItemKind.PageTemplate) continue;
                    child.Parent = book;
                    child.ImageId = null;
                    book.Children.Add(child);
                }
                to.Children.Clear();
                book.Parent = to;
                to.Children.Add(book);
            }
            return fresh;
        }

        public const string NewBookTitle = "Nouveau livre";

        private static BinderItem FirstBook(BinderItem folder)
        {
            foreach (var child in folder.Children)
            {
                if (child.Kind == ItemKind.Book) return child;
                var inside = FirstBook(child);
                if (inside != null) return inside;
            }
            return null;
        }

        /// <summary>Enregistre le cadre du projet sous ce nom (un modèle du même nom est remplacé). Rend son chemin.</summary>
        public static string Save(Project source, string name) { return Save(source, name, false); }

        /// <summary>keepLexicon : le dictionnaire du projet (ses mots enseignés au correcteur) suit dans le modèle.</summary>
        public static string Save(Project source, string name, bool keepLexicon)
        {
            var clean = CleanName(name);
            if (clean.Length == 0) throw new ArgumentException("Un modèle a besoin d'un nom.");
            var skeleton = Skeleton(source, keepLexicon);
            skeleton.Name = clean;
            Directory.CreateDirectory(Folder());
            var path = PathFor(clean);
            PlotFile.Save(skeleton, path, true);
            try { if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return path;
        }

        /// <summary>Un projet NEUF bâti sur ce modèle : sans nom, sans date, sans fichier.</summary>
        public static Project Instantiate(string path)
        {
            var project = PlotFile.Load(path);
            project.Name = "Sans titre";
            project.CreatedAt = "";
            project.ModifiedAt = "";
            project.SaveId = "";
            project.DiskIndex = null;
            project.Recents.Clear();
            return project;
        }

        public static void Delete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            // Jamais hors du dossier des modèles.
            if (!string.Equals(Path.GetFullPath(Path.GetDirectoryName(path) ?? ""), Path.GetFullPath(Folder()), StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>Ce qu'un modèle contient, en une ligne : « un livre vide à son format, 8 modèles de fiche… ».</summary>
        public static string Describe(Project skeleton)
        {
            int books = 0, texts = 0;
            var writings = skeleton.Category(Project.KeyWritings);
            if (writings != null) Count(writings, ref books, ref texts);
            return (books > 0 ? "un livre vide à son format, " : "")
                + (skeleton.Templates.Count == 1 ? "1 modèle de fiche" : skeleton.Templates.Count + " modèles de fiche") + ", les styles et la mise en page"
                + (skeleton.Lexicon.Count == 0 ? "" : skeleton.Lexicon.Count == 1 ? ", 1 mot du dictionnaire" : ", " + skeleton.Lexicon.Count + " mots du dictionnaire");
        }

        private static void Count(BinderItem item, ref int books, ref int texts)
        {
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Book) books++;
                if (child.Kind == ItemKind.Text && !child.IsExtraPage) texts++;
                Count(child, ref books, ref texts);
            }
        }
    }
}
