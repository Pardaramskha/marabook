# -*- coding: utf-8 -*-
"""Relève TOUTES les infobulles de l'interface Avalonia de Marabook (30/10),
classées par page (fichier de vue), une puce par infobulle, pour une repasse
d'écriture. Lancer depuis la racine du dépôt :

    .\\python\\python.exe tools\\extraire-infobulles.py [chemin de sortie.md]

Sans argument, le fichier va dans le dossier Téléchargements de l'utilisateur.

Ce qui est relevé :
  - ToolTip.SetTip(cible, texte) et [ToolTip.TipProperty] = texte ;
  - tout appel d'une méthode dont la déclaration porte un paramètre nommé
    tooltip, tip ou hint (Buttons.Text, Buttons.IconText, OneLine, BigSquare,
    ViewToggle, IconTool, Chip, WithClear…) — l'argument à cette position.
Le texte est recomposé depuis l'expression C# : les littéraux recollés,
Ui.Keys("Ctrl+S") rendu « Ctrl+S », les autres morceaux de code en ⟨…⟩, un
texte conditionnel (a ? b : c) donné sous ses deux formes.
"""
import io, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, 'dotnet', 'Marabook.App')

# Le nom de page de chaque fichier de vue ; les autres prennent leur nom brut.
PAGES = [
    ('WelcomeWindow.cs', 'Accueil'),
    ('HomeView.cs', 'Accueil du projet (Reprendre, Épinglés, Où j’en suis)'),
    ('ProfileChooser.cs', 'Choix du profil'),
    ('MainWindow.cs', 'Fenêtre principale — menus, barre d’état, rail, Général'),
    ('MainWindow.Tabs.cs', 'Fenêtre principale — onglets de travail'),
    ('TabStrip.cs', 'Rangée d’onglets'),
    ('MainWindow.Batch.cs', 'Fenêtre principale — sélection multiple (mode lot)'),
    ('MainWindow.Lexicon.cs', 'Fenêtre principale — Lexique du rail'),
    ('MainWindow.Images.cs', 'Fenêtre principale — images'),
    ('MainWindow.Graph.cs', 'Fenêtre principale — graphe'),
    ('MainWindow.Modules.cs', 'Fenêtre principale — modules (DLC)'),
    ('MainWindow.StyleReport.cs', 'Fenêtre principale — bilan de style'),
    ('MainWindow.Guard.cs', 'Fenêtre principale — garde du fichier'),
    ('MainWindow.Tutorial.cs', 'Tutoriel'),
    ('TutorialWindow.cs', 'Tutoriel — fenêtre'),
    ('BinderView.cs', 'La Pile'),
    ('QuickOpenPalette.cs', 'Palette « Ouvrir par le nom »'),
    ('PinnedPanel.cs', 'Rail — Épinglés'),
    ('SearchPanel.cs', 'Rail — Recherche dans le projet'),
    ('VersionsPanel.cs', 'Rail — Versions'),
    ('LexiconPanel.cs', 'Rail — Lexique'),
    ('StyleReportPanel.cs', 'Rail — Bilan de style'),
    ('EditorView.cs', 'Éditeur — surface, recherche, panneau des relevés'),
    ('EditorView.Ribbon.cs', 'Éditeur — ruban'),
    ('EditorView.Images.cs', 'Éditeur — onglet Image'),
    ('ComposedView.cs', 'Éditeur — surface composée (menus contextuels)'),
    ('ComposedView.Notes.cs', 'Éditeur — notes de bas de page'),
    ('ComposedView.Images.cs', 'Éditeur — images dans la page'),
    ('RulerView.cs', 'Éditeur — règles'),
    ('SpecialCharsDrawer.cs', 'Éditeur — tiroir des caractères spéciaux'),
    ('FontPicker.cs', 'Éditeur — choix de police'),
    ('StylesPanel.cs', 'Styles'),
    ('StylesDialog.cs', 'Gestion des styles'),
    ('SeparatorEditor.cs', 'Styles — séparateur de scène'),
    ('TypographyDialogs.cs', 'Formatage — typographie et dialogues'),
    ('ReplaceDialog.cs', 'Rechercher et remplacer'),
    ('UnknownWordsDialog.cs', 'Mots inconnus'),
    ('CharactersDialog.cs', 'Extraction de personnages'),
    ('MissingSheetsDialog.cs', 'Fiches manquantes'),
    ('BookView.cs', 'Livre — onglets du livre'),
    ('BookPanels.cs', 'Livre — panneaux'),
    ('BookEditionTab.cs', 'Livre — Édition'),
    ('BookPublicationTab.cs', 'Livre — Publication'),
    ('BookOptionsDialog.cs', 'Livre — options'),
    ('BookProgressBar.cs', 'Livre — barre d’objectif'),
    ('NewBookDialog.cs', 'Nouveau livre'),
    ('HeaderFooterDialog.cs', 'Gabarits — en-têtes et pieds'),
    ('NumbersDialog.cs', 'Gabarits — numérotation'),
    ('CompileDialog.cs', 'Compiler les écrits'),
    ('PdfExportDialog.cs', 'Export PDF'),
    ('EpubExportDialog.cs', 'Export EPUB'),
    ('CompareWindow.cs', 'Fenêtre comparative (typographie, dialogues, versions)'),
    ('CorkboardView.cs', 'Tableaux de tuiles (écrits, fiches, livres)'),
    ('ItemTiles.cs', 'Tuiles'),
    ('Scopes.cs', 'Spectre des écrits'),
    ('MediaView.cs', 'Recherche — médias'),
    ('SheetView.cs', 'Fiche'),
    ('SheetWiki.cs', 'Fiche — mode wiki'),
    ('SheetLibraryView.cs', 'Bibliothèque de fiches'),
    ('SheetDialogs.cs', 'Fiches — dialogues'),
    ('FieldEditors.cs', 'Fiches — champs'),
    ('RadarChart.cs', 'Fiches — radar'),
    ('TemplateView.cs', 'Modèles de fiches'),
    ('IconPicker.cs', 'Choix d’icône'),
    ('ColorDialog.cs', 'Choix de couleur'),
    ('ColorMenus.cs', 'Menus de couleur'),
    ('ImageEditDialog.cs', 'Retouche d’image à l’import'),
    ('RelationsEditorWindow.cs', 'Diagramme relationnel'),
    ('GraphView.cs', 'Gros Graph'),
    ('GenealogyView.cs', 'Généalogie'),
    ('DictionaryView.cs', 'Dictionnaire'),
    ('LexiconEntryDialog.cs', 'Dictionnaire — entrée'),
    ('PlanView.cs', 'Plans'),
    ('JournalView.cs', 'Journal perso'),
    ('SprintDialog.cs', 'Sprint'),
    ('AchievementBadge.cs', 'Succès'),
    ('PreferencesDialog.cs', 'Préférences'),
    ('FontCatalogTab.cs', 'Préférences — catalogue de polices'),
    ('FontSubstitutionDialog.cs', 'Polices manquantes'),
    ('ProofOptionsDialog.cs', 'Options du correcteur'),
    ('ModuleDialog.cs', 'Modules (DLC)'),
    ('ModuleSheetView.cs', 'Modules — fiche'),
    ('MarkdownView.cs', 'Texte Markdown (fiches)'),
    ('AboutDialog.cs', 'À propos'),
    ('CrashReportsDialog.cs', 'Rapports de plantage'),
    ('UpdateNotesDialog.cs', 'Nouveautés'),
    ('Dialogs.cs', 'Dialogues communs'),
    ('InputDialog.cs', 'Saisie'),
    ('ItemPickerDialog.cs', 'Choix d’un élément'),
    ('Ui.cs', 'Composants communs (Ui)'),
    ('Buttons.cs', 'Composants communs (boutons)'),
]
SKIP_FILES = {'Probes.cs', 'Theme.cs', 'Program.cs', 'MainWindow.Launch.cs'}
PAGE_OF = dict(PAGES)
PAGE_ORDER = [name for name, _ in PAGES]

