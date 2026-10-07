# CLAUDE.md

## Projet

IPAMdotNet est un IPAM (IP Address Management) en .NET qui reprend l'ergonomie et la grande majorité des fonctionnalités de [phpIPAM](https://phpipam.net/). En cas de doute sur un comportement ou un modèle de données, s'aligner sur phpIPAM.

Périmètre fonctionnel de référence (phpIPAM) :
- Sections, sous-réseaux (IPv4/IPv6, hiérarchie, calcul d'utilisation), adresses IP
- VLAN / domaines L2, VRF
- Équipements, racks, emplacements, clients, circuits, NAT
- Utilisateurs, groupes, permissions par section/sous-réseau
- Champs personnalisés, journal des modifications, demandes d'adresses
- Scan / découverte, API REST

## Licence

GNU AGPL v3 ou ultérieure (`AGPL-3.0-or-later`), texte dans `LICENSE`, embarqué dans l'assembly et affiché par l'assistant d'installation.

## Stack

ASP.NET Core Razor Pages, .NET 10, EF Core 10. Solution `IPAMdotNet.slnx` à la racine, projet unique dans `src/IPAMdotNET/`. Build : `dotnet build` depuis la racine (le build échoue sur tout `var`).

## Règles d'architecture

- **Un seul projet** (`src/IPAMdotNET`). Pas de projets séparés (pas de `.Core`, `.Domain`, `.Infrastructure`, `.Abstractions`, etc.). Tout vit dans le même `.csproj`, organisé par dossiers. **Seule exception** : `src/IPAMdotNet.ScanAgent`, l'agent de scan distant (exécutable déployé sur d'autres machines). Il ne référence pas l'application : il compile en lien les deux fichiers sans dépendance `Networking/HostProbe.cs` et `Api/AgentProtocol.cs`, qui doivent le rester (pas d'EF Core, pas d'ASP.NET).
- Authentification par cookie (`Pages/Account/Login`). Toutes les pages exigent une connexion par défaut ; une page anonyme doit être déclarée explicitement (`AllowAnonymousToPage` dans `Program.cs`). Noms d'utilisateur stockés normalisés (`User.NormalizeUserName`).
- Interface : structure de phpIPAM dans `Pages/Shared/_Layout.cshtml` (bandeau, barre de menu, sections Razor optionnelles `Breadcrumb` et `Sidebar` pour le fil d'Ariane et la colonne gauche, contenu en `.ipam-panel`). Menus Outils et Administration définis une seule fois dans `Navigation/Menus.cs` (menus déroulants, colonne gauche et tuiles en dérivent) : une fonctionnalité implémentée se branche en renseignant `Page` sur son entrée, une entrée sans `Page` s'affiche désactivée. `Pages/Administration/` est réservé au rôle `Admin`. Icônes : Bootstrap Icons (local, `wwwroot/lib/bootstrap-icons`). Thèmes clair/sombre via `data-bs-theme` de Bootstrap 5.3 ; couleurs propres à l'app uniquement en variables `--ipam-*` dans `wwwroot/css/site.css`, jamais en dur dans les pages.
- Pages métier (`Pages/Network/<Objet>/`) : `Index` (liste, lisible par tout utilisateur connecté) + `Edit` (`@page "{id:int?}"`, création/modification, suppression via le handler `OnPostDelete` et la vue partielle `_DeleteForm`). Les `Edit` portent `[Authorize(Policy = "Admin")]` (sauf sous-réseaux : droit d'écriture sur la section). Les pages Outils/Administration utilisent la mise en page imbriquée `_ToolsLayout` (fil d'Ariane + menu en colonne gauche) via leur `_ViewStart`.
- Sous-réseaux : la hiérarchie n'est **pas stockée**, elle est déduite de l'inclusion CIDR (`Networking/SubnetTree.cs`, à partir des sous-réseaux triés par adresse puis préfixe). Saisie CIDR via `Ip.TryParseNetwork` (refuse les bits d'hôte ; `IPNetwork.TryParse` de .NET 10 les masque silencieusement). Un même CIDR est unique par section.
- Clés étrangères facultatives de l'infrastructure (emplacement, client, rack, type, équipement) : `ClientSetNull` (NO ACTION en base), car SQL Server refuse les chemins multiples de `SET NULL`. Toute suppression de ces objets passe par `AppDbContext.Detach*Async` dans une transaction ; une nouvelle clé de ce type doit y être ajoutée. Idem pour les liens NAT vers sous-réseaux et adresses : toute suppression d'un sous-réseau ou d'adresses appelle `DetachSubnetAsync` / `DetachAddressesAsync` (le texte de la règle est conservé ; les associations BGP du sous-réseau sont supprimées).
- **Journal des modifications** automatique (`Data/AppDbContext.Audit.cs`) : tout `SaveChangesAsync` sur un type déclaré dans `ChangeLog.Types` est journalisé (auteur, action, valeurs avant/après avec les libellés `[Display]`). Toute nouvelle entité métier doit être ajoutée à `ChangeLog.Types` et porter des `[Display(Name = ...)]` en français. Les `ExecuteUpdate`/`ExecuteDelete` ne sont pas journalisés : à réserver aux opérations techniques. Une clé étrangère est écrite avec le libellé de l'objet référencé au moment du changement (lu sans suivi : charger puis détacher un objet détacherait ses dépendants en cours d'ajout). L'historique d'un objet s'affiche sur sa fiche avec `<vc:history entity-type="…" entity-id="…" />` (filtré par les droits).
- **Champs personnalisés** : définitions `CustomField` + valeurs `CustomFieldValue` (pas d'ALTER TABLE comme phpIPAM, non portable). Types porteurs listés dans `CustomField.SupportedTypes` ; chaque page `Edit` correspondante expose `[BindProperty(Name = CustomFieldForm.Prefix)] Dictionary<int, string?> Custom` (le nom explicite est obligatoire) et passe par `Maintenance/CustomFieldForm` (chargement, validation, enregistrement). Les valeurs d'un objet supprimé sont effacées dans `SaveChangesAsync`.
- **Journal système** (`LogEntry`, page Administration › Journaux) : connexions et opérations de maintenance via `db.LogAsync`, ou `db.TryLogAsync` pour ce qui s'exécute avant les migrations (connexion/déconnexion).
- **Permissions par section** (`Data/SectionAccess.cs`) : niveau = max(accès par défaut de la section, permissions des groupes de l'utilisateur) ; les admins ont tout. **Toute page qui lit ou modifie des sous-réseaux** doit passer par `SectionAccess` : `access.Readable(query)` pour filtrer, `CanRead`/`CanWrite` + `Forbid()` pour une page d'objet. Y compris un simple compteur de sous-réseaux. Le journal des modifications passe par `access.Visible(db.ChangeLogs)` : `ChangeLog.SectionId` est renseigné à l'écriture pour les types de `ChangeLog.SectionScopedTypes` (un nouveau type rattaché à une section doit y être ajouté, avec sa résolution dans `SectionOf`), et les types de `ChangeLog.AdminOnlyTypes` ne sont montrés qu'aux admins. Le formulaire de sous-réseau est ouvert aux non-admins ayant le droit d'écriture sur la section.
- **Adresses IP** (`IpAddress`, 16 octets comme les sous-réseaux) : appartiennent à un sous-réseau (suppression en cascade côté base : supprimer d'abord leurs valeurs de champs personnalisés, qui n'ont pas de clé étrangère), droits = ceux de sa section. En IPv4, l'adresse réseau et la diffusion ne sont pas attribuables (`Ip.UsableRange`). Un sous-réseau ne peut pas être réduit au point d'exclure ses adresses. Les étiquettes système sont identifiées par `Tag.SystemKey` (le nom est modifiable) ; leur `SystemKey` n'est jamais modifiable par formulaire.
- **Agent de scan intégré** (`Maintenance/ScanAgent`, service d'arrière-plan + `SubnetScanner`) : désactivable dans Administration › Agents de scan (`ScanSettings`, préfixe `Scan`). Ping puis, si configurés, ports TCP (`ScanSettings.TcpPorts` ; connexion acceptée ou refusée = hôte présent). Ne scanne que les sous-réseaux avec `PingCheck` / `Discover` et sans `ScanAgentId` ; ceux confiés à un **agent distant** (`RemoteAgent`, clé hachée comme les clés d'API) sont scannés par lui via `/api/agent/work` et `/api/agent/results` (`Api/AgentEndpoints.cs`), les deux passant par `SubnetScanner.TargetsAsync` / `ApplyAsync`. Les réglages (`ScanSettings`) valent pour tous les agents.  découverte limitée à l'IPv4 ≤ 1024 hôtes. « Vu le » est mis à jour par `ExecuteUpdate` (hors journal des modifications, volontairement) ; changements d'étiquette et découvertes sont journalisés au nom « Agent de scan ».
- **Import phpIPAM** (Administration › Import phpIPAM, `Maintenance/PhpIpamSource` + `PhpIpamImporter`) : depuis la base MySQL (complet) ou l'API REST (sans utilisateurs, groupes ni paramètres), uniquement dans une installation vide, en une transaction, avec `AppDbContext.SuppressAudit` (pas de journal des modifications ; un résumé dans le journal système). Les données sont lues au format des tables phpIPAM (la source API renomme ses champs : `ip` → `ip_addr`, `tag` → `state`…). Tout nouvel objet métier repris de phpIPAM doit y être ajouté.
- **API REST** (`Api/ApiEndpoints.cs`) : une clé prend les droits de son compte (`ApiKey.UserId`, null = tout) via `SectionAccess.ForApiKeyAsync` ; toute lecture passe par `access.Readable(...)` et un objet illisible répond 404. Écriture : `ApiKey.CanWrite` + `CanWrite(section)` ; VLAN, VRF, équipements : droits d'admin. Validation par les attributs de l'entité (`Validator`), erreurs en `ValidationProblem` (400), doublons en 409 ; le journal attribue les changements à « API « nom » ». Les objets d'administration sont exposés par `MapResource` (`Api/ApiEndpoints.Resources.cs`) : un `ApiResource<T>` déclare les champs modifiables et reprend les règles du formulaire (`Validate`, `Check` avec la clé appelante, `AfterSave`, `BeforeDelete`, champs supplémentaires `ExtraFields` / `ExtraJson`) ; un nouvel objet s'ajoute là. Les garde-fous des formulaires (dernier admin actif, pas d'auto-rétrogradation, étiquettes système) s'y appliquent aussi. Champs personnalisés : `customFields` (nom → valeur) en lecture et écriture.
- **Équipements** : visibles des non-admins si sans section ou rattachés à une section lisible (`access.Readable(db.Devices)`, à utiliser pour toute liste d'équipements).
- **LDAP** : avec une base de recherche, l'entrée du compte est lue avec ses propres droits (nom, e-mail, memberOf). `AutoCreateUsers` crée le compte à la première connexion, `SyncGroups` aligne les groupes locaux de même nom (CN) à chaque connexion. La connexion lit ces options à part (`LoadDirectoryOptionsAsync`, repli si non migré).
- **Agent distant, mise à jour** : le serveur annonce sa version dans `/api/agent/work` et sert `agent/IPAMdotNet.ScanAgent-<rid>.zip` (déposé par le workflow de release) ; l'agent remplace ses fichiers (pas `appsettings*.json`) et sort avec le code 3 pour être relancé.
- **Mots de passe** : toute vérification passe par `Maintenance/Passwords.Verify`, qui accepte aussi les hachés `$6$` (SHA-512 crypt) des comptes importés de phpIPAM et demande leur re-hachage.
- **Paramètres** : objets `ServerSettings`, `MailSettings`, `WidgetSettings` stockés propriété par propriété dans `AppSetting` via `Maintenance/SettingsStore` ; `SettingsStore.Server` est le cache lu par les pages (rechargé à l'enregistrement). Une fonctionnalité désactivable masque son entrée de menu (`MenuItem.Visible`) et bloque ses pages (filtre, ex. `[IpRequestsEnabled]`).
- **Connexion et schéma non migré** : la connexion, la déconnexion et la revalidation de session (`Maintenance/SessionValidator`) s'exécutent avant `/update`. Elles ne lisent que les colonnes de `Users` présentes dans `Initial` ; les colonnes ajoutées ensuite (`Enabled`, `AuthMethodId`, `Email`…) sont lues dans une requête séparée avec repli (`DbException`) sur des valeurs par défaut sûres.
- **Migrations qui ajoutent une colonne non nulle** : vérifier le `defaultValue` généré (valeur CLR par défaut). Il remplit les lignes existantes — ex. `Users.Enabled` doit valoir `true`, sinon tous les comptes seraient désactivés.
- Secrets (mot de passe SMTP) chiffrés par la protection des données ASP.NET Core ; les clés sont persistées dans `keys/` (racine de contenu, exclu de git). Clés d'API : seul le haché SHA-256 est stocké.
- Pas de `HasData` avec des identifiants explicites (la séquence PostgreSQL n'avancerait pas) : les données initiales vont dans `IpamSetupInitializer`.
- Culture `fr-FR` imposée (`UseRequestLocalization`). Ne jamais binder de nombre décimal saisi : coordonnées et valeurs à virgule en texte, normalisées en invariant (voir `Location.TryNormalizeCoordinate`).
- Texte saisi rendu en Markdown : uniquement via `Maintenance/MarkdownText.ToHtml` (HTML désactivé, liens limités à http(s), mailto et relatifs), jamais `Markdig` directement.
- Pas d'abstraction sans besoin réel : pas d'interface à une seule implémentation, pas de repository générique au-dessus d'EF Core.

## Règles de code

- **Toutes les variables sont typées explicitement. `var` est interdit**, y compris dans les `foreach`, `using` et LINQ.
  ```csharp
  // Non
  var subnet = await db.Subnets.FindAsync(id);
  // Oui
  Subnet? subnet = await db.Subnets.FindAsync(id);
  ```
  Appliqué par `.editorconfig` (IDE0008 en erreur) ; le `.csproj` doit contenir `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` pour que la règle bloque le build.
- Nullable reference types activés.

## Bases de données

Trois moteurs doivent être supportés : **SQL Server, PostgreSQL, MySQL**. Le moteur est choisi par configuration.

- Accès aux données via EF Core 10 avec les providers `Microsoft.EntityFrameworkCore.SqlServer`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `MySql.EntityFrameworkCore` (Oracle ; Pomelo n'a pas de version EF Core 10).
- Installation au premier démarrage via le package `AnthoDingo.Setup` (assistant `/setup` : acceptation de la licence en premier, puis moteur, migrations, admin par nom d'utilisateur). Il écrit `appsettings.local.json` (`Setup:Provider`, `ConnectionStrings:Default`, ignoré par git). Branchement dans `Program.cs`, initialiseur et étape licence dans `Setup/`.
- Contextes : `Data/AppDbContext.cs` (modèle commun + `AppDbContext.Create(provider, cs)`) et un contexte dérivé par moteur dans `Data/ProviderDbContexts.cs`.
- Les migrations EF Core sont spécifiques au provider : un jeu par moteur dans `Migrations/<Moteur>`. Toute modification du modèle doit générer une migration **pour les trois** :
  ```
  dotnet ef migrations add <Nom> --project src/IPAMdotNET --context SqlServerDbContext --output-dir Migrations/SqlServer
  dotnet ef migrations add <Nom> --project src/IPAMdotNET --context PostgresDbContext --output-dir Migrations/Postgres
  dotnet ef migrations add <Nom> --project src/IPAMdotNET --context MySqlDbContext --output-dir Migrations/MySql
  ```
- **Politique de migrations :**
  - **En dev (branche `dev/main`) :** les migrations **se suivent**. Une modification du modèle = une **nouvelle** migration ajoutée par-dessus les existantes (nom descriptif, ex. `AddCustomFields`), jamais la regénération ni la modification d'une migration déjà commitée : les bases de dev doivent se mettre à jour via la page `/update`, sans être recréées.
  - **En prod :** une seule migration par moteur entre deux versions publiées. La fusion se fait **uniquement au moment d'une release** (passage sur `main`), jamais pendant le développement : toutes les migrations créées depuis la version précédente sont fusionnées en une seule, nommée d'après la version (ex. `V1_2_0`) :
    1. supprimer les migrations intermédiaires (`dotnet ef migrations remove` en boucle, ou suppression des fichiers et restauration du `*ModelSnapshot.cs` de la version précédente) ;
    2. regénérer une seule migration par moteur avec les trois commandes ci-dessus ;
    3. recréer les bases de dev qui avaient appliqué les migrations intermédiaires.
  - Les migrations d'une version déjà publiée ne sont jamais modifiées ni supprimées. À la première release, tout sera fusionné dans `Initial`.
- **Mise à niveau** via le package `AnthoDingo.Update` : tant que des migrations sont en attente, toute requête est redirigée vers `/update` (composant Blazor, seul composant de l'app : `Components/App.razor` + `Routes.razor`), où un admin confirme leur application. Aucune migration n'est appliquée silencieusement au démarrage. `/Account` est exempté de la garde : la connexion s'exécute donc sur l'**ancien** schéma. Elle ne lit que les colonnes dont elle a besoin (projection dans `Login.cshtml.cs`) ; une migration ne doit jamais renommer ni supprimer `Users.Id`, `UserName`, `PasswordHash`, `DisplayName`, `IsAdmin`.
- Pas de SQL brut spécifique à un moteur. Si c'est inévitable, fournir les trois variantes.
- Éviter les types non portables (`jsonb`, `inet`/`cidr`, `hierarchyid`, tableaux Postgres, etc.).
- Adresses IP : stockage portable, de longueur fixe (ex. `byte[]` de 16 octets, IPv4 mappée en IPv6) pour garantir le tri et les comparaisons de plages de façon identique sur les trois moteurs.
- Attention aux différences de casse/collation (Postgres est sensible à la casse, SQL Server/MySQL non par défaut) dans les recherches.
- Tester toute fonctionnalité touchant aux données sur les trois moteurs.


