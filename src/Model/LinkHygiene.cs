using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>L'HYGIÈNE DES LIENS (1.0.5) : ce que le projet fait de ses
    /// [[liens]] et de ses relations quand une fiche change de nom ou
    /// disparaît pour de bon.
    /// — Renommage : chaque « [[Ancien]] » devient « [[Nouveau]] » (le texte
    ///   lu suit le nom, comme un champ de Word), chaque « [[Ancien|mots]] »
    ///   devient « [[Nouveau|mots]] » (les mots de l'auteur restent) — dans
    ///   les écrits, les corps et champs de fiche, les notes, partout où la
    ///   recherche voit du texte. Le plan est un ReplacePlan : annulable d'un
    ///   cran avec le renommage, vérifié à l'écriture (jamais un texte
    ///   corrompu si l'écrit a changé entre-temps).
    /// — Corbeille vidée : les relations et les champs « Fiche liée » qui
    ///   visaient un item purgé sont retirés (Purge), rendus par Ctrl+Z.</summary>
    public static class LinkHygiene
    {
        /// <summary>Le plan qui recible tous les liens « oldTitle » vers
        /// « newTitle » (casse et accents ignorés, comme la résolution des
        /// cibles). Vide si les titres se lisent pareil. Les titres eux-mêmes
        /// ne sont jamais touchés.</summary>
        public static ReplacePlan Retarget(Project project, string oldTitle, string newTitle)
        {
            var plan = new ReplacePlan();
            if (project == null) return plan;
            oldTitle = (oldTitle ?? "").Trim();
            newTitle = (newTitle ?? "").Trim();
            if (oldTitle.Length == 0 || newTitle.Length == 0 || oldTitle == newTitle) return plan;
            foreach (var item in project.AllItems())
            {
                if (item.IsCategory) continue;
                foreach (var field in item.SearchFields(project))
                {
                    if (field.Kind == SearchField.KindTitle || string.IsNullOrEmpty(field.Text)) continue;
                    var links = Links.Find(field.Text);
                    if (links.Count == 0) continue;
                    var edits = new List<ReplaceEdit>();
                    foreach (var link in links)
                    {
                        if (!Links.SameTitle(link.Target, oldTitle)) continue;
                        int start, end;
                        RawTarget(field.Text, link, out start, out end);
                        edits.Add(new ReplaceEdit
                        {
                            Item = item,
                            Kind = SearchField.KindParagraph,
                            ParagraphIndex = field.ParagraphIndex,
                            Start = start,
                            Before = field.Text.Substring(start, end - start),
                            After = newTitle
                        });
                    }
                    if (edits.Count == 0) continue;
                    if (!plan.Items.Contains(item)) plan.Items.Add(item);
                    if (field.IsParagraph)
                    {
                        plan.Edits.AddRange(edits);
                        plan.Occurrences += edits.Count;
                        continue;
                    }
                    // Un champ entier : les cibles posées à rebours dans le texte.
                    var after = field.Text;
                    for (var i = edits.Count - 1; i >= 0; i--)
                        after = after.Substring(0, edits[i].Start) + newTitle + after.Substring(edits[i].Start + edits[i].Before.Length);
                    plan.Edits.Add(new ReplaceEdit
                    {
                        Item = item,
                        Kind = field.Kind,
                        RefId = field.RefId,
                        Before = field.Text,
                        After = after
                    });
                    plan.Occurrences += edits.Count;
                }
            }
            return plan;
        }

        /// <summary>La plage BRUTE de la cible dans la notation (espaces de
        /// bord compris) : de « [[ » à la barre, ou à « ]] ».</summary>
        private static void RawTarget(string text, Link link, out int start, out int end)
        {
            start = link.Start + 2;
            end = link.End - 2;
            var pipe = text.IndexOf('|', start, end - start);
            if (pipe >= 0) end = pipe;
        }

        /// <summary>Le nombre de liens qui visent ce titre dans tout le projet
        /// (titres exclus) — pour dire à l'auteur ce qu'un renommage suivra.</summary>
        public static int CountLinksTo(Project project, string title)
        {
            var count = 0;
            if (project == null) return 0;
            foreach (var item in project.AllItems())
            {
                if (item.IsCategory) continue;
                foreach (var field in item.SearchFields(project))
                {
                    if (field.Kind == SearchField.KindTitle || string.IsNullOrEmpty(field.Text)) continue;
                    foreach (var link in Links.Find(field.Text))
                        if (Links.SameTitle(link.Target, title)) count++;
                }
            }
            return count;
        }

        /// <summary>Une relation retirée d'une fiche (ou un champ « Fiche
        /// liée » vidé) parce que sa cible a été purgée — de quoi la rendre.</summary>
        public class Orphan
        {
            public BinderItem Sheet;
            public SheetRelation Relation; // null pour un champ
            public int Index;
            public string FieldId;         // champ « Fiche liée » vidé
            public string FieldValue;
        }

        /// <summary>Retire, chez les fiches qui restent, les relations et les
        /// champs « Fiche liée » qui visent l'un des ids purgés ; rend ce qui a
        /// été retiré, dans l'ordre, pour Restore.</summary>
        public static List<Orphan> Purge(Project project, HashSet<string> purgedIds)
        {
            var orphans = new List<Orphan>();
            if (project == null || purgedIds == null || purgedIds.Count == 0) return orphans;
            foreach (var sheet in project.AllItems())
            {
                if (sheet.Kind != ItemKind.Sheet) continue;
                for (var i = sheet.Relations.Count - 1; i >= 0; i--)
                {
                    var relation = sheet.Relations[i];
                    if (relation.TargetId == null || !purgedIds.Contains(relation.TargetId)) continue;
                    orphans.Add(new Orphan { Sheet = sheet, Relation = relation, Index = i });
                    sheet.Relations.RemoveAt(i);
                }
                var template = project.TemplateOf(sheet);
                if (template == null) continue;
                foreach (var field in template.Fields)
                {
                    if (FieldKinds.Normalize(field.Kind) != FieldKinds.Sheet) continue;
                    string value;
                    if (!sheet.FieldValues.TryGetValue(field.Id, out value) || string.IsNullOrEmpty(value)) continue;
                    if (!purgedIds.Contains(value.Trim())) continue;
                    orphans.Add(new Orphan { Sheet = sheet, FieldId = field.Id, FieldValue = value });
                    sheet.FieldValues[field.Id] = "";
                }
            }
            return orphans;
        }

        /// <summary>Rend ce que Purge a retiré (à rebours : les index des
        /// relations redeviennent vrais).</summary>
        public static void Restore(List<Orphan> orphans)
        {
            if (orphans == null) return;
            for (var i = orphans.Count - 1; i >= 0; i--)
            {
                var orphan = orphans[i];
                if (orphan.Relation != null)
                    orphan.Sheet.Relations.Insert(Math.Min(orphan.Index, orphan.Sheet.Relations.Count), orphan.Relation);
                else
                    orphan.Sheet.FieldValues[orphan.FieldId] = orphan.FieldValue;
            }
        }

        /// <summary>Tous les ids d'un sous-arbre (l'item compris).</summary>
        public static void CollectIds(BinderItem item, HashSet<string> into)
        {
            if (item == null) return;
            into.Add(item.Id);
            foreach (var child in item.Children) CollectIds(child, into);
        }
    }
}