LITERAL = re.compile(r'@"(?:[^"]|"")*"|"(?:[^"\\\n]|\\.)*"')
LINE_COMMENT = re.compile(r'^(\s*)//.*$', re.M)
TIP_PARAM = re.compile(r'\b(?:private|public|internal|protected)\s+(?:static\s+)?[\w<>\[\],.?]+\s+(\w+)\s*\(([^)]*)\)')


def unescape(token):
    if token.startswith('@'):
        return token[2:-1].replace('""', '"')
    body = token[1:-1]
    return (body.replace('\\n', '\n').replace('\\"', '"').replace('\\t', '\t')
                .replace('\\\\', '\\').replace('\\u00A0', ' ').replace('\\u202F', ' ')
                .replace('\\u2009', ' '))


def strip_comments(source):
    """Les commentaires de ligne hors littéraux deviennent vides (les lignes restent)."""
    out = []
    for line in source.split('\n'):
        # Un « // » hors chaîne : on coupe là.
        in_str = False
        verbatim = False
        i = 0
        cut = None
        while i < len(line):
            c = line[i]
            if in_str:
                if verbatim:
                    if c == '"':
                        if i + 1 < len(line) and line[i + 1] == '"':
                            i += 1
                        else:
                            in_str = False
                elif c == '\\':
                    i += 1
                elif c == '"':
                    in_str = False
            else:
                if c == '"':
                    in_str = True
                    verbatim = i > 0 and line[i - 1] == '@'
                elif c == '/' and i + 1 < len(line) and line[i + 1] == '/':
                    cut = i
                    break
            i += 1
        out.append(line if cut is None else line[:cut])
    return '\n'.join(out)


