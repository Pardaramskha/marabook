# -*- coding: utf-8 -*-
"""Troisième passe du portage WPF → Avalonia : l'ASYNCHRONIE des dialogues.

Chez Avalonia un dialogue modal s'attend (ShowDialog est une Task). Cette passe :
1. rend asynchrones les « public static T Nom(Window owner, …) » qui font
   Dialogs.ShowModal(x) — await + async Task<T> (void → var _ = …) ;
2. remplace les dialogues de fichiers Win32 (OpenFileDialog / SaveFileDialog +
   ShowDialog) par Ui.PickOpenFile / Ui.PickSaveFile ;
3. préfixe « await » aux appels connus dont le résultat est employé, en rendant
   l'englobant async (délégué anonyme ou méthode) ;
4. en boucle : compile, lit les erreurs « Task<X> … X » et pose l'await à la
   colonne signalée, jusqu'à ce qu'il n'en reste plus (ou 8 tours).
Usage : python tools/port-avalonia-async.py [Fichier.cs…]   (sans argument : tous)
"""
import io, os, re, subprocess, sys, glob

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "dotnet", "Marabook.App")

DECL = re.compile(r"(?P<indent>[ \t]+)(?P<mods>(?:public|private|internal|protected)(?: static| override| virtual| new)*) (?P<ret>[\w<>\[\],. ?]+) (?P<name>\w+)\((?P<args>[^)]*)\)\s*\n[ \t]*\{", re.M)
DELEGATE = re.compile(r"\bdelegate(\s*\([^)]*\))?\s*\n?[ \t]*\{")
LAMBDA = re.compile(r"(\([^()]*\)|\w+) =>\s*\n?[ \t]*\{")

def load(p): return io.open(p, encoding="utf-8-sig").read()
def save(p, t): io.open(p, "w", encoding="utf-8", newline="\n").write(t)

def make_async(t, index):
    """Rend asynchrone ce qui englobe la position : le délégué anonyme ou la
    lambda la plus proche, sinon la méthode (void → async void, T → async Task<T>)."""
    last_decl = None
    for m in DECL.finditer(t, 0, index): last_decl = m
    last_del = None
    for m in DELEGATE.finditer(t, 0, index): last_del = m
    last_lam = None
    for m in LAMBDA.finditer(t, 0, index): last_lam = m
    candidates = [c for c in (last_decl, last_del, last_lam) if c is not None]
    if not candidates: return t
    # Un délégué ou une lambda ne compte que s'il est encore ouvert à la position.
    def still_open(m):
        depth = 0
        for c in t[m.end() - 1:index]:
            if c == "{": depth += 1
            elif c == "}": depth -= 1
        return depth > 0
    inner = [c for c in (last_del, last_lam) if c is not None and still_open(c)]
    if inner and (last_decl is None or max(c.start() for c in inner) > last_decl.start()):
        m = max(inner, key=lambda c: c.start())
        s = m.start()
        if t[max(0, s - 6):s] == "async ": return t
        return t[:s] + "async " + t[s:]
    if last_decl is None: return t
    m = last_decl
    if "async" in m.group("mods") or m.group("ret").strip().startswith("async"): return t
    ret = m.group("ret").strip()
    new_ret = "async void" if ret == "void" else "async Task<" + ret + ">"
    head = t[m.start():m.end()]
    new_head = head.replace(m.group("mods") + " " + m.group("ret") + " " + m.group("name") + "(",
                            m.group("mods") + " " + new_ret + " " + m.group("name") + "(", 1)
    return t[:m.start()] + new_head + t[m.end():]

def ensure_tasks_using(t):
    if "using System.Threading.Tasks;" in t: return t
    return t.replace("using System;\n", "using System;\nusing System.Threading.Tasks;\n", 1)

def dialog_statics(t):
    pos = 0
    changed = False
    while True:
        i = t.find("Dialogs.ShowModal(", pos)
        if i < 0: break
        if t[max(0, i - 6):i] == "await " or t[max(0, i - 8):i] == "var _ = ":
            pos = i + 10; continue
        j = t.index(");", i)
        args = t[i + len("Dialogs.ShowModal("):j]
        if ", owner" in args or args.endswith(", this"):
            pos = i + 10; continue
        last_decl = None
        for m in DECL.finditer(t, 0, i): last_decl = m
        owner = "owner" if last_decl is not None and "Window owner" in last_decl.group("args") else "this"
        if last_decl is not None and last_decl.group("ret").strip() == "void" and "static" in last_decl.group("mods"):
            t = t[:i] + "var _ = Dialogs.ShowModal(" + args + ", " + owner + ");" + t[j + 2:]
        else:
            t = t[:i] + "await Dialogs.ShowModal(" + args + ", " + owner + ");" + t[j + 2:]
            t = make_async(t, i)
        changed = True
        pos = i + 10
    return ensure_tasks_using(t) if changed else t

