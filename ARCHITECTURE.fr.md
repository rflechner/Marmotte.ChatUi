# Architecture — Marmotte.ChatUi

[English](ARCHITECTURE.md) | Français

## Statut et objectif

Ce document distingue le socle M1–M3 implémenté de l’architecture produit cible. Les neuf projets sources, un hôte Blazor Interactive Server, le domaine et les cas d’usage autorisés, ainsi que l’infrastructure locale Aspire existent désormais. PostgreSQL/pgvector et Keycloak démarrent avec des données persistantes et des contrôles de disponibilité ; ServiceDefaults fournit les diagnostics de l’hôte. La persistance métier, la connexion OIDC, les adaptateurs de modèles, les composants de chat réutilisables et la publication NuGet restent prévus ; Web n’est pas encore relié au cœur.

Marmotte.ChatUi vise deux usages : une application de chat en .NET et Blazor, inspirée d’AnythingLLM et d’Open WebUI, et une bibliothèque de composants et widgets intégrables dans d’autres applications. L’application complète utilisera les mêmes composants que ceux distribués en NuGet.

## Périmètre et étapes

### Première version

- Interface de chat, conversations, messages et affichage progressif des réponses lorsque le serveur le permet.
- Paramètres de connexion à des API compatibles OpenAI : [LM Studio](https://lmstudio.ai/docs/app), [Unsloth Desktop](https://www.unsloth.ai/) et endpoint personnalisé.
- Historique des conversations et préférences propres à chaque utilisateur.
- Authentification OIDC de l’application complète via Keycloak.
- Orchestration locale avec .NET Aspire, Keycloak et PostgreSQL muni de pgvector.
- Composants Blazor réutilisables destinés à un package NuGet.
- Design system personnalisable avec modes clair, sombre et système.
- Import de pièces jointes par sélection et glisser-déposer ; images envoyées aux modèles acceptant la vision.

### Étape suivante

Le traitement des PDF, en particulier scannés, est prévu mais peut suivre la première version. Il préparera des images de pages, avec sélection et limites de résolution, avant l’envoi à un modèle de vision. Un type de fichier sans traitement disponible ne devra pas être présenté comme exploitable par le modèle.

Le RAG, l’indexation documentaire et la génération d’embeddings ne sont pas inclus dans la première version. La disponibilité de pgvector prépare leur éventuelle évolution sans les implémenter. PostgreSQL est le seul fournisseur de persistance retenu pour le moment ; RavenDB n’est pas prévu dans ce périmètre.

## Principes structurants

Adopter une architecture hexagonale avec un domaine inspiré du DDD. Les cas d’usage dépendent de ports internes ; les technologies externes sont des adaptateurs. Le domaine et les contrats applicatifs ne dépendent ni des outils d’infrastructure ni des composants de présentation.

Les frontières importantes correspondent à des assemblies distinctes. Il s’agit initialement d’une application modulaire, sans obligation de déployer chaque assembly comme un service.

### Découpage cible

Les neuf projets ci-dessous existent sous `src/`. Le tableau décrit leurs responsabilités prévues. Domain, Contracts et Application implémentent M2. Les deux adaptateurs métier et Components établissent encore les frontières sans leurs futures implémentations. AppHost et ServiceDefaults implémentent l’orchestration et les diagnostics M3. Web référence aussi ServiceDefaults, en plus des dépendances indiquées ci-dessous.

| Projet / assembly | Responsabilité | Dépendances internes principales |
| --- | --- | --- |
| Marmotte.ChatUi.Domain | Conversations, messages, pièces jointes et invariants métier | Aucune infrastructure |
| Marmotte.ChatUi.Contracts | Contrats de présentation et échanges nécessaires aux composants distribués | Aucune infrastructure |
| Marmotte.ChatUi.Application | Cas d’usage, autorisations et ports de stockage, identité et génération | Domain, Contracts |
| Marmotte.ChatUi.Components | Razor Class Library, widgets, styles et tokens ; package NuGet public | Contracts |
| Marmotte.ChatUi.Infrastructure.OpenAI | Adaptateur des serveurs compatibles OpenAI | Application, Domain |
| Marmotte.ChatUi.Persistence.PostgreSql | Repositories concrets, mappings, migrations et intégration pgvector | Application, Domain |
| Marmotte.ChatUi.Web | Application Blazor, connexion OIDC et composition des implémentations | Components, Application et adaptateurs |
| Marmotte.ChatUi.AppHost | Description et lancement des ressources Aspire | Références de projets hébergés |
| Marmotte.ChatUi.ServiceDefaults | Configuration technique commune utile à l’hébergement et à l’observabilité | Aucune règle métier |

Le traitement documentaire pourra être placé dans une assembly Infrastructure.Documents lors de son implémentation. L’assemblage exact du stockage binaire sera choisi à ce moment, derrière un port dédié.

```mermaid
flowchart LR
    Web[Application Blazor] --> UI[Composants NuGet]
    UI --> Contracts[Contrats de présentation]
    Web --> App[Cas d’usage et ports]
    App --> Domain[Domaine]
    App --> Contracts
    Pg[Adaptateur PostgreSQL] --> App
    AI[Adaptateur OpenAI] --> App
    Docs[Adaptateur documentaire futur] --> App
    Web --> Pg
    Web --> AI
```

Les flèches représentent les dépendances du code. À l’exécution, les cas d’usage appellent les adaptateurs au travers des interfaces injectées.

## Modèle métier et contrats

M2 implémente `Conversation` comme agrégat immuable contrôlant les paires de messages utilisateur/assistant ordonnées et les états de génération. `Attachment` porte des métadonnées possédées séparément ; `ConnectionProfile` et `UserPreferences` sont des instantanés de paramètres possédés distincts. GUID, horodatages UTC, révisions, règles de doublons et validation à la restauration préservent l’historique. Il ne s’agit pas encore d’un schéma de base de données.

L’application expose la création, pagination, lecture, suppression, soumission de texte, enregistrement/annulation des réponses et gestion des paramètres avec autorisation. L’identité courante et les repositories sont des ports ; chaque opération tire le propriétaire d’une identité serveur vérifiée et contrôle les propriétaires renvoyés. Les secrets des profils sont absents de ces contrats.

Les écritures de conversation comparent les révisions et valident atomiquement les messages et références de pièces jointes. Les listes sont bornées et ordonnées ; le chargement d’une conversation matérialise actuellement tout l’historique. Les repositories n’exposent aucun type de base de données. Les ports de fournisseur définissent les capacités et flux de réponse, sans appel de modèle concret ni orchestration. Les ports de stockage binaire et préparation multimodale restent pour M9/M11.

Consulter la [référence du cœur](docs/core.fr.md) pour les invariants, transitions, API publiques, annulation, cohérence, durées de vie et limites. Les adaptateurs de test exercent ce comportement sans infrastructure ; les garanties PostgreSQL et l’authentification réelle restent à vérifier en M4/M5.

## Composants Blazor et distribution NuGet

La bibliothèque est une Razor Class Library destinée à être publiée sur NuGet. Son socle M1 référence uniquement Contracts et les packages de composants Blazor compatibles avec le navigateur. La liste des messages, la zone de saisie, les pièces jointes, les états de génération et le widget de chat composable sont prévus pour M7. Consulter [l’intégration](docs/integration.fr.md) pour le contrat de rendu et les limites actuelles.

Son API repose sur des paramètres, événements, templates et contrats de présentation stables. L’application consommatrice fournit les services de conversation et de pièces jointes dont elle a besoin. Les composants ne nécessitent pas l’application complète et n’imposent pas de base de données, d’identité ou de SDK de modèle.

Les assets et styles réutilisables doivent être distribués avec la bibliothèque lorsque nécessaire. Le branding de l’application Marmotte.ChatUi reste remplaçable par l’hôte du widget. Un projet de démonstration ou un test de consommation vérifiera que le package fonctionne sans dépendance transitive aux adaptateurs serveur.

La publication NuGet est un objectif de livraison ultérieur. Aucun nom réservé, version publiée ou pipeline de publication n’est supposé existant.

## Application hôte et identité

L’hôte utilise ASP.NET Core Blazor en Interactive Server avec prérendu activé pour Routes et HeadOutlet. Les futurs appels aux modèles, secrets et accès à la persistance restent ainsi côté serveur. Les composants réutilisables héritent du mode choisi par l’hôte consommateur et ne doivent pas supposer un circuit serveur. Le SSR statique permet uniquement l’affichage ; Interactive Server est la première cible d’intégration interactive. Interactive WebAssembly et Auto restent des cibles de conception nécessitant des tests de consommation distincts avant d’en annoncer la prise en charge.

Keycloak est le fournisseur d’identité OIDC. Aspire lance son conteneur comme une ressource distincte, avec une configuration reproductible du realm, du client et des redirections. Le client OIDC de l’application et les adresses publiques de l’autorité doivent être cohérents avec ce que le navigateur peut réellement joindre.

L’authentification établit l’identité ; les cas d’usage contrôlent ensuite l’accès à chaque conversation, préférence et pièce jointe. L’identité persistée repose sur un identifiant stable, lié au couple émetteur/sujet OIDC, et non à l’adresse e-mail. Un identifiant de propriétaire fourni par le navigateur ne constitue pas une autorisation.

La configuration locale de Keycloak n’est pas assimilée à une configuration de production. La cible d’hébergement et les paramètres d’exploitation restent à préciser avant un déploiement de production.

## Connexion aux modèles

Les paramètres couvrent le nom du profil, l’URL de base, le modèle, les options utiles et l’authentification si le serveur l’exige. Prévoir un test de connexion et une découverte des modèles lorsque le serveur le permet, avec saisie manuelle possible.

Le [package NuGet OpenAI](https://www.nuget.org/packages/OpenAI) est le candidat privilégié demandé pour l’adaptateur. Le [SDK officiel .NET documente les endpoints personnalisés](https://github.com/openai/openai-dotnet#using-a-custom-base-url-and-api-key). Son adoption sera confirmée par un essai des besoins réels sur LM Studio et Unsloth Desktop ; aucune version n’est encore fixée ni installée.

Le SDK reste confiné à Infrastructure.OpenAI. Ses messages, résultats et exceptions sont traduits en contrats internes. Vérifier séparément le chat, le streaming, l’annulation, la découverte des modèles, la vision et l’authentification optionnelle. La présence d’une API Responses ou Files n’est pas déduite de la seule compatibilité OpenAI.

Privilégier le sous-ensemble de conversation réellement commun aux serveurs ciblés. Une contrainte de credential du SDK ne doit pas obliger l’utilisateur à inventer une clé dans les paramètres si son serveur local n’en requiert pas ; la stratégie technique sera vérifiée et encapsulée dans l’adaptateur.

Un profil est associé à son utilisateur et ne partage pas implicitement ses secrets avec les autres. Les identifiants des modèles sont distincts de la connexion OIDC à l’application.

Attention à la topologie d’exécution : localhost désigne la machine ou le conteneur exécutant l’adaptateur, pas nécessairement l’ordinateur du navigateur. Documenter et tester l’accès aux serveurs locaux depuis la configuration Aspire retenue.

## Persistance PostgreSQL et pgvector

L’assembly Persistence.PostgreSql contient l’ensemble des détails relationnels : implémentations de repository, mappings, index, migrations, transactions et types spécifiques au fournisseur. Le choix d’un ORM ou d’un accès SQL direct reste ouvert à ce stade.

PostgreSQL conserve les conversations, messages, profils, préférences et métadonnées de pièces jointes. Le stockage des binaires dispose d’un port distinct pour éviter de figer le domaine sur une base ou un système de fichiers. L’adaptateur initial et sa politique de conservation seront définis lors de l’implémentation des uploads.

pgvector est une extension : elle doit être disponible dans l’installation PostgreSQL, puis activée dans chaque base qui l’utilise. M3 fixe `pgvector/pgvector:0.8.7-pg17-bookworm` avec une empreinte multiplateforme et le chemin explicite du volume PostgreSQL 17. Un initialiseur éphémère active vector dans `marmotte_chat` à chaque démarrage AppHost, après disponibilité de la base et avant Web :

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

Cette commande active une extension déjà installée ; elle n’installe pas ses fichiers sur le serveur. L’installation et l’activation sont décrites dans la [documentation officielle pgvector](https://github.com/pgvector/pgvector#installation).

M3 contrôle l’activation sur un volume neuf, l’idempotence au redémarrage et la réparation d’une base existante après suppression de l’extension, en conservant des données témoins applicatives et d’identité. Le volume survit à l’arrêt des conteneurs. Les migrations métier restent prévues pour M5 ; les scripts du premier démarrage ne les remplacent pas.

Les dimensions des embeddings, modèles, index vectoriels et règles de recherche seront décidés avec une fonctionnalité qui les utilise. Aucun embedding ni schéma de recherche spéculatif n’est requis pour le chat initial.

## Orchestration Aspire

M3 démarre `postgres`, les bases `chatdb`/`keycloakdb`, le conteneur `database-init`, `keycloak` et `web`. La santé PostgreSQL conditionne la création des bases ; leur disponibilité conditionne l’initialisation et Keycloak ; l’activation réussie de vector et la disponibilité de Keycloak conditionnent Web. L’initialiseur borne les délais de connexion, d’instruction et de verrou et échoue avec un code de sortie non nul en cas d’erreur SQL. La configuration d’hébergement reste hors du domaine et du NuGet de composants.

Un volume PostgreSQL nommé conserve les bases distinctes `marmotte_chat` et `marmotte_identity`. Le POC local utilise un rôle administrateur partagé ; les identifiants de production séparés et à privilèges minimaux ne sont pas implémentés. Keycloak 26.6.4 est fixé par empreinte, utilise sa base PostgreSQL et importe le realm `marmotte` et le client confidentiel `marmotte-web` seulement si le realm est absent. Les origines/redirections HTTP locales sont explicites ; la terminaison HTTPS automatique de Keycloak est désactivée pour cette ressource afin que la disponibilité d’un certificat ne change pas l’autorité. Le tableau de bord conserve HTTPS. Les paramètres secrets générés persistent dans les user secrets AppHost ; seuls les paramètres de connexion/client concernés arrivent dans Web. Les gestionnaires OIDC restent prévus pour M4.

ServiceDefaults fournit `/health` et `/alive` en Development uniquement, la découverte de services, des délais HTTP bornés sans retries et des métriques/traces OTLP optionnelles. Il ne configure ni capture de corps/en-têtes personnalisés, ni instrumentation SQL, ni export de journaux. Web contrôle actuellement la santé du processus ; les contrôles continus des dépendances des adaptateurs restent à venir. Un test d’intégration Docker valide le premier démarrage, l’initialisation répétée, la conservation des données et la configuration d’identité visible au navigateur. Consulter [l’exploitation](docs/operations.fr.md) pour le cycle de vie, les secrets, les diagnostics et les limites.

La documentation Aspire décrit les intégrations [PostgreSQL](https://aspire.dev/integrations/databases/postgres/postgres-get-started/) et [Keycloak](https://aspire.dev/integrations/security/keycloak/). Le démarrage local orchestré fait partie de la première version. Une cible de déploiement de production n’est pas encore choisie.

## Pièces jointes, vision et PDF

### Import et images

Le sélecteur et le glisser-déposer utilisent le même flux d’import. Afficher nom, format, taille, aperçu lorsque pertinent, progression, suppression avant envoi et erreurs compréhensibles. Prévoir des limites configurables par fichier et par message.

L’application vérifie le format réel, la taille, les dimensions et les droits d’accès côté serveur. Le contrat du fournisseur expose les capacités nécessaires : un modèle texte seul ne doit pas recevoir des images silencieusement ignorées. Les formats et limites acceptés seront validés pour chaque modèle et serveur ; [LM Studio documente l’entrée image pour les modèles de vision](https://lmstudio.ai/docs/typescript/llm-prediction/image-input).

Les pièces jointes originales et les représentations préparées sont distinctes. Les références et métadonnées permettent d’assurer la propriété, le cycle de vie et la traçabilité du contenu effectivement envoyé.

### Préparation des PDF — étape ultérieure

Le pipeline visé est : validation du PDF, inspection des pages, choix des pages utiles, rendu en images, redimensionnement et encodage adaptés, aperçu, puis envoi des pages sélectionnées au modèle de vision.

Le rendu sera encapsulé derrière un port de préparation documentaire. Prévoir des limites de pages, dimensions, pixels cumulés, mémoire, temps et concurrence ; traiter progressivement plutôt que charger toutes les pages en mémoire. Conserver l’ordre et les numéros des pages. Gérer explicitement les fichiers invalides ou protégés et l’annulation.

Pour les PDF scannés, le rendu en images constitue le chemin principal envisagé. Pour les PDF contenant déjà du texte exploitable, une extraction textuelle peut être évaluée afin d’éviter une conversion inutile, tout en gardant la vision pour la mise en page, les tableaux ou les illustrations.

Le poids du PDF sur disque n’est pas une mesure de sa consommation de contexte. Réduire le poids JPEG facilite le transfert mais ne garantit pas une réduction proportionnelle des tokens visuels. Le coût dépend du modèle, de son prétraitement, des dimensions et du nombre de pages/images. Borner le contenu envoyé tout en préservant la lisibilité, et vérifier les résultats avec les modèles ciblés.

Le choix de la bibliothèque de rendu PDF, de sa licence et du stockage des rendus sera effectué avant cette étape. Aucun moteur, service OCR ou système de files d’attente supplémentaire n’est imposé aujourd’hui.

## Design system et identité visuelle

Le design system expose des tokens sémantiques via des propriétés CSS personnalisées : surfaces, textes, accents, bordures, états, focus, espacements, typographie, rayons et ombres. Les composants utilisent ces tokens plutôt que des couleurs codées individuellement.

Prévoir des thèmes clair, sombre et système, avec préférence explicite de l’utilisateur. Les tokens doivent pouvoir être redéfinis par l’application consommatrice, globalement ou dans le conteneur d’un widget, sans modifier le package ni imposer une feuille de style globale intrusive. Vérifier contraste, clavier, focus et lisibilité dans chaque thème.

La marque reprend la mascotte turquoise à museau crème et écharpe rouge fournie par l’utilisateur. Une bannière Marmotte.ChatUi sert au README et une version compacte sert à l’application. L’identité du widget reste personnalisable par ses consommateurs. Les mentions MarmotteDB et Experimental Proof of Concept appartiennent aux références d’origine et ne constituent pas le nom du produit.

Les images de référence et leur provenance sont conservées dans [docs/images/README.fr.md](docs/images/README.fr.md). Les assets doivent fonctionner sur fond clair et sombre et conserver leur transparence lorsque présente.

## Vérification lors de l’implémentation

- Vérifier l’indépendance des assemblies et la consommation du NuGet dans une application minimale.
- Tester les invariants, l’isolation entre utilisateurs et les opérations de repository.
- Vérifier les migrations et l’activation de pgvector sur des bases neuves et existantes.
- Tester la connexion OIDC et les accès aux ressources protégées.
- Valider chat, streaming, annulation et images sur les serveurs et modèles réellement choisis.
- Vérifier les thèmes, les tokens substitués, le clavier et les états des uploads.
- Lorsque le pipeline PDF existera, vérifier documents scannés, limites, annulation, lisibilité et consommation de ressources.

## Décisions restant à concrétiser

.NET 10, Aspire 13.6.5, les dépendances fixées et le rendu Interactive Server sont retenus dans M1 ; consulter le [guide de développement](docs/development.fr.md) et le [guide d’exploitation](docs/operations.fr.md). L’accès aux données, le stockage des binaires, le moteur de rendu PDF et la cible de production seront précisés lors de l’implémentation correspondante. Ces points ouverts ne remettent pas en cause les frontières de dépendance, PostgreSQL, pgvector, OIDC/Keycloak ou la distribution des composants en NuGet.
