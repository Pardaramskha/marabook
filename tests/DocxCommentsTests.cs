using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C30 — commentaires Word ↔ annotations (b49) : les annotations
    /// non résolues partent en commentaires (plages, auteur, date), un docx
    /// commenté revient en annotations sur les bons runs, et les
    /// commentaires d'un document relu retrouvent leur passage par empreinte
    /// dans un texte qui a bougé — ou se posent sans rien perdre.</summary>
    public static class DocxCommentsTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C30 — commentaires Word ↔ annotations");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-comments");
            Directory.CreateDirectory(dir);
            try
            {
                RoundTrip(t, dir);
                Merge(t);
                Dates(t);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static TextDocument Sample()
        {
            var document = TextDocument.FromPlainText("Le marabout dort sur la rive.\nIl rêve.\nUn cri.");
            var open = new Annotation { Text = "Trop calme ?", Created = "2026-09-18 10:00" };
            var resolved = new Annotation { Text = "réglé", Resolved = true, Created = "2026-09-18 10:01" };
            var point = new Annotation { Text = "Qui crie ?\nÀ préciser.", Created = "2026-09-18 10:02" };
            document.Annotations.Add(open);
            document.Annotations.Add(resolved);
            document.Annotations.Add(point);
            PivotEdit.ApplyFormat(document.Paragraphs[0], 3, 16, delegate(TextRun r) { r.AnnotationId = open.Id; });
            PivotEdit.ApplyFormat(document.Paragraphs[1], 3, 7, delegate(TextRun r) { r.AnnotationId = resolved.Id; });
            PivotEdit.ApplyFormat(document.Paragraphs[2], 3, 6, delegate(TextRun r) { r.AnnotationId = point.Id; });
            // Le passage annoté est coupé en deux runs (gras au milieu).
            PivotEdit.ApplyFormat(document.Paragraphs[0], 12, 16, delegate(TextRun r) { r.Bold = true; });
            return document;
        }

        private static void RoundTrip(Harness t, string dir)
        {
            var document = Sample();
            var styles = StyleSheet.CreateDefault();
            var path = Path.Combine(dir, "commente.docx");
            Docx.Export(document, styles, path, null, "Rémi Lecteur");

            using (var zip = ZipFile.OpenRead(path))
            {
                var comments = zip.GetEntry("word/comments.xml");
                t.Check(comments != null, "word/comments.xml est écrit");
                string xml;
                using (var reader = new StreamReader(comments.Open())) xml = reader.ReadToEnd();
                t.Check(xml.Contains("w:author=\"Rémi Lecteur\""), "l'auteur est celui donné");
                t.Check(xml.Contains("w:initials=\"RL\""), "les initiales suivent");
                t.Check(xml.Contains("Trop calme ?") && xml.Contains("Qui crie ?") && xml.Contains("À préciser."), "les textes des commentaires (multi-paragraphes compris)");
                t.Check(!xml.Contains("réglé"), "une annotation résolue ne part pas");
                string body;
                using (var reader = new StreamReader(zip.GetEntry("word/document.xml").Open())) body = reader.ReadToEnd();
                t.Check(body.Contains("<w:commentRangeStart w:id=\"0\"/>") && body.Contains("<w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r>"), "plage et appel du premier commentaire");
                t.Check(body.Contains("<w:commentRangeStart w:id=\"1\"/>"), "le second commentaire est numéroté 1 (le résolu est sauté)");
                string types;
                using (var reader = new StreamReader(zip.GetEntry("[Content_Types].xml").Open())) types = reader.ReadToEnd();
                t.Check(types.Contains("comments+xml"), "le type de contenu des commentaires est déclaré");
                string rels;
                using (var reader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels").Open())) rels = reader.ReadToEnd();
                t.Check(rels.Contains("relationships/comments"), "la relation vers comments.xml est déclarée");
            }

            List<DocxComment> comments2;
            var back = Docx.ImportWithComments(path, styles, out comments2);
            t.Equal(2, comments2.Count, "deux commentaires relus");
            t.Equal(2, back.Annotations.Count, "deux annotations créées");
            t.Equal("marabout dort", comments2[0].Anchor, "l'empreinte du premier : le passage commenté, même coupé en deux runs");
            t.Equal(0, comments2[0].ParagraphIndex, "…dans le premier paragraphe");
            t.Equal("Rémi Lecteur", comments2[0].Author, "auteur relu");
            t.Equal("Trop calme ?", comments2[0].Text, "texte relu");
            t.Equal("cri", comments2[1].Anchor, "l'empreinte du second");
            t.Equal(2, comments2[1].ParagraphIndex, "…dans le troisième paragraphe");
            t.Equal("Qui crie ?\nÀ préciser.", comments2[1].Text, "texte relu sur deux lignes");
            t.Equal("Rémi Lecteur — Trop calme ?", back.Annotations[0].Text, "l'annotation porte l'auteur");
            t.Equal("2026-09-18 10:00", back.Annotations[0].Created, "la date fait l'aller-retour (heure locale)");
            int first;
            t.Equal("marabout dort", CommentMerge.AnchorOf(back, back.Annotations[0].Id, out first), "les runs relus portent l'annotation sur le passage exact");
            t.Equal("Le marabout dort sur la rive.", PivotEdit.FlatText(back.Paragraphs[0]), "le texte n'a pas bougé");
            t.Check(back.Paragraphs[0].Runs.Count >= 3, "le gras au milieu du passage est conservé (runs distincts)");
            t.Equal(2, Docx.Import(path, styles).Annotations.Count, "Import simple : les annotations aussi");

            // Sans annotation : pas de fichier de commentaires.
            var plain = TextDocument.FromPlainText("Rien.");
            var plainPath = Path.Combine(dir, "plain.docx");
            Docx.Export(plain, styles, plainPath);
            using (var zip = ZipFile.OpenRead(plainPath))
                t.Check(zip.GetEntry("word/comments.xml") == null, "sans annotation : pas de comments.xml");
            List<DocxComment> none;
            Docx.ImportWithComments(plainPath, styles, out none);
            t.Equal(0, none.Count, "…et rien à relire");
        }

        private static void Merge(Harness t)
        {
            // Le texte a été retouché depuis la relecture : le passage a bougé.
            var target = TextDocument.FromPlainText("Le vieux marabout dort sur la rive du marais.\nIl ne rêve plus.\nUn cri.\nFin.");
            var comments = new List<DocxComment>
            {
                new DocxComment { Author = "Léa", Text = "Trop calme ?", Anchor = "marabout dort", ParagraphIndex = 0, Date = "2026-09-18T08:00:00Z" },
                new DocxComment { Author = "Léa", Text = "Casse et accents", Anchor = "UN CRI", ParagraphIndex = 5 },
                new DocxComment { Author = "Léa", Text = "Passage disparu", Anchor = "phrase envolée", ParagraphIndex = 1 },
                new DocxComment { Author = "", Text = "Sans plage", Anchor = "", ParagraphIndex = 3 },
                new DocxComment { Author = "Léa", Text = "Sur deux paragraphes", Anchor = "rive du marais.\nIl ne", ParagraphIndex = 0 }
            };
            var result = CommentMerge.Merge(target, comments);
            t.Equal(5, result.Total, "cinq commentaires traités");
            t.Equal(3, result.Placed, "trois posés sur leur passage (exact, casse/accents, première ligne d'une plage à cheval)");
            t.Equal(2, result.Fallback, "deux posés en repli");
            t.Equal(0, result.Lost, "rien de perdu");
            t.Equal(5, target.Annotations.Count, "cinq annotations");
            int first;
            t.Equal("marabout dort", CommentMerge.AnchorOf(target, target.Annotations[0].Id, out first), "le premier a retrouvé son passage déplacé");
            t.Equal("Un cri", CommentMerge.AnchorOf(target, target.Annotations[1].Id, out first), "casse ignorée, paragraphe de rang faux : trouvé ailleurs");
            t.Equal(2, first, "…au troisième paragraphe");
            t.Equal("Il", CommentMerge.AnchorOf(target, target.Annotations[2].Id, out first), "passage disparu : premier mot du paragraphe de même rang");
            t.Equal(1, first, "…le deuxième paragraphe");
            t.Check(target.Annotations[2].Text.StartsWith("« phrase envolée »"), "…et le passage cité dans l'annotation");
            t.Check(target.Annotations[2].Text.EndsWith("Léa — Passage disparu"), "…suivi du commentaire");
            t.Equal("Fin.", CommentMerge.AnchorOf(target, target.Annotations[3].Id, out first), "sans plage : premier mot du paragraphe de même rang");
            t.Equal("Sans plage", target.Annotations[3].Text, "sans auteur : le texte seul");
            t.Equal("rive du marais.", CommentMerge.AnchorOf(target, target.Annotations[4].Id, out first), "plage à cheval : sa première ligne");
            t.Equal("Le vieux marabout dort sur la rive du marais.", PivotEdit.FlatText(target.Paragraphs[0]), "le texte cible n'a pas changé");

            var empty = TextDocument.FromPlainText("");
            var lost = CommentMerge.Merge(empty, new List<DocxComment> { new DocxComment { Text = "x", Anchor = "y" } });
            t.Equal(1, lost.Lost, "document vide : perdu, et compté");
            t.Equal(0, CommentMerge.Merge(target, new List<DocxComment>()).Total, "rien à fusionner");
        }

        private static void Dates(Harness t)
        {
            t.Equal("Léa — Bien vu", CommentMerge.Label("Léa", " Bien vu "), "étiquette auteur — texte");
            t.Equal("Bien vu", CommentMerge.Label(null, "Bien vu"), "sans auteur : le texte");
            t.Equal("2026-09-18 10:00", CommentMerge.ToCreated(CommentMerge.ToIsoDate("2026-09-18 10:00")), "date : aller-retour local ↔ ISO UTC");
            t.Equal(DateTime.Now.ToString("yyyy-MM-dd HH:mm"), CommentMerge.ToCreated("n'importe quoi"), "date illisible : maintenant");
            t.Check(CommentMerge.ToIsoDate("").EndsWith("Z"), "création vide : une date ISO quand même");
        }
    }
}
