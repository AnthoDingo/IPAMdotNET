# TODO

- [ ] Afficher la licence **avant** l'initialisation de la base dans l'assistant `/setup`.
  Aujourd'hui `AnthoDingo.Setup` n'insère les étapes supplémentaires (`ISetupExtraStep`) qu'après la création du compte admin : un refus de licence laisse une base migrée avec un admin, mais une installation non terminée.
  Pistes : faire évoluer `AnthoDingo.Setup` pour permettre une étape avant l'étape 1, ou remplacer la page intégrée par une page maison (`UseSetupGate()`).

## Partie Réseau — écarts restants avec phpIPAM

- [ ] Tester les migrations et les pages réseau sur **PostgreSQL et MySQL** (seul SQL Server LocalDB a été testé).
- [ ] Adresses IP dans les sous-réseaux (et taux d'occupation, widgets Top 10 du tableau de bord).
- [ ] Permissions par section (aujourd'hui : lecture pour tous, modification réservée au rôle Admin).
- [ ] Domaines L2 pour les VLAN (aujourd'hui un numéro de VLAN est unique globalement).
- [ ] NAT : lier source/destination aux objets sous-réseaux/adresses et à un équipement (aujourd'hui saisie d'une adresse ou d'un réseau).
- [ ] BGP : association des sous-réseaux annoncés à un pair.
- [ ] DNS : intégration PowerDNS (aujourd'hui seuls les jeux de serveurs de noms existent).

## Partie Infrastructure — écarts restants avec phpIPAM

- [ ] Tester sur **PostgreSQL et MySQL** (seul SQL Server LocalDB a été testé).
- [ ] Emplacements : carte intégrée (aujourd'hui lien vers OpenStreetMap).
- [ ] Racks : face arrière et numérotation descendante (aujourd'hui face avant, U1 en bas).
- [ ] Équipements : page de détail avec les adresses IP rattachées (dépend des adresses IP), visibilité par section.
- [ ] Circuits : extrémités sur un équipement (aujourd'hui sur un emplacement), types de circuits paramétrables, circuits logiques.
- [ ] Clients : coordonnées GPS et objets VLAN / adresses rattachés.
