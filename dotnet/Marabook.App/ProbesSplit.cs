using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde de la VUE SCINDÉE (2.0.0, 21/11/2026) : la zone
    /// centrale coupée par le milieu, un écrit puis une fiche éditables à
    /// droite, la croix en bas à droite, ce qui est refusé, ce qui la ferme.
    /// MARABOOK_PROBE_ONLY=scindee la joue seule.</summary>
    public static partial class Probes
    {
        private static async Task ProbeSplit(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- la vue scindée (2.0.0, 21/11)");
            var added = new List<BinderItem>();
            try
            {
                var writings = project.Category(Project.KeyWritings);
                var left = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre de gauche (sonde)", Document = TextDocument.FromPlainText("Le texte de gauche.\nSon second paragraphe.") };
                var right = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre de droite (sonde)", Document = TextDocument.FromPlainText("Le texte de droite.\nIl s'édite dans le volet.") };
                foreach (var item in new[] { left, right }) { item.Parent = writings; writings.Children.Add(item); added.Add(item); }
                shell.Binder.Rebuild();
                var keira = FirstTitled(project, "Keira");
                shell.Binder.SelectItem(left.Id);
                await Settle();

                Check(!shell.SplitOpen && shell.SplitMenuForProbe == "Vue scindée…" && AppSettings.Definition("split-view") != null, "Affichage › « Vue scindée… » existe ; fermée d'office");
                Check(shell.OpenSplit(right), "un écrit s'ouvre dans la vue scindée");
                await Settle();
                await Settle();
                var pane = shell.SplitForProbe;
                Check(shell.SplitOpen && pane.IsVisible && pane.TitleForProbe == right.Title && shell.VisibleView == "editor" && shell.CurrentForProbe == left,
                    "le volet montre l'écrit de droite ; celui de gauche reste ouvert et actif");
                Check(Math.Abs(shell.SplitShareForProbe - 0.5) < 0.03, "la coupure passe par le milieu de la zone centrale (" + (shell.SplitShareForProbe * 100).ToString("0") + " % pour le volet)");
                var close = pane.CloseForProbe;
                var at = close.TranslatePoint(new Point(close.Bounds.Width, close.Bounds.Height), pane);
                Check(at.HasValue && at.Value.X > pane.Bounds.Width - 24 && at.Value.Y > pane.Bounds.Height - 16, "la croix est en bas à droite du volet, loin des onglets");
                Check(shell.SplitMenuForProbe == "Fermer la vue scindée", "le menu propose alors de la fermer");
                var text = pane.TextForProbe;
                Check(text.HasItem && !text.ReadOnly && text.CurrentComposition.Pages.Count >= 1, "l'écrit y est composé en vraies pages, éditable");
                var paneWidth = pane.Bounds.Width;
                var pageWidth = (right.Page ?? project.Page).PageWidthPx;
                var narrow = (paneWidth - 56) / pageWidth < SplitPane.MinPageZoom;
                Check(pane.ColumnForProbe == narrow, narrow ? "le volet est trop étroit pour la vraie page (" + paneWidth.ToString("0") + " px) : le texte y passe en colonne de lecture, lisible" : "le volet est assez large : il montre la vraie page");
                Check(!narrow || shell.LeftZoomedForProbe, "à gauche, la page est réduite pour tenir dans sa moitié (le zoom choisi n'est pas touché : " + AppSettings.Zoom + " %)");
                Shot(shell, "scindee-1-deux-ecrits.png");

                // On y écrit : c'est l'écrit de droite qui change, le projet est marqué modifié.
                var leftBefore = left.Document.ToPlainText();
                text.PlaceCaretForProbe(0, 0);
                text.TypeText("Ajout. ");
                await Settle();
                Check(PivotEdit.FlatText(right.Document.Paragraphs[0]).StartsWith("Ajout. Le texte de droite") && left.Document.ToPlainText() == leftBefore && shell.DirtyForProbe,
                    "taper dans le volet modifie l'écrit de droite, et lui seul");
                // À gauche, l'éditeur de la fenêtre continue d'écrire dans SON écrit.
                shell.Editor.Composed.PlaceCaretForProbe(1, 0);
                shell.Editor.Composed.TypeText("Aussi. ");
                await Settle();
                Check(PivotEdit.FlatText(left.Document.Paragraphs[1]).StartsWith("Aussi. ") && !right.Document.ToPlainText().Contains("Aussi"), "…pendant que la gauche écrit dans le sien : deux éléments au travail");
                Check(pane.TryUndo() && PivotEdit.FlatText(right.Document.Paragraphs[0]) == "Le texte de droite." && PivotEdit.FlatText(left.Document.Paragraphs[1]).StartsWith("Aussi. "),
                    "l'annulation du volet ne défait que le volet");
                Check(pane.TryRedo() && PivotEdit.FlatText(right.Document.Paragraphs[0]).StartsWith("Ajout. "), "…et se refait");

                // Un même élément ne s'édite pas des deux côtés.
                Check(!shell.OpenSplit(left) && shell.SplitItem == right, "l'élément déjà ouvert à gauche est refusé dans le volet");
                Check(!shell.OpenSplit(writings) && !shell.OpenSplit(null), "ni un dossier, ni rien");
                shell.Binder.SelectItem(right.Id);
                await Settle();
                Check(!shell.SplitOpen && shell.CurrentForProbe == right && shell.VisibleView == "editor", "ouvrir à gauche l'élément du volet ferme le volet");
                Check(PivotEdit.FlatText(right.Document.Paragraphs[0]).StartsWith("Ajout. "), "…son texte y est tel qu'on l'a laissé");

                // Une fiche, à côté d'un écrit.
                shell.Binder.SelectItem(left.Id);
                await Settle();
                if (keira != null)
                {
                    Check(shell.OpenSplit(keira), "une fiche s'ouvre dans la vue scindée");
                    await Settle();
                    await Settle();
                    Check(shell.SplitItem == keira && pane.SheetForProbe.ShowsItem(keira) && pane.SheetForProbe.IsVisible && !pane.TextForProbe.IsVisible && shell.VisibleView == "editor",
                        "la fiche est à droite, dans sa vue d'édition ; l'écrit reste à gauche");
                    Check(!pane.SheetForProbe.BackShown && shell.Sheet.BackShown, "dans le volet, la fiche n'a pas de bouton « Retour » (celle de la fenêtre le garde)");
                    Shot(shell, "scindee-2-ecrit-et-fiche.png");
                    Check(shell.OpenSplit(right) && shell.SplitItem == right && pane.TextForProbe.IsVisible && !pane.SheetForProbe.IsVisible, "un autre élément remplace celui du volet");
                    await Settle();
                }

                // La croix du bas ferme ; la zone centrale reprend toute la largeur.
                pane.CloseForProbe.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                Check(!shell.SplitOpen && !pane.IsVisible && shell.SplitMenuForProbe == "Vue scindée…" && shell.SplitShareForProbe == 0, "la croix ferme la vue scindée ; la zone centrale reprend toute la place");
                Check(!shell.LeftZoomedForProbe, "…et la page de gauche retrouve son zoom");

                // Un élément jeté à la Corbeille quitte le volet.
                shell.OpenSplit(right);
                await Settle();
                right.Parent.Children.Remove(right);
                right.Parent = project.Trash;
                project.Trash.Children.Add(right);
                shell.TabsPruneForProbe();
                await Settle();
                Check(!shell.SplitOpen, "un élément jeté à la Corbeille quitte la vue scindée");
            }
            finally
            {
                shell.CloseSplit();
                var home = project.Category(Project.KeyWritings);
                shell.Binder.SelectItem(home.Id);
                await Settle();
                foreach (var item in added)
                    if (item != null && item.Parent != null) item.Parent.Children.Remove(item);
                shell.Binder.Rebuild();
                await Settle();
            }
        }
    }
}
