using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Ce que le rail Général montre d'une image sélectionnée
    /// (refonte des images, 0.50.0) : nom, taille affichée, pixels, format,
    /// poids — et ses octets pour « Enregistrer l'image… ».</summary>
    public sealed class SelectedImageInfo
    {
        public string Name;
        public double WidthPx, HeightPx; // taille affichée (px WPF)
        public int PixelWidth, PixelHeight;
        public string Extension;         // ".png"…
        public byte[] Bytes;
        public string Id;
    }

    /// <summary>EditorView, partie « images » (0.50.0) : l'onglet Image du
    /// ruban — insérer (grand carré), alignement horizontal et vertical,
    /// habillage, placement libre et grille — sa synchronisation avec
    /// l'image sélectionnée, et ce que le rail en montre.</summary>
    public partial class EditorView
    {
        private ToggleButton _imgAlignLeft, _imgAlignCenter, _imgAlignRight;
        private ToggleButton _imgTop, _imgMiddle, _imgBottom;
        private ToggleButton _imgExclude, _imgWrap;
        private ToggleButton _imgFree, _imgGrid;

        /// <summary>L'image sélectionnée dans la surface a changé (le rail
        /// Général remplace son contenu par les propriétés de l'image).</summary>
        public event Action ImageSelectionChanged;

        /// <summary>Onglet « Image », entre Texte et Insertion : insérer en
        /// grand carré ; alignement (gauche/centre/droite en haut, haut/
        /// centre/bas de la zone de texte en bas) ; positionnement (texte
        /// au-dessus et en dessous, ou de part et d'autre) ; placement libre
        /// et grille de placement, bascules à texte superposées.</summary>
        private UIElement BuildImageTab()
        {
            var panel = TabPanel();
            var insert = BigSquare("image-square-bold", "Insérer une image",
                "Insère une image du disque (PNG, JPEG, GIF, BMP) au curseur — réduite à la "
                + "colonne si elle dépasse ; poignées pour la redimensionner (Maj : proportions libres), "
                + "glisser pour la déplacer");
            insert.Click += delegate { InsertImage(); };
            panel.Children.Add(insert);
            panel.Children.Add(VerticalRuleTall());

            // Alignement : deux rangées de trois bascules.
            var alignRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var alignTop = RibbonRow();
            var alignBottom = RibbonRow();
            alignRows.Children.Add(alignTop);
            alignRows.Children.Add(alignBottom);
            _imgAlignLeft = IconToggle("align-left", "Image à gauche de la zone de texte");
            _imgAlignCenter = IconToggle("align-center", "Image centrée");
            _imgAlignRight = IconToggle("align-left", "Image à droite de la zone de texte");
            // Pas d'icône « droite » dans le jeu : la gauche, en miroir.
            var rightIcon = _imgAlignRight.Content as FrameworkElement;
            if (rightIcon != null) rightIcon.LayoutTransform = new ScaleTransform(-1, 1);
            _imgAlignLeft.Click += delegate { AlignImage("left", null, _imgAlignLeft); };
            _imgAlignCenter.Click += delegate { AlignImage("center", null, _imgAlignCenter); };
            _imgAlignRight.Click += delegate { AlignImage("right", null, _imgAlignRight); };
            alignTop.Children.Add(_imgAlignLeft);
            alignTop.Children.Add(_imgAlignCenter);
            alignTop.Children.Add(_imgAlignRight);
            _imgTop = IconToggle("vertical-top", "Image en haut de la page (collée à la zone de texte)");
            _imgMiddle = IconToggle("vertical-center", "Image au centre de la page");
            _imgBottom = IconToggle("vertical-bottom", "Image en bas de la page (collée à la zone de texte)");
            _imgTop.Click += delegate { AlignImage(null, "top", _imgTop); };
            _imgMiddle.Click += delegate { AlignImage(null, "center", _imgMiddle); };
            _imgBottom.Click += delegate { AlignImage(null, "bottom", _imgBottom); };
            alignBottom.Children.Add(_imgTop);
            alignBottom.Children.Add(_imgMiddle);
            alignBottom.Children.Add(_imgBottom);
            panel.Children.Add(alignRows);
            panel.Children.Add(VerticalRuleTall());

            // Positionnement : l'habillage, deux bascules exclusives.
            _imgExclude = OneLineToggle("image-exclude", "Texte au-dessus et en dessous",
                "Le texte s'écarte de l'image : au-dessus et en dessous, jamais à côté");
            _imgWrap = OneLineToggle("image-wrap", "Texte de part et d'autre",
                "Le texte coule de part et d'autre de l'image, dans les limites de la zone de texte");
            _imgExclude.Click += delegate
            {
                if (ComposedActive) _composed.SetSelectedImageWrap(ImageLayout.WrapExclude);
                SyncImageTab();
            };
            _imgWrap.Click += delegate
            {
                if (ComposedActive) _composed.SetSelectedImageWrap(ImageLayout.WrapAround);
                SyncImageTab();
            };
            panel.Children.Add(Stacked(_imgExclude, _imgWrap));
            panel.Children.Add(VerticalRuleTall());

            // Placement libre et grille de placement.
            _imgFree = OneLineToggle("free-placement", "Placement libre",
                "L'image peut sortir de la zone de texte, jusqu'aux bords de la page");
            _imgGrid = OneLineToggle("grid", "Grille de placement",
                "Une grille de 0,5 cm sur la page de l'image, qui aimante ses déplacements ; "
                + "les repères du centre de la page en couleur");
            _imgFree.Click += delegate
            {
                if (ComposedActive) _composed.SetSelectedImageFree(_imgFree.IsChecked == true);
                SyncImageTab();
            };
            _imgGrid.IsChecked = Settings.AppSettings.ImageGrid;
            _imgGrid.Click += delegate
            {
                if (ComposedActive) _composed.SetImageGrid(_imgGrid.IsChecked == true);
                else Settings.AppSettings.ImageGrid = _imgGrid.IsChecked == true;
            };
            panel.Children.Add(Stacked(_imgFree, _imgGrid));
            SyncImageTab();
            return panel;
        }

        private void AlignImage(string horizontal, string vertical, ToggleButton source)
        {
            if (ComposedActive) _composed.AlignSelectedImage(horizontal, vertical);
            SyncImageTab();
        }

        /// <summary>Les boutons de l'onglet Image suivent l'image sélectionnée :
        /// inertes sans image, cochés selon l'alignement qu'elle traduit,
        /// son habillage et son placement.</summary>
        private void SyncImageTab()
        {
            if (_imgAlignLeft == null) return;
            string horizontal = null, vertical = null, wrap = null;
            var free = false;
            var selected = ComposedActive && _item != null
                && _composed.SelectedImageState(out horizontal, out vertical, out wrap, out free);
            foreach (var button in new[] { _imgAlignLeft, _imgAlignCenter, _imgAlignRight, _imgTop, _imgMiddle, _imgBottom, _imgExclude, _imgWrap, _imgFree })
                button.IsEnabled = selected;
            _imgAlignLeft.IsChecked = selected && horizontal == "left";
            _imgAlignCenter.IsChecked = selected && horizontal == "center";
            _imgAlignRight.IsChecked = selected && horizontal == "right";
            _imgTop.IsChecked = selected && vertical == "top";
            _imgMiddle.IsChecked = selected && vertical == "center";
            _imgBottom.IsChecked = selected && vertical == "bottom";
            _imgExclude.IsChecked = selected && wrap != ImageLayout.WrapAround;
            _imgWrap.IsChecked = selected && wrap == ImageLayout.WrapAround;
            _imgFree.IsChecked = selected && free;
            _imgGrid.IsChecked = Settings.AppSettings.ImageGrid;
        }

        /// <summary>Les propriétés de l'image sélectionnée pour le rail, ou
        /// null sans image sélectionnée.</summary>
        public SelectedImageInfo SelectedImageInfo()
        {
            if (!ComposedActive || _item == null) return null;
            int pageIndex;
            var placed = _composed.SelectedPlacedImage(out pageIndex);
            var run = _composed.SelectedImage;
            if (placed == null || run == null) return null;
            var stored = _project == null ? null : _project.FindImage(run.ImageId);
            var info = new SelectedImageInfo
            {
                Name = run.Image == null ? null : run.Image.Name,
                WidthPx = placed.Rect.Width,
                HeightPx = placed.Rect.Height,
                Extension = stored == null ? null : stored.Extension,
                Bytes = stored == null ? null : stored.Bytes,
                Id = run.ImageId
            };
            var bitmap = placed.Source as System.Windows.Media.Imaging.BitmapSource;
            if (stored != null)
            {
                info.PixelWidth = ImageCache.PixelWidthOf(stored.Bytes);
                if (info.PixelWidth <= 0 && bitmap != null) info.PixelWidth = bitmap.PixelWidth;
                if (bitmap != null && bitmap.PixelWidth > 0)
                    info.PixelHeight = (int)Math.Round((double)info.PixelWidth * bitmap.PixelHeight / bitmap.PixelWidth);
            }
            return info;
        }

        /// <summary>Renomme l'image sélectionnée (le champ du rail).</summary>
        public void RenameSelectedImage(string name)
        {
            if (ComposedActive) _composed.RenameSelectedImage(name);
        }
    }
}