# L'initialiseur ne contient ni « ; » ni accolade hors chaîne : le motif ne
# peut pas enjamber une autre méthode (le piège du 27/09 : un dialogue à
# « == true ? … : … » plus haut a fait avaler tout ce qui le séparait du
# prochain « != true »).
FILE_DIALOG = re.compile(
    r"var (?P<name>\w+) = new Microsoft\.Win32\.(?P<kind>Open|Save)FileDialog\s*\{(?P<init>(?:[^;\"{}]|\"(?:[^\"\\\\]|\\\\.)*\")*?)\};\s*"
    r"if \((?P=name)\.ShowDialog\((?:[^()]|\([^()]*\))*\) != true\)(?P<bail> return[^;]*;| \{[^}]*\}|\s*\{[^}]*\})", re.S)

def split_init(init):
    """Les « Nom = valeur » d'un initialiseur, valeurs multi-lignes comprises."""
    parts = {}
    depth = 0
    current = ""
    items = []
    in_string = None
    for c in init:
        if in_string:
            current += c
            if c == in_string and not current.endswith("\\\\" + c): in_string = None
            continue
        if c in "\"'": in_string = c
        if c in "([{": depth += 1
        if c in ")]}": depth -= 1
        if c == "," and depth == 0:
            items.append(current); current = ""
        else: current += c
    if current.strip(): items.append(current)
    for item in items:
        if "=" not in item: continue
        k, v = item.split("=", 1)
        parts[k.strip()] = v.strip()
    return parts

def file_dialogs(t):
    def repl(m):
        name, kind, init, bail = m.group("name"), m.group("kind"), m.group("init"), m.group("bail")
        parts = split_init(init)
        title = parts.get("Title", '""')
        filt = parts.get("Filter", '""')
        var = name + "Path"
        if kind == "Open":
            if parts.get("Multiselect", "false").strip() == "true":
                call = "var " + var + "s = await Ui.PickOpenFiles(this, " + title + ", " + filt + ");\n            if (" + var + "s == null || " + var + "s.Length == 0)" + bail
            else:
                call = "var " + var + " = await Ui.PickOpenFile(this, " + title + ", " + filt + ");\n            if (" + var + " == null)" + bail
        else:
            suggested = parts.get("FileName", '""')
            call = "var " + var + " = await Ui.PickSaveFile(this, " + title + ", " + filt + ", " + suggested + ");\n            if (" + var + " == null)" + bail
        return call
    pos = 0
    while True:
        m = FILE_DIALOG.search(t, pos)
        if not m: break
        name = m.group("name")
        replacement = repl(m)
        t = t[:m.start()] + replacement + t[m.end():]
        # Les usages du dialogue dans la suite de la méthode.
        end = t.find("\n        }\n", m.start())
        if end < 0: end = len(t)
        body = t[m.start():end]
        body = body.replace(name + ".FileNames", name + "Paths").replace(name + ".FileName", name + "Path")
        t = t[:m.start()] + body + t[end:]
        t = make_async(t, m.start())
        pos = m.start() + len(replacement)
    return ensure_tasks_using(t) if "Ui.Pick" in t else t

# Les appels dont le résultat s'attend (le compilateur complète cette liste par ses erreurs).
KNOWN = ["InputDialog.Ask(", "ColorDialog.Ask(", "NumbersDialog.Ask(", "PdfExportDialog.Ask(", "HeaderFooterDialog.Edit(",
         "ProofOptionsDialog.Ask(", "TypographyCompareWindow.Ask(", "LexiconEntryDialog.Ask(", "LexiconEntryDialog.AskForWord(",
         "Ui.PickOpenFile(", "Ui.PickSaveFile(", "Ui.PickOpenFiles(", "Ui.ClipboardText(",
         "LinkDialog.Ask(", "SprintDialog.Ask(", "CompileDialog.Show(", "IconPickerDialog.Ask(", "BookOptionsDialog.Ask(",
         "NewSheetDialog.Ask(", "PlanEntryDialog.Ask(", "CharactersDialog.Ask(", "EpubExportDialog.Ask(", "StylesDialog.Ask("]

