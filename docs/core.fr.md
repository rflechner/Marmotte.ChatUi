# Modèle métier et ports applicatifs

[English](core.md) | Français

## Périmètre implémenté

M2 implémente le domaine, les ports indépendants des fournisseurs, les contrats de présentation et les cas d’usage autorisés des conversations et paramètres. Les [tests du cœur](../tests/Marmotte.ChatUi.Core.Tests) les exercent avec un adaptateur de test en mémoire et une horloge contrôlée. M4 enregistre l’adaptateur OIDC scoped `ICurrentUser` dans Web et le teste avec ces cas d’usage. Les services métier attendent les repositories de production avant composition dans l’hôte ; persistance en base, appels de modèles, uploads et orchestration de génération restent non implémentés.

Le modèle d’IA est le modèle distant choisi dans un profil de connexion. Le modèle métier ci-dessous décrit le comportement de l’application sans dépendre de ce fournisseur, de Blazor ou des bibliothèques de persistance. Domain et Contracts n’ont aucune dépendance de projet ou de package ; Application référence seulement Domain et Contracts.

## Vocabulaire et frontières

| Type | Responsabilité et invariants |
| --- | --- |
| `UserId` | Identité applicative opaque sensible à la casse, de 1 à 256 caractères, non vide. L’adaptateur d’identité M4 associe un couple émetteur/sujet vérifié à cet ID stable ; un propriétaire fourni par le navigateur n’est jamais accepté. |
| `Conversation` | Racine d’agrégat immuable possédant son historique complet et ordonné. GUID non vide, propriétaire immuable, titre de 1 à 200 caractères, horodatages UTC, révision positive. Le repository sauvegarde tout l’agrégat atomiquement. |
| `Message` | Enfant immuable d’une conversation, avec son propre GUID non vide, un GUID de soumission, une séquence commençant à 1, un rôle, un contenu, un cycle de vie et des horodatages UTC. Jamais sauvegardé indépendamment de sa conversation. |
| `Attachment` | Métadonnées possédées séparément et référencées par un message utilisateur : GUID, propriétaire, nom de fichier, type de média, taille positive en octets, date de création, état de préparation, largeur/hauteur positives facultatives fournies ensemble. Ni contenu binaire, ni chemin de stockage, ni URL publique. |
| `ConnectionProfile` | Agrégat de paramètres immuable distinct, propre à l’utilisateur : GUID, nom de 1 à 100 caractères, endpoint, modèle de 1 à 256 caractères, options de génération facultatives et horodatages UTC. Aucune clé API ni autre secret. |
| `UserPreferences` | Un instantané immuable par utilisateur : thème System/Light/Dark, profil sélectionné et modèle de substitution facultatifs. Un modèle de substitution nécessite un profil. |

Les chaînes obligatoires rejettent les valeurs composées uniquement d’espaces. Les GUID rejettent `Guid.Empty`. Les dates de création et de mise à jour sont fournies explicitement, normalisées en UTC et ne doivent pas reculer. Les services applicatifs utilisent le `TimeProvider` injecté. Les constructeurs/fabriques rejettent les arguments invalides avec `ArgumentException` (ou un type dérivé) ; les transitions métier invalides lèvent `InvalidOperationException`. L’application traduit ces exceptions en erreurs sûres `InvalidRequest` et `Conflict`.

`Conversation.Create` commence à la révision 1. `RecordMessage` et `UpdateResponse` renvoient de nouveaux instantanés avec la révision suivante ; les instances et collections en lecture seule existantes restent inchangées. `Restore` valide tout l’historique persisté : paires utilisateur/assistant complètes, identifiants de message/soumission distincts, séquences contiguës, rôles, chronologie, révision minimale, propriété des pièces jointes et au plus une réponse active en fin d’historique. Il sert au mapping de persistance, pas aux entrées du navigateur. Un historique corrompu doit empêcher le chargement plutôt que perdre ou réordonner silencieusement les données.

## Tours, nouvelles tentatives et génération

