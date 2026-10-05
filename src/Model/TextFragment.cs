using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Le fragment mis en forme du presse-papiers (28/09/2026) :
    /// une sélection extraite en un petit TextDocument — paragraphes (style,
    /// alignement, liste, décalages) et runs (police, taille, gras, couleur,
    /// surlignage…) — et son insertion au caret.
    /// Depuis le hotfix 1.0.3-a, TOUT ce qui est sélectionné voyage : les
    /// appels de notes avec le corps de leurs notes, les ancres d'annotation
    /// avec leurs commentaires, les images avec leurs octets (Package), les
    /// filets et les sauts de ligne. À l'insertion, notes et annotations
    /// prennent un identifiant neuf si l'original est encore en usage dans
    /// le document cible (copie), gardent le leur sinon (couper, déplacer,
    /// autre écrit) ; les images absentes du magasin du projet y entrent.
    /// Règle de la marque de paragraphe (celle de Word) : un fragment d'un
    /// seul paragraphe se glisse dans le paragraphe du caret sans en changer
    /// le style ; un fragment de plusieurs paragraphes apporte le style de
    /// ses paragraphes, sauf pour le dernier, dont le texte rejoint la fin
    /// du paragraphe de destination et en garde le style.</summary>
    public static class TextFragment
    {
        /// <summary>Le nom du format de presse-papiers du fragment (JSON du
        /// paquet, encodé en UTF-8).</summary>
        public const string ClipboardFormat = "application/x-marabook-fragment";

        /// <summary>Les octets d'une image citée par le fragment.</summary>
        public sealed class PackedImage
        {
            public string Id;
            public string Extension = ".png";
            public byte[] Bytes;
        }

        /// <summary>Le fragment complet : le document (paragraphes, notes,
        /// annotations) et les images qu'il cite.</summary>
        public sealed class Package
        {
            public TextDocument Document = new TextDocument();
            public List<PackedImage> Images = new List<PackedImage>();
        }

        /// <summary>Extrait [pa:oa, pb:ob) — bornes déjà ordonnées, offsets
        /// plats — en un document neuf, avec les notes et les annotations
        /// que la tranche cite. Jamais null ; au moins un paragraphe.</summary>
        public static TextDocument Extract(TextDocument document, int pa, int oa, int pb, int ob)
        {
            return Extract(document, pa, oa, pb, ob, true);
        }

        /// <summary>Même extraction. <paramref name="detach"/> ne change plus
        /// ce qui voyage (tout voyage depuis le hotfix 1.0.3-a) ; le paramètre
        /// reste pour les appelants du déplacement interne.</summary>
        public static TextDocument Extract(TextDocument document, int pa, int oa, int pb, int ob, bool detach)
        {
            var fragment = new TextDocument();
            if (document == null || document.Paragraphs.Count == 0) { fragment.Paragraphs.Add(new TextParagraph()); return fragment; }
            pa = Math.Max(0, Math.Min(pa, document.Paragraphs.Count - 1));
            pb = Math.Max(pa, Math.Min(pb, document.Paragraphs.Count - 1));
            for (var p = pa; p <= pb; p++)
            {
                var source = document.Paragraphs[p];
                var copy = PivotEdit.CloneParagraphShell(source);
                copy.StartOnRecto = false;
                copy.Decor = null;
                foreach (var run in source.Runs)
                    copy.Runs.Add(PivotEdit.CloneRun(run));
                var length = PivotEdit.FlatLength(copy);
                var from = p == pa ? Math.Max(0, Math.Min(oa, length)) : 0;
                var to = p == pb ? Math.Max(from, Math.Min(ob, length)) : length;
                PivotEdit.DeleteInParagraph(copy, to, length);
                PivotEdit.DeleteInParagraph(copy, 0, from);
                fragment.Paragraphs.Add(copy);
            }
            fragment.LineSpacing = document.LineSpacing;
            CarryNotesAndAnnotations(document, fragment);
            return fragment;
        }

        /// <summary>Les notes et les annotations citées par les runs du
        /// fragment, clonées depuis le document d'origine (une ancre sans
        /// annotation connue est retirée, un appel sans note aussi).</summary>
        private static void CarryNotesAndAnnotations(TextDocument source, TextDocument fragment)
        {
            var notes = new HashSet<string>();
            var annotations = new HashSet<string>();
            foreach (var paragraph in fragment.Paragraphs)
                for (var i = paragraph.Runs.Count - 1; i >= 0; i--)
                {
                    var run = paragraph.Runs[i];
                    if (run.FootnoteId != null)
                    {
                        var note = source.FindFootnote(run.FootnoteId);
                        if (note == null) { paragraph.Runs.RemoveAt(i); continue; }
                        if (notes.Add(note.Id)) fragment.Footnotes.Add(note.Clone());
                    }
                    if (run.AnnotationId != null)
                    {
                        var annotation = source.FindAnnotation(run.AnnotationId);
                        if (annotation == null) run.AnnotationId = null;
                        else if (annotations.Add(annotation.Id)) fragment.Annotations.Add(CloneAnnotation(annotation));
                    }
                }
        }

        private static Annotation CloneAnnotation(Annotation annotation)
        {
            return new Annotation
            {
                Id = annotation.Id,
                Text = annotation.Text,
                Created = annotation.Created,
                Resolved = annotation.Resolved
            };
        }

        /// <summary>Le paquet complet d'une sélection : le fragment et les
        /// octets des images qu'il cite (lues dans le magasin du projet ; une
        /// image inconnue du magasin reste un run sans octets).</summary>
        public static Package Pack(TextDocument document, Project project, int pa, int oa, int pb, int ob)
        {
            var package = new Package { Document = Extract(document, pa, oa, pb, ob) };
            var seen = new HashSet<string>();
            foreach (var paragraph in package.Document.Paragraphs)
                foreach (var run in paragraph.Runs)
                {
                    if (run.ImageId == null || !seen.Add(run.ImageId)) continue;
                    var image = project == null ? null : project.FindImage(run.ImageId);
                    if (image == null || image.Bytes == null) continue;
                    package.Images.Add(new PackedImage { Id = run.ImageId, Extension = image.Extension, Bytes = image.Bytes });
                }
            return package;
        }

        /// <summary>Insère le fragment au caret ; le caret est déplacé après le
        /// texte inséré. Rend le nombre de paragraphes AJOUTÉS au document,
        /// tous contigus juste après le paragraphe de départ (le compositeur
        /// les apprend d'un coup : ParagraphsInserted). Les notes et les
        /// annotations du fragment sont adoptées (voir Adopt).</summary>
        public static int Insert(TextDocument document, TextDocument fragment, ref int paragraph, ref int offset)
        {
            if (document == null || fragment == null || fragment.Paragraphs.Count == 0) return 0;
            Adopt(document, fragment);
            if (document.Paragraphs.Count == 0) document.Paragraphs.Add(new TextParagraph());
            paragraph = Math.Max(0, Math.Min(paragraph, document.Paragraphs.Count - 1));
            var target = document.Paragraphs[paragraph];
            offset = Math.Max(0, Math.Min(offset, PivotEdit.FlatLength(target)));
            var pieces = fragment.Paragraphs;
            var tail = PivotEdit.Split(target, offset); // la fin du paragraphe, son style
            tail.FirstIndent = target.FirstIndent;
            tail.AllowWidows = target.AllowWidows;

            if (pieces.Count == 1)
            {
                Append(target, pieces[0]);
                PivotEdit.MergeInto(target, tail);
                offset += PivotEdit.FlatLength(pieces[0]);
                return 0;
            }

            // Plusieurs paragraphes : le premier apporte sa marque au
            // paragraphe du caret, les suivants sont insérés tels quels, le
            // dernier rejoint la fin du paragraphe de destination.
            ApplyShell(pieces[0], target);
            Append(target, pieces[0]);
            PivotEdit.MergeInto(target, new TextParagraph());
            var added = 0;
            for (var i = 1; i < pieces.Count - 1; i++)
            {
                var middle = PivotEdit.CloneParagraphShell(pieces[i]);
                Append(middle, pieces[i]);
                document.Paragraphs.Insert(paragraph + i, middle);
                added++;
            }
            var last = pieces[pieces.Count - 1];
            var landing = new TextParagraph();
            Append(landing, last);
            var caretOffset = PivotEdit.FlatLength(landing);
            PivotEdit.MergeInto(landing, tail);
            landing.StyleId = tail.StyleId;
            landing.AlignOverride = tail.AlignOverride;
            landing.ListKind = tail.ListKind;
            landing.Indent = tail.Indent;
            landing.FirstIndent = tail.FirstIndent;
            landing.AllowWidows = tail.AllowWidows;
            document.Paragraphs.Insert(paragraph + pieces.Count - 1, landing);
            added++;
            paragraph += pieces.Count - 1;
            offset = caretOffset;
            return added;
        }

        /// <summary>Insère le PAQUET : les images d'abord dans le magasin du
        /// projet (celles qu'il n'a pas — sous un identifiant neuf, les runs
        /// suivent), puis le fragment comme ci-dessus. Sans projet, les runs
        /// d'images dont le magasin ne connaît pas l'identifiant sont retirés
        /// plutôt que de laisser une image fantôme.</summary>
        public static int Insert(TextDocument document, Package package, Project project, ref int paragraph, ref int offset)
        {
            if (package == null) return 0;
            AdoptImages(package, project);
            return Insert(document, package.Document, ref paragraph, ref offset);
        }

        /// <summary>Les images du paquet rejoignent le magasin du projet :
        /// l'identifiant est gardé s'il y est déjà (même projet), sinon les
        /// octets entrent sous un identifiant neuf et les runs sont renommés.</summary>
        private static void AdoptImages(Package package, Project project)
        {
            var renamed = new Dictionary<string, string>();
            if (project != null)
                foreach (var image in package.Images)
                {
                    if (image.Id == null || image.Bytes == null || project.FindImage(image.Id) != null) continue;
                    renamed[image.Id] = project.AddImage(image.Bytes, image.Extension);
                }
            foreach (var paragraph in package.Document.Paragraphs)
                for (var i = paragraph.Runs.Count - 1; i >= 0; i--)
                {
                    var run = paragraph.Runs[i];
                    if (run.ImageId == null) continue;
                    string fresh;
                    if (renamed.TryGetValue(run.ImageId, out fresh)) { run.ImageId = fresh; continue; }
                    if (project == null || project.FindImage(run.ImageId) == null) paragraph.Runs.RemoveAt(i);
                }
        }

        /// <summary>Les notes et les annotations du fragment rejoignent le
        /// document : un identifiant encore EN USAGE dans le document (un run
        /// le cite : c'est une copie) en reçoit un neuf, les runs du fragment
        /// suivent ; un identifiant connu mais orphelin (couper, déplacer)
        /// est repris ; un inconnu est ajouté. Le fragment est modifié en
        /// place (il ne sert qu'une fois par insertion).</summary>
        private static void Adopt(TextDocument document, TextDocument fragment)
        {
            if (fragment.Footnotes.Count == 0 && fragment.Annotations.Count == 0) return;
            var usedNotes = new HashSet<string>();
            var usedAnnotations = new HashSet<string>();
            foreach (var paragraph in document.Paragraphs)
                foreach (var run in paragraph.Runs)
                {
                    if (run.FootnoteId != null) usedNotes.Add(run.FootnoteId);
                    if (run.AnnotationId != null) usedAnnotations.Add(run.AnnotationId);
                }
            var notes = new Dictionary<string, string>();
            foreach (var note in fragment.Footnotes)
            {
                var copy = note.Clone();
                var existing = document.FindFootnote(note.Id);
                if (existing != null && usedNotes.Contains(note.Id))
                {
                    copy.Id = Guid.NewGuid().ToString("N");
                    document.Footnotes.Add(copy);
                }
                else if (existing != null) existing.SetRuns(copy.Runs);
                else document.Footnotes.Add(copy);
                notes[note.Id] = copy.Id;
            }
            var annotations = new Dictionary<string, string>();
            foreach (var annotation in fragment.Annotations)
            {
                var copy = CloneAnnotation(annotation);
                var existing = document.FindAnnotation(annotation.Id);
                if (existing != null && usedAnnotations.Contains(annotation.Id))
                {
                    copy.Id = Guid.NewGuid().ToString("N");
                    document.Annotations.Add(copy);
                }
                else if (existing == null) document.Annotations.Add(copy);
                annotations[annotation.Id] = copy.Id;
            }
            foreach (var paragraph in fragment.Paragraphs)
                foreach (var run in paragraph.Runs)
                {
                    string fresh;
                    if (run.FootnoteId != null && notes.TryGetValue(run.FootnoteId, out fresh)) run.FootnoteId = fresh;
                    if (run.AnnotationId != null && annotations.TryGetValue(run.AnnotationId, out fresh)) run.AnnotationId = fresh;
                }
        }

        private static void Append(TextParagraph paragraph, TextParagraph piece)
        {
            foreach (var run in piece.Runs)
                paragraph.Runs.Add(PivotEdit.CloneRun(run));
        }

        private static void ApplyShell(TextParagraph from, TextParagraph to)
        {
            to.StyleId = from.StyleId;
            to.AlignOverride = from.AlignOverride;
            to.ListKind = from.ListKind;
            to.Indent = from.Indent;
            to.FirstIndent = from.FirstIndent;
            to.PageBreakBefore = from.PageBreakBefore;
            to.AllowWidows = from.AllowWidows;
        }
    }
}
