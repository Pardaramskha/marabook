using System;
using System.Windows;
using System.Windows.Markup;

namespace Marabook.View
{
    /// <summary>The interface theme: modern templates (rounded corners, neutral
    /// palette, indigo accent) applied application-wide. Purely visual — no
    /// control behavior is modified. Ported from Mental-o's Theme.cs, extended
    /// with TreeView/TreeViewItem (for the Binder) and GridSplitter templates.
    /// The palette will become user-customizable; both palettes live here.</summary>
    public static class Theme
    {
        private static ResourceDictionary _current;

        public static void Apply(Application application)
        {
            Switch(application, Settings.AppSettings.DarkTheme);
        }

        /// <summary>(Re)builds the style dictionary for the requested theme and
        /// swaps it in — existing controls recolor themselves.</summary>
        public static void Switch(Application application, bool dark)
        {
            try
            {
                var next = (ResourceDictionary)XamlReader.Parse(
                    Header + Palette() + Templates);
                if (_current != null) application.Resources.MergedDictionaries.Remove(_current);
                application.Resources.MergedDictionaries.Add(next);
                _current = next;
            }
            catch
            {
                // If a template misparses, the application stays usable in classic style.
            }
        }

        /// <summary>Parse à blanc d'une palette (batch 34) : null si le XAML
        /// passe, sinon le message — Switch avale les erreurs, le test C12 non.</summary>
        public static string SelfCheck(bool dark)
        {
            return SelfCheck(dark, null);
        }

        public static string SelfCheck(bool dark, string accent)
        {
            var saved = Settings.AppSettings.AccentColor;
            try
            {
                Settings.AppSettings.AccentColor = accent;
                Chrome.Toggle(dark);
                var dictionary = XamlReader.Parse(Header + Palette() + Templates) as ResourceDictionary;
                return dictionary == null ? "le dictionnaire de ressources est nul" : null;
            }
            catch (Exception error)
            {
                return error.Message;
            }
            finally
            {
                Settings.AppSettings.AccentColor = saved;
                Chrome.Toggle(Settings.AppSettings.DarkTheme);
            }
        }

        /// <summary>Le garde-fou contre la bouillie (batch 40) : dans un
        /// thème, les quatre surfaces sont ordonnées (ground < chrome <
        /// raised ≤ paper — le papier sombre partage la marche raised) et
        /// séparées d'au moins huit unités de clarté, et l'encre garde un
        /// contraste suffisant sur chacune. Null si tout va, sinon la faute.</summary>
        public static string SurfaceCheck(bool dark)
        {
            var saved = Settings.AppSettings.AccentColor;
            try
            {
                Settings.AppSettings.AccentColor = null;
                Chrome.Toggle(dark);
                var ground = Chrome.Luma(Chrome.WindowBg.Color);
                var chrome = Chrome.Luma(Chrome.BarBg.Color);
                var raised = Chrome.Luma(Chrome.BarBgLight.Color);
                var paper = Chrome.Luma(Chrome.PaperBg.Color);
                // Dans les deux thèmes les surfaces s'ÉCLAIRCISSENT du plus
                // enfoncé au plus haut ; seules les encres s'inversent.
                var sign = dark ? -1 : 1;
                const double step = 8;
                if (chrome - ground < step)
                    return "chrome et ground ne sont pas séparés (" + Chrome.Hex(Chrome.BarBg.Color) + " / " + Chrome.Hex(Chrome.WindowBg.Color) + ")";
                if (raised - chrome < step)
                    return "raised et chrome ne sont pas séparés (" + Chrome.Hex(Chrome.BarBgLight.Color) + " / " + Chrome.Hex(Chrome.BarBg.Color) + ")";
                if (paper - raised < 0)
                    return "le papier est plus enfoncé que raised";
                if (!dark && paper - raised < step)
                    return "papier et raised ne sont pas séparés (" + Chrome.Hex(Chrome.PaperBg.Color) + " / " + Chrome.Hex(Chrome.BarBgLight.Color) + ")";
                if (Chrome.RaisedBg.Color != Chrome.BarBgLight.Color || Chrome.CardBg.Color != Chrome.BarBgLight.Color)
                    return "RaisedBg, CardBg et BarBgLight divergent (une seule marche raised)";
                var ink = Chrome.Luma(Chrome.Ink.Color);
                var paperInk = Chrome.Luma(Chrome.PaperInk.Color);
                foreach (var surface in new[] { ground, chrome, raised })
                    if (Math.Abs(ink - surface) < 120) return "l'encre manque de contraste sur une surface (" + Math.Abs(ink - surface) + ")";
                if (Math.Abs(paperInk - paper) < 120) return "l'encre du papier manque de contraste";
                var soft = Chrome.Luma(Chrome.SoftText.Color);
                var faint = Chrome.Luma(Chrome.FaintText.Color);
                if (sign * (soft - ink) <= 0 || sign * (faint - soft) <= 0)
                    return "les trois encres ne sont pas ordonnées (ink, ink-soft, ink-faint)";
                if (Math.Abs(faint - raised) < 60) return "ink-faint manque de contraste sur raised";
                return null;
            }
            finally
            {
                Settings.AppSettings.AccentColor = saved;
                Chrome.Toggle(Settings.AppSettings.DarkTheme);
            }
        }