`RecordMessage(submissionId, content, now, attachments)` crée ensemble un message utilisateur terminé et une réponse d’assistant en attente, avec le même identifiant de soumission et la même date de création. Le message utilisateur doit contenir du texte non blanc ou des pièces jointes prêtes. Les identifiants de pièces jointes doivent être uniques dans le message, appartenir au même propriétaire et exister à la date d’envoi. Les métadonnées ne valident ni les octets du fichier ni les capacités du modèle. L’API applicative M2 accepte uniquement du texte ; la validation des uploads, les variantes préparées et l’autorisation de vision arrivent en M9.

Une conversation autorise une seule réponse en attente ou en réception progressive. Elle rejette toute autre soumission avant son état terminal. Une nouvelle tentative identique, avec le même identifiant de soumission, texte et identifiants de pièces jointes ordonnés, renvoie l’instantané existant sans écriture, même après la fin. Réutiliser l’identifiant avec un contenu différent crée un conflit. Une tentative concurrente peut rencontrer un conflit de révision ; recharger puis réessayer avec le même identifiant. Un nouveau tour intentionnel utilise toujours un nouvel identifiant de soumission.

| État courant de la réponse | États suivants autorisés | Comportement du contenu |
| --- | --- | --- |
| Pending | Streaming, Completed, Cancelled, Failed | Ajouter le fragment fourni ; une fin directe permet les fournisseurs sans streaming. |
| Streaming | Streaming, Completed, Cancelled, Failed | Ajouter dans l’ordre d’arrivée ; conserver le texte précédent. |
| Completed, Cancelled, Failed | Aucun | Immuable ; rejeter les mises à jour tardives. Un nouveau tour utilisateur est autorisé. |

`Failed` exige une catégorie `GenerationFailure` ; les autres états l’interdisent. Ces catégories ne contiennent aucun message brut du fournisseur. Une réponse vide en attente peut être annulée. L’annulation ou l’échec conserve le texte partiel dans l’historique. Restaurer une réponse active ne reprend pas une requête réseau : une future orchestration devra marquer explicitement le travail interrompu comme échoué ou annulé avant un nouveau tour. Aucune récupération en arrière-plan ne s’exécute dans M2.

Les fragments du fournisseur sont appliqués une fois dans l’ordre et ne sont **pas idempotents**. Ne pas réessayer automatiquement une écriture de fragment au résultat incertain. Les révisions empêchent la perte de mises à jour concurrentes mais n’identifient pas les fragments répétés. Après reconnexion ou validation ambiguë, recharger l’historique persisté ; la reprise/réconciliation de génération relève de la future orchestration.

## Opérations applicatives publiques

Les composants dépendent uniquement des interfaces et DTO de `Marmotte.ChatUi.Contracts`. Aucune requête n’accepte d’identifiant de propriétaire. Les implémentations Application interrogent `ICurrentUser` à chaque opération et vérifient la propriété des résultats, y compris des listes, en complément du filtre obligatoire du repository.

