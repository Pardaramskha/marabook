using System;
using System.Globalization;

namespace UniversSale.View
{
    /// <summary>Les dates À L'ÉCRAN s'écrivent JJ/MM/AAAA (12/09/2026) ; les
    /// fichiers gardent leur forme triable « yyyy-MM-dd HH:mm[:ss] ».</summary>
    public static class Dates
    {
        private static readonly string[] Stored =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"
        };

        /// <summary>Une date enregistrée rendue lisible : « 12/09/2026 19:30 »,
        /// ou « 12/09/2026 » sans heure ; une valeur illisible est rendue telle quelle.</summary>
        public static string Display(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            DateTime parsed;
            if (!DateTime.TryParseExact(stored.Trim(), Stored, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return stored;
            return stored.Trim().Length > 10
                ? parsed.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                : parsed.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        public static string Display(DateTime when)
        {
            return when.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
