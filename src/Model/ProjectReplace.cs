using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Une édition ponctuelle du remplacement projet (batch 37, lot
    /// C) : l'item, le champ (Kind + RefId, ou l'index du paragraphe), et —
    /// pour un paragraphe — l'offset et les deux textes de l'empan ; pour un
    /// autre champ, l'ancien et le nouveau texte ENTIER du champ. C'est le
    /// DELTA, jamais un instantané : quarante documents touchés pèsent
    /// quarante fois quelques octets, pas quarante manuscrits.</summary>
    public class ReplaceEdit
    {
        public BinderItem Item;
        public string Kind = SearchField.KindParagraph;
        public string RefId;
        public int ParagraphIndex = -1;
        public int Start;
        public string Before = "";
        public string After = "";

        public bool IsParagraph { get { return Kind == SearchField.KindParagraph; } }
    }

    /// <summary>Le plan d'un remplacement : les éditions (dans l'ordre de la
    /// Pile puis du texte), les items touchés, ce qui a été écarté (passages
    /// « ne pas corriger », empans qui coupent une ligature). Apply(forward)
    /// pose les éditions — à rebours pour que chaque offset d'origine reste
    /// vrai — et Apply(backward) les défait dans l'ordre ; chaque édition
    /// VÉRIFIE le texte en place avant d'écrire (un conflit = une édition
    /// sautée et comptée, jamais un texte corrompu).</summary>
    public class ReplacePlan
    {
        public readonly List<ReplaceEdit> Edits = new List<ReplaceEdit>();
        public readonly List<BinderItem> Items = new List<BinderItem>();
        public int Occurrences;      // occurrences réellement planifiées
        public int SkippedNoProof;   // écartées : « ne pas corriger »
        public int SkippedInexact;   // écartées : ligature coupée
        public int Conflicts;        // de la dernière application

        public bool Touches(BinderItem item)
        {
            return item != null && Items.Contains(item);
        }

        /// <summary>Bâtit le plan depuis des occurrences (celles que la
        /// prévisualisation a retenues) : un paragraphe reçoit une édition par
        /// occurrence, tout autre champ une seule édition (le champ entier,
        /// avant / après).</summary>
        public static ReplacePlan Build(Project project, List<SearchHit> hits, SearchQuery query, string replacement)
        {
            var plan = new ReplacePlan();
            replacement = replacement ?? "";
            if (hits == null) return plan;
            SearchField currentField = null;
            BinderItem currentItem = null;
            var fieldHits = new List<SearchHit>();
            foreach (var hit in hits)
            {
                if (hit == null || hit.Field == null || hit.Item == null) continue;
                if (hit.NoProof) { plan.SkippedNoProof++; continue; }
                if (!hit.Exact) { plan.SkippedInexact++; continue; }
                if (hit.Field != currentField || hit.Item != currentItem)
                {
                    plan.FlushField(currentItem, currentField, fieldHits, query, replacement);
                    currentField = hit.Field;
                    currentItem = hit.Item;
                    fieldHits.Clear();
                }
                fieldHits.Add(hit);
            }
            plan.FlushField(currentItem, currentField, fieldHits, query, replacement);
            return plan;
        }

        private void FlushField(BinderItem item, SearchField field, List<SearchHit> hits, SearchQuery query, string replacement)
        {
            if (field == null || hits.Count == 0) return;
            if (!Items.Contains(item)) Items.Add(item);
            if (field.IsParagraph)
            {
                foreach (var hit in hits)
                {
                    var before = Slice(field.Text, hit.Start, hit.Length);
                    Edits.Add(new ReplaceEdit
                    {
                        Item = item,
                        Kind = SearchField.KindParagraph,
                        ParagraphIndex = field.ParagraphIndex,
                        Start = hit.Start,
                        Before = before,
                        After = query == null ? replacement : query.ReplacementFor(before, replacement)
                    });
                    Occurrences++;
                }
                return;
            }
            // Un champ entier : les occurrences posées à rebours dans le texte.
            var after = field.Text;
            for (var i = hits.Count - 1; i >= 0; i--)
            {
                var hit = hits[i];
                if (hit.Start + hit.Length > after.Length) continue;
                var before = after.Substring(hit.Start, hit.Length);
                after = after.Substring(0, hit.Start)
                    + (query == null ? replacement : query.ReplacementFor(before, replacement))
                    + after.Substring(hit.Start + hit.Length);
                Occurrences++;
            }
            Edits.Add(new ReplaceEdit
            {
                Item = item,
                Kind = field.Kind,
                RefId = field.RefId,
                Before = field.Text,
                After = after
            });
        }

        private static string Slice(string text, int start, int length)
        {
            if (text == null || start < 0 || start > text.Length) return "";
            return text.Substring(start, Math.Max(0, Math.Min(length, text.Length - start)));
        }

        /// <summary>Applique (forward) ou défait (!forward) le plan. Rend le
        /// nombre d'éditions sautées faute de trouver le texte attendu.</summary>
        public int Apply(Project project, bool forward)
        {
            var conflicts = 0;
            if (forward)
            {
                // À rebours : chaque offset d'origine reste vrai tant que les
                // éditions qui le suivent dans le texte sont posées avant lui.
                for (var i = Edits.Count - 1; i >= 0; i--)
                    if (!ApplyOne(project, Edits[i], true)) conflicts++;
            }
            else
            {
                for (var i = 0; i < Edits.Count; i++)
                    if (!ApplyOne(project, Edits[i], false)) conflicts++;
            }
            Conflicts = conflicts;
            return conflicts;
        }

        private static bool ApplyOne(Project project, ReplaceEdit edit, bool forward)
        {
            var expected = forward ? edit.Before : edit.After;
            var written = forward ? edit.After : edit.Before;
            if (edit.IsParagraph)
            {
                if (edit.Item == null || edit.Item.Document == null) return false;
                if (edit.ParagraphIndex < 0 || edit.ParagraphIndex >= edit.Item.Document.Paragraphs.Count) return false;
                var paragraph = edit.Item.Document.Paragraphs[edit.ParagraphIndex];
                var flat = PivotEdit.FlatText(paragraph);
                if (edit.Start < 0 || edit.Start + expected.Length > flat.Length) return false;
                if (string.CompareOrdinal(flat, edit.Start, expected, 0, expected.Length) != 0) return false;
                PivotEdit.ReplaceText(paragraph, edit.Start, edit.Start + expected.Length, written);
                return true;
            }
            var current = Searchable.GetFieldText(project, edit.Item, edit.Kind, edit.RefId);
            if (current == null || current != expected) return false;
            return Searchable.SetFieldText(project, edit.Item, edit.Kind, edit.RefId, written);
        }
    }
}
