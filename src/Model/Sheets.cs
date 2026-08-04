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

        public static List<SheetTemplate> CreateDefaults()
        {
            var character = new SheetTemplate { Name = "Personnage" };
            character.Fields.Add(new SheetField { Name = "Rôle" });
            character.Fields.Add(new SheetField { Name = "Âge" });
            character.Fields.Add(new SheetField { Name = "Apparence", Kind = "multiline" });
            character.Fields.Add(new SheetField { Name = "Traits", Kind = "multiline" });
            character.Fields.Add(new SheetField { Name = "Objectif", Kind = "multiline" });

            var place = new SheetTemplate { Name = "Lieu" };
            place.Fields.Add(new SheetField { Name = "Région" });
            place.Fields.Add(new SheetField { Name = "Ambiance" });
            place.Fields.Add(new SheetField { Name = "Description", Kind = "multiline" });

            return new List<SheetTemplate> { character, place };
        }
    }

    /// <summary>A free key/value entry on a sheet — the escape hatch for
    /// whatever the template did not foresee.</summary>
    public class InfoEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Value = "";
    }
}
