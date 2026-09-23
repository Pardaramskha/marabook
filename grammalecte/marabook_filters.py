#!/usr/bin/env python3
# Marabook — le second regard sur les erreurs de Grammalecte (23/09/2026).
#
# Grammalecte 2.3.0 lit une phrase de gauche à droite avec des règles de
# proximité ; deux d'entre elles se trompent de tête de groupe dans du texte
# de roman, et Marabook les repasse ICI, dans le pont, avec les mêmes
# analyses morphologiques (le dictionnaire de Grammalecte) — jamais dans
# les sources de Grammalecte, qui restent celles de l'amont (empreintes
# vérifiées au build).
#
# 1. L'ACCORD PAR-DESSUS UN COMPLÉMENT (règles g3__gn_… « Accord de nombre
#    erroné avec « au fromage » ») : dans « les roulés au fromage
#    apocalyptiques », l'adjectif s'accorde avec « roulés », la tête du
#    groupe, pas avec « fromage », le complément qui le précède. On garde
#    l'erreur seulement si l'adjectif ne s'accorde PAS NON PLUS avec la tête
#    qui précède la préposition (« un roulé au fromage apocalyptiques » reste
#    relevé).
#
# 2. LE PARTICIPE NOMINALISÉ APRÈS « DE » (règle g3__infi_de_verbe « Le verbe
#    devrait être à l'infinitif ») : « un air de déterré », « une mine de
#    pendu », « un air de déterré ou d'échappé d'asile » — le participe est
#    un nom (celui qu'on a déterré), pas un verbe à l'infinitif. Le
#    dictionnaire ne connaît pas ces noms (« déterré » n'y est que participe),
#    d'où une liste FERMÉE de noms d'apparence qui appellent cette tournure :
#    « un air / une mine / une tête / une gueule de <participe> ». « l'air de »
#    est volontairement exclu (« il a l'air de dormir » veut l'infinitif) ;
#    « envie de », « essayé de » ne sont pas concernés et restent relevés.
#
# Tout est PUR : (analyses, jetons, texte, erreurs) → erreurs gardées. Le
# fichier se joue seul (`python marabook_filters.py`) pour rejouer les
# phrases de calibrage ; la suite C40 de Marabook le vérifie par le vrai pont.

# Préposition + déterminant contractés (« au fromage »), et prépositions
# simples suivies d'un déterminant (« à la crème », « de la maison »).
_CONTRACTED = {"au", "aux", "du", "des"}
_PREPOSITIONS = {"à", "a", "de", "d", "en", "sans", "avec", "pour", "sur", "sous",
                 "contre", "par", "chez", "dans", "entre", "vers"}
_DETERMINERS = {"le", "la", "les", "l", "un", "une", "des", "du", "au", "aux",
                "ce", "cet", "cette", "ces", "mon", "ma", "mes", "ton", "ta", "tes",
                "son", "sa", "ses", "notre", "nos", "votre", "vos", "leur", "leurs",
                "quelques", "plusieurs", "certains", "certaines", "tous", "toutes",
                "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix"}
# Les noms d'apparence qui prennent « de + participe nominalisé ».
_APPEARANCE = {"air", "airs", "mine", "mines", "tête", "têtes", "gueule", "gueules",
               "allure", "allures", "figure", "figures", "visage", "visages",
               "face", "faces", "regard", "regards", "tronche", "tronches",
               "dégaine", "dégaines", "look", "looks", "physionomie", "physionomies",
               "expression", "expressions", "teint", "teints", "silhouette",
               "silhouettes", "démarche", "démarches", "posture", "postures",
               "attitude", "attitudes", "sourire", "sourires", "voix", "ton", "tons"}
_COORDINATIONS = {"ou", "et"}


def keep(spell, tokenizer, text, errors):
    "les erreurs de Grammalecte moins les faux positifs connus"
    if not errors:
        return errors
    tokens = [t for t in tokenizer.genTokens(text) if t["sType"] in ("WORD", "WORDELD", "PUNC")]
    kept = []
    for error in errors:
        if _is_false_positive(spell, tokens, error):
            continue
        kept.append(error)
    return kept


def _is_false_positive(spell, tokens, error):
    rule = error.get("sRuleId", "") or ""
    i = _token_at(tokens, error.get("nStart"), error.get("nEnd"))
    if i is None:
        return False
    if error.get("sType") == "gn" and rule.startswith("g3__gn_"):
        return _agrees_with_head(spell, tokens, i)
    if rule.startswith("g3__infi_de_verbe"):
        return _nominal_participle(spell, tokens, i)
    return False


# ------------------------------------------------------------ 1. l'accord

