using System;
using System.Collections.Generic;
using System.Text;

namespace UniversSale.Model
{
    /// <summary>Un champ cherchable d'un item (batch 37) : un paragraphe du
    /// document (offsets pivot), une note de bas de page, une annotation, un
    /// champ de fiche, une métadonnée de livre, une colonne ou une brique de
    /// plan, une entrée du dictionnaire… Kind + RefId disent où écrire en
    /// remplacement ; Label dit où c'est à l'écran.</summary>
    public class SearchField
    {
        public const string KindTitle = "title";
        public const string KindSynopsis = "synopsis";
        public const string KindNotes = "notes";
        public const string KindParagraph = "paragraph";
        public const string KindFootnote = "footnote";
        public const string KindAnnotation = "annotation";
        public const string KindField = "field";         // valeur d'un champ de modèle (RefId = id du champ)
        public const string KindInfo = "info";           // champ libre de fiche (RefId = id de l'entrée)
        public const string KindRelation = "relation";   // nom libre d'une relation (RefId = id)
        public const string KindBook = "book";           // métadonnée de livre (RefId = nom de la propriété)
        public const string KindColumn = "column";       // titre d'une colonne de plan (RefId = id)
        public const string KindEntry = "entry";         // brique d'une colonne de plan (RefId = id)
        public const string KindLexicon = "lexicon";     // mot d'une entrée du dictionnaire (RefId = index)
        public const string KindLexiconNote = "lexicon-note";

        public string Kind = KindTitle;
        public string Label = "";
        public string Text = "";
        public int ParagraphIndex = -1;  // paragraphes seulement
        public string RefId;
        // Plages « ne pas corriger » du paragraphe (paires début, fin), null = aucune.
        public List<int> NoProofRanges;

        public bool IsParagraph { get { return Kind == KindParagraph; } }

        // Cartes de pli en cache (batch 37, mesuré : 157 Mo alloués par
        // frappe sans elles) — une par casse, bâties au premier usage,
        // immuables (le fil de fond peut les bâtir ; une affectation de
        // référence est atomique, le dernier gagne sans dommage).
        private FoldMap _foldLower, _foldExact;

        public FoldMap FoldMapFor(bool lowerCase)
        {
            var map = lowerCase ? _foldLower : _foldExact;
            if (map == null)
            {
                map = FoldMap.Build(Text, lowerCase);
                if (lowerCase) _foldLower = map; else _foldExact = map;
            }
            return map;
        }

        /// <summary>Vrai si [start, start+length) chevauche une plage NoProof.</summary>
        public bool OverlapsNoProof(int start, int length)
        {
            if (NoProofRanges == null) return false;
            var end = start + length;
            for (var i = 0; i + 1 < NoProofRanges.Count; i += 2)
                if (start < NoProofRanges[i + 1] && NoProofRanges[i] < end) return true;
            return false;
        }
    }

