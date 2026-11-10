using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C61 — les notes de version et de patch (1.1.1) : majeure ou
    /// patch, le cycle d'une version, l'ordre, les patchs d'avant, et la
    /// composition de l'onglet « Notes de version » (filet « --- » et nom du
    /// patch en H1 entre les textes).</summary>
    public static class ReleaseNotesTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C61 — notes de version et de patch");
            Kinds(t);
            Ordering(t);
            Composition(t);
        }

        private static void Kinds(Harness t)
        {
            t.Check(ReleaseNotes.IsMajor("1.1.0") && !ReleaseNotes.IsPatch("1.1.0"), "1.1.0 est une majeure");
            t.Check(ReleaseNotes.IsMajor("2.0.0"), "2.0.0 aussi");
            t.Check(ReleaseNotes.IsPatch("1.1.1") && !ReleaseNotes.IsMajor("1.1.1"), "1.1.1 est un patch");
            t.Check(ReleaseNotes.IsPatch("1.0.5-patch-a"), "1.0.5-patch-a est un patch");
            t.Check(ReleaseNotes.IsPatch("1.0.3-patch-b") && !ReleaseNotes.IsMajor("1.0.3-patch-b"), "1.0.3-patch-b aussi (un correctif à chaud d'un patch)");
            t.Check(!ReleaseNotes.IsPatch("1.1.0-beta") && !ReleaseNotes.IsMajor("1.1.0-beta"), "une beta n'est ni l'un ni l'autre");
            t.Check(!ReleaseNotes.IsPatch("") && !ReleaseNotes.IsMajor(null), "vide ou nul : rien");
            t.Equal("1.1.0", ReleaseNotes.MajorOf("1.1.1"), "la majeure du cycle de 1.1.1");
            t.Equal("1.1.0", ReleaseNotes.MajorOf("1.1.0"), "…et de 1.1.0 elle-même");
            t.Equal("1.0.0", ReleaseNotes.MajorOf("1.0.5-patch-a"), "…et de 1.0.5-patch-a");
            t.Equal("# Patch 1.1.1", ReleaseNotes.PatchHeading("1.1.1"), "le titre H1 d'un patch");
        }

        private static void Ordering(Harness t)
        {
            t.Check(ReleaseNotes.Compare("1.1.1", "1.1.0") > 0 && ReleaseNotes.Compare("1.1.0", "1.1.1") < 0, "1.1.1 après 1.1.0");
            t.Check(ReleaseNotes.Compare("1.1.2", "1.1.10") < 0, "1.1.2 avant 1.1.10 (des nombres, pas des lettres)");
            t.Check(ReleaseNotes.Compare("1.0.3-patch-a", "1.0.3") > 0 && ReleaseNotes.Compare("1.0.3-patch-b", "1.0.3-patch-a") > 0, "les correctifs à chaud se suivent après leur patch");
            t.Check(ReleaseNotes.Compare("1.1.0-beta", "1.1.0") < 0, "la beta avant la stable");
            t.Equal(0, ReleaseNotes.Compare("1.1.1", "1.1.1"), "égales");
            var known = new[] { "1.0.6", "1.1.0", "1.1.2", "1.1.1", "1.1.0-beta", "1.1.3", "1.0.5-patch-a", "2.0.0" };
            var before = ReleaseNotes.PatchesBefore(known, "1.1.3");
            t.Equal("1.1.1, 1.1.2", string.Join(", ", before.ToArray()), "les patchs du cycle 1.1 avant 1.1.3, dans l'ordre (ni 1.1.0, ni la beta, ni 1.0.x)");
            t.Equal(0, ReleaseNotes.PatchesBefore(known, "1.1.1").Count, "avant 1.1.1 : aucun");
            t.Equal(0, ReleaseNotes.PatchesBefore(known, "1.1.0").Count, "avant la majeure : aucun");
            t.Equal("1.1.1, 1.1.2, 1.1.3", string.Join(", ", ReleaseNotes.PatchesBefore(known, "1.1.9").ToArray()), "avant 1.1.9 : les trois");
            t.Equal(0, ReleaseNotes.PatchesBefore(null, "1.1.9").Count, "sans liste : aucun, sans plantage");
        }

        private static void Composition(Harness t)
        {
            var patches = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("1.1.1", "* un correctif\n"),
                new KeyValuePair<string, string>("1.1.2", "  * deux correctifs  ")
            };
            var text = ReleaseNotes.Compose("## 1.1.0 - Wikipédiatre\n\n* la majeure", patches);
            t.Equal("## 1.1.0 - Wikipédiatre\n\n* la majeure\n\n---\n\n# Patch 1.1.1\n\n* un correctif\n\n---\n\n# Patch 1.1.2\n\n* deux correctifs", text,
                "la majeure, puis chaque patch après un filet et sous son nom en H1");
            t.Equal("# Patch 1.1.1\n\n* un correctif", ReleaseNotes.Compose(null, patches.GetRange(0, 1)), "sans la majeure : le patch seul, sans filet en tête");
            t.Equal("* la majeure", ReleaseNotes.Compose("* la majeure", null), "sans patch : la majeure seule");
            t.Check(ReleaseNotes.Compose("", new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("1.1.1", " ") }) == null, "rien de rien : null");
        }
    }
}