| Service / méthode | Entrée et résultat |
| --- | --- |
| `IConversationService.CreateAsync` | Titre → `ConversationView` vide avec identifiant généré côté serveur. |
| `ListAsync` | `PageRequest` → `Page<ConversationSummary>` contenant les métadonnées de navigation sans les corps des messages. |
| `LoadAsync` | Identifiant de conversation → `ConversationView` complet et ordonné. |
| `DeleteAsync` | Identifiant de conversation → aucun résultat ; chargement/autorisation puis suppression avec la révision observée. Un ID absent ou étranger produit `NotFound`. |
| `RecordMessageAsync` | Identifiant de conversation + `RecordMessageRequest(SubmissionId, Content)` → instantané mis à jour avec message utilisateur et réponse en attente. N’appelle pas de modèle. |
| `CancelResponseAsync` | Identifiant de conversation + identifiant du message assistant → instantané avec annulation durable et contenu partiel conservé. Une réponse terminale produit `Conflict`. |
| `ConversationService.RecordResponseAsync` | Identifiants de conversation/message + `ResponseDelta`, côté serveur uniquement → instantané après mise à jour du fournisseur. Absent de l’interface des composants ; ne doit pas devenir un endpoint d’écriture du navigateur. |
| `IConnectionProfileService.CreateAsync` / `UpdateAsync` | `SaveProfileRequest`, et ID pour la mise à jour → `ConnectionProfileView` ; la création génère l’ID, la mise à jour conserve l’identité et la date de création. |
| `LoadAsync` / `ListAsync` / `DeleteAsync` des profils | ID possédé ou page → profil, page ou aucun résultat. La suppression efface aussi le profil/modèle sélectionné correspondant pour le propriétaire. |
| `IUserPreferencesService.LoadAsync` / `SaveAsync` | Aucune entrée ou `PreferencesView` → préférences courantes. Sans préférences, le défaut est System sans profil/modèle. La sauvegarde vérifie la propriété du profil sélectionné. |

Toutes les méthodes acceptent un `CancellationToken` facultatif. Elles lèvent `ChatOperationException` avec `Unauthenticated` sans identité, `NotFound` pour une ressource absente ou étrangère, `InvalidRequest` pour une entrée invalide et `Conflict` pour un conflit de cycle de vie ou de révision. Le constructeur de `PageRequest` lève lui-même `ArgumentOutOfRangeException` pour des bornes invalides. Les échecs des fournisseurs utilisent des catégories sûres ; la présentation traduit un échec persisté en `GenerationFailed`, prêt pour un retour localisé par l’hôte. L’hôte doit traiter les erreurs inattendues d’adaptateur sans afficher les détails bruts des exceptions.

L’annulation d’un jeton d’E/S lève `OperationCanceledException` ; elle ne persiste pas une réponse annulée. Utiliser pour cela `CancelResponseAsync` avec un jeton d’opération encore actif. Une annulation après validation du stockage peut laisser une écriture réussie ; recharger avant de réessayer. Aucune nouvelle tentative automatique d’écriture n’est implémentée.

Les profils exigent un endpoint HTTP(S) absolu sans informations utilisateur, query string ni fragment. Les chemins et ports explicites sont conservés ; aucun `/v1`, port, clé ou version de fournisseur n’est ajouté. La température est facultative, finie et positive ou nulle ; le maximum de tokens de sortie est facultatif et strictement positif. Les plages exactes des paramètres, l’accessibilité de l’endpoint, la politique d’accès réseau, les capacités et l’authentification doivent être contrôlées par le futur adaptateur/hôte. La protection et la récupération des secrets restent prévues pour M5.

## Garanties des ports et composition

| Port | Comportement requis de l’adaptateur |
| --- | --- |
| `ICurrentUser` | Résoudre l’identité courante vérifiée ou null si anonyme/expirée ; ne jamais partager un cache entre utilisateurs. M4 fournit le mapping OIDC et contrôle la session serveur active à chaque appel. |
| `IConversationRepository` | Recherche/liste/sauvegarde/suppression filtrées par propriétaire. La sauvegarde compare `expectedRevision` ; 0 signifie insertion, sinon écrire exactement la révision suivante. Persister atomiquement le tour, l’ordre, les métadonnées et les références autorisées aux pièces jointes. Rejeter les anciennes révisions sans écriture partielle. La suppression retire atomiquement les références ; la rétention binaire reste séparée. |
| `IConnectionProfileRepository` | Recherche/liste/insertion/mise à jour/suppression filtrées par propriétaire ; distinguer insertion et mise à jour pour éviter les recréations involontaires. La suppression efface atomiquement les préférences correspondantes. Aucun secret dans ces données. |
| `IUserPreferencesRepository` | Insertion ou mise à jour possédée avec un instantané par utilisateur ; valider la propriété et l’existence du profil sélectionné dans la même transaction pour fermer la course avec sa suppression. |
| `IModelCapabilities` | Capacités du profil/modèle exact autorisé : Unknown, Unsupported ou Supported, séparément pour le streaming et les images. Unknown ne signifie jamais pris en charge. |
| `IResponseGenerator` | `IAsyncEnumerable<ResponseDelta>` ordonné pour un `ResponseRequest(Profile, History)` autorisé. L’historique finit au nouveau message utilisateur, sans sa réponse d’assistant en attente. L’appelant choisit explicitement si/comment inclure les anciennes réponses partielles. |

