using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE SPECTRE d'un relevé (1.0.5-a, Rémi) — mots inconnus, fiches
    /// manquantes : le projet actif par défaut quand il y en a un, sinon
    /// tous les écrits ; au choix, un livre ou un dossier d'Écrits (jamais
    /// un écrit seul). Texts = null : tout le projet (comportement d'avant,
    /// fiches comprises pour les mots inconnus).</summary>
    public sealed class Scope
    {
        public string Label = "";
        public List<BinderItem> Texts; // null = tout
        public bool IsActive;          // le projet actif
    }

    public static class Scopes
    {
        public static List<Scope> Build(Project project)
        {
            var list = new List<Scope>();
            if (project == null) return list;
            var active = project.ActiveScope();
            if (active != null)
                list.Add(new Scope { Label = "Projet actif — " + active.Title, Texts = project.TextsIn(active), IsActive = true });
            list.Add(new Scope { Label = "Tous les écrits", Texts = null });
            var writings = project.Category(Project.KeyWritings);
            if (writings != null) Add(list, project, writings, 0);
            return list;
        }

        private static void Add(List<Scope> list, Project project, BinderItem parent, int depth)
        {
            foreach (var child in parent.Children)
            {
                if (child.Kind != ItemKind.Book && child.Kind != ItemKind.Folder) continue;
                if (child.IsOutOfBook) continue;
                list.Add(new Scope
                {
                    Label = new string(' ', depth * 3) + (child.Kind == ItemKind.Book ? "Livre — " : "Dossier — ") + child.Title,
                    Texts = project.TextsIn(child)
                });
                Add(list, project, child, depth + 1);
            }
        }

        /// <summary>L'index du spectre par défaut : le projet actif s'il y en a un, sinon « Tous ».</summary>
        public static int DefaultIndex(List<Scope> scopes)
        {
            for (var i = 0; i < scopes.Count; i++) if (scopes[i].IsActive) return i;
            return 0;
        }
    }
}
