using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using Marabook.Model;
using Marabook.Settings;
using Marabook.View;

namespace Marabook
{
    /// <summary>Les ÉCHANGES du b49 : les commentaires d'un document Word
    /// relu ramenés en annotations sur l'écrit ouvert, et l'extraction des
    /// personnages d'un texte importé (noms propres récurrents → fiches).</summary>
    public partial class MainWindow
    {
        // ------------------------------------------------------------ commentaires relus

        /// <summary>Fichier › Importer › Les commentaires d'un document relu :
        /// le .docx est lu, ses commentaires cherchent leur passage dans
        /// l'écrit ouvert (CommentMerge), le tout en un cran d'annulation.</summary>
        private void ImportReviewedComments()
        {
            if (_project == null || _current == null || _current.Kind != ItemKind.Text) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Document Word relu (*.docx)|*.docx",
                Title = "Importer les commentaires d'un document relu"
            };
            if (dialog.ShowDialog(this) != true) return;
            List<Exchange.DocxComment> comments;
            try
            {
                Exchange.Docx.ImportWithComments(dialog.FileName, _project.Styles, out comments);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Lecture impossible :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (comments.Count == 0)
            {
                MessageDialog.Show(this, "Ce document ne contient aucun commentaire.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            CommitActive();
            var merged = PivotEdit.Clone(_current.Document);
            var result = Exchange.CommentMerge.Merge(merged, comments);
            _history.Run(new History.ReplaceDocumentAction(_current, merged, "commentaires importés"));

            var sb = new StringBuilder();
            sb.Append(result.Total == 1 ? "1 commentaire importé" : result.Total + " commentaires importés")
              .Append(" dans « ").Append(_current.Title).Append(" » :");
            if (result.Placed > 0)
                sb.Append("\n— ").Append(result.Placed).Append(result.Placed == 1 ? " posé sur son passage" : " posés sur leur passage");
            if (result.Fallback > 0)
                sb.Append("\n— ").Append(result.Fallback)
                  .Append(result.Fallback == 1 ? " posé en tête du paragraphe de même rang" : " posés en tête du paragraphe de même rang")
                  .Append(" (passage introuvable, cité dans l'annotation)");
            if (result.Lost > 0)
                sb.Append("\n— ").Append(result.Lost).Append(result.Lost == 1 ? " perdu" : " perdus").Append(" (l'écrit est vide)");
            if (!AppSettings.ShowAnnotations)
                sb.Append("\n\nLes annotations sont masquées : onglet Révision › « Afficher les annotations ».");
            sb.Append("\n\nCtrl+Z pour annuler.");
            MessageDialog.Show(this, sb.ToString(), AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ------------------------------------------------------------ personnages

        /// <summary>Édition › Indexeur de noms propres (renommé le 22/09).</summary>
        private void ExtractCharacters(BinderItem item)
        {
            if (_project == null || item == null || item.Kind != ItemKind.Text) return;
            CommitActive();
            OfferCharacters(new List<BinderItem> { item }, item.Title, true);
        }

        /// <summary>Les noms propres récurrents des textes, hors fiches
        /// existantes, proposés en fiches Personnage. explicit : dire quand
        /// il n'y a rien (l'import se tait).</summary>
        private void OfferCharacters(List<BinderItem> texts, string sourceTitle, bool explicitRequest)
        {
            var sb = new StringBuilder();
            foreach (var text in texts)
                if (text != null && text.Document != null) sb.AppendLine(text.Document.ToPlainText());
            var candidates = NameExtractor.Extract(sb.ToString(), KnownSheetNames());
            if (candidates.Count == 0)
            {
                if (explicitRequest)
                    MessageDialog.Show(this,
                        "Aucun nom propre récurrent qui ne soit déjà une fiche.\n\n"
                        + "Un nom est retenu quand il revient au moins trois fois, dont deux hors début de phrase.",
                        AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var chosen = CharactersDialog.Ask(this, sourceTitle, candidates, _project.SheetCategories, PreferredCharacterCategory());
            if (chosen == null) return;
            CreateCharacterSheets(chosen);
        }

        /// <summary>Les noms déjà portés par les fiches (titre, Nom, Prénom, Alias).</summary>
        private List<string> KnownSheetNames()
        {
            var names = new List<string>();
            foreach (var item in _project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                names.AddRange(Presence.NamesOf(item, _project.FindTemplate(item.TemplateId)));
                if (!names.Contains(item.Title)) names.Add(item.Title);
            }
            return names;
        }

        /// <summary>La catégorie proposée d'office : Personnage, sinon la première.</summary>
        private SheetCategory PreferredCharacterCategory()
        {
            foreach (var category in _project.SheetCategories)
                if (string.Equals(category.Name, "Personnage", StringComparison.CurrentCultureIgnoreCase)) return category;
            return _project.SheetCategories.Count > 0 ? _project.SheetCategories[0] : null;
        }

        /// <summary>Une fiche par nom, dans la catégorie choisie pour lui
        /// (22/09), en un cran d'annulation.</summary>
        private void CreateCharacterSheets(List<NameChoice> choices)
        {
            var sheets = _project.Category(Project.KeySheets);
            var items = new List<BinderItem>();
            foreach (var choice in choices)
            {
                var home = choice.Category ?? PreferredCharacterCategory();
                items.Add(new BinderItem
                {
                    Kind = ItemKind.Sheet,
                    Title = choice.Name,
                    CategoryId = home != null ? home.Id : null,
                    TemplateId = home != null ? home.TemplateId : null
                });
            }
            if (items.Count == 0) return;
            _history.Run(new History.AddItemsAction(sheets, items));
            MarkDirty();
            ScheduleAchievementCheck();
        }
    }
}
