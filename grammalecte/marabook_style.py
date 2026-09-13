#!/usr/bin/env python3
# Marabook — l'étage STYLE du pont Grammalecte (batch 44).
#
# Rien ici ne vient d'une librairie nouvelle : tout repose sur ce que
# Grammalecte 2.3.0 embarque déjà et que le pont grammatical n'exploitait
# pas — le dictionnaire morphologique de graphspell (étiquettes :W pour
# les adverbes, lemmes des verbes avec temps et personne), le conjugueur
# (fr/conj.py), les formes fléchies (fr/mfsp.py) et le thésaurus de
# Dicollecte (fr/thesaurus.py, 6 Mo, chargé PARESSEUSEMENT à la première
# demande de synonymes — 0,8 s qu'on ne fait pas payer à la grammaire).
#
# Deux services, appelés par marabook_grammar.py :
#
#   analyze_style(text, options) → liste de relevés
#     {"nStart", "nEnd", "kind": "adverb" | "dull", "word", "lemma"}
#     - adverb : un adverbe en -ment (étiquette :W ET suffixe « ment » —
#       « moment », « comment », « aliment » ne sont pas des adverbes,
#       le dictionnaire tranche, jamais la terminaison seule).
#     - dull   : un VERBE TERNE conjugué (lemme dans options["dullVerbs"],
#       défaut DULL_VERBS ci-dessous). Les participes passés ne sont jamais
#       relevés (« fait » adjectif, « été »), un auxiliaire suivi d'un
#       participe non plus (« avait mangé », « est parti » : c'est le verbe
#       plein qui compte), et un mot à double lecture nom/verbe précédé
#       d'un déterminant ou d'une élision est lu comme un nom (« le fait »,
#       « l'être »).
#
#   synonyms(word) → liste de groupes
#     [{"pos": "Verbe" | "Nom" | "Adjectif" | "Adverbe" | "…",
#       "words": [...]}]
#     Les synonymes sont FLÉCHIS comme le mot demandé quand c'est possible :
#     « faisait » (faire, imparfait 3s) → « accomplissait », « réalisait »…
#     via le conjugueur ; « chevaux » (cheval, pluriel) → « étalons »,
#     « poneys » via mfsp / +s ; les adjectifs suivent genre et nombre ;
#     les adverbes sont invariables. Une locution (« sans tarder ») est
#     rendue telle quelle. Les offsets, comme pour la grammaire, sont des
#     POINTS DE CODE du texte reçu : la conversion appartient à Marabook.

from grammalecte.graphspell.spellchecker import SpellChecker
from grammalecte.graphspell.tokenizer import Tokenizer
from grammalecte.fr import conj
from grammalecte.fr import mfsp

# Les verbes ternes « classiques » du roman (la liste d'Antidote, à peu
# près) — remplaçable par options["dullVerbs"] à chaque requête.
DULL_VERBS = ["être", "avoir", "faire", "dire", "mettre", "aller", "voir",
              "donner", "prendre", "pouvoir", "vouloir", "savoir", "falloir"]

# Les étiquettes de TEMPS d'un verbe conjugué (fr/conj.py) — :Q (participe
# passé) est volontairement absent : jamais relevé, jamais fléchi.
_TENSES = (":Ip", ":Iq", ":Is", ":If", ":K", ":Sp", ":Sq", ":E", ":Y", ":P")
_WHO = (":1s", ":1ś", ":2s", ":3s", ":1p", ":2p", ":3p")

_spell = None
_tokenizer = None
_thesaurus = None


def _engine():
    global _spell, _tokenizer
    if _spell is None:
        _spell = SpellChecker("fr")
        _tokenizer = Tokenizer("fr")
    return _spell, _tokenizer


def _get_thesaurus():
    global _thesaurus
    if _thesaurus is None:
        from grammalecte.fr import thesaurus
        _thesaurus = thesaurus
    return _thesaurus


def _morphs(spell, word):
    "les analyses d'un mot ; un mot capitalisé en début de phrase est retenté en minuscules"
    morphs = spell.getMorph(word)
    if not morphs and word[:1].isupper() and not word[1:2].isupper():
        morphs = spell.getMorph(word.lower())
    return morphs


def _lemma(morph):
    "« >faire/:V3_it_q__a:Iq:3s/* » → « faire »"
    head = morph.split("/", 1)[0]
    return head[1:] if head.startswith(">") else head


def _tags(morph):
    "« >faire/:V3_it_q__a:Iq:3s/* » → « :V3_it_q__a:Iq:3s »"
    parts = morph.split("/")
    return parts[1] if len(parts) > 1 else ""


