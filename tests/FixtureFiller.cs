using System;
using System.Collections.Generic;
using System.Reflection;

namespace Marabook.Tests
{
    /// <summary>Remplissage réflexif de la fixture C1 (batch 26, durci au
    /// batch 27 lot 0.5) : tout champ public SCALAIRE encore à sa valeur de
    /// constructeur reçoit une sentinelle discriminante. Un champ AJOUTÉ au
    /// modèle mais oublié par la sérialisation fait donc échouer
    /// FullRoundTrip sans que personne n'ait à penser à mettre la fixture à
    /// jour — c'est la moitié du verrou qui manquait (l'autre, Clone, est C3).
    ///
    /// DEUX RÈGLES DE DURCISSEMENT (0.5) :
    /// 1. Un défaut DÉLIBÉRÉ de la fixture est inviolable : le champ posé à
    ///    la main À SA VALEUR PAR DÉFAUT se déclare par nom à l'appel
    ///    (« Fill(header, "Italic") ») — le filler ne peut plus l'écraser.
    /// 2. Un type NON GÉRÉ fait ÉCHOUER le filler (enum, int?, long, float,
    ///    DateTime, string[], List&lt;int&gt;, dictionnaires de scalaires…) sauf
    ///    exclusion explicite dans NoFill — c'était le trou résiduel du
    ///    verrou. Les types du MODÈLE (classes Marabook.*, listes et
    ///    dictionnaires de modèles, byte[]) restent délégués à la fixture
    ///    manuelle + DeepCompare : un Fill récursif serait une fausse bonne
    ///    idée (sous-objets légitimement null, cycles via Parent, ids
    ///    croisés écrasés).</summary>
    public static class FixtureFiller
    {
        /// <summary>Champs qu'il ne faut PAS remplir aveuglément. RÈGLE :
        /// chaque entrée doit être soit un transitoire (jamais persisté),
        /// soit exercée À LA MAIN ailleurs dans BuildFullProject — sinon le
        /// verrou a un trou. Commentaire obligatoire par entrée.</summary>
        private static readonly HashSet<string> NoFill = new HashSet<string>
        {
            // --- transitoires : jamais persistés, remplis = faux rouge.
            "Project.LoadedFormatVersion",  // posé par Load, 0 avant écriture
            "Project.ReadOnlyNewerFormat",  // posé par Load (version future)
            "BinderItem.LoadDamaged",       // posé par le chargement tolérant
            "TextParagraph.StartOnRecto",   // posé par le compilateur
            // --- identifiants : uniques ou croisés, cohérence à la main.
            "BinderItem.Id",                // unicité contrôlée à la sauvegarde
            "ParagraphStyle.Id",            // références StyleId des paragraphes
            "SheetTemplate.Id",             // référence TemplateId des fiches
            "SheetField.Id",                // clés de FieldValues
            "Footnote.Id",                  // référence FootnoteId des runs
            "Annotation.Id",                // référence AnnotationId des runs
            "TextRun.FootnoteId",           // ↔ Footnote réelle (exercé au chap.)
            "TextRun.ImageId",              // ↔ image du magasin (exercée)
            "TextRun.AnnotationId",         // ↔ Annotation réelle (exercée)
            "TextParagraph.StyleId",        // ↔ style existant (exercé)
            "BinderItem.TemplateId",        // ↔ modèle de fiche (exercé)
            "BinderItem.CategoryId",        // ↔ catégorie de fiches (exercée
                                            //   par la fiche héroïne, b31)
            "SheetCategory.Id",             // référence CategoryId des fiches
            "SheetCategory.TemplateId",     // ↔ modèle de base (exercé)
            "BinderItem.PageTemplateId",    // ↔ gabarit de pages (exercé)
            "BinderItem.ImageId",           // ↔ image du magasin (exercée)
            "BinderItem.Category",          // clé des 4 catégories racines
            // --- exclusifs par construction : un run de texte n'est ni un
            //     saut ni un filet (mêmes exclusions que C3).
            "TextRun.IsLineBreak",
            "TextRun.IsRule",
            // --- bornés à la lecture : une sentinelle arbitraire est
            //     rabattue dans la plage → faux rouge.
            "PageSetup.Columns",            // clampé 1..3 — exercé par
                                            //   project.Page.Columns = 2
            // --- spécifiques à un Kind : persistés seulement pour l'item
            //     du bon Kind, qui les exerce à la main.
            "BinderItem.MediaExtension",    // média — « Carnet scanné »
            "BinderItem.TemplateColor",     // gabarit de pages
            "BinderItem.HeaderGapMm",       // gabarit de pages
            "BinderItem.FooterGapMm",       // gabarit de pages
            "BinderItem.HeaderHideFirst",   // gabarit de pages
            "BinderItem.FooterHideFirst",   // gabarit de pages
            // --- types non scalaires exercés à la main (règle 2 du 0.5).
            "BinderItem.Kind",              // enum : détermine le jeu de clés
            "BinderItem.FieldValues",       // Dictionary<string,string> — la
                                            //   fiche héroïne l'exerce
            "BinderItem.RadarValues"        // Dictionary<string,double> — la fiche
                                            //   héroïne l'exerce (b47 bis, v22)
        };

