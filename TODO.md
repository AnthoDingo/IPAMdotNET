# TODO

- [ ] Afficher la licence **avant** l'initialisation de la base dans l'assistant `/setup`.
  Aujourd'hui `AnthoDingo.Setup` n'insère les étapes supplémentaires (`ISetupExtraStep`) qu'après la création du compte admin : un refus de licence laisse une base migrée avec un admin, mais une installation non terminée.
  Pistes : faire évoluer `AnthoDingo.Setup` pour permettre une étape avant l'étape 1, ou remplacer la page intégrée par une page maison (`UseSetupGate()`).

## Serveur — écarts restants avec phpIPAM

- [ ] Agents de scan : dépend des adresses IP (découverte).
- [ ] Étiquettes : gestion faite (4 étiquettes système + personnalisées) ; reste à les appliquer aux adresses IP (affichage, regroupement des plages, mise à jour par le scan).
- [ ] Langues : l'interface est en français uniquement ; une traduction suppose d'extraire tous les textes (ressources .resx).
- [ ] API : écriture (création / modification), droits par clé, recherche.
- [ ] LDAP : synchronisation des groupes de l'annuaire, création automatique des comptes à la première connexion ; tester sur un vrai annuaire (seul le cas « annuaire injoignable » a pu l'être).
- [ ] Permissions : appliquer les sections au journal des modifications et aux objets rattachés (aujourd'hui : sous-réseaux, recherche, favoris, demandes).
- [ ] Paramètres : en multi-instance, le cache des paramètres n'est rafraîchi qu'au redémarrage des autres instances.

## Partie Réseau — écarts restants avec phpIPAM

- [ ] Tester les migrations et les pages réseau sur **PostgreSQL et MySQL** (seul SQL Server LocalDB a été testé).
- [ ] Adresses IP dans les sous-réseaux (et taux d'occupation, widgets Top 10 du tableau de bord).
- [x] Permissions par section (groupes + accès par défaut, voir partie Serveur).
- [ ] Domaines L2 pour les VLAN (aujourd'hui un numéro de VLAN est unique globalement).
- [ ] NAT : lier source/destination aux objets sous-réseaux/adresses et à un équipement (aujourd'hui saisie d'une adresse ou d'un réseau).
- [ ] BGP : association des sous-réseaux annoncés à un pair.
- [ ] DNS : intégration PowerDNS (aujourd'hui seuls les jeux de serveurs de noms existent).

## Outils — écarts restants avec phpIPAM

- [ ] Demandes d'adresses : créer l'adresse IP à l'acceptation et proposer la première adresse libre (dépend des adresses IP) ; notifications par e-mail.
- [ ] Journal : afficher les noms au lieu des identifiants pour les clés étrangères (VLAN, VRF…), historique sur la fiche de chaque objet.
- [ ] Instructions : mise en forme (Markdown) — aujourd'hui texte brut.
- [ ] Recherche : adresses IP individuelles (dépend des adresses IP).

## Maintenance — écarts restants avec phpIPAM

- [ ] Champs personnalisés : sur les autres objets (NAT, BGP, RTC, sections…), filtres dans les listes, journalisation des valeurs.
- [ ] Import / export : champs personnalisés dans les CSV, mise à jour d'objets existants (aujourd'hui création seule), export Excel.
- [ ] Journaux : erreurs applicatives (exceptions), purge automatique des anciennes entrées.

## Partie Infrastructure — écarts restants avec phpIPAM

- [ ] Tester sur **PostgreSQL et MySQL** (seul SQL Server LocalDB a été testé).
- [ ] Emplacements : carte intégrée (aujourd'hui lien vers OpenStreetMap).
- [ ] Racks : face arrière et numérotation descendante (aujourd'hui face avant, U1 en bas).
- [ ] Équipements : page de détail avec les adresses IP rattachées (dépend des adresses IP), visibilité par section.
- [ ] Circuits : extrémités sur un équipement (aujourd'hui sur un emplacement), types de circuits paramétrables, circuits logiques.
- [ ] Clients : coordonnées GPS et objets VLAN / adresses rattachés.
