#!/usr/bin/env python3
# Marabook — le pont Grammalecte (batch 29, lot B).
#
# Un processus LONG piloté par Marabook en sous-processus : l'initialisation
# de Grammalecte (~1 s) se paie UNE fois, puis chaque vérification coûte le
# temps d'analyse réel. Jamais de socket (le mode serveur est proscrit —
# voir APPROVISIONNEMENT.md) : stdin/stdout uniquement.
#
# ============================== CONTRAT DE TRAME ==============================
# Un objet JSON par ligne, dans les deux sens ; les sauts de ligne internes
# sont échappés par json.dumps — une ligne EST un message, toujours.
#
# Au démarrage, le pont annonce :
#   {"ready": true, "version": "<version Grammalecte>"}
#
# Requête (Marabook → pont) :
#   {"id": <entier>, "text": "<paragraphe>", "options": {"gn": true, ...}}
#   - id      : jeton d'appariement, RENDU TEL QUEL dans la réponse — c'est
#               lui qui apparie la réponse à sa demande dans le pipeline
#               différé de Marabook.
#   - text    : le paragraphe entier (l'accord a besoin de la phrase), déjà
#               normalisé NFC par Marabook. Les offsets rendus (nStart/nEnd)
#               sont des indices de POINTS DE CODE dans CE texte — la
#               conversion vers les unités UTF-16 du pivot appartient à
#               Marabook (OffsetMapper).
#   - options : le jeu d'options COMPLET, envoyé à chaque requête (le pont
#               reste sans état — pas de set_options rémanent).
#
# Réponse (pont → Marabook) :
#   {"id": <entier>, "errors": [{"nStart": .., "nEnd": .., "sRuleId": "..",
#                                "sType": "..", "sMessage": "..",
#                                "aSuggestions": ["..", ...]}, ...]}
# En cas d'échec d'analyse d'UNE requête :
#   {"id": <entier>, "error": "<message>"}
# Toute ligne inanalysable côté Marabook vaut « grammaire indisponible »,
# jamais une tentative de rattrapage.
#
# Commande de sortie propre : {"quit": true}  (sinon : fin de stdin, ou kill).
# ==============================================================================

import io
import json
import sys

# UTF-8 EXPLICITE des deux côtés, quelle que soit la console/codepage Windows
# (PYTHONIOENCODING est posé par Marabook en ceinture ; ceci est la bretelle).
# stdin en utf-8-SIG : le wrapper StandardInput de .NET Framework écrit le
# PRÉAMBULE de Console.InputEncoding dans le tube au moment où on y accède
# (AutoFlush=true dans son getter) — sous une console en codepage 65001, un
# BOM précède donc la première requête. utf-8-sig l'avale s'il existe et se
# comporte comme utf-8 sinon. (Attrapé par la sonde UI du batch 29 : la
# PREMIÈRE requête mourait d'un « Unexpected UTF-8 BOM ».)
sys.stdin = io.TextIOWrapper(sys.stdin.buffer, encoding="utf-8-sig")
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n")

import grammalecte


def main():
    checker = grammalecte.GrammarChecker("fr")
    engine = checker.getGCEngine()
    print(json.dumps({"ready": True, "version": engine.version}), flush=True)
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        request_id = None
        try:
            request = json.loads(line)
            if request.get("quit"):
                break
            request_id = request.get("id")
            text = request.get("text", "")
            options = request.get("options") or None
            errors = []
            for error in engine.parse(text, "FR", dOptions=options):
                errors.append({
                    "nStart": error.get("nStart"),
                    "nEnd": error.get("nEnd"),
                    "sRuleId": error.get("sRuleId", ""),
                    "sType": error.get("sType", ""),
                    "sMessage": error.get("sMessage", ""),
                    "aSuggestions": error.get("aSuggestions", []),
                })
            print(json.dumps({"id": request_id, "errors": errors},
                             ensure_ascii=False), flush=True)
        except Exception as exception:  # le pont ne meurt pas d'UNE requête
            print(json.dumps({"id": request_id, "error": str(exception)}),
                  flush=True)


if __name__ == "__main__":
    main()