def split_args(text):
    """Découpe une liste d'arguments C# au niveau 0 (parenthèses, crochets, accolades, chaînes)."""
    args = []
    depth = 0
    current = []
    i = 0
    while i < len(text):
        c = text[i]
        if c == '"' or (c == '@' and i + 1 < len(text) and text[i + 1] == '"'):
            m = LITERAL.match(text, i)
            if m:
                current.append(m.group(0))
                i = m.end()
                continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        if c == ',' and depth == 0:
            args.append(''.join(current).strip())
            current = []
        else:
            current.append(c)
        i += 1
    tail = ''.join(current).strip()
    if tail or args:
        args.append(tail)
    return args


def balanced(text, start):
    """L'index de la parenthèse fermante qui répond à text[start] == '('."""
    depth = 0
    i = start
    while i < len(text):
        c = text[i]
        if c == '"' or (c == '@' and i + 1 < len(text) and text[i + 1] == '"'):
            m = LITERAL.match(text, i)
            if m:
                i = m.end()
                continue
        if c == '(':
            depth += 1
        elif c == ')':
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def render(expr, code, at):
    """Le texte lisible d'une expression de chaîne C#."""
    expr = expr.strip()
    if not expr or expr == 'null':
        return None
    # Le paramètre d'une méthode d'aide (ToolTip.SetTip(bouton, tooltip)) :
    # pas une infobulle, le relais d'une autre.
    if expr in ('tooltip', 'tip', 'hint'):
        return None
    m = re.fullmatch(r'new\s+ToolTip\s*\{\s*Content\s*=\s*(.*)\}', expr, re.S)
    if m:
        return render(m.group(1).rstrip().rstrip(','), code, at)
    if expr == 'Ui.CommandKey':
        return 'Ctrl'
    m = re.fullmatch(r'(?:Settings\.)?AppSettings\.DisplayGesture\(\s*(?:Settings\.)?AppSettings\.Gesture\(\s*"([^"]+)"\s*\)\s*\?\?\s*""\s*\)', expr)
    if m:
        return '⟨raccourci de ' + m.group(1) + '⟩'
    m = re.fullmatch(r'(?:Settings\.)?AppSettings\.DisplayGesture\(\s*"([^"]+)"\s*\)', expr)
    if m:
        return m.group(1)
    if re.fullmatch(r'\d+', expr):
        return expr
    # Un identifiant nu : la variable définie plus haut dans le fichier.
    if re.fullmatch(r'[A-Za-z_]\w*', expr):
        m = None
        for cand in re.finditer(r'\b(?:var|string)\s+' + re.escape(expr) + r'\s*=\s*', code[:at]):
            m = cand
        if m:
            end = code.find(';', m.end())
            if end > 0:
                return render(code[m.end():end], code, m.start())
        return '⟨' + expr + '⟩'
    # Un texte conditionnel : les deux formes.
    q = top_level_index(expr, '?')
    if q > 0 and not expr[q:q + 2] == '??':
        colon = top_level_index(expr, ':', q + 1)
        if colon > 0:
            a = render(expr[q + 1:colon], code, at)
            b = render(expr[colon + 1:], code, at)
            cond = expr[:q].strip()
            return '{si ' + cond + ' : ' + (a or '∅') + ' | sinon : ' + (b or '∅') + '}'
    # Concaténation : les morceaux de code voisins (paragraph + 1) restent un seul ⟨…⟩.
    parts = split_plus(expr)
    if len(parts) > 1:
        merged = []
        for p in parts:
            is_code = not LITERAL.fullmatch(p.strip()) and not p.strip().startswith('Ui.Keys(')
            if merged and is_code and merged[-1][1]:
                merged[-1] = (merged[-1][0] + ' + ' + p.strip(), True)
            else:
                merged.append((p.strip(), is_code))
        return ''.join((('⟨' + re.sub(r'\s+', ' ', p) + '⟩') if is_code and ' + ' in p else (render(p, code, at) or '')) for p, is_code in merged)
    if LITERAL.fullmatch(expr):
        return unescape(expr)
    m = re.fullmatch(r'Ui\.Keys\(\s*("(?:[^"\\]|\\.)*")\s*\)', expr)
    if m:
        return unescape(m.group(1))
    m = re.fullmatch(r'\(\s*(.*)\s*\)', expr, re.S)
    if m:
        return render(m.group(1), code, at)
    # Un appel de mise en forme : son premier littéral, ou le code.
    return '⟨' + re.sub(r'\s+', ' ', expr) + '⟩'