        /// <summary>Remplit les scalaires encore à leur défaut de l'objet
        /// donné (jamais ses sous-objets). Sûre à appeler APRÈS le
        /// remplissage manuel : un champ déjà exercé n'est pas touché.
        /// deliberateDefaults : les champs que la fixture pose EXPRÈS à leur
        /// valeur par défaut (« Italic = false ») — l'intention est
        /// inviolable, le filler les saute.</summary>
        public static void Fill(object target, params string[] deliberateDefaults)
        {
            var type = target.GetType();
            var fresh = Activator.CreateInstance(type);
            var deliberate = new HashSet<string>(deliberateDefaults);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (NoFill.Contains(type.Name + "." + field.Name)) continue;
                if (deliberate.Contains(field.Name)) continue;
                var fieldType = field.FieldType;
                var current = field.GetValue(target);
                var initial = field.GetValue(fresh);
                if (fieldType == typeof(string))
                {
                    if (Equals(current, initial))
                        field.SetValue(target, "sentinelle-" + field.Name);
                }
                else if (fieldType == typeof(bool))
                {
                    if (Equals(current, initial)) field.SetValue(target, !(bool)initial);
                }
                else if (fieldType == typeof(bool?))
                {
                    if (current == null) field.SetValue(target, true);
                }
                else if (fieldType == typeof(int))
                {
                    if (Equals(current, initial)) field.SetValue(target, (int)initial + 7);
                }
                else if (fieldType == typeof(long))
                {
                    // Une empreinte 64 bits (Snapshot.Fingerprint, b38) : une
                    // sentinelle hors de portée d'un int, pour attraper un
                    // sérialiseur qui la tronquerait.
                    if (Equals(current, initial)) field.SetValue(target, (long)initial + 7L + (1L << 40));
                }
                else if (fieldType == typeof(double))
                {
                    if (Equals(current, initial)) field.SetValue(target, (double)initial + 3.25);
                }
                else if (fieldType == typeof(double?))
                {
                    if (current == null) field.SetValue(target, 8.5);
                }
                else if (fieldType == typeof(List<string>))
                {
                    // Une entrée sentinelle si la liste est restée au défaut.
                    var list = current as List<string>;
                    var seed = initial as List<string>;
                    if (list != null && (seed == null || list.Count == seed.Count))
                        list.Add("sentinelle-" + field.Name);
                }
                else if (IsDelegated(fieldType))
                {
                    // Types du modèle : instanciés à la main par la fixture,
                    // parcourus par DeepCompare — jamais remplis ici.
                }
                else
                    throw new InvalidOperationException(
                        "FixtureFiller : type non géré pour " + type.Name + "."
                        + field.Name + " (" + fieldType.Name + ") — le champ"
                        + " échapperait au verrou C1. Gérer le type, ou"
                        + " l'ajouter à NoFill avec la preuve qu'il est"
                        + " exercé à la main.");
            }
        }

        /// <summary>Vrai pour les types dont la couverture appartient à la
        /// fixture manuelle + DeepCompare : classes du modèle, listes et
        /// dictionnaires DE modèles, byte[] (blobs de contenu semés à la
        /// main). Tout le reste est un trou → Fill lève.</summary>
        private static bool IsDelegated(Type type)
        {
            if (type == typeof(byte[])) return true;
            if (IsModelType(type)) return true;
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var arguments = type.GetGenericArguments();
                if (definition == typeof(List<>))
                    return IsModelType(arguments[0]);
                if (definition == typeof(Dictionary<,>))
                    return IsModelType(arguments[1]);
            }
            return false;
        }

        private static bool IsModelType(Type type)
        {
            return type.IsClass && type.Namespace != null
                && type.Namespace.StartsWith("Marabook", StringComparison.Ordinal);
        }
    }
}
