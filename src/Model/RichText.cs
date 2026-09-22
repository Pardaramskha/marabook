using System;
using System.Collections.Generic;
using System.Text;

namespace Marabook.Model
{
    /// <summary>The pivot text model — the single central format every exchange
    /// goes through (editor, .plot, docx/odt/RTF later, PDF later). Deliberately
    /// aligned with docx/odt semantics: paragraphs referencing a named style,
    /// holding styled runs. Run properties are nullable: null = inherit from the
    /// paragraph style; only overrides are stored.</summary>
    public class TextRun
    {
        public string Text = "";
        public bool? Bold, Italic, Underline, Strike;
        public string Weight;       // "Light|Medium|SemiBold|Black"… fine-grained
                                    // variant; wins over Bold when set
        public double? Tracking;    // approche, en millièmes de cadratin
                                    // (unités InDesign) — rendue par le
                                    // compositeur, pas par le RichTextBox
        public string FontFamily;   // null = style font
        public double? FontSize;    // null = style size
        public string Color;        // "#RRGGBB", null = automatic (style color, else theme ink)
        public string Highlight;    // "#RRGGBB", null = none
        public string FootnoteId;   // set on footnote markers; Text is regenerated
        public string AnnotationId; // révision : le passage porte ce commentaire
                                    // (ancre de format, survit aux éditions)
        public bool NoProof;        // « ne pas corriger » — soustrait le passage
                                    // aux vérificateurs (noms inventés, langues
                                    // fictives) ; sémantique du w:noProof de Word
        public bool IsLineBreak;    // explicit line break (Shift+Enter)
        public string ImageId;      // inline image (bytes live in the project image store)
        public bool IsRule;         // horizontal rule (its paragraph holds nothing else)

        public bool HasSameFormat(TextRun other)
        {
            return Bold == other.Bold && Italic == other.Italic
                && Underline == other.Underline && Strike == other.Strike
                && Weight == other.Weight && Tracking == other.Tracking
                && FontFamily == other.FontFamily && FontSize == other.FontSize
                && Color == other.Color && Highlight == other.Highlight
                && AnnotationId == other.AnnotationId
                && NoProof == other.NoProof
                && FootnoteId == null && other.FootnoteId == null
                && ImageId == null && other.ImageId == null
                && !IsRule && !other.IsRule
                && !IsLineBreak && !other.IsLineBreak;
        }
    }

    public class TextParagraph
    {
        public string StyleId = "body";
        public string AlignOverride; // "left"|"center"|"right"|"justify", null = style alignment
        public string ListKind;      // null | "bullet" | "number"
        // Décalage (17/09/2026) : retrait gauche UNIFORME du paragraphe en px,
        // posé depuis le ruban par pas de 0,5 cm. Null = le style décide
        // (retrait gauche + alinéa + les 24 px d'une liste) ; une valeur
        // remplace les trois — 0 ramène tout au bord de la marge, alinéa
        // du style compris. Persisté (.plot v24, clé "indent").
        public double? Indent;
        // Décalage de la PREMIÈRE LIGNE seule (21/09) : sa position depuis
        // la marge, en px — le retrait du ruban posé le caret sur la première
        // ligne (recréer un alinéa) ou sur les suivantes (retrait suspendu).
        // Null = elle suit : l'alinéa du style quand Indent est null, le
        // bloc sinon. Persisté (.plot v25, clé "firstIndent").
        public double? FirstIndent;
        public List<TextRun> Runs = new List<TextRun>();

        /// <summary>La géométrie effective du paragraphe (21/09), la seule
        /// règle pour le compositeur, les exports et le ruban : le retrait de
        /// toutes les lignes depuis la marge (Indent, sinon le style plus les
        /// 24 px d'une liste) et la position de la première ligne (FirstIndent,
        /// sinon le bloc quand Indent est posé, sinon l'alinéa du style).</summary>
        public void EffectiveIndents(ParagraphStyle style, out double left, out double first)
        {
            left = Indent ?? (style.LeftIndent + (ListKind != null ? 24 : 0));
            first = FirstIndent ?? (Indent.HasValue ? left : left + style.FirstLineIndent);
        }