def _is_verb(morph):
    return _tags(morph).startswith(":V")


def _is_conjugated(morph):
    "verbe à un temps relevable (pas un participe passé)"
    tags = _tags(morph)
    if not tags.startswith(":V"):
        return False
    return any(t in tags for t in _TENSES) and ":Q" not in tags


def _is_past_participle(morphs):
    return any(_tags(m).startswith(":V") and ":Q" in _tags(m) for m in morphs)


def _has_pos(morphs, letter):
    "une lecture portant l'étiquette :<letter> (N nom, A adjectif, D déterminant, W adverbe…) — les déterminants s'écrivent :G:D…, on cherche le segment, pas le début"
    return any(_has_tag(_tags(m), letter) for m in morphs)


def _has_tag(tags, letter):
    return (":" + letter) in tags


def _is_invariable(morphs):
    "une lecture d'adverbe, de conjonction ou de préposition : « puis », « soit » — le verbe homographe est l'exception, on se tait"
    return any(_has_tag(_tags(m), "W") or _has_tag(_tags(m), "Cc")
               or _has_tag(_tags(m), "Cs") or _has_tag(_tags(m), "R")
               for m in morphs)


def analyze_style(text, options):
    spell, tokenizer = _engine()
    options = options or {}
    want_adverbs = options.get("adverbs", True)
    want_dull = options.get("dull", True)
    dull_verbs = set(options.get("dullVerbs") or DULL_VERBS)
    tokens = [t for t in tokenizer.genTokens(text)]
    findings = []
    for i, token in enumerate(tokens):
        if token["sType"] != "WORD":
            continue
        word = token["sValue"]
        morphs = _morphs(spell, word)
        if not morphs:
            continue
        lower = word.lower()
        if want_adverbs and lower.endswith("ment") and _has_pos(morphs, "W"):
            findings.append({
                "nStart": token["nStart"], "nEnd": token["nEnd"],
                "kind": "adverb", "word": word, "lemma": lower,
            })
        if want_dull:
            lemmas = set(_lemma(m) for m in morphs if _is_conjugated(m))
            dull = lemmas & dull_verbs
            if not dull:
                continue
            if _is_invariable(morphs):
                continue
            # Double lecture nom/verbe (« fait », « dit », « sort ») :
            # un déterminant ou une élision juste avant → c'est un nom.
            if _has_pos(morphs, "N") or _has_pos(morphs, "A"):
                previous = _previous_word(tokens, i)
                if previous is not None:
                    if previous["sType"] == "WORDELD":
                        continue
                    if _has_pos(_morphs(spell, previous["sValue"]), "D"):
                        continue
            # Participe après un auxiliaire (« il a fait », « ils ont dit »,
            # « elle est allée ») : le temps composé n'est pas relevé comme
            # verbe terne — la lecture conjuguée homographe (« fait » :Ip:3s)
            # est un leurre (revue du 13/09).
            if _is_past_participle(morphs):
                previous = _previous_word(tokens, i)
                if previous is not None and previous["sType"] == "WORD" and any(
                        _is_conjugated(m) and _lemma(m) in ("avoir", "être")
                        for m in _morphs(spell, previous["sValue"])):
                    continue
            # Participe après un nom, en fin de proposition (« au moment
            # dit », « la chose faite ») : un adjectif, pas un verbe — si le
            # mot a aussi cette lecture.
            if _is_past_participle(morphs):
                previous = _previous_word(tokens, i)
                following = _next_word(tokens, i)
                if (previous is not None and previous["sType"] == "WORD"
                        and following is None
                        and _has_pos(_morphs(spell, previous["sValue"]), "N")
                        and not _has_pos(_morphs(spell, previous["sValue"]), "O")):
                    continue
            # Auxiliaire : « avait mangé », « est parti », « a été » — le
            # verbe plein est le suivant, pas l'auxiliaire.
            if dull & {"avoir", "être"}:
                following = _next_word(tokens, i)
                if following is not None and _is_past_participle(
                        _morphs(spell, following["sValue"])):
                    continue
            findings.append({
                "nStart": token["nStart"], "nEnd": token["nEnd"],
                "kind": "dull", "word": word, "lemma": sorted(dull)[0],
            })
    return findings


def _previous_word(tokens, i):
    j = i - 1
    while j >= 0:
        if tokens[j]["sType"] in ("WORD", "WORDELD"):
            return tokens[j]
        if tokens[j]["sType"] == "PUNC":
            return None
        j -= 1
    return None


