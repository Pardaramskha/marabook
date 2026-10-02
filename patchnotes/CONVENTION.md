# Patch notes

Un fichier par version, nommé comme `VERSION` : `patchnotes/1.0.2.md`.

Le contenu est du Markdown libre (une liste à puces suffit). Il est repris **tel quel** :

* comme texte de la release GitHub (workflow `release.yml`, `gh release create --notes-file`) ;
* dans la fenêtre « Nouveautés » de l'application, ouverte depuis l'écran d'accueil, le toast de mise à jour ou Aide › Vérifier les mises à jour.

Le workflow refuse de publier sur `main` sans ce fichier et avertit sur `dev`. À chaque bump de version, le fichier de la nouvelle version doit donc exister avant la fusion dans `main`.