def top_level_index(expr, char, start=0):
    depth = 0
    i = start
    while i < len(expr):
        c = expr[i]
        if c == '"' or (c == '@' and i + 1 < len(expr) and expr[i + 1] == '"'):
            m = LITERAL.match(expr, i)
            if m:
                i = m.end()
                continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        elif depth == 0 and c == char:
            return i
        i += 1
    return -1


def split_plus(expr):
    parts = []
    depth = 0
    current = []
    i = 0
    while i < len(expr):
        c = expr[i]
        if c == '"' or (c == '@' and i + 1 < len(expr) and expr[i + 1] == '"'):
            m = LITERAL.match(expr, i)
            if m:
                current.append(m.group(0))
                i = m.end()
                continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        if c == '+' and depth == 0:
            parts.append(''.join(current))
            current = []
        else:
            current.append(c)
        i += 1
    parts.append(''.join(current))
    return [p for p in parts if p.strip()]


def helpers_of(code):
    """{nom: (index du paramètre infobulle, index du libellé ou None)} des méthodes du fichier."""
    found = {}
    for m in TIP_PARAM.finditer(code):
        name, params = m.group(1), m.group(2)
        plist = [p.strip() for p in params.split(',') if p.strip()]
        tip = label = None
        for i, p in enumerate(plist):
            pname = p.split('=')[0].split()[-1]
            if pname in ('tooltip', 'tip', 'hint') and tip is None:
                tip = i
            if pname in ('label', 'text', 'name', 'header') and label is None:
                label = i
        if tip is not None:
            found[name] = (tip, label)
    return found


def line_of(code, index):
    return code.count('\n', 0, index) + 1