        /// <summary>La palette XAML, ENGENDRÉE depuis les brosses de Chrome
        /// (une seule déclaration de chaque couleur dans le dépôt). Les clés
        /// historiques des gabarits sont conservées : Paper et Veil = raised
        /// (surface des contrôles et des dialogues), Border = line-strong,
        /// Line = line, Hover = accent-tint, Press = accent-soft, Thumb =
        /// line-strong. Chrome.Toggle doit avoir tourné AVANT.</summary>
        private static string Palette()
        {
            var b = new System.Text.StringBuilder();
            Key(b, "Ink", Chrome.Ink);
            Key(b, "InkSoft", Chrome.SoftText);
            Key(b, "InkFaint", Chrome.FaintText);
            Key(b, "Paper", Chrome.RaisedBg);
            Key(b, "Veil", Chrome.RaisedBg);
            Key(b, "Border", Chrome.BorderStrong);
            Key(b, "Line", Chrome.Border);
            Key(b, "Hover", Chrome.AccentTint);
            Key(b, "Press", Chrome.AccentSoft);
            Key(b, "Accent", Chrome.Accent);
            Key(b, "AccentTint", Chrome.AccentTint);
            Key(b, "AccentSoft", Chrome.AccentSoft);
            Key(b, "AccentStrong", Chrome.AccentStrong);
            Key(b, "Thumb", Chrome.BorderStrong);
            Key(b, "Ok", Chrome.Ok);
            Key(b, "Warn", Chrome.Warn);
            Key(b, "Danger", Chrome.Danger);
            return b.ToString();
        }

        private static void Key(System.Text.StringBuilder b, string key, System.Windows.Media.SolidColorBrush brush)
        {
            b.Append("  <SolidColorBrush x:Key=\"").Append(key).Append("\" Color=\"")
             .Append(Chrome.Hex(brush.Color)).Append("\"/>\n");
        }

        private const string Header = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
";

        private const string Templates = @"
  <!-- Dialog windows follow the theme; the main window is handled separately. -->
  <Style TargetType=""Window"">
    <Setter Property=""Background"" Value=""{StaticResource Veil}""/>
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
  </Style>