        // Manual page break (Mise en page), also set by the compiler on chapter
        // starts. Persisted in .plot since v4; honored by the docx exporter.
        public bool PageBreakBefore;

        // Books: the compiler marks chapter starts so pagination opens them on
        // a RECTO (odd folio), inserting a blank verso when needed. Transient,
        // never persisted.
        public bool StartOnRecto;

        // « Autoriser veuves et orphelines ici » : coupe ce paragraphe où bon
        // lui semble, sans contrôle 2/2 — l'annulation ciblée d'une correction
        // qui déséquilibrait les pages. Persisté (.plot v6, clé "wo").
        public bool AllowWidows;

        // Compiled books: the chapter's header/footer decor rides on its
        // paragraphs so each page of the merged manuscript knows its chapter.
        // Transient, never persisted.
        public PageDecor Decor;
    }

    public class Footnote
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text = "";
    }

    /// <summary>Une annotation de révision : un commentaire ancré à un passage
    /// (les runs du passage portent AnnotationId). Résolue = conservée mais
    /// éteinte à l'écran. Jamais imprimée ni exportée.</summary>
    public class Annotation
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text = "";
        public string Created = ""; // "yyyy-MM-dd HH:mm"
        public bool Resolved;
    }

    public class TextDocument
    {
        public List<TextParagraph> Paragraphs = new List<TextParagraph>();
        public List<Footnote> Footnotes = new List<Footnote>();
        public List<Annotation> Annotations = new List<Annotation>();
        // L'INTERLIGNE DU DOCUMENT (22/09, v28) : un multiplicateur de la
        // valeur d'interligne de chaque style de paragraphe — 1 (simple),
        // 1,25, 1,5, 1,75, 2 (double). Le ruban Texte le règle par écrit.
        public double LineSpacing = 1;

        public static TextDocument FromPlainText(string text)
        {
            var document = new TextDocument();
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var paragraph = new TextParagraph();
                if (line.Length > 0)
                    paragraph.Runs.Add(new TextRun { Text = line });
                document.Paragraphs.Add(paragraph);
            }
            if (document.Paragraphs.Count == 0)
                document.Paragraphs.Add(new TextParagraph());
            return document;
        }

        /// <summary>Body text only (footnote markers and note texts excluded) —
        /// used for statistics and search indexing.</summary>
        public string ToPlainText()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < Paragraphs.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                foreach (var run in Paragraphs[i].Runs)
                {
                    if (run.FootnoteId != null || run.ImageId != null || run.IsRule) continue;
                    if (run.IsLineBreak) { sb.Append('\n'); continue; }
                    sb.Append(run.Text);
                }
            }
            return sb.ToString();
        }

        public Footnote FindFootnote(string id)
        {
            foreach (var note in Footnotes)
                if (note.Id == id) return note;
            return null;
        }

        public Annotation FindAnnotation(string id)
        {
            foreach (var annotation in Annotations)
                if (annotation.Id == id) return annotation;
            return null;
        }

        /// <summary>Ids d'annotations dans l'ordre du texte, et purge : une
        /// annotation dont plus aucun run ne porte l'ancre est abandonnée
        /// (passage supprimé) ; une ancre sans annotation est effacée.</summary>
        public List<string> AnnotationOrder(bool purge)
        {
            var order = new List<string>();
            foreach (var paragraph in Paragraphs)
                foreach (var run in paragraph.Runs)
                {
                    if (run.AnnotationId == null) continue;
                    if (FindAnnotation(run.AnnotationId) == null)
                    {
                        if (purge) run.AnnotationId = null;
                        continue;
                    }
                    if (!order.Contains(run.AnnotationId)) order.Add(run.AnnotationId);
                }
            if (purge)
                Annotations.RemoveAll(delegate(Annotation annotation)
                {
                    return !order.Contains(annotation.Id);
                });
            return order;
        }

        /// <summary>Le texte du passage ancré (extrait pour le panneau).</summary>
        public string AnnotatedText(string id)
        {
            var sb = new StringBuilder();
            foreach (var paragraph in Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.AnnotationId == id && run.FootnoteId == null
                        && run.ImageId == null && !run.IsRule)
                        sb.Append(run.Text);
            return sb.ToString();
        }
    }
}
