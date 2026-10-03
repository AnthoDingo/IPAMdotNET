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

- **Un seul projet** (`src/IPAMdotNET`). Pas de projets séparés (pas de `.Core`, `.Domain`, `.Infrastructure`, `.Abstractions`, etc.). Tout vit dans le même `.csproj`, organisé par dossiers.
- Authentification par cookie (`Pages/Account/Login`). Toutes les pages exigent une connexion par défaut ; une page anonyme doit être déclarée explicitement (`AllowAnonymousToPage` dans `Program.cs`). Noms d'utilisateur stockés normalisés (`User.NormalizeUserName`).
- Interface : structure de phpIPAM dans `Pages/Shared/_Layout.cshtml` (bandeau, barre de menu, sections Razor optionnelles `Breadcrumb` et `Sidebar` pour le fil d'Ariane et la colonne gauche, contenu en `.ipam-panel`). Menus Outils et Administration définis une seule fois dans `Navigation/Menus.cs` (menus déroulants, colonne gauche et tuiles en dérivent) : une fonctionnalité implémentée se branche en renseignant `Page` sur son entrée, une entrée sans `Page` s'affiche désactivée. `Pages/Administration/` est réservé au rôle `Admin`. Icônes : Bootstrap Icons (local, `wwwroot/lib/bootstrap-icons`). Thèmes clair/sombre via `data-bs-theme` de Bootstrap 5.3 ; couleurs propres à l'app uniquement en variables `--ipam-*` dans `wwwroot/css/site.css`, jamais en dur dans les pages.
- Pages métier (`Pages/Network/<Objet>/`) : `Index` (liste, lisible par tout utilisateur connecté) + `Edit` (`@page "{id:int?}"`, création/modification, suppression via le handler `OnPostDelete` et la vue partielle `_DeleteForm`). Les `Edit` portent `[Authorize(Policy = "Admin")]`. Les pages Outils/Administration utilisent la mise en page imbriquée `_ToolsLayout` (fil d'Ariane + menu en colonne gauche) via leur `_ViewStart`.
- Sous-réseaux : la hiérarchie n'est **pas stockée**, elle est déduite de l'inclusion CIDR (`Networking/SubnetTree.cs`, à partir des sous-réseaux triés par adresse puis préfixe). Saisie CIDR via `Ip.TryParseNetwork` (refuse les bits d'hôte ; `IPNetwork.TryParse` de .NET 10 les masque silencieusement). Un même CIDR est unique par section.
- Clés étrangères facultatives de l'infrastructure (emplacement, client, rack, type, équipement) : `ClientSetNull` (NO ACTION en base), car SQL Server refuse les chemins multiples de `SET NULL`. Toute suppression de ces objets passe par `AppDbContext.Detach*Async` dans une transaction ; une nouvelle clé de ce type doit y être ajoutée.
- Pas de `HasData` avec des identifiants explicites (la séquence PostgreSQL n'avancerait pas) : les données initiales vont dans `IpamSetupInitializer`.
- Culture `fr-FR` imposée (`UseRequestLocalization`). Ne jamais binder de nombre décimal saisi : coordonnées et valeurs à virgule en texte, normalisées en invariant (voir `Location.TryNormalizeCoordinate`).
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
- Installation au premier démarrage via le package `AnthoDingo.Setup` (assistant `/setup` : moteur, migrations, admin par nom d'utilisateur, acceptation de la licence). Il écrit `appsettings.local.json` (`Setup:Provider`, `ConnectionStrings:Default`, ignoré par git). Branchement dans `Program.cs`, initialiseur et étape licence dans `Setup/`.
- Contextes : `Data/AppDbContext.cs` (modèle commun + `AppDbContext.Create(provider, cs)`) et un contexte dérivé par moteur dans `Data/ProviderDbContexts.cs`.
- Les migrations EF Core sont spécifiques au provider : un jeu par moteur dans `Migrations/<Moteur>`. Toute modification du modèle doit générer une migration **pour les trois** :
  ```
  dotnet ef migrations add <Nom> --project src/IPAMdotNET --context SqlServerDbContext --output-dir Migrations/SqlServer
  dotnet ef migrations add <Nom> --project src/IPAMdotNET --context PostgresDbContext --output-dir Migrations/Postgres
  dotnet ef migrations add <Nom> --project src/IPAMdotNET --context MySqlDbContext --output-dir Migrations/MySql
  ```
- **Politique de migrations :**
  - **En dev :** aucune limite, autant de migrations intermédiaires que nécessaire.
  - **En prod :** une seule migration par moteur entre deux versions publiées. Avant une release, toutes les migrations créées depuis la version précédente sont fusionnées en une seule, nommée d'après la version (ex. `V1_2_0`) :
    1. supprimer les migrations intermédiaires (`dotnet ef migrations remove` en boucle, ou suppression des fichiers et restauration du `*ModelSnapshot.cs` de la version précédente) ;
    2. regénérer une seule migration par moteur avec les trois commandes ci-dessus ;
    3. recréer les bases de dev qui avaient appliqué les migrations intermédiaires.
  - Les migrations d'une version déjà publiée ne sont jamais modifiées ni supprimées. Avant la première release, tout est fusionné dans `Initial`.
- **Mise à niveau** via le package `AnthoDingo.Update` : tant que des migrations sont en attente, toute requête est redirigée vers `/update` (composant Blazor, seul composant de l'app : `Components/App.razor` + `Routes.razor`), où un admin confirme leur application. Aucune migration n'est appliquée silencieusement au démarrage. `/Account` est exempté de la garde : la connexion s'exécute donc sur l'**ancien** schéma. Elle ne lit que les colonnes dont elle a besoin (projection dans `Login.cshtml.cs`) ; une migration ne doit jamais renommer ni supprimer `Users.Id`, `UserName`, `PasswordHash`, `DisplayName`, `IsAdmin`.
- Pas de SQL brut spécifique à un moteur. Si c'est inévitable, fournir les trois variantes.
- Éviter les types non portables (`jsonb`, `inet`/`cidr`, `hierarchyid`, tableaux Postgres, etc.).
- Adresses IP : stockage portable, de longueur fixe (ex. `byte[]` de 16 octets, IPv4 mappée en IPv6) pour garantir le tri et les comparaisons de plages de façon identique sur les trois moteurs.
- Attention aux différences de casse/collation (Postgres est sensible à la casse, SQL Server/MySQL non par défaut) dans les recherches.
- Tester toute fonctionnalité touchant aux données sur les trois moteurs.


