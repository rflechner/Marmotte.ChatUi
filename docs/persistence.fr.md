# Persistance PostgreSQL

[English](persistence.md) | Français

## Périmètre et approche

M5 implémente les repositories applicatifs dans `Marmotte.ChatUi.Persistence.PostgreSql` avec Npgsql 10.0.0 et du SQL paramétré. Le SQL explicite rend vérifiables les filtres de propriétaire, comparaisons de révision, verrous et migrations, sans introduire d’ORM ni d’entités de base dans le domaine. Les [sources de données Npgsql](https://www.npgsql.org/doc/basic-usage.html) fournissent le pool singleton ; chaque opération obtient sa connexion, y compris dans les circuits Blazor. Aucun type de connexion, lecteur, transaction ou fournisseur ne traverse l’API publique de l’adaptateur.

`AddPostgreSqlPersistence` enregistre le pool et `PostgreSqlMigrations` comme singletons et les repositories comme services scoped. DI gère leur libération. Web compose les services de conversations, profils, préférences et secrets quand `ConnectionStrings:chatdb` existe. Sans cette valeur, Web autonome conserve son mode de page de fondation. Les écrans de paramètres et appels aux modèles restent prévus pour M8/M6.

## Schéma et cohérence

Toutes les tables applicatives sont dans le schéma `chat`. Les fichiers SQL embarqués numérotés définissent le schéma ; `chat.schema_migrations` conserve leurs noms, empreintes SHA-256 et dates d’application.

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

La dernière écriture validée gagne pour profils et préférences. Sélection de profil, écriture de secret et suppression de profil verrouillent d’abord la ligne du profil possédé. La suppression efface la sélection profil/modèle correspondante et le secret chiffré dans la même transaction, en préservant le thème. Une sauvegarde tardive ne peut recréer une sélection supprimée simultanément.

`IAttachmentMetadataRepository` insère et lit des métadonnées immuables pour une préparation serveur fiable. Une référence exige des métadonnées existantes, identiques, prêtes et possédées par l’utilisateur de la conversation, dans la même transaction. Des métadonnées absentes, étrangères ou modifiées annulent révision et messages. Aucun endpoint d’upload, accès binaire, transition de préparation, nettoyage d’orphelins ou durée de conservation n’est implémenté ; ils relèvent de M9.

## Protection des secrets

`IModelCredentialStore` est séparé des paramètres ordinaires. `ModelCredentialService` résout l’identité courante à chaque appel : `SaveAsync` remplace un secret validé, `DeleteAsync` le retire et `ResolveAsync` le retourne uniquement au transport serveur. Null indique qu’un profil possédé n’a pas de secret ; les profils absents/étrangers donnent `NotFound`. Aucune valeur n’appartient aux DTO de composants. Ne pas exposer la récupération de secrets dans un endpoint navigateur. `ModelCredential.ToString()` masque la valeur ; `Value` ne doit jamais être journalisée ou sérialisée.

ASP.NET Core Data Protection chiffre et authentifie les octets avant l’exécution SQL. La chaîne de finalités comprend une version, le propriétaire exact et l’UUID du profil : un texte chiffré copié ou modifié ne peut être déchiffré pour un autre profil/utilisateur. Les clés restent hors de PostgreSQL. Clés absentes ou données altérées échouent avec `StorageUnavailable`, sans devenir un secret vide. Des chaînes managées en clair existent encore brièvement en mémoire serveur.

Web utilise le nom d’application `Marmotte.ChatUi` et `Persistence:KeyDirectory`, par défaut `Environment.SpecialFolder.LocalApplicationData/Marmotte.ChatUi/keys`. La valeur doit être un chemin absolu. Aspire transmet l’option `Infrastructure:KeyDirectory` à Web ; les tests fournissent des répertoires temporaires privés. Les nouveaux répertoires Unix sont accessibles au propriétaire uniquement. Protéger les répertoires existants et les ACL Windows. Cette configuration POC locale ne chiffre pas les clés au repos. Voir la [configuration Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Sauvegarder la base **et** son répertoire de clés distinct. Conserver le nom d’application et les anciennes clés lors des déploiements/restaurations ; la rotation automatique garde les anciennes clés pour déchiffrer. Leur perte impose aux utilisateurs de remplacer leurs secrets de modèle. Coffre de clés/certificat de production, stockage partagé entre instances et procédure de rechiffrement sont hors de ce POC. Les jetons OIDC et le magasin de sessions en mémoire restent distincts des secrets de modèle ; un redémarrage Web exige toujours une reconnexion.

## Démarrage, mises à niveau et erreurs

Aspire attend PostgreSQL et l’activation réussie de pgvector avant de démarrer Web. Le service de démarrage Web applique les migrations avant d’accepter les requêtes, avec un délai global de deux minutes. Un verrou consultatif transactionnel sérialise les migrateurs. Toutes les migrations en attente et leurs entrées d’historique sont validées ensemble ; un échec annule la transaction. Empreintes divergentes, versions inconnues et trous dans l’historique appliqué sont refusés. Ne jamais modifier une migration appliquée : ajouter le fichier numéroté suivant. Aucun retour arrière automatique ni réinitialisation destructive. Sauvegarder avant mise à niveau ; vérifier séparément la restauration avant un déploiement de production.

Acquisition de connexion limitée à 15 secondes, commandes/instructions serveur à 30 secondes, attente de verrous à 10 secondes. L’annulation atteint chaque opération de base. Les violations de contraintes donnent `Conflict`, `NotFound` ou `InvalidRequest` ; les autres erreurs de base donnent `StorageUnavailable`, sans détails SQL bruts. Aucune écriture n’est réessayée automatiquement. Une annulation ou perte de connexion près du commit peut laisser un résultat incertain : recharger avant de réessayer. L’adaptateur désactive journalisation des paramètres et détails d’erreurs serveur, et n’active pas de traces SQL.

Les migrations applicatives n’installent pas pgvector : l’initialiseur Aspire existant gère son activation indépendamment du schéma métier. En mode autonome, provisionner la base et l’extension. Aucune table d’embeddings ou recherche vectorielle n’est créée. Les endpoints de santé indiquent encore l’état du processus, sans contrôle continu de disponibilité de la base.

## Vérification

Après les [contrôles de développement](development.fr.md), avec Docker actif :

```sh
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Web.Tests --configuration Release --no-build --no-restore
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Infrastructure.Tests --configuration Release --no-build --no-restore
```

La suite Web crée un conteneur PostgreSQL fixé et un répertoire de clés isolés, puis les supprime. Elle couvre de vrais utilisateurs OIDC avec repositories PostgreSQL, redémarrage hôte/base et récupération du secret, pagination, écritures concurrentes, annulation sous verrou de ligne, rollback/conservation des références de pièces jointes, courses entre préférences/suppression, altération du chiffré et isolation des finalités, mise à niveau depuis le schéma 001, démarrage concurrent des migrations et dérive d’empreinte. La suite Aspire vérifie le démarrage complet, l’activation pgvector, l’historique de migrations et les redémarrages sur volume existant. Les exécutions ordinaires ignorent les tests Docker ; aucun serveur de modèle personnel ni clé payante n’est nécessaire.