La recherche renvoie null pour une donnée absente ou étrangère ; les erreurs d’écriture/suppression ne révèlent pas les données d’autrui. Les repositories renvoient des instantanés métier détachés et immuables. Les écritures de conversation utilisent la concurrence optimiste ; pour les paramètres, la dernière écriture validée gagne. Les listes sont triées par `CreatedAt` croissant puis `Id` croissant, avec un décalage commençant à zéro et une limite de 1 à 100 (25 par défaut). Lire un enregistrement supplémentaire pour calculer `HasMore` ; aucun total ni instantané entre requêtes n’est garanti. Des insertions/suppressions concurrentes peuvent décaler les pages.

Les adaptateurs de réponse doivent valider les options/capacités, émettre les fragments progressifs puis exactement un résultat terminal, traduire les erreurs sans exposer les exceptions du SDK, imposer un délai maximal fini documenté, propager l’annulation pendant l’énumération et libérer le transport lors d’un arrêt anticipé. Aucune implémentation de fournisseur, valeur de délai de transport ou compatibilité n’est encore revendiquée. Les ports de stockage/préparation des pièces jointes seront introduits avec leurs cas d’usage réels en M9/M11.

Composer les trois services manuellement ou dans le conteneur DI de l’hôte avec une identité vérifiée, les adaptateurs de repository et `TimeProvider.System`. Les services ne possèdent ni ne libèrent les dépendances injectées ; leur portée suit la requête/l’utilisateur courant, et les adaptateurs portant une identité ne doivent pas être des singletons partagés. Les instantanés métier sont immuables ; les adaptateurs gèrent leurs propres ressources. Les adaptateurs de test restent sous tests et ne constituent pas une persistance applicative.

## Présentation et limites actuelles

`ConversationView`, `MessageView`, `AttachmentView`, `ConnectionProfileView` et `PreferencesView` ne contiennent ni propriétaire, ni secret, ni type SDK, ni adresse de stockage. `MessageView` expose la séquence, les horodatages, l’identifiant de soumission, le cycle de vie, le contenu partiel et une catégorie d’erreur sûre. `GenerationUpdate` définit le futur événement progressif d’UI (identifiants de conversation/message, fragment, état, erreur facultative) ; aucune source d’événements n’est encore connectée. Les consommateurs appliquent les mises à jour une fois dans l’ordre et rechargent les instantanés après reconnexion. L’état d’une pièce jointe décrit sa préparation, pas une preuve d’envoi à un modèle compatible.

La liste paginée des conversations exclut les corps des messages, mais le chargement et la mise à jour d’une conversation matérialisent actuellement tout son historique. Ce petit agrégat initial ne constitue pas une optimisation pour des historiques illimités ou des écritures en base à chaque token. M5/M8 devront valider les transactions, le chargement d’historique, les sauvegardes intermédiaires et la récupération avec de vrais adaptateurs. L’authentification des modèles, le stockage, l’UI de chat, le streaming réel, les uploads et NuGet restent pour les milestones suivants. L’authentification utilisateur est implémentée dans M4.

Lancer les vérifications du [guide de développement](development.fr.md). Les tests couvrent la propriété (y compris des adaptateurs de lecture défectueux), la pagination, la validation/sélection/suppression des paramètres, l’annulation, les doublons, les écritures concurrentes, l’immuabilité, la restauration, les invariants des métadonnées de pièces jointes et le cycle des réponses sans infrastructure externe. Cela ne prouve pas une compatibilité de base de données ou de fournisseur.
