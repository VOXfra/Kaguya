AC1-to-ACE Converter v0.1.5 — Donor Safety Upgrade

BUT
- Corriger le défaut de v0.1.4 qui pouvait choisir un donneur mécaniquement absurde (ex: Ford Escort RS Cosworth pour une Bugatti Mistral).
- Garder le pipeline v0.1.4 déjà corrigé (KSPKG 64/32 MiB, audit bloquant, dépendances, métadonnées).
- Forcer le donneur sélectionné via le CLI existant, sans réécrire les données du jeu.

INSTALLATION
1. Garde le ZIP ou le dossier v0.1.4 que tu as déjà téléchargé.
2. Double-clique INSTALL-v0.1.5.bat.
3. L'upgrade cherche automatiquement la v0.1.4 dans le dossier courant et dans Downloads.
4. Il crée:
   %USERPROFILE%\Downloads\AC1-to-ACE-Converter-v0.1.5\
   %USERPROFILE%\Downloads\AC1-to-ACE-Converter-v0.1.5.zip
5. Lance AC1-to-ACE-v0.1.5.bat dans ce nouveau dossier.

SELECTION DONNEUR
Le score tient compte de:
- catégorie véhicule (garde-fou dur sur les incompatibilités flagrantes);
- transmission;
- empattement;
- masse;
- nombre de rapports;
- régime limiteur;
- freinage quand disponible.

Une Mistral ne peut plus sélectionner automatiquement une Escort: hyper/supercar ↔ rally/compact est rejeté.
Si aucun donneur n'atteint 58%, la conversion est bloquée avant extraction du donneur.
Le Top 10 est enregistré sous <id>.donor-ranking.v015.json dans le dossier de sortie.

IMPORTANT
- Un score élevé réduit le risque d'hériter de mauvaises données résiduelles du donneur; il ne prouve pas à lui seul la jouabilité en jeu.
- L'audit final v0.1.4 reste obligatoire.
- La validation définitive reste: voiture visible dans ACE, chargement piste, roulage, roues/visuels/physique cohérents.
