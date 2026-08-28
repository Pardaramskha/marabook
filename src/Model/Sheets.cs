using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    /// <summary>A field declared by a sheet template, e.g. "Âge" on a character
    /// sheet. Values are stored on instances by field id, so renaming a field
    /// keeps every instance's value and deleting one leaves values dormant
    /// (never destroyed) in the instance dictionaries.</summary>
    public class SheetField
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Champ";
        public string Kind = "text"; // "text" (one line) | "multiline"

        // Groupe d'affichage (batch 31) : les champs d'un même groupe sont
        // rendus sous un intertitre ("Infos", "Physique"…). Vide = groupe
        // par défaut, affiché sans intertitre en tête de fiche.
        public string Group = "";

        public SheetField Clone()
        {
            return (SheetField)MemberwiseClone();
        }
    }

    /// <summary>A sheet template ("Personnage", "Lieu"…), duplicable ad infinitum
    /// into instances. Modeled on The Universe Project's dual pattern: fixed
    /// typed fields here, plus a free key/value list on each instance.</summary>
    public class SheetTemplate
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Modèle";
        public List<SheetField> Fields = new List<SheetField>();

        public SheetTemplate Clone()
        {
            var copy = new SheetTemplate { Id = Id, Name = Name };
            foreach (var field in Fields) copy.Fields.Add(field.Clone());
            return copy;
        }
    }

    /// <summary>Une catégorie de fiches (batch 31) : Personnage, Lieu, Magie…
    /// plus les catégories personnalisées. Chaque catégorie porte son MODÈLE
    /// DE BASE (les nouvelles fiches le reçoivent) ; une fiche existante dont
    /// le modèle diffère est marquée d'une puce dans la bibliothèque.</summary>
    public class SheetCategory
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Catégorie";
        public string TemplateId; // modèle de base des nouvelles fiches
    }

    /// <summary>Les sept catégories livrées et leurs modèles par défaut —
    /// volontairement sobres (« sans trop en mettre ») ; le détail viendra
    /// au fil de l'usage. Sert aussi de source à la migration des projets
    /// d'avant les catégories (PlotFile).</summary>
    public static class SheetDefaults
    {
        /// <summary>Le groupe de champs rendu dans le paper « Apparence ».</summary>
        public const string GroupLooks = "Physique";

        public static readonly string[] CategoryNames =
        {
            "Personnage", "Lieu", "Magie", "Objet",
            "Peuple", "Gouvernement", "Environnement"
        };

        /// <summary>Le modèle par défaut d'une catégorie livrée (null pour un
        /// nom inconnu). Les ids sont neufs à chaque appel.</summary>
        public static SheetTemplate TemplateFor(string categoryName)
        {
            switch (categoryName)
            {
                case "Personnage": return Character();
                case "Lieu": return Simple("Lieu",
                    F("Région"), F("Climat"), F("Population"),
                    M("Description"));
                case "Magie": return Simple("Magie",
                    F("Source"), F("Coût"), F("Limites"),
                    M("Description"));
                case "Objet": return Simple("Objet",
                    F("Type"), F("Propriétaire"), F("Origine"),
                    M("Description"));
                case "Peuple": return Simple("Peuple",
                    F("Territoire"), F("Langue"), F("Coutumes"),
                    M("Description"));
                case "Gouvernement": return Simple("Gouvernement",
                    F("Type de régime"), F("Dirigeant"), F("Siège"),
                    M("Description"));
                case "Environnement": return Simple("Environnement",
                    F("Type"), F("Faune"), F("Flore"),
                    M("Description"));
                default: return null;
            }
        }

        /// <summary>Le modèle Personnage, en deux groupes : « Infos » (l'état
        /// civil déjà en place, plus le genre) et « Physique » (batch 31).</summary>
        private static SheetTemplate Character()
        {
            var t = new SheetTemplate { Name = "Personnage" };
            t.Fields.Add(G("Nom", "Infos"));
            t.Fields.Add(G("Prénom", "Infos"));
            t.Fields.Add(G("Alias", "Infos"));
            t.Fields.Add(G("Genre", "Infos"));
            t.Fields.Add(G("Date de naissance", "Infos"));
            t.Fields.Add(G("Lieu de naissance", "Infos"));
            t.Fields.Add(G("Affiliation", "Infos"));
            t.Fields.Add(G("Religion", "Infos"));
            t.Fields.Add(G("Magie", "Infos"));
            t.Fields.Add(G("Taille", "Physique"));
            t.Fields.Add(G("Poids", "Physique"));
            t.Fields.Add(G("Yeux", "Physique"));
            t.Fields.Add(G("Cheveux", "Physique"));
            t.Fields.Add(G("Couleur de peau", "Physique"));
            t.Fields.Add(G("Sexe de naissance", "Physique"));
            var marks = G("Particularités", "Physique");
            marks.Kind = "multiline";
            t.Fields.Add(marks);
            return t;
        }

        private static SheetTemplate Simple(string name, params SheetField[] fields)
        {
            var template = new SheetTemplate { Name = name };
            template.Fields.AddRange(fields);
            return template;
        }

        private static SheetField F(string name)
        {
            return new SheetField { Name = name };
        }

        private static SheetField M(string name)
        {
            return new SheetField { Name = name, Kind = "multiline" };
        }

        private static SheetField G(string name, string group)
        {
            return new SheetField { Name = name, Group = group };
        }

        /// <summary>Peuple un projet NEUF : sept modèles, sept catégories.</summary>
        public static void Seed(List<SheetTemplate> templates,
            List<SheetCategory> categories)
        {
            foreach (var name in CategoryNames)
            {
                var template = TemplateFor(name);
                templates.Add(template);
                categories.Add(new SheetCategory
                {
                    Name = name,
                    TemplateId = template.Id
                });
            }
        }
    }

    /// <summary>A free key/value entry on a sheet — the escape hatch for
    /// whatever the template did not foresee.</summary>
    public class InfoEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Value = "";
        public string Group = ""; // "" = Informations, "Physique" = Apparence (batch 34)
    }

    /// <summary>Une relation d'une fiche vers une autre (batch 34) : sa
    /// nature (« frère », « mentor »…) et sa cible — une fiche du projet
    /// (TargetId) ou un simple nom (Name) quand la fiche n'existe pas.</summary>
    public class SheetRelation
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Kind = "";
        public string TargetId;   // null = cible libre
        public string Name = "";
    }
}
