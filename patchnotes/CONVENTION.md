# Patch notes

Un fichier par version, nommé comme `VERSION` : `patchnotes/1.0.2.md`.

Le contenu est du Markdown libre (une liste à puces suffit). Il est repris **tel quel** :

* comme texte de la release GitHub ;
* dans la fenêtre « Nouveautés » de l'application, ouverte depuis l'écran d'accueil, le toast de mise à jour ou Aide › Vérifier les mises à jour.

Un digest simple et efficace, écrit pour les utilisateurs : **jamais de référence à des commits, des branches, des PR ou des URL**.

Le workflow de release refuse de publier sans ce fichier et avertit sur la branche de pré-release. À chaque bump de version, le fichier de la nouvelle version doit donc exister avant la publication.

## Image de tête (versions majeures)

Une version majeure (1.1.0, 2.0.0…) ouvre ses notes sur une image : `patchnotes/marabook-X.Y.Z.jpg` (ou `.png`), posée en PREMIÈRE ligne du Markdown avec son adresse publique, pour que la release GitHub l'affiche :

```
![Marabook 1.1.0](https://raw.githubusercontent.com/Pardaramskha/marabook/main/patchnotes/marabook-1.1.0.jpg)
```

L'application embarque les images du dossier (`*.jpg`, `*.png`) et la fenêtre « Nouveautés » retrouve la sienne par son NOM de fichier, hors ligne — l'adresse n'est lue que par GitHub. Une image large (1100 × 550 environ) sous 500 Ko suffit.

## Beta

Une version beta (les patchs massifs, publiée depuis la branche `beta` quand `VERSION` finit par `-beta`) a ses notes comme les autres : `patchnotes/1.1.0-beta.md`. Elles sont reprises telles quelles dans la pré-release et dans la fenêtre de la beta, sous l'avertissement que l'application y pose elle-même. Le workflow refuse de publier une beta sans ce fichier.

## Notes de version et notes de patch (1.1.1)

La fenêtre « Nouveautés » a deux onglets. **« Notes de version »** montre la majeure du cycle (`x.y.0` : 1.1.0, 2.0.0…), suivie des patchs déjà sortis de ce cycle, chacun après un filet `---` et sous son nom en H1 (`# Patch 1.1.1`). **« Notes du patch »** montre le patch qui arrive (`x.y.z` avec z > 0, ou `-patch-a`). Un patch s'ouvre sur ses notes de patch, une majeure sur ses notes de version.

Rien à écrire de spécial : chaque version garde son fichier `patchnotes/<VERSION>.md`, l'application compose les onglets à partir des notes embarquées et de celles des releases qu'elle a vues en ligne (retenues dans `notes-de-version.json` à côté des réglages ; pour un patch dont la majeure est inconnue de l'installation, la release de la majeure est relue sur GitHub par son tag).
