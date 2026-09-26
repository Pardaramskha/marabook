using System;
using System.Collections.Generic;
using System.Globalization;
using Marabook.Model;

namespace Marabook.Settings
{
    /// <summary>LES STYLES GLOBAUX (22/09) : la feuille de Préférences ›
    /// Styles globaux vaut pour tous les projets. Chaque projet en garde une
    /// copie dans son .plot (les styles de portée « global ») pour rester
    /// lisible ailleurs ; à l'ouverture, la copie se resynchronise :
    ///  - un projet jamais synchronisé (d'avant la v28) POUSSE ses styles
    ///    globaux personnalisés vers les réglages — ce que l'auteur avait
    ///    réglé dans son projet devient la règle partout, et un style resté
    ///    au défaut de Marabook ne renverse jamais un style personnalisé ;
    ///  - un projet déjà synchronisé, dont l'empreinte diffère, TIRE la
    ///    feuille des réglages (modifiée depuis un autre projet).
    /// Toute édition d'un style global (dialogue des styles, onglet Styles
    /// d'un livre, Préférences) passe par Push. Les styles de livre et de
    /// document ne quittent jamais leur projet.</summary>
    public static class GlobalStyles
    {
        /// <summary>Faux dans les tests : la synchronisation n'écrit jamais settings.json.</summary>
        public static bool Persist = true;

        /// <summary>Synchronise le projet ouvert avec la feuille globale.
        /// Rend vrai si la feuille du projet a changé (l'éditeur se recharge).</summary>
        /// <summary>La dernière synchronisation a poussé des styles du projet
        /// vers les réglages (l'empreinte du projet doit alors être
        /// enregistrée, sinon il repousserait à chaque ouverture).</summary>
        public static bool LastSyncPushed;

        public static bool Sync(Project project)
        {
            LastSyncPushed = false;
            if (project == null || project.Styles == null) return false;
            if (project.ReadOnlyNewerFormat) return false; // lecture seule : ni pousser ni tirer (revue 22/09)
            if (AppSettings.GlobalStyles == null)
            {
                // Réglages vierges (nouveau poste, settings.json perdu) : le
                // projet ouvert sème, quelle que soit son empreinte — sans quoi
                // un projet déjà estampillé tirerait les défauts par-dessus ses
                // styles personnalisés (revue 22/09).
                AppSettings.GlobalStyles = StyleSheet.CreateDefault();
                AppSettings.GlobalStylesStamp = NewStamp();
                project.GlobalStylesStamp = "";
            }
            var settings = AppSettings.GlobalStyles;
            // Le style des notes de bas de page (0.50.0) existe des deux côtés
            // avant toute comparaison : les réglages d'avant ne l'ont pas, et
            // sans lui ici la règle du « style disparu » retirerait au projet
            // celui que le chargement vient de lui donner.
            settings.EnsureFootnoteStyle();
            var changed = project.Styles.Find(StyleSheet.FootnoteId).Id != StyleSheet.FootnoteId;
            project.Styles.EnsureFootnoteStyle();
            if (project.GlobalStylesStamp.Length == 0)
            {
                // Première rencontre : les styles globaux du projet qui ne sont
                // pas au défaut de Marabook montent dans les réglages ; ceux qui
                // le sont prennent la version des réglages.
                var defaults = StyleSheet.CreateDefault();
                var pushed = false;
                foreach (var style in project.Styles.Styles)
                {
                    if (!style.IsGlobal) continue;
                    var global = Find(settings, style.Id);
                    var reference = Find(defaults, style.Id);
                    if (reference != null)
                    {
                        // La sentinelle de césure (2 dans les projets persistés
                        // d'avant le batch 24, 3 au défaut) n'est pas une
                        // personnalisation (revue 22/09).
                        reference = reference.Clone();
                        reference.HyphenMinAfter = style.HyphenMinAfter;
                    }
                    var customized = reference == null || !SameStyle(style, reference);
                    if (customized)
                    {
                        if (global == null) settings.Styles.Add(style.Clone());
                        else if (!SameStyle(global, style)) Copy(style, global);
                        else continue;
                        pushed = true;
                    }
                    else if (global != null && !SameStyle(global, style))
                    {
                        Copy(global, style);
                        changed = true;
                    }
                }
                changed |= AdoptMissing(project, settings);
                LastSyncPushed = pushed;
                if (pushed) AppSettings.GlobalStylesStamp = NewStamp();
                project.GlobalStylesStamp = AppSettings.GlobalStylesStamp;
                if (Persist) AppSettings.Save();
                return changed;
            }
            if (project.GlobalStylesStamp == AppSettings.GlobalStylesStamp) return changed;
            // Les réglages ont bougé depuis : le projet les reprend.
            foreach (var global in settings.Styles)
            {
                var own = Find(project.Styles, global.Id);
                if (own == null) { project.Styles.Styles.Add(global.Clone()); changed = true; }
                else if (own.IsGlobal && !SameStyle(own, global)) { Copy(global, own); changed = true; }
            }
            // Un style global disparu des réglages quitte le projet — sauf s'il
            // y est encore employé : il reste, à ce projet (revue 22/09).
            var stale = new List<ParagraphStyle>();
            foreach (var style in project.Styles.Styles)
                if (style.IsGlobal && Find(settings, style.Id) == null && style.Id != "body" && style.Id != Model.ExtraPages.StyleId
                    && style.Id != StyleSheet.FootnoteId
                    && !InUse(project, style.Id)) stale.Add(style);
            foreach (var style in stale) { project.Styles.Styles.Remove(style); changed = true; }
            project.GlobalStylesStamp = AppSettings.GlobalStylesStamp;
            return changed;
        }

