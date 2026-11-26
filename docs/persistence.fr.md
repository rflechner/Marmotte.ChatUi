# Persistance PostgreSQL

[English](persistence.md) | Français

## Périmètre et approche

M5 implémente les repositories applicatifs dans `Marmotte.ChatUi.Persistence.PostgreSql` avec **EF Core 10.0.12 en code first** et le [fournisseur EF Npgsql](https://www.npgsql.org/efcore/) 10.0.0. Des entités de persistance internes et leurs configurations Fluent API définissent le modèle relationnel ; des migrations C# générées et un instantané du modèle le versionnent. Les objets métier restent indépendants d’EF. Aucun contexte, entité, queryable, connexion, transaction ou type fournisseur ne traverse l’API publique de l’adaptateur. LINQ, écritures suivies et mises à jour/suppressions en masse EF implémentent les repositories. Le SQL paramétré via EF se limite aux verrous PostgreSQL de ligne/consultatifs et à la reprise unique de l’ancien schéma.

`AddPostgreSqlPersistence` enregistre un `IDbContextFactory<ChatDbContext>` singleton et `PostgreSqlMigrations`, avec des repositories scoped. Chaque opération crée et libère son contexte, sans partager le suivi des modifications sur tout un circuit Blazor. Npgsql gère le pool de connexions sous-jacent. Web compose les services de conversations, profils, préférences et secrets quand `ConnectionStrings:chatdb` existe. Sans cette valeur, Web autonome conserve son mode de page de fondation. Les écrans de paramètres et appels aux modèles restent prévus pour M8/M6.

## Schéma et cohérence

Toutes les tables applicatives sont dans le schéma `chat`. `ChatDbContext` et `EntityConfigurations.cs` définissent mappings, clés, index, contraintes et relations. Le répertoire `Migrations` contient `InitialHistoryAndSettings`, `ProtectedModelCredentials`, leurs métadonnées générées et `ChatDbContextModelSnapshot`. EF enregistre les identifiants appliqués et versions produit dans `chat."__EFMigrationsHistory"`. Aucun `EnsureCreated` ni schéma SQL maintenu manuellement n’est utilisé.

| Table | Clés, index et conservation |
| --- | --- |
| `conversations` | UUID global, unicité propriétaire/UUID, révision positive, index propriétaire/création/UUID pour les pages. Suppression en cascade des messages. |
| `messages` | Clé propriétaire/conversation/message ; séquence et soumission/rôle uniques par conversation ; une seule réponse assistant active. Suppression en cascade des références aux pièces jointes. |
| `attachments` | UUID global et clé propriétaire/UUID, métadonnées validées, aucun octet ni chemin de stockage. Conservées après suppression des conversations. |
| `message_attachments` | Clé propriétaire/conversation/message/pièce jointe, positions ordonnées, clés étrangères composites avec propriétaire vers messages et métadonnées. |
| `profiles` | UUID global, unicité propriétaire/UUID, index propriétaire/création/UUID pour les pages. Paramètres ordinaires uniquement. |
| `preferences` | Une ligne par propriétaire, thème, profil possédé et modèle optionnels. |
| `profile_credentials` | Clé propriétaire/profil, octets protégés, suppression en cascade avec le profil. |

Chaque lecture et écriture filtre sur le propriétaire fiable du serveur. Les clés étrangères composites empêchent aussi les références entre propriétaires. Un identifiant reçu du navigateur ne prouve jamais la propriété. Le chargement restaure les objets métier via leurs constructeurs de validation ; les historiques corrompus échouent sans réparation silencieuse. Les horodatages PostgreSQL ont une précision à la microseconde : les fractions inférieures des ticks .NET sont tronquées au stockage.

Les sauvegardes comparent la révision attendue et écrivent exactement la suivante, en remplaçant atomiquement les lignes de messages/références. Une seule écriture concurrente gagne ; les modifications et suppressions obsolètes donnent `Conflict`. Une transaction repeatable-read charge métadonnées, messages et références depuis un même instantané cohérent. La navigation ne sélectionne aucun contenu de message, trie par création puis UUID et lit une ligne supplémentaire pour `HasMore`. Les pages par décalage ne forment pas un instantané entre requêtes.

Une soumission identique réessayée est reconnue par l’application/domaine après rechargement ; les deltas ne sont pas idempotents. Texte partiel, état de génération et catégories d’échec sûres survivent au redémarrage. Celui-ci ne relance aucun appel modèle et ne marque pas automatiquement les réponses actives comme interrompues. M8 doit définir la récupération et la fréquence des sauvegardes. Le remplacement de l’historique complet reste volontairement simple et proportionnel à sa taille ; il ne convient pas aux historiques illimités ni à une écriture par token.

La dernière écriture validée gagne pour profils et préférences. Un verrou transactionnel par propriétaire sérialise les sauvegardes de préférences, y compris les premières sauvegardes simultanées. Sélection de profil, écriture de secret et suppression de profil verrouillent d’abord la ligne du profil possédé. La suppression efface la sélection profil/modèle correspondante et le secret chiffré dans la même transaction, en préservant le thème. Une sauvegarde tardive ne peut recréer une sélection supprimée simultanément.

`IAttachmentMetadataRepository` insère et lit des métadonnées immuables pour une préparation serveur fiable. Une référence exige des métadonnées existantes, identiques, prêtes et possédées par l’utilisateur de la conversation, dans la même transaction. Des métadonnées absentes, étrangères ou modifiées annulent révision et messages. Aucun endpoint d’upload, accès binaire, transition de préparation, nettoyage d’orphelins ou durée de conservation n’est implémenté ; ils relèvent de M9.

## Protection des secrets

`IModelCredentialStore` est séparé des paramètres ordinaires. `ModelCredentialService` résout l’identité courante à chaque appel : `SaveAsync` remplace un secret validé, `DeleteAsync` le retire et `ResolveAsync` le retourne uniquement au transport serveur. Null indique qu’un profil possédé n’a pas de secret ; les profils absents/étrangers donnent `NotFound`. Aucune valeur n’appartient aux DTO de composants. Ne pas exposer la récupération de secrets dans un endpoint navigateur. `ModelCredential.ToString()` masque la valeur ; `Value` ne doit jamais être journalisée ou sérialisée.

ASP.NET Core Data Protection chiffre et authentifie les octets avant affectation à l’entité EF ; aucun secret en clair n’est suivi. La chaîne de finalités comprend une version, le propriétaire exact et l’UUID du profil : un texte chiffré copié ou modifié ne peut être déchiffré pour un autre profil/utilisateur. Les clés restent hors de PostgreSQL. Clés absentes ou données altérées échouent avec `StorageUnavailable`, sans devenir un secret vide. Des chaînes managées en clair existent encore brièvement en mémoire serveur.

Web utilise le nom d’application `Marmotte.ChatUi` et `Persistence:KeyDirectory`, par défaut `Environment.SpecialFolder.LocalApplicationData/Marmotte.ChatUi/keys`. La valeur doit être un chemin absolu. Aspire transmet l’option `Infrastructure:KeyDirectory` à Web ; les tests fournissent des répertoires temporaires privés. Les nouveaux répertoires Unix sont accessibles au propriétaire uniquement. Protéger les répertoires existants et les ACL Windows. Cette configuration POC locale ne chiffre pas les clés au repos. Voir la [configuration Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Sauvegarder la base **et** son répertoire de clés distinct. Conserver le nom d’application et les anciennes clés lors des déploiements/restaurations ; la rotation automatique garde les anciennes clés pour déchiffrer. Leur perte impose aux utilisateurs de remplacer leurs secrets de modèle. Coffre de clés/certificat de production, stockage partagé entre instances et procédure de rechiffrement sont hors de ce POC. Les jetons OIDC et le magasin de sessions en mémoire restent distincts des secrets de modèle ; un redémarrage Web exige toujours une reconnexion.

## Démarrage, mises à niveau et erreurs

Aspire attend PostgreSQL et l’activation réussie de pgvector avant de démarrer Web. Le service de démarrage Web appelle `PostgreSqlMigrations.ApplyAsync`, puis `Database.MigrateAsync` d’EF, avant d’accepter les requêtes, avec un délai global de deux minutes. EF gère verrous de migration, transactions et historique. Les limites transactionnelles suivent EF et les opérations générées, y compris celles explicitement exclues d’une transaction ; diagnostiquer une mise à niveau défaillante avant redémarrage. Le service refuse les identifiants inconnus et trous dans l’historique EF. Ne jamais modifier une migration appliquée : modifier le modèle et générer la suivante. Aucun retour arrière automatique ni réinitialisation destructive. Sauvegarder avant mise à niveau ; vérifier séparément la restauration avant un déploiement de production.

Acquisition de connexion limitée à 15 secondes, commandes/instructions serveur à 30 secondes, attente de verrous à 10 secondes. L’annulation atteint chaque opération de base. Les violations de contraintes donnent `Conflict`, `NotFound` ou `InvalidRequest` ; les autres erreurs de base donnent `StorageUnavailable`, sans détails SQL bruts. Aucune écriture n’est réessayée automatiquement. Une annulation ou perte de connexion près du commit peut laisser un résultat incertain : recharger avant de réessayer. L’adaptateur désactive journalisation des paramètres et détails d’erreurs serveur, et désactive les journaux EF pour empêcher les données privées des instructions d’atteindre les fournisseurs de journaux de l’hôte.

Les migrations applicatives n’installent pas pgvector : l’initialiseur Aspire existant gère son activation indépendamment du schéma métier. En mode autonome, provisionner la base et l’extension. Aucune table d’embeddings ou recherche vectorielle n’est créée. Les endpoints de santé indiquent encore l’état du processus, sans contrôle continu de disponibilité de la base.

## Procédure de migrations EF

Exécuter ces commandes depuis la racine du dépôt. Le manifeste d’outils local fixe `dotnet-ef` à 10.0.12. La fabrique de conception lit uniquement `ConnectionStrings__chatdb` ; elle ne démarre ni Web, ni Aspire, ni Keycloak. Une connexion fictive sans secret suffit pour lister, générer, produire les scripts et contrôler le modèle :

```sh
dotnet tool restore
export ConnectionStrings__chatdb='Host=localhost;Database=marmotte_design;Username=design'
dotnet ef migrations list --no-connect --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
dotnet ef migrations has-pending-model-changes --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
dotnet ef migrations script --idempotent --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release --output artifacts/postgresql-migrations.sql
```

Pour modifier le schéma, changer les entités/configurations internes, puis exécuter :

```sh
dotnet ef migrations add DescribeSchemaChange --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
```

Remplacer `DescribeSchemaChange` par un nom explicite. Relire ensemble `Up`, `Down`, métadonnées générées et instantané ; les versionner ensemble. Adapter le code généré aux conventions du dépôt : types internes, résumés XML, namespaces limités au fichier et collections conformes aux analyseurs. Recompiler et vérifier l’absence de changement du modèle en attente. Tester une base neuve et une mise à niveau avec données ; un contrôle de différence vide ne prouve aucun comportement à l’exécution. Retirer une migration ne convient qu’avant son application/partage ; ne jamais supprimer l’historique pour contourner un échec.

Pour une base neuve ou déjà gérée par EF, fournir sa connexion réelle dans une variable d’environnement privée, puis exécuter :

```sh
dotnet ef database update --project src/Marmotte.ChatUi.Persistence.PostgreSql --configuration Release
```

Ne pas mettre d’identifiants dans les arguments, paramètres versionnés ou historique du shell. L’opérateur doit provisionner base/extension et accorder les modifications de schéma nécessaires. Le POC local migre automatiquement au démarrage ; la production nécessiterait une procédure de déploiement revue. Relire les scripts générés avant application à un schéma correspondant. Voir la [gestion des migrations EF](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing) et leur [déploiement](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

## Bases existantes gérées par SQL

Les bases de l’implémentation M5 précédente conservent `chat.schema_migrations`. Au **premier démarrage de Web**, une passerelle de compatibilité vérifie les deux empreintes SQL connues, la structure des colonnes mappées et les noms des contraintes sous l’ancien verrou consultatif. Elle crée l’index manquant de clé étrangère des préférences et enregistre atomiquement le point de départ EF équivalent, sans remplacer les tables utilisateur. EF applique ensuite les migrations restantes. Une base en version 001 reçoit la migration des secrets ; une base en version 002 conserve ses secrets chiffrés et son répertoire de clés existant. L’ancien registre est conservé comme trace historique ; EF gère les changements suivants.

Un schéma ancien inconnu, modifié ou incomplet fait échouer le démarrage sans supposition ni réinitialisation. Les contrôles détectent les colonnes manquantes/modifiées et noms de contraintes ; ce n’est pas un audit complet de leurs expressions ou des index. Résoudre la dérive depuis une sauvegarde/réparation revue. **Ne pas exécuter `dotnet ef database update` ou un script initial généré sur une ancienne base non encore reprise** : ces commandes contournent la passerelle de l’hôte. Démarrer une fois la nouvelle version de Web avant d’utiliser les outils EF. Les fichiers SQL historiques restent uniquement comme fixtures de test, jamais comme migrations d’exécution.

## Vérification

Après les [contrôles de développement](development.fr.md), avec Docker actif :

```sh
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Web.Tests --configuration Release --no-build --no-restore
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Infrastructure.Tests --configuration Release --no-build --no-restore
```

La suite Web crée un conteneur PostgreSQL fixé et un répertoire de clés isolés, puis les supprime. Elle couvre de vrais utilisateurs OIDC avec repositories PostgreSQL, redémarrage hôte/base et récupération du secret, pagination, écritures concurrentes, annulation sous verrou de ligne, rollback/conservation des références de pièces jointes, courses entre préférences/suppression, altération du chiffré et isolation des finalités, mise à niveau entre migrations EF, démarrage concurrent, reprise des deux versions SQL historiques avec conservation des données, refus des historiques/schémas incompatibles et dérive modèle/instantané. La suite Aspire vérifie le démarrage complet, l’activation pgvector, l’historique de migrations et les redémarrages sur volume existant. Les exécutions ordinaires ignorent les tests Docker ; aucun serveur de modèle personnel ni clé payante n’est nécessaire.