    /// <summary>LA définition de « ce qui est cherchable » dans un item
    /// (batch 37) — une seule, pour la recherche projet, le panneau, le
    /// remplacement et le scanner de liens. Titre, synopsis et notes pour
    /// tous ; le document (paragraphes, notes de bas de page, annotations)
    /// pour écrits et fiches ; champs, infos libres et noms libres de
    /// relations pour les fiches ; métadonnées d'un livre ; colonnes et
    /// briques d'un plan ; entrées du dictionnaire (racine Dictionnaire) ;
    /// un média n'offre que son titre (fichier opaque — décision b37).
    /// L'empreinte (FNV-1a, sans allocation) suit exactement le même
    /// parcours : elle sert de clé au cache de BinderItem.SearchFields.</summary>
    public static class Searchable
    {
        public static List<SearchField> Build(BinderItem item, Project project)
        {
            var fields = new List<SearchField>();
            if (item == null) return fields;
            if (item.IsCategory)
            {
                if (item.CategoryKey == Project.KeyDictionary && project != null)
                    for (var i = 0; i < project.Lexicon.Count; i++)
                    {
                        var entry = project.Lexicon[i];
                        Add(fields, SearchField.KindLexicon, "Entrée", entry.Word, i.ToString());
                        Add(fields, SearchField.KindLexiconNote, "Note de « " + entry.Word + " »", entry.Note, i.ToString());
                    }
                return fields;
            }
            Add(fields, SearchField.KindTitle, "Titre", item.Title, null);
            Add(fields, SearchField.KindSynopsis, "Synopsis", item.Synopsis, null);
            Add(fields, SearchField.KindNotes, "Notes", item.Notes, null);
            if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
            {
                var document = item.Document;
                for (var p = 0; p < document.Paragraphs.Count; p++)
                {
                    var paragraph = document.Paragraphs[p];
                    var field = new SearchField
                    {
                        Kind = SearchField.KindParagraph,
                        Label = "¶ " + (p + 1),
                        Text = PivotEdit.FlatText(paragraph),
                        ParagraphIndex = p,
                        NoProofRanges = NoProofOf(paragraph)
                    };
                    fields.Add(field);
                }
                for (var n = 0; n < document.Footnotes.Count; n++)
                    Add(fields, SearchField.KindFootnote, "Note " + (n + 1), document.Footnotes[n].Text, document.Footnotes[n].Id);
                foreach (var annotation in document.Annotations)
                    Add(fields, SearchField.KindAnnotation, annotation.Resolved ? "Annotation (résolue)" : "Annotation", annotation.Text, annotation.Id);
            }
            if (item.Kind == ItemKind.Sheet)
            {
                var template = project == null ? null : project.FindTemplate(item.TemplateId);
                if (template != null)
                    foreach (var templateField in template.Fields)
                    {
                        string value;
                        if (item.FieldValues.TryGetValue(templateField.Id, out value))
                            Add(fields, SearchField.KindField, templateField.Name, value, templateField.Id);
                    }
                else
                    foreach (var pair in item.FieldValues)
                        Add(fields, SearchField.KindField, "Champ", pair.Value, pair.Key);
                foreach (var entry in item.FreeInfo)
                    Add(fields, SearchField.KindInfo, entry.Title.Length > 0 ? entry.Title : "Champ libre", entry.Value, entry.Id);
                foreach (var relation in item.Relations)
                    Add(fields, SearchField.KindRelation, relation.Kind.Length > 0 ? "Relation (" + relation.Kind + ")" : "Relation", relation.Name, relation.Id);
            }
            if (item.Kind == ItemKind.Book && item.Book != null)
            {
                Add(fields, SearchField.KindBook, "Sous-titre", item.Book.Subtitle, "Subtitle");
                Add(fields, SearchField.KindBook, "Auteur", item.Book.AuthorOverride, "AuthorOverride");
                Add(fields, SearchField.KindBook, "Éditeur", item.Book.Publisher, "Publisher");
                Add(fields, SearchField.KindBook, "Collection", item.Book.Collection, "Collection");
                Add(fields, SearchField.KindBook, "ISBN", item.Book.Isbn, "Isbn");
                Add(fields, SearchField.KindBook, "Année", item.Book.Year, "Year");
            }
            if (item.Kind == ItemKind.Plan && item.Plan != null)
                for (var c = 0; c < item.Plan.Columns.Count; c++)
                {
                    var column = item.Plan.Columns[c];
                    var word = item.Plan.ColumnWord + " " + (c + 1);
                    Add(fields, SearchField.KindColumn, word, column.Title, column.Id);
                    foreach (var entry in column.Entries)
                        Add(fields, SearchField.KindEntry, (entry.IsNote ? "Note, " : "Élément, ") + word, entry.Text, entry.Id);
                }
            return fields;
        }

        private static void Add(List<SearchField> fields, string kind, string label, string text, string refId)
        {
            if (string.IsNullOrEmpty(text)) return;
            fields.Add(new SearchField { Kind = kind, Label = label, Text = text, RefId = refId });
        }

        private static List<int> NoProofOf(TextParagraph paragraph)
        {
            List<int> ranges = null;
            var cursor = 0;
            foreach (var run in paragraph.Runs)
            {
                var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                if (run.NoProof && length > 0)
                {
                    if (ranges == null) ranges = new List<int>();
                    if (ranges.Count > 0 && ranges[ranges.Count - 1] == cursor) ranges[ranges.Count - 1] = cursor + length;
                    else { ranges.Add(cursor); ranges.Add(cursor + length); }
                }
                cursor += length;
            }
            return ranges;
        }

        // ------------------------------------------------- lire / écrire un champ

