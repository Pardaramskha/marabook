using System.Text;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>LE « LISEZMOI » D'UN .PLOT (21/11) : un texte glissé dans
    /// chaque projet enregistré, qui explique comment le relire SANS
    /// Marabook — dans dix ans, sur une autre machine, si l'application
    /// n'existe plus. Le même texte pour tous les projets (rien du projet
    /// n'y figure, hors la version qui l'a écrit) : l'entrée ne change pas
    /// d'un enregistrement à l'autre. Marabook ne le relit jamais.</summary>
    public static class PlotReadme
    {
        public const string EntryName = "LISEZMOI.txt";

        public static string Text(int formatVersion)
        {
            var sb = new StringBuilder();
            sb.Append("CE FICHIER EST UN PROJET MARABOOK\r\n");
            sb.Append("=================================\r\n\r\n");
            sb.Append("Un fichier « .plot » est une archive ZIP ordinaire. Vous lisez ce texte parce que\r\n");
            sb.Append("vous l'avez ouverte sans Marabook : tout ce que le projet contient est ici, dans\r\n");
            sb.Append("des formats ouverts, et se relit avec les outils de n'importe quel ordinateur.\r\n\r\n");
            sb.Append("Pour l'ouvrir : copiez le fichier, renommez la copie en « .zip », décompressez-la.\r\n");
            sb.Append("Travaillez toujours sur une copie.\r\n\r\n");
            sb.Append("CE QU'IL Y A DEDANS\r\n");
            sb.Append("-------------------\r\n\r\n");
            sb.Append("manifest.json\r\n");
            sb.Append("    Le sommaire du projet (JSON, UTF-8). Sa clé « binder » est l'arbre de la Pile :\r\n");
            sb.Append("    chaque élément a un identifiant « id », un titre « title », une sorte « kind »\r\n");
            sb.Append("    (écrit, fiche, dossier, livre, plan, frise, carte…) et ses enfants « children ».\r\n");
            sb.Append("    C'est là qu'on retrouve le TITRE et l'ORDRE des textes. On y trouve aussi le nom\r\n");
            sb.Append("    du projet, l'auteur, le dictionnaire, les relations entre fiches, les frises,\r\n");
            sb.Append("    les mondes et les calendriers.\r\n\r\n");
            sb.Append("texts/<id>.json\r\n");
            sb.Append("    Le texte d'un écrit ou d'une fiche, sous l'identifiant du sommaire. « paragraphs »\r\n");
            sb.Append("    est la liste des paragraphes ; chacun a des « runs », des morceaux de texte dont\r\n");
            sb.Append("    la clé « t » porte les mots (les autres clés disent le gras, l'italique, le\r\n");
            sb.Append("    style). Mettre bout à bout les « t » d'un paragraphe rend sa phrase.\r\n");
            sb.Append("    Un lien vers une fiche s'écrit [[Nom de la fiche]] dans le texte même.\r\n\r\n");
            sb.Append("styles.json\r\n");
            sb.Append("    Les styles de paragraphe (police, corps, alinéa, interligne).\r\n\r\n");
            sb.Append("sheets/templates.json\r\n");
            sb.Append("    Les modèles de fiche et leurs champs. Les valeurs d'une fiche sont dans le\r\n");
            sb.Append("    sommaire, sur son élément.\r\n\r\n");
            sb.Append("images/\r\n");
            sb.Append("    Les images du projet (portraits, illustrations, couvertures), telles qu'importées.\r\n\r\n");
            sb.Append("research/\r\n");
            sb.Append("    Les documents de recherche, dans leur format d'origine (PDF, image, texte…).\r\n\r\n");
            sb.Append("cartes/<id>/<niveau>/<x>_<y>.jpg\r\n");
            sb.Append("    Les cartes du monde, découpées en tuiles. Le niveau le plus élevé est l'image\r\n");
            sb.Append("    entière ; ses tuiles, posées côte à côte (x = colonne, y = rangée), la recomposent.\r\n\r\n");
            sb.Append("maps/<id>.tea\r\n");
            sb.Append("    Les cartes mentales (format du module Mental-O, lui-même une archive).\r\n\r\n");
            sb.Append("snapshots/<id de l'écrit>/<id>.json\r\n");
            sb.Append("    Les versions gardées d'un écrit : une ligne de renseignements (date, libellé),\r\n");
            sb.Append("    puis le texte de cette version, dans la même forme que « texts/ ».\r\n\r\n");
            sb.Append("RETROUVER UN TEXTE À LA MAIN\r\n");
            sb.Append("----------------------------\r\n\r\n");
            sb.Append("1. Ouvrez manifest.json dans un éditeur de texte et cherchez le titre du chapitre.\r\n");
            sb.Append("2. Relevez son « id ».\r\n");
            sb.Append("3. Ouvrez texts/<cet id>.json : les mots sont dans les clés « t ».\r\n\r\n");
            sb.Append("Tous les fichiers JSON sont en UTF-8. Un fichier que ce texte ne cite pas vient\r\n");
            sb.Append("d'une version plus récente : son nom dit le plus souvent ce qu'il contient.\r\n\r\n");
            sb.Append("--\r\n");
            sb.Append("Écrit par Marabook ").Append(AppInfo.Version).Append(" (format ").Append(formatVersion).Append("). Marabook est un logiciel libre et gratuit :\r\n");
            sb.Append("https://github.com/Pardaramskha/marabook\r\n");
            return sb.ToString();
        }
    }
}
