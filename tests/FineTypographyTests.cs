using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C82 — la typographie fine (2.0.0, 20/11), le tableau A3 du
    /// comparatif : la césure hybride (syllabes de la maison, motifs de Liang
    /// du français, coupes choisies à la main, pas de mot coupé d'une page à
    /// l'autre), les tables OpenType lues à la main (crénage GPOS et « kern »,
    /// ligatures, petites capitales) sur une police synthétique, le façonnage
    /// dans le compositeur, et le PDF/X-1a (identifiant, XMP, condition
    /// d'impression, profil ICC, couleur de repérage, contrôle).</summary>
    public static class FineTypographyTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C82 — typographie fine : césure, crénage, ligatures, PDF/X");
            Hyphenation(t);
            PageEnd(t);
            Tables(t);
            Shaping(t);
            PdfX(t);
            Icc(t);
        }

        // ------------------------------------------------------------ césure

        private static string Cut(string word)
        {
            return FrenchHyphenator.Mark(word, FrenchHyphenator.BreakPoints(word, 5, 2, 3));
        }

        private static void Hyphenation(Harness t)
        {
            // Les motifs de Liang corrigent la coupe syllabique là où l'étymologie commande.
            t.Equal("trans-at-lan-tique", Cut("transatlantique"), "trans-atlantique : le préfixe reste entier");
            t.Equal("in-utile", Cut("inutile"), "in-utile");
            t.Equal("pré-avis", Cut("préavis"), "pré-avis");
            t.Equal("sur-ex-posé", Cut("surexposé"), "sur-exposé");
            t.Equal("co-opé-rer", Cut("coopérer"), "co-opérer : la coupe entre deux voyelles que l'étymologie permet");
            t.Equal("sub-ur-bain", Cut("suburbain"), "sub-urbain");
            t.Equal("stag-na-tion", Cut("stagnation"), "stag-nation : le « gn » qui se prononce en deux sons");
            // Les choix de la maison tiennent là où les motifs se taisent ou interdisent par pudeur.
            t.Equal("con-seil", Cut("conseil"), "con-seil : la coupe syllabique reste");
            t.Equal("au-tre", Cut("autre"), "au-tre : les groupes à liquide se coupent toujours");
            t.Equal("pos-sible", Cut("possible"), "pos-sible : jamais devant une finale muette");
            t.Equal("exa-men", Cut("examen"), "exa-men : rien autour du x entre deux voyelles");
            t.Equal("mer", Cut("mer"), "un mot trop court ne se coupe pas");
            t.Equal(0, FrenchHyphenator.BreakPoints("UNESCO", 5, 2, 3).Count, "un sigle ne se coupe pas");

            // Les niveaux bruts des motifs.
            var liang = FrenchHyphenator.LiangPoints("transatlantique");
            t.Check(liang.Contains(5) && !liang.Contains(4), "motifs de Liang : « trans- », jamais « tran- »");
            t.Equal(0, FrenchHyphenator.LiangPoints("").Count, "un mot vide : aucun point");

            // Les coupes choisies à la main.
            var chosen = FrenchHyphenator.ExplicitPoints("ma-ra-bout");
            t.Check(chosen != null && chosen.Count == 2 && chosen[0] == 2 && chosen[1] == 4, "« ma-ra-bout » : deux coupes choisies");
            t.Check(FrenchHyphenator.ExplicitPoints("marabout") == null, "sans trait : pas de coupe choisie (le mot ne se coupe jamais)");
            t.Equal(1, FrenchHyphenator.ExplicitPoints("-ma--rabout-").Count, "les traits de bord et les doublons ne comptent pas");
            t.Equal("marabout", FrenchHyphenator.ExceptionWord("ma-ra-bout"), "le mot d'une exception, ses traits retirés");
            t.Equal("ma-ra-bout", FrenchHyphenator.Mark("marabout", chosen), "…et remis");
            var rest = FrenchHyphenator.ShiftPoints(new List<int> { 2, 4, -6 }, 2, 6, 2, 3);
            t.Check(rest.Count == 2 && rest[0] == 2 && rest[1] == -4, "le reste d'un mot coupé garde ses points, décalés (le trait existant garde son signe)");

            // Dans le compositeur : l'exception du projet commande la coupe.
            var project = Project.CreateNew();
            var styles = project.Styles.Clone();
            var body = styles.Body;
            body.HyphenationEnabled = true;
            body.HyphenMinWordLength = 5; body.HyphenMinBefore = 2; body.HyphenMinAfter = 2;
            body.Align = "left"; body.FirstLineIndent = 0;
            // Une colonne où « xxxx xxxx marabout » ne tient pas : 8 px par caractère au corps 16.
            var setup = new PageSetup { PageWidthMm = 60, PageHeightMm = 200, MarginLeftMm = 10, MarginRightMm = 10, MarginTopMm = 10, MarginBottomMm = 10 };
            var avail = setup.ContentWidthPx;
            var fits = (int)Math.Floor(avail / 8);
            var filler = new string('x', Math.Max(1, fits - 5));
            var document = TextDocument.FromPlainText(filler + " marabout suite");
            Func<string> firstLine = delegate
            {
                var engine = new CompositionEngine(document, styles, setup, project, false, new StubGlyphMetrics());
                engine.ComposeAll();
                var line = engine.Current.Paragraphs[0].Lines[0];
                var sb = new StringBuilder();
                foreach (var piece in line.Pieces) if (piece.Text != null) sb.Append(piece.Text).Append('|');
                return sb.ToString() + (line.Hyphenated ? "coupe" : "entier");
            };
            t.Check(firstLine().EndsWith("ma-|coupe"), "sans exception : « ma- » en bout de ligne (" + firstLine() + ")");
            project.HyphenExceptions.Add("marabout");
            t.Check(firstLine().EndsWith("entier") && !firstLine().Contains("ma"), "« marabout » dans les exceptions : il ne se coupe plus");
            project.HyphenExceptions.Clear();
            project.HyphenExceptions.Add("mar-about");
            t.Check(firstLine().EndsWith("mar-|coupe"), "« mar-about » : coupé là où on l'a choisi (" + firstLine() + ")");
        }

        /// <summary>Pas de mot coupé d'une page à l'autre.</summary>
        private static void PageEnd(Harness t)
        {
            var project = Project.CreateNew();
            var styles = project.Styles.Clone();
            var body = styles.Body;
            body.HyphenationEnabled = true;
            body.HyphenMinWordLength = 5; body.HyphenMinBefore = 2; body.HyphenMinAfter = 2;
            body.HyphenConsecutiveLimit = 10;
            body.Align = "left"; body.FirstLineIndent = 0; body.SpaceBefore = 0; body.SpaceAfter = 0;
            // Des lignes qui finissent toutes sur une coupe : « xxx…x marabout » répété.
            var setup = new PageSetup { PageWidthMm = 60, PageHeightMm = 60, MarginLeftMm = 10, MarginRightMm = 10, MarginTopMm = 10, MarginBottomMm = 10 };
            var fits = (int)Math.Floor(setup.ContentWidthPx / 8);
            var filler = new string('x', Math.Max(1, fits - 5));
            var words = new List<string>();
            for (var i = 0; i < 40; i++) { words.Add(filler); words.Add("marabout"); }
            var document = TextDocument.FromPlainText(string.Join(" ", words.ToArray()));
            var engine = new CompositionEngine(document, styles, setup, project, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var pages = engine.Current.Pages;
            t.Check(pages.Count >= 3, "le texte court sur plusieurs pages (" + pages.Count + ")");
            var hyphenated = 0;
            var bad = 0;
            foreach (var layout in engine.Current.Paragraphs) foreach (var line in layout.Lines) if (line.Hyphenated) hyphenated++;
            for (var p = 0; p < pages.Count - 1; p++)
            {
                var lines = pages[p].Lines;
                if (lines.Count > 1 && lines[lines.Count - 1].Line.Hyphenated) bad++;
            }
            t.Check(hyphenated > 10, "des coupes en nombre (" + hyphenated + ")");
            t.Equal(0, bad, "aucune page ne finit sur un mot coupé");
            var total = 0;
            foreach (var page in pages) total += page.Lines.Count;
            t.Equal(engine.Current.Paragraphs[0].Lines.Count, total, "aucune ligne perdue ni doublée en route");
        }

        // ------------------------------------------------------------ une police synthétique

        private sealed class Blob
        {
            private readonly List<byte> _bytes = new List<byte>();
            public int Length { get { return _bytes.Count; } }
            public Blob U16(int value) { _bytes.Add((byte)((value >> 8) & 0xFF)); _bytes.Add((byte)(value & 0xFF)); return this; }
            public Blob U32(long value) { U16((int)((value >> 16) & 0xFFFF)); return U16((int)(value & 0xFFFF)); }
            public Blob Tag(string tag) { foreach (var c in tag) _bytes.Add((byte)c); return this; }
            public Blob Add(Blob other) { _bytes.AddRange(other._bytes); return this; }
            public Blob Pad(int count) { for (var i = 0; i < count; i++) _bytes.Add(0); return this; }
            public byte[] ToArray() { return _bytes.ToArray(); }
        }

        private static Blob ScriptList(int features)
        {
            // Une écriture « latn », un système de langue par défaut, ses fonctions 0…n-1.
            var blob = new Blob().U16(1).Tag("latn").U16(8);
            blob.U16(4).U16(0);                       // la table d'écriture : langue par défaut à +4, aucune autre
            blob.U16(0).U16(0xFFFF).U16(features);    // le système de langue
            for (var i = 0; i < features; i++) blob.U16(i);
            return blob;
        }

        private static Blob FeatureList(params string[] tags)
        {
            var blob = new Blob().U16(tags.Length);
            for (var i = 0; i < tags.Length; i++) blob.Tag(tags[i]).U16(2 + 6 * tags.Length + 6 * i);
            for (var i = 0; i < tags.Length; i++) blob.U16(0).U16(1).U16(i); // la fonction i consulte le lookup i
            return blob;
        }

        private static Blob Table(Blob scripts, Blob features, Blob lookups)
        {
            return new Blob().U32(0x00010000).U16(10).U16(10 + scripts.Length).U16(10 + scripts.Length + features.Length).Add(scripts).Add(features).Add(lookups);
        }

        // Les glyphes de la police d'essai : f, i, leurs ligatures, deux bas-de-casse et leurs petites capitales, des paires à créner.
        private const int F = 10, I = 11, Fi = 20, Ffi = 21, A = 30, B = 31, ASc = 40, BSc = 41, Av = 50, Vv = 51, To = 60, Oo = 70, Ee = 71;

        private static byte[] Gsub()
        {
            // Lookup 0 : les ligatures de « f » — « fi » d'abord dans la police, « ffi » ensuite (le lecteur doit prendre la plus longue).
            var ligatures = new Blob().U16(1).U16(8 + 6 + 6 + 8).U16(1).U16(8)  // format, couverture, un jeu, à +8
                .U16(2).U16(6).U16(12)                                          // le jeu : deux ligatures
                .U16(Fi).U16(2).U16(I)                                          // fi
                .U16(Ffi).U16(3).U16(F).U16(I)                                  // ffi
                .U16(1).U16(1).U16(F);                                          // couverture : f
            // Lookup 1 : les petites capitales, par tableau.
            var small = new Blob().U16(2).U16(10).U16(2).U16(ASc).U16(BSc).U16(1).U16(2).U16(A).U16(B);
            var lookup0 = new Blob().U16(4).U16(0).U16(1).U16(8).Add(ligatures);
            var lookup1 = new Blob().U16(1).U16(0).U16(1).U16(8).Add(small);
            var lookups = new Blob().U16(2).U16(6).U16(6 + lookup0.Length).Add(lookup0).Add(lookup1);
            return Table(ScriptList(2), FeatureList("liga", "smcp"), lookups).ToArray();
        }

        private static byte[] Gpos()
        {
            // Sous-table 1 : une paire nommée (Av, Vv) → −80.
            var pairs = new Blob().U16(1).U16(12 + 6).U16(4).U16(0).U16(1).U16(12)  // format 1, couverture, XAdvance sur le premier, un jeu
                .U16(1).U16(Vv).U16(0x10000 - 80)                                   // le jeu : (Vv, −80)
                .U16(1).U16(1).U16(Av);                                             // couverture : Av
            // Sous-table 2 : par classes — To (classe 1) devant Oo ou Ee (classe 1) → −50.
            var classes = new Blob().U16(2).U16(24).U16(4).U16(0).U16(30).U16(38).U16(2).U16(2)
                .U16(0).U16(0).U16(0).U16(0x10000 - 50)                             // la matrice 2 × 2
                .U16(1).U16(1).U16(To)                                              // couverture : To
                .U16(1).U16(To).U16(1).U16(1)                                       // classes du premier : To → 1
                .U16(2).U16(1).U16(Oo).U16(Ee).U16(1);                              // classes du second : Oo…Ee → 1
            var lookup = new Blob().U16(2).U16(0).U16(2).U16(10).U16(10 + pairs.Length).Add(pairs).Add(classes);
            var lookups = new Blob().U16(1).U16(4).Add(lookup);
            return Table(ScriptList(1), FeatureList("kern"), lookups).ToArray();
        }

        private static byte[] Kern()
        {
            return new Blob().U16(0).U16(1).U16(0).U16(14 + 6).U16(1).U16(1).U16(6).U16(0).U16(0).U16(Av).U16(Vv).U16(0x10000 - 120).ToArray();
        }

        private static byte[] Font(Dictionary<string, byte[]> tables)
        {
            var head = new Blob().Pad(18).U16(2048).Pad(34).ToArray();
            var all = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("head", head) };
            foreach (var table in tables) all.Add(table);
            var blob = new Blob().U32(0x00010000).U16(all.Count).U16(0).U16(0).U16(0);
            var offset = 12 + 16 * all.Count;
            foreach (var table in all)
            {
                blob.Tag(table.Key).U32(0).U32(offset).U32(table.Value.Length);
                offset += table.Value.Length;
            }
            var bytes = new List<byte>(blob.ToArray());
            foreach (var table in all) bytes.AddRange(table.Value);
            return bytes.ToArray();
        }

        private static byte[] FullFont()
        {
            return Font(new Dictionary<string, byte[]> { { "GSUB", Gsub() }, { "GPOS", Gpos() }, { "kern", Kern() } });
        }

        private static void Tables(Harness t)
        {
            var layout = OpenTypeLayout.Load(FullFont(), 0);
            t.Equal(2048, layout.UnitsPerEm, "le cadratin de la police");
            t.Check(layout.HasKerning && layout.HasLigatures && layout.HasSmallCaps, "la police d'essai a crénage, ligatures et petites capitales");

            // Le crénage : la paire nommée, les classes, et le silence ailleurs.
            t.Equal(-80, layout.Kern(Av, Vv), "GPOS, paire nommée : −80");
            t.Equal(0, layout.Kern(Vv, Av), "…pas dans l'autre sens");
            t.Equal(0, layout.Kern(Av, Oo), "une paire absente de sa sous-table : rien");
            t.Equal(-50, layout.Kern(To, Oo), "GPOS, par classes : −50");
            t.Equal(-50, layout.Kern(To, Ee), "…pour toute la classe");
            t.Equal(0, layout.Kern(To, Av), "…et rien hors classe");
            t.Equal(-80, layout.Kern(Av, Vv), "le cache rend la même valeur");
            t.Check(layout.Kern(Av, Vv) != -120, "GPOS présent : la vieille table « kern » se tait");

            var old = OpenTypeLayout.Load(Font(new Dictionary<string, byte[]> { { "kern", Kern() } }), 0);
            t.Check(old.HasKerning && !old.HasLigatures && !old.HasSmallCaps, "une police à seule table « kern »");
            t.Equal(-120, old.Kern(Av, Vv), "…sa paire");
            t.Equal(0, old.Kern(Vv, Av), "…et rien d'autre");

            // Les ligatures : la plus longue gagne, quel que soit l'ordre de la police.
            ushort glyph;
            int count;
            t.Check(layout.TryLigature(new ushort[] { F, I }, 0, out glyph, out count) && glyph == Fi && count == 2, "f + i → fi");
            t.Check(layout.TryLigature(new ushort[] { F, F, I, A }, 0, out glyph, out count) && glyph == Ffi && count == 3, "f + f + i → ffi, pas f + (fi)");
            t.Check(!layout.TryLigature(new ushort[] { F, A }, 0, out glyph, out count), "f + a : pas de ligature");
            t.Check(!layout.TryLigature(new ushort[] { A, F }, 1, out glyph, out count), "un f en fin de mot : rien à lier");
            t.Check(!layout.TryLigature(new ushort[] { I, F }, 0, out glyph, out count), "i ne commence aucune ligature");

            // Les petites capitales.
            ushort small;
            t.Check(layout.TrySmallCap(A, out small) && small == ASc && layout.TrySmallCap(B, out small) && small == BSc, "a et b ont leur petite capitale");
            t.Check(!layout.TrySmallCap(F, out small), "f n'en a pas");

            // La robustesse : rien de faux sur un fichier tordu.
            var empty = OpenTypeLayout.Load(new byte[4], 0);
            t.Check(!empty.HasKerning && !empty.HasLigatures && !empty.HasSmallCaps && empty.Kern(1, 2) == 0, "un fichier trop court : une mise en forme vide");
            var truncated = FullFont();
            Array.Resize(ref truncated, truncated.Length / 2);
            var broken = OpenTypeLayout.Load(truncated, 0);
            t.Check(broken != null && broken.Kern(Av, Vv) == broken.Kern(Av, Vv), "un fichier tronqué ne lève pas");
            t.Check(OpenTypeLayout.Load(null, 0) != null, "pas de fichier : une mise en forme vide, jamais null");
        }

        // ------------------------------------------------------------ le façonnage dans le compositeur

        /// <summary>Un moteur de polices d'essai : chaque caractère a son
        /// glyphe (son code), large d'un demi-cadratin, et la face porte les
        /// tables de la police synthétique — ses glyphes y sont ceux des
        /// lettres f, i, a, b, A, V, T, o, e.</summary>
        private sealed class ShapingEngine : IFontEngine, IFontShaper
        {
            public OpenTypeLayout Tables;
            private readonly FaceInfo _face = new FaceInfo { Family = "Essai", Weight = 400, ActualWeight = 400, Key = "essai", File = "essai", HasGlyphs = true, Baseline = 0.8 };
            private static readonly Dictionary<char, ushort> Map = new Dictionary<char, ushort>
            {
                { 'f', F }, { 'i', I }, { 'a', A }, { 'b', B }, { 'A', Av }, { 'V', Vv }, { 'T', To }, { 'o', Oo }, { 'e', Ee }
            };

            public OpenTypeLayout Layout(FaceInfo face) { return Tables; }
            public FaceInfo Resolve(string family, int weight, bool italic) { return _face; }
            public bool TryGlyph(FaceInfo face, char c, out ushort glyph)
            {
                if (Map.TryGetValue(c, out glyph)) return true;
                glyph = (ushort)(1000 + c);
                return c > ' ' && c < 0x2000;
            }
            public double GlyphAdvance(FaceInfo face, ushort glyph) { return glyph == Fi ? 0.9 : glyph == Ffi ? 1.3 : glyph == ASc || glyph == BSc ? 0.4 : 0.5; }
            public IEnumerable<KeyValuePair<int, ushort>> CharacterMap(FaceInfo face) { foreach (var pair in Map) yield return new KeyValuePair<int, ushort>(pair.Key, pair.Value); }
            public TextExtent Measure(FaceInfo face, string text, double emSize) { return new TextExtent { Width = emSize * 0.5 * (text ?? "").Length, Height = emSize * 1.25, Baseline = emSize * 0.8 }; }
            public byte[] FontFile(FaceInfo face, out int faceIndex, out string fallbackName) { faceIndex = 0; fallbackName = null; return null; }
            public byte[] RasterizeText(FaceInfo face, string text, double emSize, Ink ink, double scale, out int width, out int height) { width = 0; height = 0; return null; }
            public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, char c) { return emSize * 0.5; }
            public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, string s) { return emSize * 0.5 * (s ?? "").Length; }
            public bool HasGlyph(string fontFamily, int weight, bool italic, char c) { return true; }
            public FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic) { return new FontMetrics { Ascent = emSize * 0.8, Descent = emSize * 0.2 }; }
        }

        private static List<ComposedPiece> Pieces(string text, Action<ParagraphStyle> tune, Action<TextDocument> edit = null)
        {
            var project = Project.CreateNew();
            var styles = project.Styles.Clone();
            var body = styles.Body;
            body.Align = "left"; body.FirstLineIndent = 0; body.FontSize = 20; body.HyphenationEnabled = false;
            if (tune != null) tune(body);
            var document = TextDocument.FromPlainText(text);
            if (edit != null) edit(document);
            var engine = new CompositionEngine(document, styles, new PageSetup(), project, false, new ShapingEngine { Tables = OpenTypeLayout.Load(FullFont(), 0) });
            engine.ComposeAll();
            var pieces = new List<ComposedPiece>();
            foreach (var piece in engine.Current.Paragraphs[0].Lines[0].Pieces) if (piece.IsGlyphs) pieces.Add(piece);
            return pieces;
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) < 0.001; }

        private static void Shaping(Harness t)
        {
            // Les ligatures : « fi » devient un glyphe qui porte deux caractères.
            var pieces = Pieces("fia affib", null);
            t.Equal(2, pieces.Count, "deux mots, deux pièces");
            var first = pieces[0];
            t.Check(first.GlyphIndices.Length == 2 && first.GlyphIndices[0] == Fi && first.GlyphIndices[1] == A, "« fia » : la ligature fi, puis a");
            t.Check(first.GlyphChars != null && first.GlyphChars[0] == 2 && first.GlyphChars[1] == 1, "…la ligature porte deux caractères");
            t.Check(Near(first.Advances[0], 0.9 * 20) && Near(first.Advances[1], 0.5 * 20), "…à l'avance de son glyphe");
            t.Check(first.CharRights.Length == 3 && Near(first.CharRights[0], 9) && Near(first.CharRights[1], 18) && Near(first.CharRights[2], 28), "le curseur passe ENTRE le f et le i : la ligature se partage en deux");
            t.Equal("fia", first.Text, "le texte de la pièce reste celui de la source");
            var second = pieces[1];
            t.Check(second.GlyphIndices.Length == 3 && second.GlyphIndices[0] == A && second.GlyphIndices[1] == Ffi && second.GlyphIndices[2] == B, "« affib » : a, la ligature ffi, b");
            t.Check(second.GlyphChars[1] == 3 && second.CharRights.Length == 5 && Near(second.CharRights[4], (0.5 + 1.3 + 0.5) * 20), "…ffi porte trois caractères, la pièce garde ses cinq");
            t.Check(Near(second.Origin.X, first.VisualWidth() + 10), "le mot suivant part après la largeur FAÇONNÉE du premier (mesure et rendu s'accordent)");

            // Le crénage : la paire resserrée, en proportion du corps.
            var kerned = Pieces("AV To", null);
            t.Check(Near(kerned[0].Advances[0], 0.5 * 20 - 80 * 20.0 / 2048) && Near(kerned[0].Advances[1], 10), "« AV » : le A cède 80 unités au V");
            t.Check(kerned[0].GlyphChars == null, "sans substitution : un glyphe par caractère, comme avant");
            t.Check(Near(kerned[1].Advances[0], 10 - 50 * 20.0 / 2048), "« To » : la paire par classes");
            t.Check(Near(kerned[1].Origin.X, kerned[0].VisualWidth() + 10), "l'espace part après le mot crêné");

            // Les réglages du style.
            var loose = Pieces("AV fia", delegate(ParagraphStyle s) { s.Kerning = false; });
            t.Check(Near(loose[0].Advances[0], 10) && loose[1].GlyphIndices[0] == Fi, "crénage décoché : les avances nues, les ligatures restent");
            var apart = Pieces("AV fia", delegate(ParagraphStyle s) { s.Ligatures = false; });
            t.Check(apart[1].GlyphIndices.Length == 3 && apart[1].GlyphIndices[0] == F && apart[0].Advances[0] < 10, "ligatures décochées : f et i séparés, le crénage reste");
            var bare = Pieces("AV fia", delegate(ParagraphStyle s) { s.Ligatures = false; s.Kerning = false; });
            t.Check(Near(bare[0].Advances[0], 10) && bare[1].GlyphIndices.Length == 3 && bare[1].GlyphChars == null, "tout décoché : la composition d'avant");

            // Une approche posée à la main défait la ligature.
            var tracked = Pieces("fia", null, delegate(TextDocument d) { d.Paragraphs[0].Runs[0].Tracking = 100; });
            t.Check(tracked[0].GlyphIndices.Length == 3 && tracked[0].GlyphIndices[0] == F && Near(tracked[0].Advances[0], 10 + 2), "avec une approche : pas de ligature, les lettres s'écartent");

            // Les vraies petites capitales : les glyphes de la police, au corps entier.
            var caps = Pieces("Tab", null, delegate(TextDocument d) { d.Paragraphs[0].Runs[0].SmallCaps = true; });
            t.Equal(1, caps.Count, "petites capitales de la police : le mot reste UNE pièce");
            t.Check(caps[0].GlyphIndices.Length == 3 && caps[0].GlyphIndices[0] == To && caps[0].GlyphIndices[1] == ASc && caps[0].GlyphIndices[2] == BSc, "…T tel quel, a et b en petites capitales dessinées");
            t.Check(Near(caps[0].EmSize, 20) && Near(caps[0].Advances[1], 0.4 * 20) && caps[0].Text == "Tab" && caps[0].GlyphChars != null, "…au corps entier, à l'avance du glyphe, la source intacte");

            // Sans tables (le stub des tests) : rien ne change.
            var project = Project.CreateNew();
            var plain = new CompositionEngine(TextDocument.FromPlainText("fia AV"), project.Styles, new PageSetup(), project, false, new StubGlyphMetrics());
            plain.ComposeAll();
            var fallback = plain.Current.Paragraphs[0].Lines[0].Pieces[0];
            t.Check(fallback.IsFallback && fallback.GlyphChars == null, "sans moteur de façonnage : la composition reste nue");
        }

        // ------------------------------------------------------------ PDF/X

        internal static List<PdfPart> PartsForTests(string text) { return Parts(text); }

        private static List<PdfPart> Parts(string text)
        {
            var project = Project.CreateNew();
            var engine = new CompositionEngine(TextDocument.FromPlainText(text), project.Styles, new PageSetup(), project, true, new StubGlyphMetrics());
            engine.ComposeAll();
            return new List<PdfPart> { new PdfPart { Composition = engine.Current } };
        }

        private static void PdfX(Harness t)
        {
            var latin = Encoding.GetEncoding(28591);
            var icc = IccProfiles.Embedded() == null ? FakeIcc("Coated FOGRA39 (ISO 12647-2:2004)") : IccProfiles.Embedded().Bytes; // le profil livré (21/11) : le contrôle refait exige sa table AtoB1
            var options = new PdfExportOptions { Title = "Le livre", Author = "Autrice d'essai", Cmyk = true, PdfX = true, CropMarks = true, BleedMm = 5, IccProfile = icc };
            var bytes = PdfWriter.Build(Parts("Un texte d'essai."), options);
            var text = latin.GetString(bytes);
            var problems = PdfXCheck.Verify(bytes);
            t.Equal(0, problems.Count, "le PDF/X de Marabook passe son contrôle" + (problems.Count > 0 ? " — " + problems[0] : ""));
            t.Check(text.Contains("/GTS_PDFXVersion (PDF/X-1a:2003)") && text.Contains("/Trapped /False"), "il se déclare PDF/X-1a:2003, piégeage dit");
            t.Check(text.Contains("/ID [<") && text.Contains("/CreationDate (D:") && text.Contains("/ModDate (D:"), "identifiant et dates");
            t.Check(text.Contains("/Type /Metadata /Subtype /XML") && text.Contains("<pdfxid:GTS_PDFXVersion>PDF/X-1a:2003</pdfxid:GTS_PDFXVersion>") && text.Contains("<pdf:Trapped>False</pdf:Trapped>"), "les métadonnées XMP, en clair");
            t.Check(text.Contains("<rdf:li xml:lang=\"x-default\">Le livre</rdf:li>") && text.Contains("<rdf:li>Autrice d'essai</rdf:li>"), "…avec le titre et l'autrice");
            t.Check(text.Contains("/OutputConditionIdentifier (FOGRA39)") && text.Contains("/DestOutputProfile ") && text.Contains("/N 4 /Alternate /DeviceCMYK"), "la condition d'impression, son profil ICC incorporé");
            t.Check(text.Contains("/Reg [/Separation /All /DeviceCMYK"), "la couleur de repérage est déclarée");
            t.Check(!text.Contains("/DeviceRGB"), "aucune image en RVB");

            // Sans profil : toujours conforme, la condition par son nom.
            options.IccProfile = null;
            bytes = PdfWriter.Build(Parts("Un texte d'essai."), options);
            text = latin.GetString(bytes);
            t.Check(PdfXCheck.Verify(bytes).Count == 0 && !text.Contains("/DestOutputProfile") && text.Contains("/RegistryName (http://www.color.org)"), "sans profil sur la machine : la condition déclarée par son nom enregistré, le contrôle passe");

            // Les liens tombent en PDF/X.
            var linked = Parts("Un lien vers la cible.");
            linked[0].Marks.Add(new PdfMark { Paragraph = 0, Name = "cible", Title = "Cible" });
            linked[0].Links.Add(new PdfLink { Paragraph = 0, Start = 3, Length = 4, Target = "cible" });
            t.Check(!latin.GetString(PdfWriter.Build(linked, options)).Contains("/Annots"), "PDF/X : aucun lien cliquable");
            var free = new PdfExportOptions { Title = "Le livre", Cmyk = true };
            t.Check(latin.GetString(PdfWriter.Build(linked, free)).Contains("/Annots"), "…sans PDF/X, les liens restent");

            // Le cahier garde une boîte de coupe.
            var booklet = new PdfExportOptions { Title = "Cahier", Cmyk = true, PdfX = true, Booklet = true };
            var sheet = PdfWriter.Build(Parts("Un texte d'essai."), booklet);
            t.Equal(0, PdfXCheck.Verify(sheet).Count, "le cahier en PDF/X passe le contrôle (une boîte de coupe par feuille)");

            // Un PDF ordinaire : identifiant et XMP aussi, mais pas de PDF/X — et le contrôle le dit.
            var rgb = PdfWriter.Build(Parts("Un texte d'essai."), new PdfExportOptions { Title = "Lecture" });
            text = latin.GetString(rgb);
            t.Check(text.Contains("/ID [<") && text.Contains("/Type /Metadata") && !text.Contains("GTS_PDFXVersion") && !text.Contains("/OutputIntents"), "un PDF de lecture : identifiant et XMP, sans rien déclarer de PDF/X");
            problems = PdfXCheck.Verify(rgb);
            var joined = string.Join("\n", problems.ToArray());
            t.Check(joined.Contains("ne se déclare pas PDF/X-1a") && joined.Contains("condition d'impression"), "le contrôle relève ce qui manque à un PDF de lecture (" + problems.Count + " défauts)");
            t.Check(PdfXCheck.Verify(new byte[10]).Count == 1 && PdfXCheck.Verify(latin.GetBytes(new string('x', 100))).Count == 1, "un fichier vide ou étranger : un défaut, pas d'exception");
            // PDF/X demandé en RVB : il ne se déclare pas.
            var wrong = latin.GetString(PdfWriter.Build(Parts("Essai."), new PdfExportOptions { Title = "x", PdfX = true }));
            t.Check(!wrong.Contains("GTS_PDFXVersion"), "PDF/X sans CMJN : le fichier ne ment pas");

            // Le plan le dit.
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var ids = new List<string> { writings.Children[0].Id };
            var preset = new ExportPreset { Format = ExportFormats.Pdf, PdfX = true, Links = true };
            var plan = ExportPlan.Build(project, ids, preset, null);
            t.Check(plan.Warnings.Exists(delegate(ExportWarning w) { return w.Level == ExportWarning.Warn && w.Text.StartsWith("PDF/X-1a ne vaut qu'en CMJN"); }), "PDF/X coché en RVB : un avertissement");
            preset.Cmyk = true;
            plan = ExportPlan.Build(project, ids, preset, null);
            t.Check(plan.Warnings.Exists(delegate(ExportWarning w) { return w.Text.StartsWith("PDF/X-1a interdit les liens"); }), "PDF/X avec des liens : la remarque");
            t.Check(ExportPresets.Find(ExportPresets.BuiltIn(), ExportPresets.Bat).PdfX, "le BAT Imprimeur est en PDF/X");
            var back = ExportPreset.FromJson(Json.Parse(Json.Write(preset.ToJson())));
            t.Check(back.PdfX && back.Signature() == preset.Signature() && new ExportPreset().Signature() != new ExportPreset { PdfX = true }.Signature(), "l'option fait l'aller-retour et compte dans l'empreinte");
        }

        // ------------------------------------------------------------ profils ICC

        private static byte[] FakeIcc(string description, string deviceClass = "prtr", string space = "CMYK")
        {
            var desc = Encoding.ASCII.GetBytes(description + "\0");
            var bytes = new byte[128 + 4 + 12 + 12 + desc.Length];
            Action<int, string> tag = delegate(int at, string value) { Encoding.ASCII.GetBytes(value, 0, 4, bytes, at); };
            Action<int, int> u32 = delegate(int at, int value) { bytes[at] = (byte)(value >> 24); bytes[at + 1] = (byte)(value >> 16); bytes[at + 2] = (byte)(value >> 8); bytes[at + 3] = (byte)value; };
            u32(0, bytes.Length);
            tag(12, deviceClass); tag(16, space); tag(36, "acsp");
            u32(128, 1);
            tag(132, "desc"); u32(136, 144); u32(140, 12 + desc.Length);
            tag(144, "desc"); u32(152, desc.Length);
            Array.Copy(desc, 0, bytes, 156, desc.Length);
            return bytes;
        }

        private static void Icc(Harness t)
        {
            var good = FakeIcc("Coated FOGRA39 (ISO 12647-2:2004)");
            t.Check(IccProfiles.IsCmykPrinterProfile(good), "un profil d'imprimante CMJN est reconnu");
            t.Check(!IccProfiles.IsCmykPrinterProfile(FakeIcc("sRGB", "mntr", "RGB ")) && !IccProfiles.IsCmykPrinterProfile(new byte[20]) && !IccProfiles.IsCmykPrinterProfile(null), "un profil d'écran, un fichier trop court : refusés");
            t.Equal("Coated FOGRA39 (ISO 12647-2:2004)", IccProfiles.DescriptionOf(good), "la description du profil");
            t.Equal("", IccProfiles.DescriptionOf(new byte[200]), "sans balise : pas de description, pas d'exception");
            t.Check(IccProfiles.Fogra39Rank("CoatedFOGRA39", "") == 2 && IccProfiles.Fogra39Rank("x", "Coated FOGRA39 (ISO 12647-2:2004)") == 2, "FOGRA39 par son nom de fichier ou sa description");
            t.Check(IccProfiles.Fogra39Rank("ISOcoated_v2_eci", "") == 1 && IccProfiles.Fogra39Rank("ISOcoated_v2_300_eci", "") == 0 && IccProfiles.Fogra39Rank("USWebCoatedSWOP", "") == 0, "son jumeau de l'ECI en second, les autres jamais");

            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c82-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "sous"));
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "USWebCoatedSWOP.icc"), FakeIcc("U.S. Web Coated (SWOP) v2"));
                File.WriteAllBytes(Path.Combine(dir, "ecran-coated.icc"), FakeIcc("Coated FOGRA39 pour écran", "mntr", "RGB "));
                t.Check(IccProfiles.Find(new[] { dir }) == null, "aucun FOGRA39 dans le dossier : rien");
                File.WriteAllBytes(Path.Combine(dir, "ISOcoated_v2_eci.icc"), FakeIcc("ISO Coated v2 (ECI)"));
                var twin = IccProfiles.Find(new[] { dir });
                t.Check(twin != null && twin.Description == "ISO Coated v2 (ECI)", "à défaut, le jumeau de l'ECI");
                File.WriteAllBytes(Path.Combine(dir, "sous", "CoatedFOGRA39.icc"), good);
                var found = IccProfiles.Find(new[] { Path.Combine(dir, "absent"), dir });
                t.Check(found != null && found.Path.EndsWith("CoatedFOGRA39.icc") && found.Bytes.Length == good.Length && found.Description.StartsWith("Coated FOGRA39"), "FOGRA39 trouvé, jusque dans un sous-dossier, de préférence à son jumeau");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
            t.Check(IccProfiles.SystemFolders().Count > 0, "des dossiers de profils sont connus sur ce système");
        }
    }
}
