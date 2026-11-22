# Approvisionnement des ressources linguistiques

> Ce document fixe la provenance, les empreintes et les licences des
> ressources tierces embarquées dans Marabook. Vérifié le 8 août 2026 ;
> Grammalecte et Python embarqués le 9 août 2026 (batch 29, voir
> « Réalisé » en fin de document).

## Résumé

| Ressource | Version | Licence | Poids | Embarqué ? |
|---|---|---|---|---|
| Dictionnaires Hunspell français | 7.7 | MPL-2.0 | ~3 Mo | oui, en dur |
| Grammalecte | 2.3.0 (15/12/2025) | GPL-3.0+ | ~24 Mo déployé | oui, en dur, CLI seul |
| Python embeddable (Windows x64) | 3.13.15 (embarqué b29) | PSF | ~10,4 Mo zippé, ~21 Mo déployé | oui, runtime de Grammalecte |
| Python portable (macOS x64/arm64, Linux x64) | 3.13.18, python-build-standalone 20261026 (30/10) | PSF (+ OpenSSL, libffi… du tarball) | ~25–35 Mo compressé | oui, téléchargé et vérifié à la publication (tools/publish.ps1) |
| Motifs de césure du français (hyph-utf8) | V2.13 du 12/05/2016 (embarqués le 20/11/2026) | MIT | 9 Ko | oui, en dur, dans le code (`src/Print/FrenchHyphenPatterns.cs`) |
| Profil ICC « ISO Coated v2 (basICColor) », FOGRA39L | fichier du 02/02/2007, paquet Debian icc-profiles-free 2.4 (embarqué le 21/11/2026) | zlib/libpng | 1,05 Mo | oui, en dur, dans `Marabook.Core` |
| PdfPig (lecture des PDF à l'import) | 0.1.16 (22/08/2026) | Apache-2.0 | 5,5 Mo installés, 16,7 Mo téléchargés | **non** : téléchargée à la demande, après accord |
| PDFium (affichage, recomposition et déverrouillage des PDF — l'Atelier) | 160.0.8165 (16/11/2026) | BSD-3-Clause (PDFium), Apache-2.0 (binaires) | 7 à 8 Mo installés ; 11 Mo (Windows), 7 Mo (macOS) ou 43 Mo (Linux) téléchargés | **non** : téléchargée à la demande, après accord |

### Motifs de césure (20 novembre 2026)

- **Source** : `hyph-fr.tex` du paquet hyph-utf8, dépôt `github.com/hyphenation/tex-hyphen`, branche `master`,
  chemin `hyph-utf8/tex/generic/hyph-utf8/patterns/tex/hyph-fr.tex`.
- **Empreinte SHA-256 du fichier téléchargé** : `526cad6fe8f52fcd3caa3fcaf667cb1740cfc14bca198680e00dc343a84594de` (30 353 octets).
- **Auteurs** : Daniel Flipo, Bernard Gaulle, Arthur Reutenauer. Licence MIT : la notice de droit d'auteur et le texte
  de la licence sont repris en tête du fichier généré.
- **Ce qu'on en garde** : les 1 145 motifs du bloc `\patterns{…}`, tels quels, sans les commentaires. Le fichier C# est
  généré ; pour le régénérer, extraire les motifs et les recoller dans la constante `All`.
### Profil ICC de la condition d'impression (21 novembre 2026)

- **Fichier** : `assets/icc/ISOcoated_v2_bas.ICC`, « ISO Coated v2 (basICColor) », incorporé dans `Marabook.Core` (ressource
  `icc/ISOcoated_v2_bas.ICC`) avec sa licence (`assets/icc/LICENCE-basICColor.txt`).
- **Source** : paquet Debian `icc-profiles-free` 2.4 (section *main*), `icc-profiles-basiccolor-printing2009/default_profiles/printing/`,
  lu sur `sources.debian.org`. L'empreinte du fichier reçu est celle que publie l'API de Debian pour ce chemin.
- **Empreinte SHA-256** : `7cdd1a643a1703fe62c30cd0cd2aba0fb01ae8d5a1ac985c8f8964ff73e4fae0` (1 052 608 octets). Elle est en dur dans
  `src/Print/PdfX.cs` et vérifiée à chaque lecture : un fichier différent n'est jamais incorporé à un PDF.
- **Licence** : zlib/libpng, © 2007-2010 basICColor GmbH — usage, modification et redistribution libres ; ne pas se dire l'auteur,
  marquer une version modifiée, garder la notice. Le fichier est livré NON modifié.
- **Ce qui a été contrôlé dans le fichier** : profil ICC v2.1, classe `prtr`, espace CMJN, PCS Lab ; la taille déclarée est celle du
  fichier ; son identifiant MD5 interne se recalcule à l'identique ; 12 balises, toutes ordinaires (desc, cprt, wtpt, bkpt, A2B0-2,
  B2A0-2, gamt, targ), toutes dans le fichier, 9 octets de remplissage hors balises, tous nuls ; aucun contenu étranger (ni
  adresse web, ni script, ni exécutable, ni archive). La balise `targ` porte les 1 617 mesures de la caractérisation
  **FOGRA39L** (papier 95 / 0 / −2 ; cyan 55 / −37 / −50 ; magenta 48 / 74 / −3 ; jaune 89 / −5 / 93 ; noir 16 / 0 / 0).
  Encrage total maximal des tables de séparation : 330 %.
- **Écartés** : « Coated FOGRA39 » d'Adobe (licence de distribution à contre-signer par l'utilisateur final, modification
  interdite) et « ISO Coated v2 » de l'ECI (ni modification ni vente sans accord écrit ; *non-free* chez Debian).