  <!-- ============================== Buttons ============================== -->
  <Style TargetType=""Button"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Padding"" Value=""7,3""/>
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Button"">
          <Border x:Name=""Bg"" CornerRadius=""6"" Background=""{StaticResource Paper}""
                  BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource InkSoft}""/>
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Press}""/>
            </Trigger>
            <Trigger Property=""IsDefault"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.45""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""ToggleButton"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Padding"" Value=""7,3""/>
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ToggleButton"">
          <!-- Au repos : papier + bordure fine (batch 34 — un bouton se
               distingue d'un simple texte) ; enfoncé : accent doux. -->
          <Border x:Name=""Bg"" CornerRadius=""6"" Background=""{StaticResource Paper}""
                  BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
            </Trigger>
            <!-- Bascule active (batch 40) : accent-tint, bordure accent-soft,
                 texte accent-strong — l'accent plein reste à l'action. -->
            <Trigger Property=""IsChecked"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource AccentSoft}""/>
              <Setter Property=""Foreground"" Value=""{StaticResource AccentStrong}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.45""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Les trois apparences de Buttons.cs (batch 40). Le style implicite
       ci-dessus est le CONTOUR (raised + line-strong) ; calme = transparent
       au repos, accent-tint au survol, accent-soft enfoncé ; principal =
       accent plein, accent-strong enfoncé ; la bascule calme cochée est
       l'état ACTIF (accent-tint, bordure accent-soft, texte accent-strong). -->
  <Style x:Key=""CalmButton"" TargetType=""Button"" BasedOn=""{StaticResource {x:Type Button}}"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Button"">
          <Border x:Name=""Bg"" CornerRadius=""6"" Background=""Transparent""
                  BorderBrush=""Transparent"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentSoft}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.45""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key=""PrimaryButton"" TargetType=""Button"" BasedOn=""{StaticResource {x:Type Button}}"">
    <Setter Property=""Foreground"" Value=""{StaticResource Paper}""/>
    <Setter Property=""FontWeight"" Value=""SemiBold""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Button"">
          <Border x:Name=""Bg"" CornerRadius=""6"" Background=""{StaticResource Accent}""
                  BorderBrush=""{StaticResource Accent}"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource AccentStrong}""/>
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentStrong}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.45""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key=""CalmToggle"" TargetType=""ToggleButton"" BasedOn=""{StaticResource {x:Type ToggleButton}}"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ToggleButton"">
          <Border x:Name=""Bg"" CornerRadius=""6"" Background=""Transparent""
                  BorderBrush=""Transparent"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
            </Trigger>
            <Trigger Property=""IsChecked"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource AccentSoft}""/>
              <Setter Property=""Foreground"" Value=""{StaticResource AccentStrong}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.45""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Text boxes ============================== -->
  <Style x:Key=""BareText"" TargetType=""TextBox"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""CaretBrush"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""BorderThickness"" Value=""0""/>
    <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TextBox"">
          <ScrollViewer x:Name=""PART_ContentHost""/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- The writing surface: no chrome at all, top-aligned scrolling content. -->
  <Style x:Key=""EditorText"" TargetType=""TextBox"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""CaretBrush"" Value=""{StaticResource Ink}""/>
    <Setter Property=""SelectionBrush"" Value=""{StaticResource Accent}""/>
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""BorderThickness"" Value=""0""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TextBox"">
          <ScrollViewer x:Name=""PART_ContentHost""/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""TextBox"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""CaretBrush"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Padding"" Value=""5,2""/>
    <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TextBox"">
          <Border x:Name=""Bg"" CornerRadius=""6"" Background=""{StaticResource Paper}""
                  BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <!-- Étiré, jamais aligné (17/09) : c'est le TextBoxView qui applique
                 VerticalContentAlignment. Un ScrollViewer aligné en haut ne
                 couvrait que les lignes écrites — flèche et clic mort sur le
                 reste de la zone (synopsis, 4e de couverture, notes…). -->
            <ScrollViewer x:Name=""PART_ContentHost""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource InkSoft}""/>
            </Trigger>
            <Trigger Property=""IsKeyboardFocusWithin"" Value=""True"">
              <!-- Accent seul : l'épaisseur constante évite tout décalage de
                   mise en page au focus. -->
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.45""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <!-- Zones multilignes (batch 34) : le curseur en HAUT, toute la zone
           est vivante — plus de caret centré dans un cadre de 90 px. -->
      <Trigger Property=""AcceptsReturn"" Value=""True"">
        <Setter Property=""VerticalContentAlignment"" Value=""Top""/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- ============================== Check boxes ============================== -->
  <Style TargetType=""CheckBox"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""CheckBox"">
          <StackPanel Orientation=""Horizontal"" Background=""Transparent"">
            <Border x:Name=""Box"" Width=""16"" Height=""16"" CornerRadius=""4""
                    Background=""{StaticResource Paper}"" BorderBrush=""{StaticResource Border}""
                    BorderThickness=""1"" VerticalAlignment=""Center"">
              <Path x:Name=""Check"" Data=""M3,8 L7,12 13,4"" Stroke=""White"" StrokeThickness=""2""
                    Visibility=""Collapsed"" Stretch=""Uniform"" Margin=""3""/>
            </Border>
            <ContentPresenter Margin=""7,0,0,0"" VerticalAlignment=""Center""/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsChecked"" Value=""True"">
              <Setter TargetName=""Box"" Property=""Background"" Value=""{StaticResource Accent}""/>
              <Setter TargetName=""Box"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
              <Setter TargetName=""Check"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Box"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Combo boxes ============================== -->
  <Style TargetType=""ComboBoxItem"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Padding"" Value=""7,3""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ComboBoxItem"">
          <Border x:Name=""Bg"" CornerRadius=""4"" Margin=""3,1"" Padding=""{TemplateBinding Padding}""
                  Background=""Transparent"">
            <ContentPresenter VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsHighlighted"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
              <Setter Property=""Foreground"" Value=""{StaticResource AccentStrong}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""ComboBox"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ComboBox"">
          <Grid>
            <ToggleButton Focusable=""False"" ClickMode=""Press""
                          IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"">
              <ToggleButton.Template>
                <ControlTemplate TargetType=""ToggleButton"">
                  <Border x:Name=""Bg"" CornerRadius=""6"" Background=""{StaticResource Paper}""
                          BorderBrush=""{StaticResource Border}"" BorderThickness=""1"">
                    <Path Data=""M0,0 L4,4 8,0"" Stroke=""{StaticResource InkSoft}"" StrokeThickness=""1.6""
                          HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,1,8,0""/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                      <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource InkSoft}""/>
                    </Trigger>
                    <Trigger Property=""IsChecked"" Value=""True"">
                      <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter x:Name=""Choice"" Margin=""8,2,22,2"" VerticalAlignment=""Center""
                              HorizontalAlignment=""Left""
                              Content=""{TemplateBinding SelectionBoxItem}""
                              ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}""
                              IsHitTestVisible=""False""/>
            <TextBox x:Name=""PART_EditableTextBox"" Style=""{StaticResource BareText}""
                     Margin=""8,1,22,1"" Visibility=""Collapsed""
                     IsReadOnly=""{TemplateBinding IsReadOnly}""/>
            <Popup IsOpen=""{TemplateBinding IsDropDownOpen}"" Placement=""Bottom""
                   AllowsTransparency=""True"" PopupAnimation=""Fade"">
              <Border CornerRadius=""6"" Background=""{StaticResource Paper}""
                      BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                      Margin=""6,2,6,6"" MinWidth=""{TemplateBinding ActualWidth}""
                      MaxHeight=""{TemplateBinding MaxDropDownHeight}"">
                <Border.Effect>
                  <DropShadowEffect Color=""Black"" Opacity=""0.22"" BlurRadius=""8"" ShadowDepth=""2""/>
                </Border.Effect>
                <ScrollViewer Padding=""0,3"">
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsEditable"" Value=""True"">
              <Setter TargetName=""Choice"" Property=""Visibility"" Value=""Collapsed""/>
              <Setter TargetName=""PART_EditableTextBox"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.45""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Menus ============================== -->
  <Style TargetType=""Menu"">
    <Setter Property=""Background"" Value=""{StaticResource Veil}""/>
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Padding"" Value=""3,2""/>
  </Style>

  <Style TargetType=""ContextMenu"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ContextMenu"">
          <Border CornerRadius=""7"" Background=""{StaticResource Paper}""
                  BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                  Margin=""6"" Padding=""0,4"">
            <Border.Effect>
              <DropShadowEffect Color=""Black"" Opacity=""0.25"" BlurRadius=""10"" ShadowDepth=""2""/>
            </Border.Effect>
            <ItemsPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <ControlTemplate x:Key=""MenuTop"" TargetType=""MenuItem"">
    <Border x:Name=""Bg"" CornerRadius=""6"" Padding=""9,4"" Background=""Transparent"">
      <ContentPresenter ContentSource=""Header"" RecognizesAccessKey=""True"" VerticalAlignment=""Center""/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property=""IsHighlighted"" Value=""True"">
        <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <ControlTemplate x:Key=""MenuTopParent"" TargetType=""MenuItem"">
    <Grid>
      <Border x:Name=""Bg"" CornerRadius=""6"" Padding=""9,4"" Background=""Transparent"">
        <ContentPresenter ContentSource=""Header"" RecognizesAccessKey=""True"" VerticalAlignment=""Center""/>
      </Border>
      <Popup IsOpen=""{TemplateBinding IsSubmenuOpen}"" Placement=""Bottom""
             AllowsTransparency=""True"" PopupAnimation=""Fade"" Focusable=""False"">
        <Border CornerRadius=""7"" Background=""{StaticResource Paper}""
                BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                Margin=""6"" Padding=""0,4"" MinWidth=""170"">
          <Border.Effect>
            <DropShadowEffect Color=""Black"" Opacity=""0.25"" BlurRadius=""10"" ShadowDepth=""2""/>
          </Border.Effect>
          <ScrollViewer>
            <ItemsPresenter/>
          </ScrollViewer>
        </Border>
      </Popup>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property=""IsHighlighted"" Value=""True"">
        <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
      </Trigger>
      <Trigger Property=""IsSubmenuOpen"" Value=""True"">
        <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Press}""/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <ControlTemplate x:Key=""MenuEntry"" TargetType=""MenuItem"">
    <Border x:Name=""Bg"" CornerRadius=""4"" Margin=""4,1"" Padding=""6,4"" Background=""Transparent"">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width=""20""/>
          <ColumnDefinition Width=""*""/>
          <ColumnDefinition Width=""Auto""/>
        </Grid.ColumnDefinitions>
        <ContentPresenter Grid.Column=""0"" ContentSource=""Icon"" VerticalAlignment=""Center""/>
        <Path x:Name=""Check"" Grid.Column=""0"" Data=""M1,5 L4,8 9,1"" Stroke=""{StaticResource Accent}""
              StrokeThickness=""2"" Visibility=""Collapsed"" VerticalAlignment=""Center"" HorizontalAlignment=""Center""/>
        <ContentPresenter Grid.Column=""1"" ContentSource=""Header"" RecognizesAccessKey=""True""
                          VerticalAlignment=""Center"" Margin=""4,0,0,0""/>
        <TextBlock Grid.Column=""2"" Text=""{TemplateBinding InputGestureText}""
                   Foreground=""{StaticResource InkSoft}"" Margin=""20,0,6,0"" VerticalAlignment=""Center""/>
      </Grid>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property=""IsChecked"" Value=""True"">
        <Setter TargetName=""Check"" Property=""Visibility"" Value=""Visible""/>
      </Trigger>
      <Trigger Property=""IsHighlighted"" Value=""True"">
        <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
      </Trigger>
      <Trigger Property=""IsEnabled"" Value=""False"">
        <Setter Property=""Opacity"" Value=""0.45""/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <ControlTemplate x:Key=""MenuEntryParent"" TargetType=""MenuItem"">
    <Grid>
      <Border x:Name=""Bg"" CornerRadius=""4"" Margin=""4,1"" Padding=""6,4"" Background=""Transparent"">
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width=""20""/>
            <ColumnDefinition Width=""*""/>
            <ColumnDefinition Width=""Auto""/>
          </Grid.ColumnDefinitions>
          <ContentPresenter Grid.Column=""0"" ContentSource=""Icon"" VerticalAlignment=""Center""/>
          <ContentPresenter Grid.Column=""1"" ContentSource=""Header"" RecognizesAccessKey=""True""
                            VerticalAlignment=""Center"" Margin=""4,0,0,0""/>
          <Path Grid.Column=""2"" Data=""M0,0 L4,4 0,8"" Stroke=""{StaticResource InkSoft}""
                StrokeThickness=""1.6"" Margin=""14,0,4,0"" VerticalAlignment=""Center""/>
        </Grid>
      </Border>
      <Popup IsOpen=""{TemplateBinding IsSubmenuOpen}"" Placement=""Right""
             AllowsTransparency=""True"" PopupAnimation=""Fade"" Focusable=""False""
             HorizontalOffset=""-4"" VerticalOffset=""-6"">
        <Border CornerRadius=""7"" Background=""{StaticResource Paper}""
                BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                Margin=""6"" Padding=""0,4"" MinWidth=""150"">
          <Border.Effect>
            <DropShadowEffect Color=""Black"" Opacity=""0.25"" BlurRadius=""10"" ShadowDepth=""2""/>
          </Border.Effect>
          <ScrollViewer>
            <ItemsPresenter/>
          </ScrollViewer>
        </Border>
      </Popup>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property=""IsHighlighted"" Value=""True"">
        <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
      </Trigger>
      <Trigger Property=""IsEnabled"" Value=""False"">
        <Setter Property=""Opacity"" Value=""0.45""/>
      </Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <Style TargetType=""MenuItem"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Style.Triggers>
      <Trigger Property=""Role"" Value=""TopLevelHeader"">
        <Setter Property=""Template"" Value=""{StaticResource MenuTopParent}""/>
      </Trigger>
      <Trigger Property=""Role"" Value=""TopLevelItem"">
        <Setter Property=""Template"" Value=""{StaticResource MenuTop}""/>
      </Trigger>
      <Trigger Property=""Role"" Value=""SubmenuHeader"">
        <Setter Property=""Template"" Value=""{StaticResource MenuEntryParent}""/>
      </Trigger>
      <Trigger Property=""Role"" Value=""SubmenuItem"">
        <Setter Property=""Template"" Value=""{StaticResource MenuEntry}""/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType=""Separator"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Separator"">
          <Border Height=""1"" Background=""{StaticResource Line}"" Margin=""8,4""/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Tabs and lists ============================== -->
  <Style TargetType=""TabControl"">
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""BorderThickness"" Value=""0""/>
    <Setter Property=""Padding"" Value=""4""/>
  </Style>

  <!-- Les onglets du RUBAN (17/09) : la rangée des chips partage sa ligne
       avec l'axe d'affichage (Pages/Brouillon/Calme, passé dans Tag), qui
       se cale à droite ; le contenu de l'onglet, dessous, prend TOUTE la
       largeur — plus de réserve de 236 px qui rognait les sections de
       droite, plus rien qui passe sous le sélecteur. Fenêtre étroite : les
       chips se replient sur deux rangées, le sélecteur reste en haut à
       droite. -->
  <Style x:Key=""RibbonTabs"" TargetType=""TabControl"">
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""BorderThickness"" Value=""0""/>
    <Setter Property=""Padding"" Value=""0""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TabControl"">
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height=""Auto""/>
              <RowDefinition Height=""*""/>
            </Grid.RowDefinitions>
            <Grid Grid.Row=""0"">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""Auto""/>
              </Grid.ColumnDefinitions>
              <!-- WrapPanel et non TabPanel : sur deux rangées, le TabPanel
                   étire les chips sur toute la largeur. -->
              <WrapPanel Grid.Column=""0"" IsItemsHost=""True"" KeyboardNavigation.TabIndex=""1""/>
              <ContentPresenter Grid.Column=""1"" Content=""{TemplateBinding Tag}""
                                VerticalAlignment=""Top""/>
            </Grid>
            <ContentPresenter Grid.Row=""1"" x:Name=""PART_SelectedContentHost""
                              ContentSource=""SelectedContent""
                              Margin=""{TemplateBinding Padding}""/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Onglets en CHIP (batch 34) : l'actif est une pastille arrondie à la
       couleur d'accent, texte papier ; les autres sont nus, survol grisé. -->
  <!-- Curseur, encre et graisse sont posés sur l'EN-TÊTE (Bg), jamais sur
       le TabItem : son contenu en hériterait (« Texte libre » en gras,
       curseur main sur les papers — b42). -->
  <Style TargetType=""TabItem"">
    <Setter Property=""Foreground"" Value=""{StaticResource InkSoft}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TabItem"">
          <Border x:Name=""Bg"" Background=""Transparent"" Padding=""12,4"" Margin=""3,4,0,4""
                  CornerRadius=""12"" BorderThickness=""1"" BorderBrush=""Transparent"" Cursor=""Hand"">
            <ContentPresenter ContentSource=""Header"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Accent}""/>
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
              <Setter TargetName=""Bg"" Property=""TextElement.Foreground"" Value=""{StaticResource Paper}""/>
              <Setter TargetName=""Bg"" Property=""TextElement.FontWeight"" Value=""SemiBold""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Slider ============================== -->
  <Style TargetType=""Slider"">
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Slider"">
          <Grid MinHeight=""18"" VerticalAlignment=""Center"">
            <Border Height=""4"" CornerRadius=""2"" Margin=""7,0"" VerticalAlignment=""Center""
                    Background=""{StaticResource Border}""/>
            <Track x:Name=""PART_Track"">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command=""{x:Static Slider.DecreaseLarge}"" Focusable=""False"">
                  <RepeatButton.Template>
                    <ControlTemplate TargetType=""RepeatButton"">
                      <Border Background=""Transparent"" Height=""18""/>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command=""{x:Static Slider.IncreaseLarge}"" Focusable=""False"">
                  <RepeatButton.Template>
                    <ControlTemplate TargetType=""RepeatButton"">
                      <Border Background=""Transparent"" Height=""18""/>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Width=""14"" Height=""14"" Focusable=""False"">
                  <Thumb.Template>
                    <ControlTemplate TargetType=""Thumb"">
                      <Ellipse Fill=""{StaticResource Accent}"" Stroke=""{StaticResource Paper}"" StrokeThickness=""2""/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""ListBox"">
    <Setter Property=""Background"" Value=""{StaticResource Paper}""/>
    <Setter Property=""BorderBrush"" Value=""{StaticResource Border}""/>
    <Setter Property=""BorderThickness"" Value=""1""/>
    <Setter Property=""Padding"" Value=""2""/>
  </Style>

  <Style TargetType=""ListBoxItem"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ListBoxItem"">
          <Border x:Name=""Bg"" CornerRadius=""4"" Margin=""1"" Padding=""6,3"" Background=""Transparent"">
            <ContentPresenter VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
              <Setter Property=""Foreground"" Value=""{StaticResource AccentStrong}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Binder tree ============================== -->
  <Style TargetType=""TreeView"">
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""BorderThickness"" Value=""0""/>
    <Setter Property=""Padding"" Value=""4""/>
  </Style>

  <Style TargetType=""TreeViewItem"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TreeViewItem"">
          <StackPanel>
            <!-- Ligne sélectionnée (batch 40) : accent-tint, liseré d'accent
                 à gauche, encre normale — l'aplat saturé est rendu à l'action. -->
            <Border x:Name=""Bg"" CornerRadius=""6"" Padding=""2,3"" Background=""Transparent""
                    BorderThickness=""3,0,0,0"" BorderBrush=""Transparent"">
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width=""16""/>
                  <ColumnDefinition Width=""*""/>
                </Grid.ColumnDefinitions>
                <ToggleButton x:Name=""Expander"" ClickMode=""Press""
                              IsChecked=""{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}}"">
                  <ToggleButton.Template>
                    <ControlTemplate TargetType=""ToggleButton"">
                      <Border Background=""Transparent"" Width=""16"" Height=""16"">
                        <Path x:Name=""Chevron"" Data=""M0,0 L4,4 0,8"" Stroke=""{StaticResource InkSoft}""
                              StrokeThickness=""1.6"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                      </Border>
                      <ControlTemplate.Triggers>
                        <Trigger Property=""IsChecked"" Value=""True"">
                          <Setter TargetName=""Chevron"" Property=""Data"" Value=""M0,0 L4,4 8,0""/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Grid.Column=""1"" ContentSource=""Header"" VerticalAlignment=""Center""/>
              </Grid>
            </Border>
            <ItemsPresenter x:Name=""Children"" Margin=""14,0,0,0""/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsExpanded"" Value=""False"">
              <Setter TargetName=""Children"" Property=""Visibility"" Value=""Collapsed""/>
            </Trigger>
            <Trigger Property=""HasItems"" Value=""False"">
              <Setter TargetName=""Expander"" Property=""Visibility"" Value=""Hidden""/>
            </Trigger>
            <Trigger SourceName=""Bg"" Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentTint}""/>
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Splitters ============================== -->
  <Style TargetType=""GridSplitter"">
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""GridSplitter"">
          <Border x:Name=""Bg"" Background=""Transparent"">
            <Border Width=""1"" Background=""{StaticResource Line}"" HorizontalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================== Scroll bars ============================== -->
  <Style TargetType=""ScrollBar"">
    <Setter Property=""Width"" Value=""10""/>
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ScrollBar"">
          <Grid Background=""Transparent"">
            <Track x:Name=""PART_Track"" IsDirectionReversed=""True"">
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType=""Thumb"">
                      <Border CornerRadius=""4"" Background=""{StaticResource Thumb}"" Margin=""2""/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property=""Orientation"" Value=""Horizontal"">
        <Setter Property=""Width"" Value=""Auto""/>
        <Setter Property=""Height"" Value=""10""/>
        <Setter Property=""Template"">
          <Setter.Value>
            <ControlTemplate TargetType=""ScrollBar"">
              <Grid Background=""Transparent"">
                <Track x:Name=""PART_Track"">
                  <Track.Thumb>
                    <Thumb>
                      <Thumb.Template>
                        <ControlTemplate TargetType=""Thumb"">
                          <Border CornerRadius=""4"" Background=""{StaticResource Thumb}"" Margin=""2""/>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- ============================== Tooltips ============================== -->
  <!-- Façon web/MUI : pilule gris foncé inversée, texte blanc compact —
       la même dans les deux thèmes (surface inversée assumée). La flèche
       pointe vers la cible (placement Bottom global, cf. Program.Main) ;
       Tag=above (posé par Program.OnToolTipOpened quand le popup est
       retourné au-dessus de la cible) la bascule vers le bas. -->
  <Style TargetType=""ToolTip"">
    <Setter Property=""Foreground"" Value=""#FFFFFF""/>
    <Setter Property=""FontSize"" Value=""11""/>
    <Setter Property=""HasDropShadow"" Value=""False""/>
    <Setter Property=""Placement"" Value=""Bottom""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ToolTip"">
          <Grid Margin=""4,3,4,3"" UseLayoutRounding=""True"" SnapsToDevicePixels=""True""
                TextOptions.TextFormattingMode=""Display"">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""Auto""/>
              <ColumnDefinition Width=""Auto""/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
              <RowDefinition Height=""Auto""/>
              <RowDefinition Height=""Auto""/>
              <RowDefinition Height=""Auto""/>
            </Grid.RowDefinitions>
            <Path x:Name=""ArrowTop"" Grid.Row=""0"" Data=""M0,5 L5,0 10,5 Z""
                  Fill=""#E8616161"" HorizontalAlignment=""Center""
                  Margin=""0,0,0,-1""/>
            <!-- L'ombre est portée par un cadre VIDE derrière le texte (22/09) :
                 un Effect force le rendu intermédiaire de tout son sous-arbre
                 et éteint ClearType — c'était le texte flou des infobulles. -->
            <Border Grid.Row=""1"" CornerRadius=""4"" Background=""#E8616161"">
              <Border.Effect>
                <DropShadowEffect Color=""Black"" Opacity=""0.18"" BlurRadius=""5"" ShadowDepth=""1""/>
              </Border.Effect>
            </Border>
            <Border Grid.Row=""1"" Padding=""8,4,8,5"">
              <ContentPresenter TextBlock.Foreground=""#FFFFFF""/>
            </Border>
            <Path x:Name=""ArrowBottom"" Grid.Row=""2"" Data=""M0,0 L5,5 10,0 Z""
                  Fill=""#E8616161"" HorizontalAlignment=""Center""
                  Margin=""0,-1,0,0"" Visibility=""Collapsed""/>
            <!-- Tag=beside (bulle posée à GAUCHE de sa cible — les onglets
                 du rail, b39) : la flèche pointe vers la droite, centrée. -->
            <Path x:Name=""ArrowRight"" Grid.Row=""1"" Grid.Column=""1"" Data=""M0,0 L5,5 0,10 Z""
                  Fill=""#E8616161"" VerticalAlignment=""Center""
                  Margin=""-1,0,0,0"" Visibility=""Collapsed""/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property=""Tag"" Value=""above"">
              <Setter TargetName=""ArrowTop"" Property=""Visibility"" Value=""Collapsed""/>
              <Setter TargetName=""ArrowBottom"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
            <Trigger Property=""Tag"" Value=""beside"">
              <Setter TargetName=""ArrowTop"" Property=""Visibility"" Value=""Collapsed""/>
              <Setter TargetName=""ArrowRight"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";
    }
}