def collect(path, shared_helpers):
    with io.open(path, encoding='utf-8-sig') as f:
        source = f.read()
    code = strip_comments(source)
    items = []  # (ligne, libellé ou None, texte)
    seen = set()

    def add(at, label, expr):
        text = render(expr, code, at)
        if text is None:
            return
        key = (line_of(code, at), text)
        if key in seen:
            return
        seen.add(key)
        items.append((line_of(code, at), label, text))

    for m in re.finditer(r'ToolTip\.SetTip\(', code):
        close = balanced(code, m.end() - 1)
        if close < 0:
            continue
        args = split_args(code[m.end():close])
        if len(args) >= 2:
            add(m.start(), None, args[1])
    for m in re.finditer(r'\[ToolTip\.TipProperty\]\s*=\s*', code):
        # Jusqu'à la virgule ou l'accolade fermante de niveau 0.
        i = m.end()
        depth = 0
        while i < len(code):
            c = code[i]
            if c == '"' or (c == '@' and i + 1 < len(code) and code[i + 1] == '"'):
                lit = LITERAL.match(code, i)
                if lit:
                    i = lit.end()
                    continue
            if c in '([{':
                depth += 1
            elif c in ')]}':
                if depth == 0:
                    break
                depth -= 1
            elif c in ',;' and depth == 0:
                break
            i += 1
        add(m.start(), None, code[m.end():i])
    local = helpers_of(code)
    helpers = dict(shared_helpers)
    helpers.update(local)
    for name, (tip_index, label_index) in helpers.items():
        for m in re.finditer(r'(?<![\w.])(?:Buttons\.|Ui\.)?' + re.escape(name) + r'\(', code):
            # Pas la déclaration elle-même.
            head = code[max(0, m.start() - 60):m.start()]
            if re.search(r'(private|public|internal|protected|static|override)\s+[\w<>\[\],.?]*\s*$', head):
                continue
            # Un appel d'une méthode partagée se fait qualifié (Buttons., Ui.) ; une locale, nue.
            qualified = code[m.start():m.end()]
            if name in shared_helpers and name not in local and not (qualified.startswith('Buttons.') or qualified.startswith('Ui.')):
                continue
            close = balanced(code, m.end() - 1)
            if close < 0:
                continue
            args = split_args(code[m.end():close])
            if len(args) <= tip_index:
                continue
            label = None
            if label_index is not None and label_index < len(args):
                label = render(args[label_index], code, m.start())
            add(m.start(), label, args[tip_index])
    items.sort()
    return items


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.expanduser('~'), 'Downloads', 'marabook-infobulles.md')
    shared = {}
    for shared_file in ('Buttons.cs', 'Ui.cs'):
        p = os.path.join(APP, shared_file)
        if os.path.exists(p):
            with io.open(p, encoding='utf-8-sig') as f:
                shared.update(helpers_of(strip_comments(f.read())))
    pages = {}
    for name in sorted(os.listdir(APP)):
        if not name.endswith('.cs') or name in SKIP_FILES:
            continue
        items = collect(os.path.join(APP, name), shared)
        if items:
            pages[name] = items
    ordered = [n for n in PAGE_ORDER if n in pages] + sorted(n for n in pages if n not in PAGE_OF)
    total = sum(len(v) for v in pages.values())
    lines = []
    lines.append('# Marabook — toutes les infobulles, par page')
    lines.append('')
    lines.append('> Relevé automatique du 30/10/2026 (tools/extraire-infobulles.py) sur feat/1.1.0 : ' + str(total)
                 + ' infobulles dans ' + str(len(pages)) + ' fichiers de vue. Chaque puce = une infobulle ; « **libellé** » = le bouton qui la porte quand il a un texte ; '
                 + '(l. n) = la ligne du fichier. Les morceaux calculés sont en ⟨code⟩ ; un texte conditionnel est donné sous ses deux formes {si … | sinon …}. '
                 + 'Les retours à la ligne de l’infobulle sont rendus par ⏎.')
    lines.append('')
    lines.append('## Sommaire')
    lines.append('')
    for name in ordered:
        lines.append('- ' + PAGE_OF.get(name, name) + ' — ' + str(len(pages[name])) + ' (' + name + ')')
    lines.append('')
    for name in ordered:
        lines.append('## ' + PAGE_OF.get(name, name))
        lines.append('')
        lines.append('_' + name + ' — ' + str(len(pages[name])) + ' infobulle(s)_')
        lines.append('')
        for line, label, text in pages[name]:
            shown = text.replace('\r', '').replace('\n', ' ⏎ ').replace(' ', ' ').replace(' ', ' ').replace(' ', ' ')
            shown = re.sub(r'\s+', ' ', shown).strip()
            prefix = ('**« ' + re.sub(r'\s+', ' ', label).strip() + ' »** : ') if label and not label.startswith('⟨') else ''
            lines.append('- ' + prefix + shown + ' _(l. ' + str(line) + ')_')
        lines.append('')
    with io.open(out, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    print(str(total) + ' infobulles, ' + str(len(pages)) + ' fichiers -> ' + out)


if __name__ == '__main__':
    main()