def await_known(t):
    for pattern in KNOWN:
        pos = 0
        while True:
            i = t.find(pattern, pos)
            if i < 0: break
            line_start = t.rfind("\n", 0, i) + 1
            before = t[line_start:i]
            used = before.strip() != ""  # pas un appel en instruction seule
            if used and t[max(0, i - 6):i] != "await ":
                t = t[:i] + "await " + t[i:]
                t = make_async(t, i)
                i = t.find(pattern, i)
            pos = i + len(pattern)
    return t

TASK_ERROR = re.compile(r"^(?P<file>.*?)\((?P<line>\d+),(?P<col>\d+)\): error (?P<code>CS\d+): (?P<msg>.*)$")

def compile_errors():
    out = subprocess.run(["dotnet", "build", os.path.join(ROOT, "dotnet", "Marabook.App", "Marabook.App.csproj"), "-c", "Debug", "-nologo", "-v", "q"],
                         capture_output=True, text=True, encoding="utf-8", errors="replace")
    errors = set()
    for line in (out.stdout + out.stderr).splitlines():
        m = TASK_ERROR.match(line.strip())
        if m: errors.add((os.path.normpath(m.group("file")), int(m.group("line")), int(m.group("col")), m.group("code"), m.group("msg")))
    return errors

def await_from_errors(max_rounds=8):
    for round_no in range(max_rounds):
        errors = compile_errors()
        task = [e for e in errors if ("Task<" in e[4] and e[3] in ("CS0029", "CS1503", "CS1929", "CS0019", "CS0021", "CS1061", "CS0266", "CS1502", "CS0030", "CS0023")) or e[3] == "CS4033"]
        print("tour", round_no + 1, ":", len(errors), "erreurs, dont", len(task), "de Task")
        if not task: return
        by_file = {}
        for f, line, col, code, msg in task: by_file.setdefault(f, []).append((line, col, code))
        for f, sites in by_file.items():
            t = load(f)
            lines = t.split("\n")
            # De la fin vers le début : les insertions ne décalent pas les positions restantes.
            for line, col, code in sorted(set(sites), reverse=True):
                idx = line - 1
                if idx >= len(lines): continue
                text_line = lines[idx]
                at = col - 1
                if at < 0 or at > len(text_line): continue
                if code == "CS4033":
                    continue  # l'englobant seul devient async (plus bas)
                if code == "CS1061":
                    # « Task<X> ne contient pas Membre » : la colonne vise le
                    # membre ; l'await va sur la DÉCLARATION de la variable
                    # (var x = Dialogue.Ask(...)).
                    m = re.search(r"(\w+)\.$", text_line[:at])
                    if not m: continue
                    name = m.group(1)
                    for k in range(idx, -1, -1):
                        d = re.search(r"\bvar " + name + r" = (?!await )", lines[k])
                        if d:
                            lines[k] = lines[k][:d.end()] + "await " + lines[k][d.end():]
                            break
                    continue
                if text_line[max(0, at - 6):at] == "await ": continue
                if code == "CS0023" and text_line[at:at + 1] == "!":
                    # « !Task<bool> » : l'await après la négation.
                    if text_line[at + 1:at + 7] == "await ": continue
                    lines[idx] = text_line[:at + 1] + "await " + text_line[at + 1:]
                    continue
                # Pour CS0019 la colonne vise l'opérande gauche entier : bien.
                lines[idx] = text_line[:at] + "await " + text_line[at:]
            t = "\n".join(lines)
            for line, col, code in sorted(set(sites)):
                index = sum(len(l) + 1 for l in t.split("\n")[:line - 1]) + col - 1
                t = make_async(t, index)
            save(f, ensure_tasks_using(t))
    print("tours épuisés")

if __name__ == "__main__":
    # Les fichiers écrits à la main pour Avalonia (P1) ne passent pas par ici.
    SKIP = {"Probes.cs", "MainWindow.Launch.cs", "Ui.cs", "WelcomeWindow.cs", "App.cs", "AppPlatform.cs", "MessageDialog.cs", "InputDialog.cs", "Dialogs.cs"}
    names = sys.argv[1:] or [os.path.basename(p) for p in glob.glob(os.path.join(APP, "*.cs"))]
    names = [n for n in names if n not in SKIP]
    for name in names:
        p = os.path.join(APP, name)
        t = load(p)
        o = t
        t = dialog_statics(t)
        t = file_dialogs(t)
        t = await_known(t)
        if t != o:
            save(p, t)
            print("asynchrone :", name)
    await_from_errors()
