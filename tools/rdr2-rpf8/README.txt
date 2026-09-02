VOX RDR2 RPF8 Cataloger v0.3.0
================================

Catalogue les metadonnees RPF8 des archives RDR2 ciblees pour GTA V Enhanced -> GTA VI.
Aucun fichier du jeu n'est modifie et aucun asset Rockstar n'est extrait.

Cibles par defaut : common_0.rpf, dlc_content_extra, mp004, mp005, mp006, mp008, patchpack001 et S_MISC.rpf.

UTILISATION
-----------
Double-clique Run-VOX-RDR2-RPF8-Cataloger.bat et colle le dossier qui contient RDR2.exe.
Le mode DEEP releve en plus tous les offsets bruts RPF8/RSC8 des archives cibles.

SORTIES A ENVOYER
-----------------
VOX-RDR2-RPF8-Catalog/RPF8-archives.csv
VOX-RDR2-RPF8-Catalog/RPF8-entries.csv
VOX-RDR2-RPF8-Catalog/RPF8-summary.json
Et, si mode DEEP, RPF8-signature-offsets.csv.

IMPORTANT
---------
Une TOC TFIT est detectee mais volontairement non parse. Le programme ne devine pas les cles et n'interprete pas du ciphertext comme des entrees valides.
Les noms du type 1234ABCD.ycd sont des labels hash+extension, pas des noms Rockstar reconstitues.
