# Approvisionnement des ressources linguistiques

> Ce document fixe la provenance, les empreintes et les licences des
> ressources tierces embarquées dans Marabook. Vérifié le 8 août 2026.

## Résumé

| Ressource | Version | Licence | Poids | Embarqué ? |
|---|---|---|---|---|
| Dictionnaires Hunspell français | 7.7 | MPL-2.0 | ~3 Mo | oui, en dur |
| Grammalecte | 2.3.0 (15/12/2025) | GPL-3.0+ | ~24 Mo déployé | oui, en dur, CLI seul |
| Python embeddable (Windows x64) | 3.13.x | PSF | ~10,4 Mo | oui, runtime de Grammalecte |

## Sources interdites

- **`dicollecte.org`** — domaine abandonné puis **réenregistré par un tiers**.
  Redirige aujourd'hui vers du spam. Tout lien historique pointant vers ce
  domaine est à considérer comme hostile. Ne jamais y télécharger quoi que ce soit.
- **`codeberg.org/dicollage/dictionnaires`** — miroir personnel, archive de la
  **7.0** (périmée : l'amont est en 7.7), extensions « recréées » et XPI
  « modifié » à la main. Pas de caution tierce. Sans objet depuis la reprise
  de l'amont.
- **`extensions.libreoffice.org` (fiche « Dictionnaires français »)** — bloquée
  en **v5.7 du 5 mars 2020**. Le dépôt git de LibreOffice, lui, suit bien la 7.7.

## Amont officiel

**`https://www.grammalecte.net/`** — projet d'Olivier R., repris par la société
**Algoo** (Chambéry) début 2026.

- Dictionnaires : `https://www.grammalecte.net/dic/hunspell-french-dictionaries-v7.7.zip`
- Grammalecte : `https://www.grammalecte.net/zip/Grammalecte-fr-v2.3.0.zip`

⚠️ **`grammalecte.net` ne publie ni somme de contrôle ni signature GPG.**
C'est le point faible de la chaîne, et la raison d'être de la contre-vérification
Debian ci-dessous : le `.dsc` de Debian est signé et fixe le SHA256 de l'archive
amont. C'est notre substitut à la signature manquante.

## Empreintes à vérifier

### Archives

```
44314d992f94b4658c31a86ef2351724a43067531b0af3643f91bf0220eee616  hunspell-french-dictionaries-v7.7.zip
aaa4219704857778038ecc1db18f6a907994c0125b8e762dcecc69130280684f  Grammalecte-fr-v2.3.0.zip
8f595a322bd942816d5d4087d420a8bb414e5369c9f89df6ffa3cf60d5ae56e6  hunspell-fr_7.7.orig.tar.xz  (Debian)
```

### Les six fichiers de dictionnaire

Identiques dans les trois sources (grammalecte.net, Debian, LibreOffice git) :

```
b78a868e31dd6e373b6c3217969afb898a9acde828a5e7ef97308da42218c88c  fr-classique.dic
c176610cd5dc4846806a65ddd029f422d87978bf58f224aa44222662a16a2de5  fr-classique.aff
66874125c8827331e53e9184e5581e8e0a7d8a2371b4a0f9374a9a7bbcff2cb5  fr-reforme1990.dic
306c9ea8742064a4b4f4ba14c7de92ca7e7747e4710c5ba182ddf717f5bc57ea  fr-reforme1990.aff
140c5296b514bc55af7433f8913a73245b9b18116830a7db48e2aabfa85fcbed  fr-toutesvariantes.dic
254756d4fd286a923b3f51dae65965bfc8619aa96db8528326bf31fcdc0840e4  fr-toutesvariantes.aff
```

Marabook embarque **`fr-toutesvariantes`** (86 491 entrées lemmatisées, tolère
les graphies classiques et rectifiées — le bon choix par défaut pour un
romancier, qui n'a pas à choisir un camp orthographique).

## Protocole de vérification

1. Télécharger l'archive depuis `grammalecte.net`, vérifier son SHA256.
2. **Contre-vérifier par Debian** — c'est l'étape qui remplace la signature GPG :
   - récupérer et vérifier `hunspell-fr_7.7-1.dsc`
     (`deb.debian.org/debian/pool/main/h/hunspell-fr/`) ;
   - télécharger `hunspell-fr_7.7.orig.tar.xz`, confirmer son SHA256 ;
   - décompresser les deux, `diff -rq` → **doit être vide**.
   Mainteneur Debian : Sébastien Villemot (Debian Developer).
3. **Troisième source** : comparer `fr_FR/dictionaries/fr.dic` et `fr.aff` du
   dépôt `git.libreoffice.org/dictionaries` à `fr-classique.dic`/`.aff` →
   égalité exacte attendue.
4. Grammalecte : vérifier le SHA256 du zip, recouper avec le tag correspondant
   de `github.com/algoo/grammalecte`.
5. **Python embeddable** : télécharger depuis `python.org` **et vérifier le
   `.asc` GPG (clé du release manager) ou le `.sigstore`**. C'est le seul
   maillon cryptographiquement signé de toute la chaîne — ne pas le sauter.
6. **Le build vérifie ces empreintes et échoue en cas d'écart.**

## Ce qu'on embarque de Grammalecte, et ce qu'on jette

**Retirer avant d'embarquer** :

- `grammalecte-server.py`
- `grammalecte/bottle.py`

Motif : le mode serveur écoute sur `127.0.0.1:8080` **sans authentification,
sans en-tête CORS et sans validation de `Host` ni d'`Origin`**. Un `POST` de
formulaire depuis une page web part sans préflight (écritures de type CSRF sur
`set_options`), et l'absence de validation du `Host` ouvre la porte au DNS
rebinding, qui contourne la same-origin policy. L'impact reste limité — le
serveur ne lit pas de fichiers locaux — mais c'est de la surface d'attaque
**entièrement gratuite** : le mode CLI couvre tout le besoin.

*(Note : le `bottle` embarqué est en 0.13.4, la version la plus récente publiée,
et les quatre CVE connues de bottle sont toutes corrigées bien avant. On le
retire par principe de surface minimale, pas parce qu'il est vulnérable.)*

**Pilotage retenu** :

```
grammalecte-cli.py -ff <fichier temporaire UTF-8> -j -ctx
```

Options utiles : `-wss` (suggestions orthographiques), `-pdi` (dictionnaire
personnel), `-on`/`-off`/`-roff` (les 39 options : `gn`, `conj`, `ppas`,
`conf`, `pleo`, `typo`, `nbsp`, `virg`, `redon1`, `redon2`…), `-tf`
(formateur typographique). Sortie JSON avec offsets `nStart`/`nEnd`,
`sRuleId`, `aSuggestions`.

**Deux garde-fous obligatoires** :

- **Timeout sur le sous-processus.** Le seul risque réaliste est un ReDoS :
  un texte pathologique qui fait tourner le moteur de règles indéfiniment.
- **Répertoire temporaire dédié**, nettoyé. Sous Windows, `-f` se comporte
  comme `-ff` : passer par des fichiers, pas par stdin.

## Licences — obligations concrètes

Les trois se combinent sans difficulté ni démarche à effectuer.

**Dictionnaires — MPL-2.0.** Sans l'Exhibit B, donc le §3.3 autorise
explicitement la distribution de l'œuvre combinée sous GPL. Copyleft **au
fichier** : les `.dic`/`.aff` restent sous MPL-2.0 au sein d'un ensemble GPL-3.

- Les livrer comme **fichiers séparés**, jamais fusionnés dans les sources.
- Conserver **`README_dict_fr.txt` intact** à côté d'eux : c'est lui qui porte
  la notice de licence et le crédit à Olivier R.
- Mettre l'archive 7.7 d'origine à disposition (« Source Code Form »).

**Grammalecte — GPL-3.0+.** Même licence que Marabook. On distribue le source
Python, qui *est* le source : rien de plus à faire.

**Python embeddable — PSF.** Permissive, redistribution sans difficulté.

**Écran « À propos »** : lister les trois, avec versions et liens.

## Entretien

- Re-vérifier `grammalecte.net` une à deux fois par an (dictionnaires 7.8+,
  Grammalecte 2.4+).
- **Mettre à jour le runtime Python embarqué à chaque version corrective**,
  même si Grammalecte reste figé. Figer Grammalecte est peu risqué ; figer
  l'interpréteur l'est davantage.
- La dégradation fonctionnelle d'un Grammalecte figé se compte en années :
  faux positifs non corrigés, néologismes et noms propres absents du lexique.

## Non vérifié

- Versions et URL amont déclarées chez Fedora, openSUSE et Arch.
- Régénération complète des `.dic`/`.aff` depuis le lexique source :
  `gc_lang/fr/dictionnaire/genfrdic.py` est bien présent dans le dépôt
  Grammalecte, mais le fichier lexique d'entrée n'a pas été localisé.
  *(La concordance à trois sources est de toute façon une garantie plus forte
  et bien moins coûteuse qu'une reconstruction.)*
- Termes juridiques exacts de la cession Olivier R. → Algoo.