def _next_word(tokens, i):
    j = i + 1
    while j < len(tokens):
        if tokens[j]["sType"] == "WORD":
            return tokens[j]
        if tokens[j]["sType"] == "PUNC":
            return None
        j += 1
    return None


# ------------------------------------------------------------ synonymes

_GROUP_POS = {
    "(Verbe)": "V", "(Nom)": "N", "(nom)": "N", "(Adjectif)": "A",
    "(Adverbe)": "W", "(Adjectif Nom)": "AN", "(Preposition)": "R",
}


def _group_matches(group_label, pos_letters):
    "vrai si le groupe du thésaurus correspond à l'une des natures du mot"
    known = _GROUP_POS.get(group_label)
    if known is None:
        return True  # nature inconnue du thésaurus : on garde
    return any(letter in known for letter in pos_letters)


def _pos_label(letter):
    return {"V": "Verbe", "N": "Nom", "A": "Adjectif", "W": "Adverbe"}.get(letter, "")


def synonyms(word):
    if not word:
        return []
    spell, _ = _engine()
    thesaurus = _get_thesaurus()
    morphs = _morphs(spell, word)
    # Les natures (lettres) et, par lemme, la flexion à reproduire.
    readings = []  # (lemma, letters, morph) — letters : les natures lues
    for morph in morphs:
        tags = _tags(morph)
        letters = ""
        if _has_tag(tags, "V"):
            # participe passé employé (« fatigué ») : synonymes d'adjectif
            letters = "A" if ":Q" in tags else "V"
        else:
            for letter in ("A", "N", "W"):
                if _has_tag(tags, letter):
                    letters += letter
        if not letters:
            continue
        readings.append((_lemma(morph), letters, morph))
    if not readings:
        readings.append((word.lower(), "?", ""))
    groups = []
    seen_groups = set()
    for lemma, letters, morph in readings:
        for entry in thesaurus.getSyns(lemma):
            label, words = entry[0], entry[1]
            if letters != "?" and not _group_matches(label, letters):
                continue
            # L'adjectif prime pour la flexion (« grandes » : féminin
            # pluriel), le nom ne connaît que le nombre.
            letter = "A" if "A" in letters else letters[0]
            inflected = []
            for candidate in words:
                if candidate == lemma or candidate == word.lower():
                    continue
                form = _inflect(candidate, letter, morph)
                if form and form not in inflected:
                    inflected.append(form)
            if not inflected:
                continue
            key = (label, tuple(inflected))
            if key in seen_groups:
                continue
            seen_groups.add(key)
            groups.append({"pos": _pos_label(letter) or label.strip("()"),
                           "lemma": lemma, "words": inflected})
    return groups


def _inflect(candidate, letter, morph):
    "fléchit un synonyme comme le mot demandé ; la forme brute si on ne sait pas"
    candidate = candidate.replace(" ", " ")
    if candidate.endswith("(se)"):
        candidate = "se " + candidate[:-4]
    if " " in candidate or "(" in candidate:
        return candidate  # locution : telle quelle
    tags = _tags(morph)
    if letter == "V":
        if not conj.isVerb(candidate):
            return candidate
        tense = next((t for t in _TENSES if t in tags), None)
        who = next((w for w in _WHO if w in tags), None)
        if tense is None or tense in (":Y",):
            return candidate
        if tense == ":P":
            # La clé de personne du participe présent est « :P » lui-même.
            form = conj.getConj(candidate, ":P", ":P")
            return form or candidate
        if who is None:
            return candidate
        if not conj.hasConj(candidate, tense, who):
            return candidate
        return conj.getConj(candidate, tense, who) or candidate
    if letter in ("N", "A"):
        feminine = ":f" in tags and ":m" not in tags
        plural = ":p" in tags and ":s" not in tags
        form = candidate
        if letter == "A" and feminine:
            fem = mfsp.getFemForm(candidate, plural)
            if fem:
                return fem[0]
            # pas de forme connue : accord régulier
            form = candidate if candidate.endswith("e") else candidate + "e"
            return form + "s" if plural and not form.endswith("s") else form
        if plural:
            if mfsp.hasMiscPlural(candidate):
                misc = mfsp.getMiscPlural(candidate)
                if misc:
                    return misc[0]
            if candidate.endswith(("s", "x", "z")):
                return candidate
            if candidate.endswith(("au", "eu")):
                return candidate + "x"
            if candidate.endswith("al"):
                return candidate[:-2] + "aux"
            return candidate + "s"
        return form
    return candidate
