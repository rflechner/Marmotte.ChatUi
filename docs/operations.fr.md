# Exploitation

[English](operations.md) | Français

## Périmètre implémenté

M3 fournit l’orchestration locale Aspire, PostgreSQL avec pgvector, un realm/client Keycloak persistant et les paramètres de santé/télémétrie de Web. Web affiche encore la page du socle. La connexion OIDC (M4), les repositories/migrations métier (M5), les appels de modèles (M6) et les uploads ne sont pas implémentés. Les paramètres d’authentification et de base injectés préparent ces adaptateurs ; ils ne les activent pas. Aucun déploiement de production n’est configuré.

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

[L’hôte web](http://localhost:5180) et [la console d’administration Keycloak](http://localhost:8180/admin/) sont accessibles au navigateur. Aucun bouton de connexion utilisateur n’existe encore dans Web. Ctrl+C dans le terminal AppHost arrête ses processus et conteneurs, en conservant le volume de base. Relancer la même commande pour reprendre. Ne pas démarrer deux AppHosts sur le même volume, ni l’hôte autonome et l’AppHost sur le même port web.

Pour travailler sur l’UI/le cœur sans Docker :

```sh
dotnet run --project src/Marmotte.ChatUi.Web --configuration Release --no-build
```

Cet hôte autonome n’a pas de connexion à une base ou à l’identité. `/health` et `/alive` renvoient `Healthy` en Development ; ces contrôles de processus ne revendiquent aucune dépendance externe.

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

Cette opération s’exécute à **chaque démarrage de l’AppHost**, y compris avec un volume existant, contrairement aux scripts du premier démarrage de l’image. Elle ne concerne que `marmotte_chat` ; aucune table métier ni aucun embedding n’est créé. Les connexions sont limitées à 15 secondes, les instructions à 30 secondes et l’attente des verrous à 10 secondes. `psql` échoue sur les erreurs SQL et Web attend sa réussite. Les migrations du schéma métier restent prévues pour M5. La [documentation pgvector](https://github.com/pgvector/pgvector#docker) décrit l’image et l’extension amont.

Un changement de version majeure PostgreSQL nécessite une stratégie de migration/export ; ne pas monter le volume existant sur une nouvelle version majeure. Supprimer le volume efface **les données applicatives et d’identité**. L’arrêt normal et les vérifications automatisées ne suppriment pas le volume de développement.

## Initialisation de l’identité et adresses

[La source du realm](../src/Marmotte.ChatUi.AppHost/Realms/marmotte-realm.json) crée le realm `marmotte` et le client confidentiel `marmotte-web`. Des variables d’environnement fournissent le secret du client et l’origine visible au navigateur. Authorization Code avec PKCE S256 est activé ; les flux implicite, mot de passe direct, compte de service et l’inscription publique sont désactivés. Aucun utilisateur applicatif ni mot de passe utilisateur n’est préchargé.

L’autorité par défaut est `http://localhost:8180/realms/marmotte`. Son document de découverte annonce cette adresse visible au navigateur. Les redirections sont limitées à `http://localhost:5180/signin-oidc` ; celles après déconnexion utilisent `http://localhost:5180/signout-callback-oidc`. Elles sont préparées pour M4 et ne sont pas encore traitées par Web. Le realm exige TLS pour les accès externes ; HTTP en boucle locale et `start-dev` de Keycloak sont réservés au développement local. L’AppHost désactive explicitement la terminaison HTTPS automatique de Keycloak afin que l’autorité reste identique avec ou sans certificat de développement. Cela utilise l’API expérimentale de certificats d’Aspire 13.6 avec une suppression d’avertissement ciblée ; le tableau de bord conserve HTTPS.

L’import au démarrage de Keycloak ignore un realm existant et préserve les utilisateurs et modifications des clients. Modifier le JSON, le port Web ou le secret configuré du client ne met **pas** à jour un realm existant. Actualiser les URL/le secret du client dans la console d’administration authentifiée (et les paramètres AppHost correspondants), ou créer explicitement un volume de développement distinct. Ne pas supprimer des données existantes pour appliquer un changement de configuration. Consulter le [comportement d’import Keycloak](https://www.keycloak.org/server/importExport#_importing_a_realm_during_startup).

L’administrateur initial est `admin` ; son mot de passe généré est un paramètre secret du tableau de bord Aspire local. Y créer les utilisateurs de développement lorsque nécessaire. Les identifiants d’amorçage créent seulement l’administrateur initial ; les modifier ensuite ne réinitialise pas son mot de passe.

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

Aucun identifiant d’accès ne doit figurer dans les JSON versionnés, profils de lancement, journaux ou exemples. Les identifiants des modèles seront distincts de ceux d’OIDC. Les paramètres AppHost ordinaires sont :

| Clé | Valeur par défaut | Signification |
| --- | --- | --- |
| `Infrastructure:WebPort` | `5180` | Port Web publié et origine navigateur du realm initial |
| `Infrastructure:KeycloakPort` | `8180` | Port Keycloak publié et autorité |
| `Infrastructure:PgAdminPort` | `5050` | Port web local publié de pgAdmin |
| `Infrastructure:PostgresVolume` | `marmotte-chatui-postgres-data` | Volume Docker existant ou nouveau ; permet d’isoler les tests/copies du dépôt |

Les ports doivent être distincts et compris dans `1024..65535`. Par exemple, passer `-- --Infrastructure:WebPort=5280 --Infrastructure:KeycloakPort=8280` à la commande `dotnet run` de l’AppHost. Appliquer la règle de mise à jour des URL d’un realm existant ci-dessus. Web autonome utilise son propre profil de lancement.

L’AppHost injecte `ConnectionStrings__chatdb`, les endpoints de découverte de services Keycloak, `Authentication__Authority`, `Authentication__ClientId` et `Authentication__ClientSecret` dans le processus Web **serveur**. Il ne transmet ni le mot de passe administrateur Keycloak ni la connexion à la base d’identité. L’adresse JDBC de Keycloak utilise l’endpoint PostgreSQL adapté aux conteneurs par Aspire ; la connexion Web utilise l’endpoint adapté à l’hôte. Aucune configuration de connexion n’entre dans Domain, Contracts ou Components.

## Santé et diagnostics

ServiceDefaults enregistre la découverte de services pour `HttpClient`, un délai HTTP par défaut de 30 secondes, des métriques du runtime/HTTP serveur/client et des traces, avec export OTLP optionnel lorsque `OTEL_EXPORTER_OTLP_ENDPOINT` est configuré par Aspire. Aucun retry automatique n’est ajouté ; l’adaptateur de génération définira ses propres délais/annulation. La capture des corps/en-têtes personnalisés, l’instrumentation SQL, l’enregistrement des exceptions dans les traces HTTP et l’export des journaux OpenTelemetry ne sont pas activés. L’instrumentation HTTP conserve le masquage par défaut des valeurs de requête ; ne pas le désactiver ni placer des secrets/contenus dans les chemins d’URL. Les journaux applicatifs doivent toujours exclure conversations, contenus de fichiers, données base64 et identifiants d’accès.

En Development, `/alive` exécute uniquement les contrôles marqués `live` ; `/health` exécute tous les contrôles enregistrés et renvoie 503 en cas d’échec. Les réponses ne contiennent que l’état, sans détails de diagnostic. Les appels de ces endpoints sont exclus des traces HTTP. Web ne contrôle actuellement que lui-même : les conditions de démarrage Aspire vérifient les dépendances, tandis que leurs contrôles continus côté repositories/OIDC relèveront des futurs adaptateurs. Aucun de ces endpoints n’est publié en Production. `Logging:LogLevel` contrôle les diagnostics console ; aucune journalisation des corps de requête n’est configurée. Considérer l’accès au tableau de bord comme privilégié, car il permet d’accéder à la configuration et aux secrets des ressources.

## Topologie réseau des serveurs de modèles

Web est un processus de l’hôte ; une future URL de modèle utilisant `localhost` désigne donc cet ordinateur de développement. Utiliser l’adresse et le port réellement configurés du serveur de modèles ; M3 n’effectue aucun appel de modèle et ne revendique aucune compatibilité. Dans un conteneur, `localhost` désigne ce conteneur. Un futur adaptateur conteneurisé devra utiliser un endpoint fourni par Aspire ou la passerelle de l’hôte du moteur de conteneurs (par exemple `host.docker.internal` lorsqu’elle est prise en charge), avec un serveur à l’écoute sur une interface accessible et des paramètres de pare-feu/authentification adaptés. Un serveur de modèles sur un autre ordinateur nécessite l’adresse accessible de celui-ci. Ces paramètres ne suivent jamais automatiquement l’emplacement du navigateur. Le 2026-11-26, une sonde HTTP temporaire sur cet hôte macOS ARM64/Rancher Desktop a été atteinte depuis un processus hôte en boucle locale et depuis l’image PostgreSQL fixée via `host.docker.internal`. Cela vérifie ce chemin réseau local, pas le comportement des API LM Studio/Unsloth ou celui d’autres moteurs de conteneurs.

## Vérification et diagnostic

Le [test d’infrastructure](../tests/Marmotte.ChatUi.Infrastructure.Tests/InfrastructureTests.cs), activé explicitement, démarre le véritable AppHost avec des secrets aléatoires, des ports distincts et un volume temporaire unique. Il contrôle la disponibilité, l’interface web pgAdmin, les bases séparées, vector, la découverte visible au navigateur, le client confidentiel/PKCE/les redirections et la conservation des données applicatives/d’identité. Les redémarrages couvrent une extension déjà activée et sa réparation après suppression. Il libère les ressources et supprime seulement son propre volume, y compris après un échec. Les tests courants l’ignorent ; la CI possède un job Docker séparé. Consulter le [développement](development.fr.md) pour la commande.

- Échec SDK/restauration : contrôler `dotnet --info`, l’accès NuGet et les fichiers de verrouillage. Ne pas désactiver l’audit ou le mode verrouillé pour masquer un échec.
- Docker indisponible : démarrer le moteur et vérifier que `docker version` affiche client et serveur. Les téléchargements d’images nécessitent l’accès à Docker Hub/Quay. Attendre les premiers téléchargements et l’initialisation de Keycloak.
- Ressource en attente : inspecter sa dépendance immédiate dans le tableau ci-dessus. `database-init` doit réussir ; en cas d’échec, vérifier les identifiants PostgreSQL, la disponibilité de l’extension et les permissions.
- Échec de port/certificat : arrêter le processus en conflit, configurer les ports pris en charge ou réparer le certificat de développement. Les ports du tableau de bord/OTLP/service de ressources figurent dans le profil AppHost.
- Redirection ou issuer Keycloak incohérent : utiliser `localhost` systématiquement et inspecter la découverte/les URL du client. Les realms existants ne sont pas réimportés ; actualiser explicitement le client après un changement de port.
- Données apparemment absentes : vérifier le moteur/contexte Docker et le volume choisi. Ne pas supprimer de volume ni régénérer de secrets avant d’avoir retrouvé les données d’origine.
- Interruption des tests : un processus tué brutalement peut laisser un volume `marmotte-infrastructure-test-*`. Inspecter son nom exact et vérifier qu’aucun conteneur ne l’utilise avant de supprimer ce volume de test ; ne jamais purger tous les volumes.
- Paramètres de modèle/identité/repository inactifs dans Web : ces adaptateurs relèvent des milestones suivants, pas des fonctions d’exécution de M3.