        /// <summary>Le texte actuel d'un champ non-paragraphe (Kind + RefId),
        /// ou null s'il n'existe plus — le remplacement vérifie avant d'écrire.</summary>
        public static string GetFieldText(Project project, BinderItem item, string kind, string refId)
        {
            if (item == null) return null;
            switch (kind)
            {
                case SearchField.KindTitle: return item.Title;
                case SearchField.KindSynopsis: return item.Synopsis;
                case SearchField.KindNotes: return item.Notes;
                case SearchField.KindFootnote:
                {
                    var note = item.Document == null ? null : item.Document.FindFootnote(refId);
                    return note == null ? null : note.Text;
                }
                case SearchField.KindAnnotation:
                {
                    var annotation = item.Document == null ? null : item.Document.FindAnnotation(refId);
                    return annotation == null ? null : annotation.Text;
                }
                case SearchField.KindField:
                {
                    string value;
                    return refId != null && item.FieldValues.TryGetValue(refId, out value) ? value : null;
                }
                case SearchField.KindInfo:
                {
                    var entry = FindInfo(item, refId);
                    return entry == null ? null : entry.Value;
                }
                case SearchField.KindRelation:
                {
                    var relation = FindRelation(item, refId);
                    return relation == null ? null : relation.Name;
                }
                case SearchField.KindBook:
                    if (item.Book == null) return null;
                    switch (refId)
                    {
                        case "Subtitle": return item.Book.Subtitle;
                        case "AuthorOverride": return item.Book.AuthorOverride;
                        case "Publisher": return item.Book.Publisher;
                        case "Collection": return item.Book.Collection;
                        case "Isbn": return item.Book.Isbn;
                        case "Year": return item.Book.Year;
                        default: return null;
                    }
                case SearchField.KindColumn:
                {
                    var column = item.Plan == null ? null : item.Plan.FindColumn(refId);
                    return column == null ? null : column.Title;
                }
                case SearchField.KindEntry:
                {
                    var entry = FindEntry(item, refId);
                    return entry == null ? null : entry.Text;
                }
                case SearchField.KindLexicon:
                case SearchField.KindLexiconNote:
                {
                    var lexical = FindLexicon(project, refId);
                    return lexical == null ? null : kind == SearchField.KindLexicon ? lexical.Word : lexical.Note;
                }
                default: return null;
            }
        }

        /// <summary>Écrit le texte d'un champ non-paragraphe ; faux si la cible
        /// n'existe plus.</summary>
        public static bool SetFieldText(Project project, BinderItem item, string kind, string refId, string text)
        {
            if (item == null) return false;
            text = text ?? "";
            switch (kind)
            {
                case SearchField.KindTitle: item.Title = text; return true;
                case SearchField.KindSynopsis: item.Synopsis = text; return true;
                case SearchField.KindNotes: item.Notes = text; return true;
                case SearchField.KindFootnote:
                {
                    var note = item.Document == null ? null : item.Document.FindFootnote(refId);
                    if (note == null) return false;
                    note.Text = text;
                    return true;
                }
                case SearchField.KindAnnotation:
                {
                    var annotation = item.Document == null ? null : item.Document.FindAnnotation(refId);
                    if (annotation == null) return false;
                    annotation.Text = text;
                    return true;
                }
                case SearchField.KindField:
                    if (refId == null || !item.FieldValues.ContainsKey(refId)) return false;
                    item.FieldValues[refId] = text;
                    return true;
                case SearchField.KindInfo:
                {
                    var entry = FindInfo(item, refId);
                    if (entry == null) return false;
                    entry.Value = text;
                    return true;
                }
                case SearchField.KindRelation:
                {
                    var relation = FindRelation(item, refId);
                    if (relation == null) return false;
                    relation.Name = text;
                    return true;
                }
                case SearchField.KindBook:
                    if (item.Book == null) return false;
                    switch (refId)
                    {
                        case "Subtitle": item.Book.Subtitle = text; return true;
                        case "AuthorOverride": item.Book.AuthorOverride = text; return true;
                        case "Publisher": item.Book.Publisher = text; return true;
                        case "Collection": item.Book.Collection = text; return true;
                        case "Isbn": item.Book.Isbn = text; return true;
                        case "Year": item.Book.Year = text; return true;
                        default: return false;
                    }
                case SearchField.KindColumn:
                {
                    var column = item.Plan == null ? null : item.Plan.FindColumn(refId);
                    if (column == null) return false;
                    column.Title = text;
                    return true;
                }
                case SearchField.KindEntry:
                {
                    var entry = FindEntry(item, refId);
                    if (entry == null) return false;
                    entry.Text = text;
                    return true;
                }
                case SearchField.KindLexicon:
                case SearchField.KindLexiconNote:
                {
                    var lexical = FindLexicon(project, refId);
                    if (lexical == null) return false;
                    if (kind == SearchField.KindLexicon) lexical.Word = text; else lexical.Note = text;
                    return true;
                }
                default: return false;
            }
        }