        /// <summary>Les styles globaux du projet remplacent la feuille des
        /// réglages (après une édition) : nouvelle empreinte, réglages
        /// enregistrés, projet aligné.</summary>
        public static void Push(Project project)
        {
            if (project == null || project.Styles == null) return;
            var sheet = new StyleSheet();
            foreach (var style in project.Styles.Styles)
                if (style.IsGlobal) sheet.Styles.Add(style.Clone());
            if (sheet.Styles.Count == 0) return;
            AppSettings.GlobalStyles = sheet;
            AppSettings.GlobalStylesStamp = NewStamp();
            project.GlobalStylesStamp = AppSettings.GlobalStylesStamp;
            if (Persist) AppSettings.Save();
        }

        /// <summary>Les réglages ont été édités directement (Préférences) :
        /// nouvelle empreinte, et le projet ouvert les reprend.</summary>
        public static bool PushFromSettings(Project project)
        {
            AppSettings.GlobalStylesStamp = NewStamp();
            if (Persist) AppSettings.Save();
            if (project == null) return false;
            project.GlobalStylesStamp = "";
            // Un projet « jamais synchronisé » pousserait ; ici les réglages
            // font foi : on le marque synchronisé avec une empreinte périmée.
            project.GlobalStylesStamp = "settings";
            return Sync(project);
        }

        /// <summary>La feuille d'un projet NEUF : les styles globaux.</summary>
        public static StyleSheet ForNewProject()
        {
            if (AppSettings.GlobalStyles == null) return StyleSheet.CreateDefault();
            var sheet = AppSettings.GlobalStyles.Clone();
            if (sheet.Find("body") == null || sheet.Find("body").Id != "body") sheet.Styles.Insert(0, StyleSheet.CreateDefault().Body);
            return sheet;
        }

        /// <summary>Un paragraphe du projet porte ce style.</summary>
        public static bool InUse(Project project, string styleId)
        {
            foreach (var item in project.AllItems())
            {
                if (item.Document == null) continue;
                foreach (var paragraph in item.Document.Paragraphs)
                    if (paragraph.StyleId == styleId) return true;
            }
            return false;
        }

        private static bool AdoptMissing(Project project, StyleSheet settings)
        {
            var changed = false;
            foreach (var global in settings.Styles)
                if (Find(project.Styles, global.Id) == null) { project.Styles.Styles.Add(global.Clone()); changed = true; }
            return changed;
        }

        private static ParagraphStyle Find(StyleSheet sheet, string id)
        {
            foreach (var style in sheet.Styles) if (style.Id == id) return style;
            return null;
        }

        /// <summary>Deux styles aux mêmes attributs (id compris).</summary>
        public static bool SameStyle(ParagraphStyle a, ParagraphStyle b)
        {
            var one = new StyleSheet(); one.Styles.Add(a);
            var two = new StyleSheet(); two.Styles.Add(b);
            return Json.Write(Persistence.PlotFile.BuildStyles(one)) == Json.Write(Persistence.PlotFile.BuildStyles(two));
        }

        /// <summary>Recopie les attributs de « from » dans « to » (même objet
        /// conservé : l'éditeur qui le référence le voit changer).</summary>
        public static void Copy(ParagraphStyle from, ParagraphStyle to)
        {
            var id = to.Id;
            foreach (var field in typeof(ParagraphStyle).GetFields())
                if (!field.IsStatic && !field.IsLiteral) field.SetValue(to, field.GetValue(from));
            to.Id = id;
        }

        private static string NewStamp()
        {
            return DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
