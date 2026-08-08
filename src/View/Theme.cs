using System;
using System.Windows;
using System.Windows.Markup;

namespace UniversSale.View
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
                var palette = ApplyAccent(dark ? DarkPalette : LightPalette, dark);
                var next = (ResourceDictionary)XamlReader.Parse(
                    Header + palette + Templates);
                if (_current != null) application.Resources.MergedDictionaries.Remove(_current);
                application.Resources.MergedDictionaries.Add(next);
                _current = next;
            }
            catch
            {
                // If a template misparses, the application stays usable in classic style.
            }
        }

        /// <summary>Substitutes the customized accent into the palette string
        /// before parsing. Soft and hover steps are re-derived from the accent
        /// with the same blends that produced the stock indigo steps (≈ 0.82 and
        /// 0.90 toward the theme's ground), so any hue keeps the house look.</summary>
        private static string ApplyAccent(string palette, bool dark)
        {
            var custom = Chrome.ParseAccent();
            if (custom == null) return palette;

            var ground = dark
                ? System.Windows.Media.Color.FromRgb(0x1B, 0x1E, 0x23)
                : System.Windows.Media.Colors.White;
            var accent = dark ? Chrome.Lighten(custom.Value, 0.22) : custom.Value;
            var soft = Chrome.Blend(accent, ground, 0.82);
            var hover = Chrome.Blend(accent, ground, 0.90);

            if (dark)
                return palette
                    .Replace("#7B86E8", Hex(accent))
                    .Replace("#2C3352", Hex(soft))
                    .Replace("#262B3C", Hex(hover));
            return palette
                .Replace("#5B67D8", Hex(accent))
                .Replace("#E2E5F9", Hex(soft))
                .Replace("#EEF0FB", Hex(hover));
        }

        private static string Hex(System.Windows.Media.Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private const string Header = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
";

        private const string LightPalette = @"
  <SolidColorBrush x:Key=""Ink"" Color=""#23262A""/>
  <SolidColorBrush x:Key=""InkSoft"" Color=""#6B7280""/>
  <SolidColorBrush x:Key=""Paper"" Color=""#FFFFFF""/>
  <SolidColorBrush x:Key=""Veil"" Color=""#F4F5F7""/>
  <SolidColorBrush x:Key=""Border"" Color=""#D7DAE0""/>
  <SolidColorBrush x:Key=""Hover"" Color=""#EAECEF""/>
  <SolidColorBrush x:Key=""Press"" Color=""#DFE2E8""/>
  <SolidColorBrush x:Key=""Accent"" Color=""#5B67D8""/>
  <SolidColorBrush x:Key=""AccentSoft"" Color=""#E2E5F9""/>
  <SolidColorBrush x:Key=""AccentHover"" Color=""#EEF0FB""/>
  <SolidColorBrush x:Key=""Line"" Color=""#E7E9ED""/>
  <SolidColorBrush x:Key=""Thumb"" Color=""#C6CAD2""/>
";

        private const string DarkPalette = @"
  <SolidColorBrush x:Key=""Ink"" Color=""#E6E8EC""/>
  <SolidColorBrush x:Key=""InkSoft"" Color=""#9AA1AC""/>
  <SolidColorBrush x:Key=""Paper"" Color=""#23262C""/>
  <SolidColorBrush x:Key=""Veil"" Color=""#1B1E23""/>
  <SolidColorBrush x:Key=""Border"" Color=""#383D46""/>
  <SolidColorBrush x:Key=""Hover"" Color=""#2C3038""/>
  <SolidColorBrush x:Key=""Press"" Color=""#343943""/>
  <SolidColorBrush x:Key=""Accent"" Color=""#7B86E8""/>
  <SolidColorBrush x:Key=""AccentSoft"" Color=""#2C3352""/>
  <SolidColorBrush x:Key=""AccentHover"" Color=""#262B3C""/>
  <SolidColorBrush x:Key=""Line"" Color=""#31353E""/>
  <SolidColorBrush x:Key=""Thumb"" Color=""#454B56""/>
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
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Button"">
          <Border x:Name=""Bg"" CornerRadius=""5"" Background=""{StaticResource Paper}""
                  BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
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
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""ToggleButton"">
          <Border x:Name=""Bg"" CornerRadius=""5"" Background=""Transparent""
                  BorderBrush=""Transparent"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
            </Trigger>
            <Trigger Property=""IsChecked"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentSoft}""/>
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

  <!-- ============================== Text boxes ============================== -->
  <Style x:Key=""BareText"" TargetType=""TextBox"">
    <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
    <Setter Property=""CaretBrush"" Value=""{StaticResource Ink}""/>
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""BorderThickness"" Value=""0""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TextBox"">
          <ScrollViewer x:Name=""PART_ContentHost"" VerticalAlignment=""Center""/>
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
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TextBox"">
          <Border x:Name=""Bg"" CornerRadius=""5"" Background=""{StaticResource Paper}""
                  BorderBrush=""{StaticResource Border}"" BorderThickness=""1""
                  Padding=""{TemplateBinding Padding}"">
            <ScrollViewer x:Name=""PART_ContentHost"" VerticalAlignment=""Center""/>
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
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentHover}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentSoft}""/>
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
                  <Border x:Name=""Bg"" CornerRadius=""5"" Background=""{StaticResource Paper}""
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
    <Border x:Name=""Bg"" CornerRadius=""5"" Padding=""9,4"" Background=""Transparent"">
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
      <Border x:Name=""Bg"" CornerRadius=""5"" Padding=""9,4"" Background=""Transparent"">
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
        <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentHover}""/>
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
        <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentHover}""/>
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

  <Style TargetType=""TabItem"">
    <Setter Property=""Foreground"" Value=""{StaticResource InkSoft}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TabItem"">
          <Border x:Name=""Bg"" Background=""Transparent"" Padding=""12,6"" Margin=""2,2,0,0""
                  BorderThickness=""0,0,0,2"" BorderBrush=""Transparent"">
            <ContentPresenter ContentSource=""Header"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""BorderBrush"" Value=""{StaticResource Accent}""/>
              <Setter Property=""Foreground"" Value=""{StaticResource Ink}""/>
              <Setter Property=""FontWeight"" Value=""SemiBold""/>
            </Trigger>
          </ControlTemplate.Triggers>
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
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentHover}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentSoft}""/>
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
            <Border x:Name=""Bg"" CornerRadius=""5"" Padding=""2,3"" Background=""Transparent"">
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
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource Hover}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""Bg"" Property=""Background"" Value=""{StaticResource AccentSoft}""/>
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
          <Grid Margin=""4,3,4,3"">
            <Grid.RowDefinitions>
              <RowDefinition Height=""Auto""/>
              <RowDefinition Height=""Auto""/>
              <RowDefinition Height=""Auto""/>
            </Grid.RowDefinitions>
            <Path x:Name=""ArrowTop"" Grid.Row=""0"" Data=""M0,5 L5,0 10,5 Z""
                  Fill=""#E8616161"" HorizontalAlignment=""Center""
                  Margin=""0,0,0,-0.5""/>
            <Border Grid.Row=""1"" CornerRadius=""4"" Background=""#E8616161""
                    Padding=""8,4,8,5"">
              <Border.Effect>
                <DropShadowEffect Color=""Black"" Opacity=""0.18"" BlurRadius=""5"" ShadowDepth=""1""/>
              </Border.Effect>
              <ContentPresenter TextBlock.Foreground=""#FFFFFF""/>
            </Border>
            <Path x:Name=""ArrowBottom"" Grid.Row=""2"" Data=""M0,0 L5,5 10,0 Z""
                  Fill=""#E8616161"" HorizontalAlignment=""Center""
                  Margin=""0,-0.5,0,0"" Visibility=""Collapsed""/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property=""Tag"" Value=""above"">
              <Setter TargetName=""ArrowTop"" Property=""Visibility"" Value=""Collapsed""/>
              <Setter TargetName=""ArrowBottom"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";
    }
}