        private static InfoEntry FindInfo(BinderItem item, string id)
        {
            foreach (var entry in item.FreeInfo) if (entry.Id == id) return entry;
            return null;
        }

        private static SheetRelation FindRelation(BinderItem item, string id)
        {
            foreach (var relation in item.Relations) if (relation.Id == id) return relation;
            return null;
        }

        private static PlanEntry FindEntry(BinderItem item, string id)
        {
            if (item.Plan == null) return null;
            foreach (var column in item.Plan.Columns)
                foreach (var entry in column.Entries)
                    if (entry.Id == id) return entry;
            return null;
        }

        private static LexiconEntry FindLexicon(Project project, string refId)
        {
            int index;
            if (project == null || refId == null || !int.TryParse(refId, out index)) return null;
            return index >= 0 && index < project.Lexicon.Count ? project.Lexicon[index] : null;
        }

        // ------------------------------------------------------ empreinte

        /// <summary>FNV-1a 64 bits de tout le contenu cherchable, calculée
        /// SANS allocation — le même parcours que Build. Un item inchangé
        /// garde la même empreinte : son cache de champs est réutilisé.</summary>
        public static long Fingerprint(BinderItem item, Project project)
        {
            var h = new Hasher();
            if (item == null) return h.Value;
            if (item.IsCategory)
            {
                if (item.CategoryKey == Project.KeyDictionary && project != null)
                    foreach (var entry in project.Lexicon) { h.Add(entry.Word); h.Add(entry.Note); }
                return h.Value;
            }
            h.Add(item.Title); h.Add(item.Synopsis); h.Add(item.Notes);
            if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
            {
                foreach (var paragraph in item.Document.Paragraphs)
                {
                    foreach (var run in paragraph.Runs)
                    {
                        if (PivotEdit.IsElement(run)) h.Add('￼');
                        else h.Add(run.Text);
                        if (run.NoProof) h.Add('');
                    }
                    h.Add('\n');
                }
                foreach (var note in item.Document.Footnotes) h.Add(note.Text);
                foreach (var annotation in item.Document.Annotations) { h.Add(annotation.Text); h.Add(annotation.Resolved ? '' : ''); }
            }
            if (item.Kind == ItemKind.Sheet)
            {
                var template = project == null ? null : project.FindTemplate(item.TemplateId);
                if (template != null)
                    foreach (var templateField in template.Fields)
                    {
                        string value;
                        h.Add(templateField.Name);
                        if (item.FieldValues.TryGetValue(templateField.Id, out value)) h.Add(value);
                    }
                else
                    foreach (var pair in item.FieldValues) { h.Add(pair.Key); h.Add(pair.Value); }
                foreach (var entry in item.FreeInfo) { h.Add(entry.Title); h.Add(entry.Value); }
                foreach (var relation in item.Relations) { h.Add(relation.Kind); h.Add(relation.Name); }
            }
            if (item.Kind == ItemKind.Book && item.Book != null)
            {
                h.Add(item.Book.Subtitle); h.Add(item.Book.AuthorOverride); h.Add(item.Book.Publisher);
                h.Add(item.Book.Collection); h.Add(item.Book.Isbn); h.Add(item.Book.Year);
            }
            if (item.Kind == ItemKind.Plan && item.Plan != null)
            {
                h.Add(item.Plan.ColumnWord);
                foreach (var column in item.Plan.Columns)
                {
                    h.Add(column.Title);
                    foreach (var entry in column.Entries) { h.Add(entry.Text); h.Add(entry.Kind); }
                }
            }
            return h.Value;
        }

        private struct Hasher
        {
            private ulong _hash;
            private bool _started;

            public long Value { get { return unchecked((long)(_started ? _hash : 14695981039346656037UL)); } }

            private void Start() { if (!_started) { _hash = 14695981039346656037UL; _started = true; } }

            public void Add(char c)
            {
                Start();
                _hash = unchecked((_hash ^ c) * 1099511628211UL);
            }

            public void Add(string text)
            {
                Start();
                if (text != null)
                    for (var i = 0; i < text.Length; i++)
                        _hash = unchecked((_hash ^ text[i]) * 1099511628211UL);
                _hash = unchecked((_hash ^ 0xFFFFUL) * 1099511628211UL); // séparateur
            }
        }
    }
}