### PdfPig, dépendance téléchargée à la demande (21 novembre 2026)

- **N'est PAS livrée avec Marabook.** Elle est téléchargée la première fois qu'un PDF est importé, après accord de l'auteur,
  dans `<données>/dependances/pdfpig-0.1.16/`.
- **Source unique** : `https://api.nuget.org/v3-flatcontainer/pdfpig/0.1.16/pdfpig.0.1.16.nupkg` (dépôt officiel NuGet ;
  dépôt du projet : `github.com/UglyToad/PdfPig`, commit `a7bb35662bbbf405efddad50aedc9bcdcf515afc` d'après le nuspec). Licence Apache-2.0.
- **Empreinte SHA-512 de l'archive** (base64) : `clPAR660u7oGMGO0+I4JAq3olrsbIcKO2m3JtAuOow+IbS62Pyfh00GFnG10Ngl2nizTS5+DsCSlndW6sLvNBQ==`,
  16 710 625 octets — recoupée avec le `packageHash` du catalogue NuGet (`catalog0/data/2026.08.22.08.11.05/pdfpig.0.1.16.json`).
- **Ce qui en est tiré** : six bibliothèques de `lib/net8.0/` (aucune dépendance déclarée pour net8.0), chacune à son SHA-256 :
  - `UglyToad.PdfPig.dll` — `76b49537658d3ec0a56e33c9551c115136ea71ad3866815c24b34370b1d3861a`
  - `UglyToad.PdfPig.Core.dll` — `35995ff06a995fff5237b25b992becea340d2a85af444f0f5cac6b39dc75af1c`
  - `UglyToad.PdfPig.Fonts.dll` — `f1b8263824330714b9cb7d0876c12103e3af913f9474bc55ced078b5d5e56506`
  - `UglyToad.PdfPig.Tokenization.dll` — `21f8d67bbe60f00f9f6b1bfc347c03db4606338a008e8d9d1dcf1073be7020d2`
  - `UglyToad.PdfPig.Tokens.dll` — `75a1e796a4a25d0dc116b94e4a1bdd4ad1c3785bab362b911d6d26043856fa38`
  - `UglyToad.PdfPig.Package.dll` — `5da6b80a0a23552806726801bf7f0a48521bb394268a6f9f8d4faf8da002682a`
- **Garde-fous** (`src/Exchange/PdfPigPackage.cs`) : hôte et version en dur, HTTPS, aucune redirection suivie ; taille et SHA-512
  de l'archive vérifiés avant toute écriture ; seuls les six fichiers nommés sont extraits, chacun vérifié ; les six sont
  revérifiés à CHAQUE chargement. Monter de version = changer ces constantes après avoir refait ce relevé.

### PDFium, dépendance téléchargée à la demande (21 novembre 2026)

- **N'est PAS livré avec Marabook.** L'Atelier (PDFaah) le télécharge la première fois que Chop-Chop ou l'Unlocker en a
  besoin, ou quand l'auteur clique « Installer », après son accord, dans `<données>/dependances/pdfium-160.0.8165/`.
  Il se retire de la même page (« Désinstaller »).
- **Ce que c'est** : le moteur PDF de Chromium (Google, BSD-3-Clause), compilé et publié chaque semaine par Benoît Blanchon
  (`github.com/bblanchon/pdfium-binaries`, commit `83c18af693945b735503665259b728ba7d409fd0` d'après le nuspec ; binaires
  sous Apache-2.0). Version `160.0.8165` = Chromium 8165, publiée sur NuGet le 16/11/2026.
- **Source unique** : le dépôt officiel NuGet, un paquet par système —
  `https://api.nuget.org/v3-flatcontainer/bblanchon.pdfium.{win32|linux|macos}/160.0.8165/…nupkg`.
- **Empreintes SHA-512 des archives** (base64), recoupées avec le `packageHash` du catalogue NuGet de chacune :
  - `bblanchon.pdfium.win32` — 11 367 600 octets — `bf5UskRyHdeDXnRX1h5MStuab1U8c4r2vd7JfY6OYn2cJvV/CMVfSknEO7qqmA5anGxQiAVVGEMhvdgNNjfsqw==`
    (`catalog0/data/2026.11.16.17.42.29/bblanchon.pdfium.win32.160.0.8165.json`)
  - `bblanchon.pdfium.linux` — 43 492 415 octets — `UeHyeQHanssVVZmIHa4XTCvUCfHTt4043LODXv4+BTJqPDvvftr6vOVji/2x3ENKfFMZeawEbsiYARfdHhV4jQ==`
    (`catalog0/data/2026.11.16.17.48.40/bblanchon.pdfium.linux.160.0.8165.json`)
  - `bblanchon.pdfium.macos` — 7 353 296 octets — `zwfBKEC/VaZOe5lj4iW8vNRItxpwwnT2lYaHhQ7CF+ySn+ndM4u5Cj/9cGoUNQ4CAPe5muCDZQavmqgASFAmgQ==`
    (`catalog0/data/2026.11.16.17.41.15/bblanchon.pdfium.macos.160.0.8165.json`)
- **Ce qui en est tiré** : UN fichier, la bibliothèque native du système et du processeur en cours
  (`runtimes/<système>/native/…`), à son SHA-256 :
  - `win-x64/pdfium.dll` — `31d249b8535dd5f351ae7f4b714dc4bb1d76b6069b321a309bc99271dd65f566`
  - `win-arm64/pdfium.dll` — `9b1eb281e6f8b2da9f54fc71619198c90142adab2f57f5251a4824a08da621b3`
  - `linux-x64/libpdfium.so` — `63b70290e3e88eff6b236a4c87cb58461b4fe9fae56217314eeda31aff741479`
  - `linux-arm64/libpdfium.so` — `963309fe119e4614afb750bc6a4e35272bd5b8bad61fb84df282c9c821f7de8b`
  - `osx-x64/libpdfium.dylib` — `fcbfee9dc7f895289fbfb8f7d1f0d5d643f60fcb790708425daaa0cfa64f330d`
  - `osx-arm64/libpdfium.dylib` — `d421e426bd487de02fa3bd1f2aeaf56947261f790e6116951e26f729ced1b018`
- **Second recoupement** : le `pdfium.dll` de `win-x64` a la même empreinte que celui de l'archive
  `pdfium-win-x64.tgz` de la publication GitHub `chromium/8165` du même projet (deux canaux, un seul fichier).
  Les cinq autres n'ont été recoupés qu'avec NuGet.
- **Ce qui n'a PAS été fait** : aucun des six fichiers n'a été compilé ici, ni passé à un antivirus tiers ; seul
  `win-x64` a été chargé et exercé (sonde `MARABOOK_PROBE_ONLY=atelier`). La version a cinq jours au moment du relevé.
- **Garde-fous** (`src/Exchange/PdfiumPackage.cs`, `dotnet/Marabook.App/Pdfium.cs`) : hôte et version en dur, HTTPS, aucune
  redirection suivie ; taille et SHA-512 de l'archive vérifiés avant toute écriture ; un seul fichier extrait, vérifié ;
  revérifié à CHAQUE chargement, et chargé par son chemin complet (jamais cherché dans le système). Rien des fichiers de
  l'auteur ne part : PDFium travaille en local. Monter de version = changer ces constantes après avoir refait ce relevé.

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

À côté, **`dict/fr-complement.dic`** est un fichier MAISON (Marabook 1.0.5,
10/10/2026), pas une ressource Grammalecte : les mots que le romancier emploie et
que `fr-toutesvariantes` n'a pas — registre familier ou vulgaire, régionalismes,
mots rares attestés, interjections et onomatopées — avec les drapeaux du même
`.aff`. Il n'est PAS soumis à l'empreinte : on l'édite librement (un mot par
ligne, `#` ouvre un commentaire) ; le `.dic` de Grammalecte, lui, ne bouge jamais.
Diagnostics : `Marabook --mot a,b,c` (accepté / refusé, radicaux, suggestions)
et `Marabook --ortho projet.plot` (les mots inconnus de chaque écrit).

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

