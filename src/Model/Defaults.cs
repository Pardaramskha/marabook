using System;

namespace Marabook.Model
{
    /// <summary>Les défauts de l'utilisateur (22/09) : l'auteur·ice et les
    /// métadonnées générales posées dans Préférences › Auteur, que les
    /// réglages recopient ici à chaque chargement — le modèle ne dépend pas
    /// des réglages, il lit ces valeurs. L'auteur par défaut est celui des
    /// documents qui ne sont pas des livres (compilation, commentaires Word,
    /// liminaires) quand ni le livre ni le projet n'en nomment un ; l'éditeur
    /// et la collection pré-remplissent chaque nouveau livre.</summary>
    public static class Defaults
    {
        public static string Author = "";
        public static string Publisher = "";
        public static string Collection = "";

        /// <summary>La valeur si elle est renseignée, sinon le défaut.</summary>
        public static string Or(string value, string fallback)
        {
            return value != null && value.Trim().Length > 0 ? value : (fallback ?? "");
        }

        /// <summary>Un livre neuf reçoit l'éditeur et la collection par défaut
        /// dans ses champs vides — rien n'est écrasé.</summary>
        public static void Seed(BookInfo book)
        {
            if (book == null) return;
            if (book.Publisher.Length == 0) book.Publisher = Publisher ?? "";
            if (book.Collection.Length == 0) book.Collection = Collection ?? "";
        }
    }
}
