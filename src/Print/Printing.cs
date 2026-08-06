using System;
using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using UniversSale.Model;
using UniversSale.View;

namespace UniversSale.Print
{
    /// <summary>Phase 4a: real pagination. Builds a print-ready FlowDocument
    /// from the pivot (page size, margins, columns, hyphenation from the
    /// project's PageSetup, ink forced to black, footnotes appended as an end
    /// section), wraps its paginator to stamp centered page numbers, then
    /// either prints via the system dialog (PDF = « Microsoft Print to PDF »)
    /// or shows a page-by-page preview (in-memory XPS in a DocumentViewer).</summary>
    public static class Printing
    {
        private static int _previewCounter;

        // ------------------------------------------------------- document

        public static FlowDocument BuildFlow(TextDocument document, StyleSheet styles, Project project)
        {
            var flow = FlowConverter.ToFlow(document, styles, project);
            var setup = project.Page;
            flow.PageWidth = setup.PageWidthMm * PageSetup.PxPerMm;
            flow.PageHeight = setup.PageHeightMm * PageSetup.PxPerMm;
            flow.PagePadding = new Thickness(
                setup.MarginLeftMm * PageSetup.PxPerMm,
                setup.MarginTopMm * PageSetup.PxPerMm,
                setup.MarginRightMm * PageSetup.PxPerMm,
                setup.MarginBottomMm * PageSetup.PxPerMm);
            flow.IsHyphenationEnabled = setup.Hyphenation;
            flow.ColumnGap = 20;
            flow.ColumnWidth = setup.Columns > 1
                ? Math.Max(60, (setup.ContentWidthPx - (setup.Columns - 1) * 20) / setup.Columns)
                : double.PositiveInfinity;
            flow.IsOptimalParagraphEnabled = true;

            AppendFootnotes(flow, document, styles);
            ForcePrintInk(flow);
            return flow;
        }

        /// <summary>End-of-document notes section — only used by this WPF flow
        /// fallback. The composer path (normal route) puts each footnote at
        /// the bottom of the page carrying its marker.</summary>
        private static void AppendFootnotes(FlowDocument flow, TextDocument document, StyleSheet styles)
        {
            if (document.Footnotes.Count == 0) return;
            var rule = new Paragraph { Margin = new Thickness(0, 18, 0, 6) };
            rule.Inlines.Add(new Run("Notes")
            {
                FontWeight = FontWeights.Bold,
                FontSize = styles.Body.FontSize
            });
            flow.Blocks.Add(rule);
            for (var i = 0; i < document.Footnotes.Count; i++)
            {
                var note = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                note.Inlines.Add(new Run((i + 1) + ". " + document.Footnotes[i].Text)
                {
                    FontSize = Math.Max(8, styles.Body.FontSize * 0.85)
                });
                flow.Blocks.Add(note);
            }
        }

        /// <summary>The editor's "automatic" ink is the theme brush — white in
        /// dark mode. Paper is always white: swap those shared brushes for
        /// black, leaving explicit color overrides untouched.</summary>
        private static void ForcePrintInk(FlowDocument flow)
        {
            flow.Foreground = Brushes.Black;
            foreach (var block in flow.Blocks) ForceBlockInk(block);
        }

        private static void ForceBlockInk(Block block)
        {
            if (ReferenceEquals(block.Foreground, Chrome.Ink)) block.Foreground = Brushes.Black;
            var paragraph = block as Paragraph;
            if (paragraph != null) { ForceInlineInk(paragraph.Inlines); return; }
            var section = block as Section;
            if (section != null)
            {
                foreach (var inner in section.Blocks) ForceBlockInk(inner);
                return;
            }
            var list = block as List;
            if (list != null)
            {
                if (ReferenceEquals(list.Foreground, Chrome.Ink)) list.Foreground = Brushes.Black;
                foreach (ListItem item in list.ListItems)
                    foreach (var inner in item.Blocks) ForceBlockInk(inner);
            }
        }

        private static void ForceInlineInk(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                if (ReferenceEquals(inline.Foreground, Chrome.Ink)) inline.Foreground = Brushes.Black;
                var span = inline as Span;
                if (span != null) ForceInlineInk(span.Inlines);
            }
        }

        // ------------------------------------------------------- pagination

        public static DocumentPaginator Paginate(FlowDocument flow, PageSetup setup)
        {
            var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            paginator.PageSize = new Size(flow.PageWidth, flow.PageHeight);
            return setup.FooterPageNumbers ? new FooterPaginator(paginator, setup) : paginator;
        }

        /// <summary>Wraps the flow paginator to stamp the page number, centered
        /// in the bottom margin, footer font per the page setup.</summary>
        private sealed class FooterPaginator : DocumentPaginator
        {
            private readonly DocumentPaginator _inner;
            private readonly PageSetup _setup;

            public FooterPaginator(DocumentPaginator inner, PageSetup setup)
            {
                _inner = inner;
                _setup = setup;
            }

