# Patch notes

Un fichier par version, nommé comme `VERSION` : `patchnotes/1.0.2.md`.

Le contenu est du Markdown libre (une liste à puces suffit). Il est repris **tel quel** :

* comme texte de la release GitHub ;
* dans la fenêtre « Nouveautés » de l'application, ouverte depuis l'écran d'accueil, le toast de mise à jour ou Aide › Vérifier les mises à jour.

Un digest simple et efficace, écrit pour les utilisateurs : **jamais de référence à des commits, des branches, des PR ou des URL**.

Le workflow de release refuse de publier sans ce fichier et avertit sur la branche de pré-release. À chaque bump de version, le fichier de la nouvelle version doit donc exister avant la publication.

## Beta

Une version beta (les patchs massifs, publiée depuis la branche `beta` quand `VERSION` finit par `-beta`) a ses notes comme les autres : `patchnotes/1.1.0-beta.md`. Elles sont reprises telles quelles dans la pré-release et dans la fenêtre de la beta, sous l'avertissement que l'application y pose elle-même. Le workflow refuse de publier une beta sans ce fichier.
