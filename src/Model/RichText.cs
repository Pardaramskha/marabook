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
        public bool? SmallCaps;     // petites majuscules (0.50.0) : les bas-de-casse
                                    // en capitales réduites ; null = non
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
        public string ImageId;      // image ancrée ici (octets dans le magasin du projet)
        public ImageLayout Image;   // son placement (0.50.0) ; null = défauts
                                    // (attachée à sa ligne, centrée, texte
                                    // au-dessus et en dessous)
        public bool IsRule;         // horizontal rule (its paragraph holds nothing else)

        /// <summary>Le placement de l'image, créé au besoin (run image seulement).</summary>
        public ImageLayout EnsureImage()
        {
            if (Image == null) Image = new ImageLayout();
            return Image;
        }

        public bool HasSameFormat(TextRun other)
        {
            return Bold == other.Bold && Italic == other.Italic
                && Underline == other.Underline && Strike == other.Strike
                && SmallCaps == other.SmallCaps
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

        /// <summary>Le texte plat du paragraphe, aux règles de
        /// TextDocument.ToPlainText : marqueurs de notes, images et filets
        /// omis, saut de ligne = retour.</summary>
        public string ToPlainText()
        {
            var sb = new StringBuilder();
            AppendPlainText(sb);
            return sb.ToString();
        }

        public void AppendPlainText(StringBuilder sb)
        {
            foreach (var run in Runs)
            {
                if (run.FootnoteId != null || run.ImageId != null || run.IsRule) continue;
                if (run.IsLineBreak) { sb.Append('\n'); continue; }
                sb.Append(run.Text);
            }
        }

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

        // Le corps de la note (0.50.0) : des runs, comme un paragraphe —
        // gras, italique, police, taille… Avant, une chaîne nue. Text reste
        // la projection plate (recherche, différentiel, empreinte, markdown) :
        // la lire concatène les runs, l'écrire remplace tout par un run nu.
        public List<TextRun> Runs = new List<TextRun>();

        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var run in Runs)
                {
                    if (run.IsLineBreak) { sb.Append('\n'); continue; }
                    if (run.FootnoteId != null || run.ImageId != null || run.IsRule) continue;
                    sb.Append(run.Text);
                }
                return sb.ToString();
            }
            set
            {
                Runs.Clear();
                if (!string.IsNullOrEmpty(value)) Runs.Add(new TextRun { Text = value });
            }
        }

        /// <summary>Vrai si un run porte un format (au-delà du texte nu) — la
        /// persistance n'écrit les runs que dans ce cas.</summary>
        public bool HasFormatting
        {
            get
            {
                if (Runs.Count > 1) return true;
                foreach (var run in Runs)
                    if (run.Bold.HasValue || run.Italic.HasValue || run.Underline.HasValue || run.Strike.HasValue
                        || run.SmallCaps.HasValue
                        || run.Weight != null || run.Tracking.HasValue || run.FontFamily != null || run.FontSize.HasValue
                        || run.Color != null || run.Highlight != null || run.IsLineBreak)
                        return true;
                return false;
            }
        }

        /// <summary>Une signature texte + formats — la clé de cache du
        /// compositeur, qui ne recompose une note que si elle a changé.</summary>
        public string FormatKey()
        {
            var sb = new StringBuilder();
            foreach (var run in Runs)
            {
                if (run.IsLineBreak) { sb.Append("\u0001"); continue; }
                sb.Append(run.Text).Append('|').Append(run.Bold).Append(run.Italic).Append(run.Underline)
                  .Append(run.Strike).Append(run.SmallCaps).Append(run.Weight).Append(run.FontFamily).Append(run.FontSize)
                  .Append(run.Color).Append(run.Highlight).Append(run.Tracking).Append('\u0002');
            }
            return sb.ToString();
        }

        /// <summary>Le corps de la note posé dans un paragraphe (copie des runs)
        /// — ce que composent et exportent les sorties riches.</summary>
        public TextParagraph ToParagraph(string styleId)
        {
            var paragraph = new TextParagraph { StyleId = styleId };
            foreach (var run in Runs) paragraph.Runs.Add(PivotEdit.CloneRun(run));
            return paragraph;
        }

        /// <summary>Remplace le corps par les runs de TEXTE d'un paragraphe
        /// (les éléments — appels, images, filets — n'ont pas leur place dans
        /// une note ; un saut de ligne reste).</summary>
        public void SetRuns(IEnumerable<TextRun> runs)
        {
            Runs.Clear();
            foreach (var run in runs)
            {
                if (run.FootnoteId != null || run.ImageId != null || run.IsRule) continue;
                Runs.Add(PivotEdit.CloneRun(run));
            }
        }

        /// <summary>Efface des runs ce que le style des notes dit déjà (police,
        /// taille, gras, italique) — après un import qui comparait au style du
        /// paragraphe porteur.</summary>
        public void NormalizeAgainst(ParagraphStyle style)
        {
            if (style == null) return;
            foreach (var run in Runs)
            {
                if (run.FontFamily == style.FontFamily) run.FontFamily = null;
                if (run.FontSize.HasValue && Math.Abs(run.FontSize.Value - style.FontSize) < 0.1) run.FontSize = null;
                if (run.Bold.HasValue && run.Bold.Value == style.Bold) run.Bold = null;
                if (run.Italic.HasValue && run.Italic.Value == style.Italic) run.Italic = null;
            }
        }

        public Footnote Clone()
        {
            var copy = new Footnote { Id = Id };
            foreach (var run in Runs) copy.Runs.Add(PivotEdit.CloneRun(run));
            return copy;
        }
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
        /// used for statistics and search indexing. Le texte d'un paragraphe
        /// seul suit les mêmes règles (TextParagraph.ToPlainText) : les
        /// statistiques se comptent paragraphe par paragraphe (22/09).</summary>
        public string ToPlainText()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < Paragraphs.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                Paragraphs[i].AppendPlainText(sb);
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
                {
                    if (run.AnnotationId != id || run.FootnoteId != null || run.IsRule) continue;
                    // Une image annotée (0.50.0) : son nom entre crochets.
                    if (run.ImageId != null)
                    {
                        var name = run.Image == null ? null : run.Image.Name;
                        sb.Append("[" + (string.IsNullOrEmpty(name) ? "image" : name) + "]");
                        continue;
                    }
                    sb.Append(run.Text);
                }
            return sb.ToString();
        }
    }
}
