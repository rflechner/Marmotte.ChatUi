# Exploitation

[English](operations.md) | Français

## Périmètre implémenté

M3 fournit l’orchestration locale Aspire, PostgreSQL avec pgvector, un realm/client Keycloak persistant et les paramètres de santé/télémétrie de Web. M4 ajoute connexion/déconnexion OIDC, espace de travail protégé et contrôles serveur de session/propriété. M5 ajoute repositories métier, migrations et secrets de modèle protégés. M6 ajoute l’adaptateur texte et M8 relie l’[espace de chat](chat.fr.md) ; les uploads restent non implémentés. Voir [modèles](models.fr.md) pour la configuration et la compatibilité. Aucun déploiement de production n’est configuré.

## Démarrage et arrêt locaux

Installer le SDK sélectionné par [global.json](../global.json), démarrer un moteur de conteneurs compatible Docker et autoriser l’accès à NuGet, Docker Hub et Quay pour les premiers téléchargements. Aspire utilise les packages du tableau de bord et de l’orchestrateur du SDK, sans CLI séparée. Vérifier la disponibilité du certificat HTTPS de développement ; consulter les [certificats de développement .NET](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).

Depuis la racine du dépôt :

```sh
dotnet restore Marmotte.ChatUi.slnx --locked-mode
dotnet build Marmotte.ChatUi.slnx --configuration Release --no-restore
dotnet run --project src/Marmotte.ChatUi.AppHost --configuration Release --no-build
```

Ouvrir le lien authentifié du tableau de bord affiché dans le terminal ; son jeton de connexion est privé. Le tableau de bord utilise `https://localhost:17291`, OTLP utilise `21291` et le service de ressources utilise `22291`. Ces ports du profil de lancement sont des choix de développement local, pas des valeurs par défaut des serveurs de modèles.

Ressources attendues :

| Ressource | Disponibilité et rôle |
| --- | --- |
| `postgres` | Santé de la connexion PostgreSQL ; un volume nommé survit à la suppression des conteneurs |
| `pgadmin` | Administration web locale, préconfigurée pour `postgres` ; attend PostgreSQL et contrôle `/browser` |
| `chatdb` | Base `marmotte_chat`, créée si absente et contrôlée par connexion |
| `keycloakdb` | Base distincte `marmotte_identity`, créée si absente et contrôlée par connexion |
| `database-init` | Attend `chatdb`, active l’extension puis termine avec le code 0 ; cet arrêt de l’initialiseur est attendu |
| `keycloak` | Attend `keycloakdb`, importe le realm s’il est absent puis valide son contrôle de gestion `/health/ready` |
| `web` | Attend la réussite de l’initialisation et la disponibilité de Keycloak, puis valide `/health` |