            public override bool IsPageCountValid { get { return _inner.IsPageCountValid; } }
            public override int PageCount { get { return _inner.PageCount; } }
            public override Size PageSize
            {
                get { return _inner.PageSize; }
                set { _inner.PageSize = value; }
            }
            public override IDocumentPaginatorSource Source { get { return _inner.Source; } }

            public override DocumentPage GetPage(int pageNumber)
            {
                var page = _inner.GetPage(pageNumber);
                if (page == DocumentPage.Missing) return page;

                var root = new ContainerVisual();
                root.Children.Add(page.Visual);

                var footer = new DrawingVisual();
                using (var dc = footer.RenderOpen())
                {
                    var text = new FormattedText(
                        (pageNumber + 1).ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(_setup.FooterFont ?? "Times New Roman"),
                        Math.Max(6, _setup.FooterSizePt * 4.0 / 3.0),
                        Brushes.Black,
                        1.0); // pixelsPerDip: print/XPS output is device-independent
                    var bottomMargin = _setup.MarginBottomMm * PageSetup.PxPerMm;
                    dc.DrawText(text, new Point(
                        (page.Size.Width - text.Width) / 2,
                        page.Size.Height - bottomMargin / 2 - text.Height / 2));
                }
                root.Children.Add(footer);
                return new DocumentPage(root, page.Size, page.BleedBox, page.ContentBox);
            }
        }

        // ------------------------------------------------------- composer output

        /// <summary>Pages composed by the 4b engine, rendered for print/XPS —
        /// the same strokes as the on-screen Composition mode. Falls back to
        /// the WPF flow paginator if composing ever fails.</summary>
        private sealed class ComposerPaginator : DocumentPaginator
        {
            private readonly Composition _composition;
            private Size _size;

            public ComposerPaginator(Composition composition)
            {
                _composition = composition;
                _size = new Size(
                    composition.Setup.PageWidthMm * PageSetup.PxPerMm,
                    composition.Setup.PageHeightMm * PageSetup.PxPerMm);
            }

            public override bool IsPageCountValid { get { return true; } }
            public override int PageCount { get { return _composition.Pages.Count; } }
            public override Size PageSize { get { return _size; } set { _size = value; } }
            public override IDocumentPaginatorSource Source { get { return null; } }

            public override DocumentPage GetPage(int pageNumber)
            {
                // Contract: out-of-range probes (ComputePageCount, viewers)
                // expect Missing, never an exception.
                if (pageNumber < 0 || pageNumber >= _composition.Pages.Count)
                    return DocumentPage.Missing;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, _size.Width, _size.Height));
                    UniversSale.View.ComposedRenderer.DrawPage(dc, _composition, pageNumber, false);
                }
                return new DocumentPage(visual, _size,
                    new Rect(_size), new Rect(_size));
            }
        }

        /// <summary>Composer first, WPF flow paginator as safety net.</summary>
        private static DocumentPaginator BuildPaginator(TextDocument document,
            StyleSheet styles, Project project)
        {
            try
            {
                return new ComposerPaginator(
                    Composer.Compose(document, styles, project.Page, project));
            }
            catch
            {
                var flow = BuildFlow(document, styles, project);
                return Paginate(flow, project.Page);
            }
        }

        // ------------------------------------------------------- print & preview

        public static void Print(TextDocument document, StyleSheet styles, Project project, string jobName)
        {
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() != true) return;
            dialog.PrintDocument(BuildPaginator(document, styles, project), jobName);
        }

        /// <summary>Page-by-page preview: the paginated document is written to
        /// an in-memory XPS package shown in a DocumentViewer (continuous page
        /// scroll, fit/zoom controls — PDF export goes through Imprimer with
        /// « Microsoft Print to PDF »).</summary>
        public static void ShowPreview(Window owner, TextDocument document, StyleSheet styles,
            Project project, string title)
        {
            var paginator = BuildPaginator(document, styles, project);

            var buffer = new MemoryStream();
            var package = Package.Open(buffer, FileMode.Create, FileAccess.ReadWrite);
            // PackageStore rejects the "pack" scheme itself; any other opaque
            // scheme works as the in-memory package's identity.
            var uri = new Uri("memorystream://preview" + (++_previewCounter) + ".xps");
            PackageStore.AddPackage(uri, package);
            var xps = new XpsDocument(package, CompressionOption.NotCompressed, uri.AbsoluteUri);
            XpsDocument.CreateXpsDocumentWriter(xps).Write(paginator);

            var viewer = new DocumentViewer { Document = xps.GetFixedDocumentSequence() };
            var window = new Window
            {
                Title = "Aperçu des pages — " + title,
                Owner = owner,
                Width = 900,
                Height = 760,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = viewer
            };
            window.Closed += delegate
            {
                try
                {
                    PackageStore.RemovePackage(uri);
                    xps.Close();
                    package.Close();
                    buffer.Dispose();
                }
                catch { }
            };
            window.Show();
        }
    }
}