## Réalisé (batch 29, 9 août 2026)

- **Grammalecte 2.3.0** téléchargé depuis `grammalecte.net`, SHA256 du zip
  conforme (`aaa42197…`), déployé dans `grammalecte/` — **sans
  `grammalecte-server.py` ni `grammalecte/bottle.py`** (retirés comme
  prescrit), sans `setup.py` ni `Dockerfile` (surface minimale). Les
  `LICENSE*.txt` et `README.txt` amont accompagnent le paquet. Le pilotage
  réel est `grammalecte/marabook_grammar.py` (à nous) : processus long
  stdin/stdout, **pas** `grammalecte-cli.py -ff` — lu avant de trancher,
  son mode interactif est inutilisable en
  machine (sortie texte sans JSON, apostrophes réécrites sous Windows) et
  son mode fichier paie l'initialisation à chaque appel.
- **Python embeddable 3.13.15 (amd64)** téléchargé depuis `python.org`,
  **signature GPG « Good signature » vérifiée** — clé de **Steve Dower
  (Python Release Signing, Windows binaries)**, empreinte
  `7ED1 0B65 31D7 C8E1 BC29 6021 FC62 4643 4870 34E5`, recoupée avec la
  liste officielle `python.org/downloads/metadata/pgp/`. (Les binaires
  Windows sont signés par lui, pas par le release manager de la 3.13 —
  détail qui surprend, vérifié.) SHA256 du zip :
  `d1f04d990aee1253d8569e8e5104e30fa9f5fa830899f14843448872d936a2cf`.
  Déployé dans `python/` ; `python313._pth` ajusté (une ligne
  `../grammalecte` — sa présence fige `sys.path`, ni PYTHONPATH ni
  répertoire du script n'y entrent).
- **Python portable pour macOS et Linux (30/10/2026, Rémi : « on ne VEUT
  PAS que Grammalecte manque »)** : `python-build-standalone` (astral-sh,
  release `20261026`), CPython **3.13.18** en variante `install_only_stripped`,
  un tarball par cible (`x86_64-unknown-linux-gnu`, `x86_64-apple-darwin`,
  `aarch64-apple-darwin`). Pas dans le dépôt : `tools/publish.ps1` le
  télécharge depuis la release GitHub au moment de fabriquer l'archive,
  **vérifie son SHA-256 épinglé dans le script** (empreintes relevées sur
  l'API GitHub le 30/10/2026) et le déballe dans `python/` ; les dossiers
  `test`, `idlelib`, `tkinter`, `turtledemo` et les `__pycache__` sont
  retirés. `Platform.PythonExecutable` préfère `python/bin/python3` au
  python3 du système. Sur macOS, chaque binaire (`python3.13`, `.dylib`,
  `.so`) est signé ad hoc avant le bundle. Pas de `._pth` : le répertoire
  du script (`grammalecte/`) entre dans `sys.path` de lui-même. Pour
  changer de version : les trois noms et empreintes de `$pythonBuilds`.
- **Vérification au build étendue** : `tools/embedded-hashes.txt` (78
  empreintes SHA256 des deux arbres, générées depuis ces sources
  vérifiées) contrôlé par `tools/verify-dict.ps1` à chaque build — un
  arbre absent EN BLOC est toléré (la grammaire se tait), un arbre partiel
  ou altéré fait échouer la compilation. `__pycache__/` (créé au premier
  lancement) est ignoré de git et du manifeste.

### Batch 44 (13/09/2026) — l'étage style puise dans le paquet, rien de neuf

Les adverbes en -ment, les verbes ternes et les synonymes n'ajoutent
AUCUNE ressource : `grammalecte/marabook_style.py` (à nous, GPL-3 comme
le reste) appelle le dictionnaire morphologique de graphspell, le
conjugueur `fr/conj.py`, les flexions `fr/mfsp.py` et le thésaurus
`fr/thesaurus_data.py` (données Dicollecte, déjà dans le paquet 2.3.0,
déjà couvertes par les empreintes). Le pont `marabook_grammar.py` reste
le seul processus. À ne pas retirer du déploiement : `fr/thesaurus*.py`,
`fr/conj*.py`, `fr/mfsp*.py`.

### 20/11/2026 — le conjugueur, EPUBCheck, le Wiktionnaire : rien de neuf n'est embarqué

- **Le conjugueur** (Édition › Conjugueur…, clic droit › « Conjuguer… »)
  est une requête de plus du même pont (`{"conjugate": …}`), servie par
  `marabook_style.conjugate` avec `fr/conj.py` — déjà dans le paquet.
- **EPUBCheck** (le validateur officiel du W3C, licence BSD-3) n'est
  **pas embarqué** : il lui faut Java. `src/Exchange/EpubCheckTool.cs` le
  cherche sur la machine — le lanceur `epubcheck` dans le PATH, la
  variable d'environnement `EPUBCHECK` (un `.jar` ou un lanceur), ou un
  dossier `epubcheck-x.y.z` déballé dans `<données de Marabook>/outils`,
  le dossier personnel ou les dossiers de programmes, avec `java` dans le
  PATH ou `JAVA_HOME`. Rien n'est téléchargé ; sans lui, le contrôle de
  structure maison (`EpubCheck.Verify`) reste seul.
- **Le Wiktionnaire** n'est pas embarqué non plus (poids, licence
  CC BY-SA des définitions) : clic droit › « Chercher … sur le
  Wiktionnaire » ouvre `https://fr.wiktionary.org/wiki/<mot>` dans le
  navigateur de l'utilisateur. C'est le seul moment où un mot du texte
  quitte la machine, et seulement sur ce clic — l'infobulle le dit.

## Non vérifié

- **EPUBCheck réel** : la lecture de son rapport JSON et la recherche de
  l'outil sont testées (C83), et le test valide l'EPUB de Marabook avec
  le vrai outil **quand il est installé** — il ne l'était pas sur la
  machine de développement le 20/11/2026 : cette validation n'a pas été jouée.

- Versions et URL amont déclarées chez Fedora, openSUSE et Arch.
- Régénération complète des `.dic`/`.aff` depuis le lexique source :
  `gc_lang/fr/dictionnaire/genfrdic.py` est bien présent dans le dépôt
  Grammalecte, mais le fichier lexique d'entrée n'a pas été localisé.
  *(La concordance à trois sources est de toute façon une garantie plus forte
  et bien moins coûteuse qu'une reconstruction.)*
- Termes juridiques exacts de la cession Olivier R. → Algoo.