[L’hôte web](http://localhost:5180) et [la console d’administration Keycloak](http://localhost:8180/admin/) sont accessibles au navigateur. Web fournit un lien de connexion et un `/workspace` protégé avec formulaire de déconnexion. Ctrl+C dans le terminal AppHost arrête ses processus et conteneurs, en conservant le volume de base. Relancer la même commande pour reprendre. Ne pas démarrer deux AppHosts sur le même volume, ni l’hôte autonome et l’AppHost sur le même port web.

Pour travailler sur l’UI/le cœur sans Docker :

```sh
dotnet run --project src/Marmotte.ChatUi.Web --configuration Release --no-build
```

Sans paramètres d’authentification, l’hôte autonome affiche sa page publique et laisse les routes protégées inaccessibles. Une configuration OIDC partielle échoue au démarrage. `/health` et `/alive` renvoient `Healthy` en Development ; ces contrôles de processus ne revendiquent aucune dépendance externe.

## pgAdmin dans le navigateur

Ouvrir [pgAdmin](http://localhost:5050) après le démarrage de l’AppHost, ou sélectionner l’endpoint `pgadmin` dans le tableau de bord Aspire. Déplier `postgres`, puis `Databases`, pour consulter `marmotte_chat` et `marmotte_identity`. Aspire enregistre le serveur et fournit automatiquement ses identifiants PostgreSQL existants ; aucune configuration manuelle de connexion n’est nécessaire.

Cette intégration utilise la configuration locale mono-utilisateur d’Aspire, sans connexion pgAdmin séparée ni demande de mot de passe maître. L’interface web donne un accès administrateur aux bases et est destinée uniquement au développement local. Elle est indépendante de la connexion Web/Keycloak et ne bloque pas la disponibilité de Web. La session et les préférences pgAdmin sont éphémères ; les données PostgreSQL restent dans le volume persistant existant. L’enregistrement du serveur est recréé à chaque démarrage. La ressource pgAdmin est exclue des manifestes de déploiement Aspire.

L’image est fixée à `dpage/pgadmin4:9.15.0` (version retenue par Aspire 13.6.5) et à son empreinte SHA-256 multiplateforme. Aucune nouvelle dépendance NuGet n’est nécessaire. Modifier `Infrastructure:PgAdminPort` si le port `5050` est occupé ; il doit être distinct des ports Web et Keycloak. Par exemple :

```sh
dotnet run --project src/Marmotte.ChatUi.AppHost --configuration Release --no-build -- --Infrastructure:PgAdminPort=5150
```

## Cycle de vie des bases

L’AppHost fixe `pgvector/pgvector:0.8.7-pg17-bookworm` et son empreinte SHA-256 multiplateforme, ainsi que `quay.io/keycloak/keycloak:26.6.4` et son empreinte. Les empreintes exactes figurent dans [Program.cs](../src/Marmotte.ChatUi.AppHost/Program.cs). Toute mise à jour nécessite une vérification explicite de compatibilité des images et volumes ; modifier le tag seul ne remplace pas l’empreinte.

`marmotte-chatui-postgres-data` est monté sur `/var/lib/postgresql/data`, le chemin des données de PostgreSQL 17. Les deux bases y persistent. Keycloak utilise PostgreSQL plutôt qu’une base embarquée ; il n’a donc pas besoin d’un volume d’identité supplémentaire. Le POC local partage le rôle administrateur PostgreSQL entre l’initialiseur et les consommateurs ; les deux bases constituent des stockages séparés, pas des frontières distinctes d’identifiants ou de sécurité. Les rôles et le déploiement de production ne sont pas implémentés.

Aspire crée les bases absentes à chaque démarrage. Un conteneur éphémère distinct exécute ensuite :

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

Cette opération s’exécute à **chaque démarrage de l’AppHost**, y compris avec un volume existant, contrairement aux scripts du premier démarrage de l’image. Elle ne concerne que `marmotte_chat` ; aucune table métier ni aucun embedding n’est créé. Les connexions sont limitées à 15 secondes, les instructions à 30 secondes et l’attente des verrous à 10 secondes. `psql` échoue sur les erreurs SQL et Web attend sa réussite. Web applique le schéma métier M5 avant de servir les requêtes ; voir [persistance](persistence.fr.md) pour l’historique des migrations et les mises à niveau. La [documentation pgvector](https://github.com/pgvector/pgvector#docker) décrit l’image et l’extension amont.

Un changement de version majeure PostgreSQL nécessite une stratégie de migration/export ; ne pas monter le volume existant sur une nouvelle version majeure. Supprimer le volume efface **les données applicatives et d’identité**. L’arrêt normal et les vérifications automatisées ne suppriment pas le volume de développement.

## Initialisation de l’identité et adresses

[La source du realm](../src/Marmotte.ChatUi.AppHost/Realms/marmotte-realm.json) crée le realm `marmotte` et le client confidentiel `marmotte-web`. Des variables d’environnement fournissent le secret du client et l’origine visible au navigateur. Authorization Code avec PKCE S256 est activé ; les flux implicite, mot de passe direct, compte de service et l’inscription publique sont désactivés. Aucun utilisateur applicatif ni mot de passe utilisateur n’est préchargé.

L’autorité par défaut est `http://localhost:8180/realms/marmotte`. Son document de découverte annonce cette adresse visible au navigateur. Les redirections sont limitées à `http://localhost:5180/signin-oidc` ; celles après déconnexion utilisent `http://localhost:5180/signout-callback-oidc`. Le gestionnaire OIDC de Web traite les deux callbacks. Le realm exige TLS pour les accès externes ; HTTP en boucle locale et `start-dev` de Keycloak sont réservés au développement local. L’AppHost désactive explicitement la terminaison HTTPS automatique de Keycloak afin que l’autorité reste identique avec ou sans certificat de développement. Cela utilise l’API expérimentale de certificats d’Aspire 13.6 avec une suppression d’avertissement ciblée ; le tableau de bord conserve HTTPS.

L’import au démarrage de Keycloak ignore un realm existant et préserve les utilisateurs et modifications des clients. Modifier le JSON, le port Web ou le secret configuré du client ne met **pas** à jour un realm existant. Actualiser les URL/le secret du client dans la console d’administration authentifiée (et les paramètres AppHost correspondants), ou créer explicitement un volume de développement distinct. Ne pas supprimer des données existantes pour appliquer un changement de configuration. Consulter le [comportement d’import Keycloak](https://www.keycloak.org/server/importExport#_importing_a_realm_during_startup).

L’administrateur initial est `admin` ; son mot de passe généré est un paramètre secret du tableau de bord Aspire local. Y créer les utilisateurs de développement lorsque nécessaire. Les identifiants d’amorçage créent seulement l’administrateur initial ; les modifier ensuite ne réinitialise pas son mot de passe.

## Connexion et sessions

Dans la console d’administration Keycloak, sélectionner le realm `marmotte`, ouvrir **Users** et créer deux utilisateurs de développement activés avec noms d’utilisateur distincts, prénoms/noms et adresses e-mail. Définir les mots de passe dans **Credentials** ; les mots de passe temporaires exigent un changement à la première connexion. Aucun compte/mot de passe n’est versionné. Ouvrir Web, choisir **Sign in to your workspace**, puis saisir les identifiants utilisateur dans Keycloak. Utiliser des profils de navigateur distincts ou une fenêtre privée pour des utilisateurs indépendants. L’espace protégé affiche le nom connecté ; les écrans de chat restent à venir ; les services applicatifs configurés persistent via PostgreSQL.

Aspire injecte les paramètres du client confidentiel. Web autonome accepte les mêmes clés ci-dessous (les variables d’environnement utilisent `__`). Conserver le secret du client dans les user secrets Web ou un coffre, séparément des identifiants des modèles. Les trois clés OIDC doivent être présentes ensemble. HTTPS est obligatoire sauf HTTP en boucle locale en Development. L’autorité ne peut contenir ni informations utilisateur, ni query string, ni fragment et doit correspondre exactement à l’émetteur. L’origine du callback doit correspondre à l’URL réelle du navigateur et à l’enregistrement Keycloak.

| Clé Web | Défaut / comportement |
| --- | --- |
| `Authentication:Authority` | Aucun défaut ; Aspire fournit l’émetteur du realm visible au navigateur |
| `Authentication:ClientId` | Aucun défaut ; Aspire fournit `marmotte-web` |
| `Authentication:ClientSecret` | Requis avec OIDC ; secret serveur uniquement |
| `Authentication:SessionMinutes` | `30` ; plage `1..480`, également bornée par l’expiration de l’ID token |

L’hôte utilise Authorization Code/PKCE, callbacks en mode query et contrôles nonce/corrélation. Les appels réseau OIDC ont un délai maximal de 15 secondes et le parcours de connexion de cinq minutes. La connexion revient toujours à `/workspace` ; destinations de retour et propriétaires fournis par l’appelant sont ignorés. `/`, `/error`, `/account/error`, les ressources statiques et les endpoints de santé Development sont publics. Les routes applicatives de composants et SignalR exigent une session serveur active. Les échecs d’authentification affichent une page générique pour réessayer, sans détails du fournisseur.

Le cookie HttpOnly, SameSite=Lax contient une référence aléatoire protégée vers un ticket. Les cookies Secure sont obligatoires hors Development ; le développement local utilise le schéma de la requête. Seul l’ID token nécessaire à la déconnexion Keycloak reste dans le ticket serveur en mémoire ; les access/refresh tokens sont abandonnés et les jetons d’identité n’entrent jamais dans les contrats de présentation. Les sessions expirent de façon absolue, sans renouvellement glissant ni refresh. L’expiration ou la déconnexion refuse immédiatement les opérations applicatives suivantes, y compris depuis un circuit déjà ouvert ; l’UI du circuit revalide l’état toutes les 30 secondes. Les opérations déjà autorisées et en cours ne sont pas automatiquement annulées.

**Sign out** envoie un POST protégé par antiforgery. Web révoque d’abord le ticket local, puis redirige vers Keycloak et revient à `/`. Un échec distant termine quand même la session locale, mais la session SSO Keycloak peut rester active. La session indépendante d’un autre utilisateur reste active. Rejouer un cookie après déconnexion est refusé.

Il s’agit d’un POC à processus unique : un redémarrage Web efface toutes les sessions. Plusieurs instances exigent un stockage partagé de tickets/révocations et des clés de protection des données partagées et protégées. La déconnexion initiée par Keycloak/back-channel, la propagation des révocations administrateur, les refresh tokens et l’annulation automatique des opérations en cours ne sont pas implémentés. Une révocation distante prend effet à la prochaine connexion ou à l’expiration locale. Avant production, définir ces politiques, les reverse proxies/en-têtes transférés de confiance, les origines HTTPS, la rotation des secrets, la protection des clés et les rôles de base à privilèges minimaux. Changer l’émetteur modifie les identifiants de propriété et exige une migration. M5 vérifie l’autorisation en base avec des utilisateurs authentifiés et PostgreSQL ; l’autorisation sur les fichiers binaires reste pour M9.

Références : [OIDC ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0) et [état d’authentification Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/authentication-state?view=aspnetcore-10.0).

## Persistance métier et clés de protection

Avec `ConnectionStrings:chatdb` configuré, Web enregistre les services applicatifs et applique les migrations EF code first générées avant disponibilité. Aspire fournit automatiquement cette connexion. `Persistence:KeyDirectory` choisit le répertoire absolu des clés Data Protection ; Aspire peut transmettre `Infrastructure:KeyDirectory`. La valeur par défaut est `Environment.SpecialFolder.LocalApplicationData/Marmotte.ChatUi/keys`. Garder ce répertoire privé et le sauvegarder séparément de PostgreSQL ; le POC local ne chiffre pas ces fichiers de clés au repos. Voir [persistance](persistence.fr.md) pour schéma, migrations/rollback, concurrence, suppression/conservation, récupération des secrets et commandes de vérification.

## Configuration et secrets

L’AppHost génère et conserve ces paramètres secrets dans son magasin user-secrets de développement lors de leur première utilisation :

| Clé AppHost | Consommateurs |
| --- | --- |
| `Parameters:postgres-password` | PostgreSQL, initialiseur, connexion de base Keycloak, connexion de base Web |
| `Parameters:keycloak-password` | Administrateur initial Keycloak uniquement |
| `Parameters:keycloak-client-secret` | Import du realm Keycloak et configuration d’authentification Web |

Conserver ces valeurs tant que le volume est conservé. Le paramètre de mot de passe au démarrage de PostgreSQL ne change pas le mot de passe d’un rôle existant. Un secret modifié/absent avec un volume existant peut provoquer des échecs d’authentification ; restaurer le secret correspondant ou effectuer explicitement un changement de mot de passe avant d’actualiser les consommateurs.

Les deux hôtes possèdent des `UserSecretsId` stables ; leurs magasins sont séparés et non chiffrés, destinés au développement local. Utiliser `dotnet user-secrets set` avec le projet AppHost pour fournir une véritable valeur locale. Un contrôle de syntaxe sans valeur sensible utilise un paramètre inutilisé :

```sh
dotnet user-secrets set "Development:ExampleSecret" "local-example-only" --project src/Marmotte.ChatUi.AppHost
dotnet user-secrets remove "Development:ExampleSecret" --project src/Marmotte.ChatUi.AppHost
```

Aucun identifiant d’accès ne doit figurer dans les JSON versionnés, profils de lancement, journaux ou exemples. Les identifiants des modèles sont chiffrés séparément de ceux d’OIDC ; conserver le répertoire externe des clés Data Protection avec les sauvegardes de base. Les paramètres AppHost ordinaires sont :

| Clé | Valeur par défaut | Signification |
| --- | --- | --- |
| `Infrastructure:WebPort` | `5180` | Port Web publié et origine navigateur du realm initial |
| `Infrastructure:KeycloakPort` | `8180` | Port Keycloak publié et autorité |
| `Infrastructure:PgAdminPort` | `5050` | Port web local publié de pgAdmin |
| `Infrastructure:PostgresVolume` | `marmotte-chatui-postgres-data` | Volume Docker existant ou nouveau ; permet d’isoler les tests/copies du dépôt |

Les ports doivent être distincts et compris dans `1024..65535`. Par exemple, passer `-- --Infrastructure:WebPort=5280 --Infrastructure:KeycloakPort=8280` à la commande `dotnet run` de l’AppHost. Appliquer la règle de mise à jour des URL d’un realm existant ci-dessus. Web autonome utilise son propre profil de lancement.

L’AppHost injecte `ConnectionStrings__chatdb`, les endpoints de découverte de services Keycloak, `Authentication__Authority`, `Authentication__ClientId` et `Authentication__ClientSecret` dans le processus Web **serveur**. Il ne transmet ni le mot de passe administrateur Keycloak ni la connexion à la base d’identité. L’adresse JDBC de Keycloak utilise l’endpoint PostgreSQL adapté aux conteneurs par Aspire ; la connexion Web utilise l’endpoint adapté à l’hôte. Aucune configuration de connexion n’entre dans Domain, Contracts ou Components.

## Santé et diagnostics

ServiceDefaults enregistre la découverte de services pour `HttpClient`, un délai HTTP par défaut de 30 secondes, des métriques du runtime/HTTP serveur/client et des traces, avec export OTLP optionnel lorsque `OTEL_EXPORTER_OTLP_ENDPOINT` est configuré par Aspire. Aucun retry automatique n’est ajouté ; la génération utilise sa propre échéance totale (120 secondes par défaut, configurées par `Models:OperationTimeoutSeconds`). La capture des corps/en-têtes personnalisés, l’instrumentation SQL, l’enregistrement des exceptions dans les traces HTTP et l’export des journaux OpenTelemetry ne sont pas activés. L’instrumentation HTTP conserve le masquage par défaut des valeurs de requête ; ne pas le désactiver ni placer des secrets/contenus dans les chemins d’URL. Les journaux applicatifs doivent toujours exclure conversations, contenus de fichiers, données base64 et identifiants d’accès.

En Development, `/alive` exécute uniquement les contrôles marqués `live` ; `/health` exécute tous les contrôles enregistrés et renvoie 503 en cas d’échec. Les réponses ne contiennent que l’état, sans détails de diagnostic. Les appels de ces endpoints sont exclus des traces HTTP. Web ne contrôle actuellement que lui-même : les conditions de démarrage Aspire vérifient les dépendances, tandis que les contrôles continus de dépendances repositories/OIDC restent non implémentés. Aucun de ces endpoints n’est publié en Production. `Logging:LogLevel` contrôle les diagnostics console ; aucune journalisation des corps de requête n’est configurée. Considérer l’accès au tableau de bord comme privilégié, car il permet d’accéder à la configuration et aux secrets des ressources.

## Topologie réseau des serveurs de modèles

Web est un processus de l’hôte ; une URL de modèle utilisant `localhost` désigne donc cet ordinateur de développement. Utiliser l’adresse et le port réellement configurés du serveur de modèles ; M3 n’effectue aucun appel de modèle et ne revendique aucune compatibilité. Dans un conteneur, `localhost` désigne ce conteneur. Un futur adaptateur conteneurisé devra utiliser un endpoint fourni par Aspire ou la passerelle de l’hôte du moteur de conteneurs (par exemple `host.docker.internal` lorsqu’elle est prise en charge), avec un serveur à l’écoute sur une interface accessible et des paramètres de pare-feu/authentification adaptés. Un serveur de modèles sur un autre ordinateur nécessite l’adresse accessible de celui-ci. Ces paramètres ne suivent jamais automatiquement l’emplacement du navigateur. Le 2026-11-26, une sonde HTTP temporaire sur cet hôte macOS ARM64/Rancher Desktop a été atteinte depuis un processus hôte en boucle locale et depuis l’image PostgreSQL fixée via `host.docker.internal`. Cela vérifie ce chemin réseau local, pas le comportement des API LM Studio/Unsloth ou celui d’autres moteurs de conteneurs.

## Vérification et diagnostic

Le [test d’infrastructure](../tests/Marmotte.ChatUi.Infrastructure.Tests/InfrastructureTests.cs), activé explicitement, démarre le véritable AppHost avec des secrets aléatoires, des ports distincts et un volume temporaire unique. Il contrôle la disponibilité, l’interface web pgAdmin, les bases séparées, vector, la découverte visible au navigateur, le client confidentiel/PKCE/les redirections et la conservation des données applicatives/d’identité. Il connecte aussi deux utilisateurs temporaires via les vrais formulaires Keycloak avec des cookies indépendants, suit les callbacks, contrôle l’antiforgery, déconnecte via Keycloak, refuse un cookie révoqué et préserve l’autre session. Les ports publics du test sont choisis une fois et conservés pour rendre les origines OIDC annoncées joignables. Les redémarrages couvrent une extension déjà activée et sa réparation après suppression. Il libère les ressources et supprime seulement son propre volume, y compris après un échec. Les tests courants l’ignorent ; la CI possède un job Docker séparé. Consulter le [développement](development.fr.md) pour la commande.

- Échec SDK/restauration : contrôler `dotnet --info`, l’accès NuGet et les fichiers de verrouillage. Ne pas désactiver l’audit ou le mode verrouillé pour masquer un échec.
- Docker indisponible : démarrer le moteur et vérifier que `docker version` affiche client et serveur. Les téléchargements d’images nécessitent l’accès à Docker Hub/Quay. Attendre les premiers téléchargements et l’initialisation de Keycloak.
- Ressource en attente : inspecter sa dépendance immédiate dans le tableau ci-dessus. `database-init` doit réussir ; en cas d’échec, vérifier les identifiants PostgreSQL, la disponibilité de l’extension et les permissions.
- Échec de port/certificat : arrêter le processus en conflit, configurer les ports pris en charge ou réparer le certificat de développement. Les ports du tableau de bord/OTLP/service de ressources figurent dans le profil AppHost.
- Redirection ou issuer Keycloak incohérent : utiliser `localhost` systématiquement et inspecter la découverte/les URL du client. Les realms existants ne sont pas réimportés ; actualiser explicitement le client après un changement de port.
- Données apparemment absentes : vérifier le moteur/contexte Docker et le volume choisi. Ne pas supprimer de volume ni régénérer de secrets avant d’avoir retrouvé les données d’origine.
- Interruption des tests : un processus tué brutalement peut laisser un volume `marmotte-infrastructure-test-*`. Inspecter son nom exact et vérifier qu’aucun conteneur ne l’utilise avant de supprimer ce volume de test ; ne jamais purger tous les volumes.
- Connexion indisponible : vérifier les trois paramètres `Authentication`, la découverte joignable depuis Web, les secrets du client sur un realm existant et l’origine de redirection enregistrée. La page d’erreur omet les diagnostics du fournisseur.
- Connexions répétées : l’ID token peut expirer avant le plafond de session ; se reconnecter. Un redémarrage Web termine aussi toutes les sessions.
- Paramètres de repository inactifs : configurer `ConnectionStrings:chatdb` et vérifier les migrations au démarrage. Les écrans de paramètres et ports de modèles sont disponibles lorsque la persistance est configurée. Voir [chat](chat.fr.md) pour tests de connexion, preuve de streaming et récupération des réponses interrompues. N’exécuter qu’un processus Web par base avec le registre de récupération actuel.
- Récupération de secrets impossible après restauration : restaurer le répertoire de clés Data Protection et le nom d’application correspondants ; sans les anciennes clés, remplacer les secrets de modèle concernés.
