# TODO

- [x] Afficher la licence **avant** l'initialisation de la base dans l'assistant `/setup` (corrigé dans le package `AnthoDingo.Setup`).

## Serveur — écarts restants avec phpIPAM

- [x] Agent de scan intégré (ping, découverte IPv4, mise à jour des étiquettes) ; désactivable.
- [x] Scan par ports TCP en plus du ping (ports configurables).
- [x] Agents de scan distants (projet `IPAMdotNet.ScanAgent`, service Windows / systemd).
- [x] Agents de scan distants : mise à jour automatique (paquets embarqués par le serveur) ; service systemd testé (Type=notify, relance après mise à jour), notice d'installation (`src/IPAMdotNet.ScanAgent/README.md`).
- [ ] Agents de scan distants : tester l'installation en service Windows (droits administrateur nécessaires, non testée).
- [x] Étiquettes : gestion, affichage dans les adresses, regroupement des plages, mise à jour par le scan.
- [x] Langues : catalogue de traduction façon gettext (`Localization/L.cs`, clé = texte français), langue par défaut du serveur et choix par utilisateur (profil).
- [ ] Langues : remplir les fichiers `Localization/i18n/*.json` (tous vides, l'interface reste en français) ; traduire `/setup` et `/update` ; page Administration › Langues (entrée de menu désactivée).
- [x] Profil : format d'affichage des adresses MAC par utilisateur.
- [x] API : écriture (sous-réseaux, adresses avec première adresse libre, VLAN, VRF, équipements), droits par clé (compte et écriture), recherche.
- [x] API : champs personnalisés en lecture / écriture ; sections, emplacements, clients, racks, serveurs de noms, types, fournisseurs, circuits, NAT, BGP, préfixes et numéros RTC en lecture / écriture.
- [x] API : utilisateurs (mot de passe en écriture seule, groupes), groupes (membres, permissions), étiquettes et définitions de champs personnalisés en écriture.
- [x] LDAP : création des comptes à la première connexion, synchronisation des groupes (memberOf ↔ nom du groupe local), testées sur OpenLDAP.
- [ ] LDAP : tester sur un Active Directory réel (testé sur OpenLDAP avec le module memberof).
- [x] Permissions : sections appliquées au journal des modifications (page et tableau de bord, objets d'administration réservés aux admins), aux statistiques et aux compteurs de sous-réseaux (VLAN, VRF, DNS, emplacements).
- [x] Permissions : droits par clé d'API ; visibilité des équipements par section (aucune section = visible de tous).
- [x] Paramètres : cache relu toutes les 30 s (multi-instance).

## Partie Réseau — écarts restants avec phpIPAM

- [ ] Tester les migrations et les pages réseau sur **PostgreSQL et MySQL** (seul SQL Server LocalDB a été testé).
- [x] Adresses IP dans les sous-réseaux (taux d'occupation, Top 10, recherche, API, CSV).
- [x] Adresses IP : champs personnalisés, ajout en masse d'une plage, affichage visuel.
- [x] Adresses IP : champs personnalisés dans l'API et le CSV ; modification / suppression en masse.
- [x] Import d'une instance phpIPAM (base MySQL ou API).
- [ ] Import phpIPAM : tester contre une vraie API phpIPAM (testé contre une base phpIPAM réelle, et l'API contre un serveur simulé) ; domaines L2, dossiers, sous-réseaux en double par VRF (aujourd'hui fusionnés).
- [x] Permissions par section (groupes + accès par défaut, voir partie Serveur).
- [ ] Domaines L2 pour les VLAN (aujourd'hui un numéro de VLAN est unique globalement).
- [ ] NAT : lier source/destination aux objets sous-réseaux/adresses et à un équipement (aujourd'hui saisie d'une adresse ou d'un réseau).
- [ ] BGP : association des sous-réseaux annoncés à un pair.
- [ ] DNS : intégration PowerDNS (aujourd'hui seuls les jeux de serveurs de noms existent).

## Outils — écarts restants avec phpIPAM

- [x] Demandes d'adresses : adresse demandée si elle est libre, sinon première adresse libre proposée au traitement.
- [x] Journal : libellé des objets référencés (« Serveurs (n°3) »), historique sur la fiche de chaque objet et filtre par objet.
- [ ] Journal : les entrées antérieures gardent les identifiants seuls (libellés résolus à l'écriture).
- [x] Instructions : Markdown (Markdig, HTML désactivé, liens filtrés), avec aperçu.
- [x] Recherche : adresses IP individuelles (IP exacte, nom d'hôte, MAC, propriétaire).

## Maintenance — écarts restants avec phpIPAM

- [ ] Champs personnalisés : sur les autres objets (NAT, BGP, RTC, sections…), filtres dans les listes, journalisation des valeurs.
- [ ] Import / export : mise à jour d'objets existants (aujourd'hui création seule), export Excel.
- [ ] Journaux : erreurs applicatives (exceptions), purge automatique des anciennes entrées.

## Partie Infrastructure — écarts restants avec phpIPAM

- [ ] Tester sur **PostgreSQL et MySQL** (seul SQL Server LocalDB a été testé).
- [ ] Emplacements : carte intégrée (aujourd'hui lien vers OpenStreetMap).
- [ ] Racks : face arrière et numérotation descendante (aujourd'hui face avant, U1 en bas).
- [ ] Équipements : page de détail avec les adresses IP rattachées (dépend des adresses IP), visibilité par section.
- [ ] Circuits : extrémités sur un équipement (aujourd'hui sur un emplacement), types de circuits paramétrables, circuits logiques.
- [ ] Clients : coordonnées GPS et objets VLAN / adresses rattachés.