def _agrees_with_head(spell, tokens, i):
    "l'adjectif relevé s'accorde avec la tête du groupe, avant la préposition"
    word = _word_readings(spell, tokens[i])
    if not any(_has(parts, "A") or _has(parts, "Q") for parts in word):
        return False
    j = i - 1
    if not _is_word(tokens, j):
        return False  # le complément (« fromage ») juste avant
    j -= 1
    if _is_word(tokens, j) and _norm(tokens[j]) in _CONTRACTED:
        j -= 1
    elif _is_word(tokens, j) and _norm(tokens[j]) in _DETERMINERS \
            and _is_word(tokens, j - 1) and _norm(tokens[j - 1]) in _PREPOSITIONS:
        j -= 2
    else:
        return False
    # La tête : le mot avant la préposition, en sautant des adjectifs, à
    # condition qu'un déterminant le précède (« les roulés », « des gâteaux »)
    # ou qu'il soit un nom du dictionnaire.
    steps = 0
    while _is_word(tokens, j) and steps < 3:
        head = _word_readings(spell, tokens[j])
        nominal = [parts for parts in head if _has(parts, "N") or _has(parts, "A") or _has(parts, "Q")]
        determined = any(_is_word(tokens, k) and _norm(tokens[k]) in _DETERMINERS for k in (j - 1, j - 2))
        if nominal and (determined or any(_has(parts, "N") for parts in nominal)):
            for adjective in word:
                for noun in nominal:
                    if _compatible(adjective, noun):
                        return True
            return False
        if not any(_has(parts, "A") for parts in head):
            return False
        j -= 1
        steps += 1
    return False


def _compatible(a, b):
    "genre et nombre compatibles (e = épicène, i = invariable)"
    ga, na = _gender(a), _number(a)
    gb, nb = _gender(b), _number(b)
    if na is None or nb is None:
        return False
    if na != nb and "i" not in (na, nb):
        return False
    if ga and gb and ga != gb and "e" not in (ga, gb):
        return False
    return True


def _gender(parts):
    for g in ("m", "f", "e"):
        if g in parts:
            return g
    return None


def _number(parts):
    for n in ("s", "p", "i"):
        if n in parts:
            return n
    return None


# ----------------------------------------------- 2. le participe nominalisé

def _nominal_participle(spell, tokens, i):
    "« un air de déterré » (ou « … ou d'échappé ») : nom, pas infinitif"
    if not any(_has(parts, "Q") for parts in _word_readings(spell, tokens[i])):
        return False
    j = i - 1
    if not (_is_word(tokens, j) and _norm(tokens[j]) in ("de", "d")):
        return False
    k = j - 1
    if not _is_word(tokens, k):
        return False
    before = _norm(tokens[k])
    if before in _APPEARANCE:
        det = tokens[k - 1] if _is_word(tokens, k - 1) else None
        return det is not None and _norm(det) in _DETERMINERS and _norm(det) != "l"
    if before in _COORDINATIONS:
        # « de déterré ou d'échappé » : le premier participe ouvre la voie
        return _is_word(tokens, k - 1) and _nominal_participle(spell, tokens, k - 1)
    return False


# ------------------------------------------------------------ les jetons

def _token_at(tokens, start, end):
    if start is None or end is None:
        return None
    for i, token in enumerate(tokens):
        if token["nStart"] == start and token["nEnd"] == end:
            return i
    for i, token in enumerate(tokens):
        if token["nStart"] <= start < token["nEnd"]:
            return i
    return None


def _is_word(tokens, i):
    return 0 <= i < len(tokens) and tokens[i]["sType"] in ("WORD", "WORDELD")


def _norm(token):
    return token["sValue"].rstrip("'’").lower()


def _word_readings(spell, token):
    "les analyses d'un mot, chacune en liste d'étiquettes (« :A:e:p » → [A, e, p])"
    value = _norm(token) if token["sType"] == "WORDELD" else token["sValue"]
    morphs = spell.getMorph(value)
    if not morphs and value[:1].isupper() and not value[1:2].isupper():
        morphs = spell.getMorph(value.lower())
    readings = []
    for morph in morphs:
        pieces = morph.split("/")
        tags = pieces[1] if len(pieces) > 1 else ""
        readings.append([p for p in tags.split(":") if p])
    return readings


def _has(parts, tag):
    return tag in parts


# ------------------------------------------------------------ calibrage

if __name__ == "__main__":
    import os
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import grammalecte
    from grammalecte.graphspell.tokenizer import Tokenizer
    checker = grammalecte.GrammarChecker("fr")
    spell = checker.getSpellChecker()
    engine = checker.getGCEngine()
    tokenizer = Tokenizer("fr")
    # (phrase, mots qui doivent rester relevés)
    cases = [
        ("Les roulés au fromage apocalyptiques", []),
        ("Les petits roulés au fromage apocalyptiques.", []),
        ("Des gâteaux au chocolat amers.", []),
        ("Un roulé au fromage apocalyptiques.", ["apocalyptiques"]),
        ("Les pupilles gonflées comme des ballons, il abordait un air de déterré ou d'échappé d'asile, au choix.", []),
        ("Elle avait une mine de déterré.", []),
        ("J'ai envie de mangé une pomme.", ["mangé"]),
        ("Il a essayé de mangé.", ["mangé"]),
        ("Les chat mange.", ["chat"]),
    ]
    failed = 0
    for text, expected in cases:
        # les règles de grammaire seules (g…), pas la typographie des apostrophes
        raw = [e for e in engine.parse(text, "FR") if (e.get("sRuleId") or "").startswith("g")]
        kept = keep(spell, tokenizer, text, raw)
        words = [text[e["nStart"]:e["nEnd"]] for e in kept]
        ok = words == expected
        failed += 0 if ok else 1
        print(("OK   " if ok else "ECHEC") + " " + text + " -> " + str(words))
    sys.exit(1 if failed else 0)
