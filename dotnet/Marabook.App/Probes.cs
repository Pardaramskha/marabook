using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde de l'application (P1, « --probe ») : de vraies
    /// fenêtres, comme les sondes WPF — la coquille sur le projet d'exemple,
    /// la Pile, l'inspecteur, les Préférences, la boîte de message, le thème.
    /// Chaque vérification écrit OK ou ÉCHEC ; le code de retour du processus
    /// est le nombre d'échecs. Tourne sur Windows et sous WSLg.</summary>
    public static class Probes
    {
        public static int Failures;
        private static int _checks;

        private static void Check(bool condition, string label)
        {
            _checks++;
            if (!condition) Failures++;
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
        }

        private static async Task Settle()
        {
            await Task.Delay(120);
            await Dispatcher.UIThread.InvokeAsync(delegate { }, DispatcherPriority.Background);
        }

        public static async Task Run(MainWindow shell)
        {
            Console.WriteLine("== Sonde Avalonia P1 (" + AppPlatform.OsName + ")");
            try
            {
                await Settle();
                // — La coquille sur le projet d'exemple.
                Check(shell.Project != null, "le projet d'exemple est ouvert");
                var roots = shell.Binder.ItemsSource as List<BinderItem>;
                Check(roots != null && roots.Count == 8, "la Pile montre les huit racines (" + (roots == null ? "-" : roots.Count.ToString()) + ")");
                var writings = shell.Project.Category(Project.KeyWritings);
                var book = writings.Children.Count > 1 ? writings.Children[1] : null;
                Check(book != null && book.Kind == ItemKind.Book && book.Children.Count == 3, "le livre d'exemple a trois chapitres");
                var chapter = book == null ? null : book.Children[0];
                shell.Binder.SelectedItem = chapter;
                await Settle();
                Check(shell.InspectorTitle == (chapter == null ? "" : chapter.Title), "sélectionner un écrit : l'inspecteur montre son titre (" + shell.InspectorTitle + ")");
                Check(shell.InspectorKind == "Écrit" && shell.InspectorDetail.Contains("mots"), "…sa nature et ses mots (" + shell.InspectorDetail + ")");
                Check(shell.Title.StartsWith("Projet d'exemple"), "le titre de la fenêtre nomme le projet");
                Check(shell.StatusText.Contains("éléments"), "la barre d'état compte les éléments");

                // — Le thème bascule et revient.
                var before = Chrome.Ink.Color;
                App.ApplyTheme(!Chrome.Dark);
                await Settle();
                Check(Chrome.Ink.Color != before, "basculer le thème recolore l'encre");
                App.ApplyTheme(!Chrome.Dark);
                await Settle();
                Check(Chrome.Ink.Color == before, "…et revient");

                // — Les Préférences : huit onglets, chacun un contenu.
                var prefs = new PreferencesDialog(shell, shell.Project);
                prefs.Show(shell);
                await Settle();
                Check(prefs.Tabs.Items.Count == 8, "les Préférences ont huit onglets (" + prefs.Tabs.Items.Count + ")");
                var titles = new List<string>();
                foreach (var item in prefs.Tabs.Items) titles.Add(((TabItem)item).Header as string);
                Check(titles.Contains("Raccourcis") && titles.Contains("DLC") && titles.Contains("Catalogue de polices"),
                    "…Raccourcis, DLC et Catalogue de polices en font partie");
                for (var i = 0; i < prefs.Tabs.Items.Count; i++)
                {
                    prefs.Tabs.SelectedIndex = i;
                    await Settle();
                }
                Check(prefs.Tabs.SelectedIndex == prefs.Tabs.Items.Count - 1, "chaque onglet s'ouvre sans erreur");
                prefs.Tabs.SelectedIndex = 0;
                await Settle();
                // Diagnostic de rendu : la graisse et la police que voit un
                // texte courant de l'onglet (le contenu ne doit pas hériter
                // de la graisse de la chip active).
                TextBlock sample = null;
                foreach (var text in prefs.GetVisualDescendants().OfType<TextBlock>())
                    if (text.Text != null && text.Text.StartsWith("Boutons, sélections")) { sample = text; break; }
                Check(sample != null && sample.FontWeight == Avalonia.Media.FontWeight.Normal,
                    "un texte courant des Préférences reste en graisse normale (" + (sample == null ? "introuvable" : sample.FontWeight + ", " + sample.FontFamily.Name + " " + sample.FontSize) + ")");
                prefs.Close();
                await Settle();

                // — La boîte de message se montre et se ferme.
                var dialogTask = MessageDialog.Show(shell, "Sonde : cette boîte se ferme toute seule.", "Sonde", MessageButtons.OKCancel, MessageIcon.Information);
                await Settle();
                MessageDialog open = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is MessageDialog) open = (MessageDialog)window;
                Check(open != null && open.IsVisible, "la boîte de message est ouverte");
                if (open != null) open.Close();
                var result = await dialogTask;
                Check(result == MessageResult.Cancel, "fermée par la croix : le refus le plus sûr (Annuler)");

                // — Le dialogue de saisie : Entrée valide.
                var askTask = InputDialog.Ask(shell, "Sonde", "Un nom :", "Chapitre neuf");
                await Settle();
                InputDialog input = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is InputDialog) input = (InputDialog)window;
                Check(input != null, "le dialogue de saisie est ouvert");
                if (input != null) input.Close();
                var answer = await askTask;
                Check(answer == null, "fermé sans valider : null");

                // — Fermer le projet : l'Accueil se pose sur la coquille.
                shell.CloseProjectPublic();
                await Settle();
                Check(shell.Project == null && shell.Welcome != null && shell.Welcome.IsVisible, "projet fermé : l'accueil est posé sur la coquille");
                Check(shell.Welcome.Width >= 640 && shell.Welcome.Height >= 420, "l'accueil fait au moins 640×420 (" + shell.Welcome.Width.ToString("0") + "×" + shell.Welcome.Height.ToString("0") + ")");
            }
            catch (Exception error)
            {
                Failures++;
                Console.WriteLine("  ÉCHEC  exception : " + error);
            }
            Console.WriteLine(Failures == 0 ? "SONDE OK (" + _checks + " vérifications)" : "*** SONDE : " + Failures + " échec(s) sur " + _checks + " ***");
        }
    }
}
