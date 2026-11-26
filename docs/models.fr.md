# Adaptateur de modèles compatibles OpenAI

[English](models.md) | Français

## Périmètre implémenté et choix du SDK

M6 ajoute `OpenAIModelAdapter` dans Infrastructure.OpenAI et l’enregistre dans Web lorsque `ConnectionStrings:chatdb` est configuré. Il implémente `IResponseGenerator`, `IModelConnection` et `IModelCapabilities` sans exposer de type SDK. Le raccordement de l’UI de chat, de la persistance des générations et des écrans de paramètres reste pour M8. Les composants et le cœur applicatif ne dépendent pas du SDK.

[OpenAI 2.15.0](https://www.nuget.org/packages/OpenAI/2.15.0) est fixé. L’adaptateur utilise uniquement Chat Completions et Models, avec endpoints configurables. Sa politique d’authentification personnalisée ajoute un en-tête Bearer seulement lorsqu’un secret possédé existe ; aucune clé factice n’est inventée. Le constructeur d’authentification personnalisé du SDK est expérimental (`OPENAI001`) : la suppression est limitée à la création du client, protégée par des tests HTTP. Les mises à jour du SDK doivent relancer ces tests. Les retries, journaux, journaux de contenu et traces distribuées du SDK sont désactivés ; la télémétrie HTTP de l’hôte conserve les métadonnées ordinaires des requêtes, sans corps ni secrets.

La [documentation du SDK](https://github.com/openai/openai-dotnet#using-a-custom-base-url-and-api-key), l’[API LM Studio](https://lmstudio.ai/docs/developer/openai-compat), l’[authentification LM Studio](https://lmstudio.ai/docs/developer/core/authentication) et l’[API Unsloth](https://unsloth.ai/docs/basics/api) ont servi à l’évaluation. La documentation ne prouve pas la compatibilité d’exécution : seuls les contrôles ci-dessous ont été effectués.

## Profils, propriété et transport

Persister l’URL de base complète de l’API, avec le chemin réel du serveur. L’adaptateur ajoute `chat/completions` ou `models` ; il n’ajoute jamais `/v1`, ne change pas de port et ne sonde aucune adresse supposée. La barre oblique finale est normalisée. Pour l’instance LM Studio testée, l’URL de base est `http://127.0.0.1:1234/v1` et le modèle `lfm3-1.7b`. Ce sont des paramètres testés, pas des valeurs par défaut pour d’autres installations.

Les profils métier exigent HTTP(S), sans informations utilisateur, query string ni fragment. L’adaptateur accepte des identifiants de modèle opaques non vides jusqu’à 256 caractères sans contrôles, une température de 0 à 2 et des limites de sortie facultatives strictement positives. Les options nulles omettent les champs et utilisent les valeurs du serveur. La limite de sortie est sérialisée en `max_completion_tokens` ; une option non prise en charge échoue de manière sûre, sans suppression ni retry automatique. La découverte est facultative : un modèle saisi manuellement n’a pas besoin de figurer dans la liste. Les plages propres au serveur et les fenêtres de contexte dépendent toujours du serveur/modèle choisi.

Avant chaque opération, l’adaptateur résout `ICurrentUser` de confiance, vérifie le propriétaire du profil persisté et compare endpoint, modèle, options et date de mise à jour avec l’instantané reçu. Un profil absent/étranger donne `NotFound` ; des paramètres modifiés/obsolètes donnent `Conflict`, avant récupération du secret ou accès réseau. Les secrets sont relus depuis `IModelCredentialStore`. L’appelant doit autoriser séparément l’historique : ces ports sont réservés au serveur et ne doivent pas accepter d’historiques ou de profils arbitraires soumis par le navigateur.

Web enregistre une seule instance scoped de l’adaptateur derrière les trois ports. Les clients SDK propres à chaque opération contiennent le secret et ne sont pas mis en cache. Le transport HTTP partagé n’a ni secrets par défaut, ni cookies, ni redirections, ni loggers de requêtes, ni retries. Les redirections ne sont pas suivies, même vers la même origine : configurer l’URL finale de l’API. Les consommateurs directs possèdent leur HttpClient injecté et doivent conserver ces règles. HTTP et les adresses privées permettent les serveurs locaux ; l’hôte doit restreindre les personnes pouvant configurer les endpoints et définir une politique réseau sortante adaptée au déploiement. Aucune liste d’autorisation SSRF ou filtration DNS de production n’est revendiquée.

`Models:OperationTimeoutSeconds` vaut 120 par défaut, doit être supérieur à zéro et au plus égal à 1800. Une échéance liée couvre l’autorisation et toute l’opération HTTP, y compris les corps de flux bloqués. Le délai de 30 secondes du client de l’hôte est remplacé par celui de l’adaptateur. Une échéance pendant la résolution d’identité/stockage donne `StorageUnavailable` ; l’annulation de l’appelant reste une `OperationCanceledException`.

## Appels et cycle de vie

| Contrat | Comportement |
| --- | --- |
| `DiscoverAsync(profile, token)` | Appelle Models ; renvoie des identifiants opaques dédupliqués. HTTP 404/405/501 signifie Unsupported et autorise la saisie manuelle. Les erreurs d’authentification/indisponibilité donnent Unknown avec une erreur sûre, pas une liste vide réussie. |
| `CheckAsync(profile, streaming, token)` | Envoie la consigne fixe `Reply with OK.` avec au plus 256 tokens de sortie (ou la limite inférieure du profil), sans historique ni retour/journalisation du texte généré. Consomme des ressources d’inférence. Une troncature avec texte visible peut établir la connectivité ; avant tout texte, elle fait échouer le contrôle. |
| `GetAsync(profile, token)` | Renvoie des preuves récentes, sans supposer les capacités. Un contrôle streaming réussi établit Supported pour ce propriétaire/profil/endpoint/modèle/date exacts pendant cinq minutes. Une seule preuve de profil est conservée par scope. Les autres profils, preuves expirées et nouveaux scopes donnent Unknown. |
| `GenerateAsync(ResponseRequest(profile, history, Stream), token)` | `Stream` vaut false par défaut et produit un résultat terminal unique. True exige un contrôle streaming réussi dans le même scope. Aucun repli automatique ne lance une autre génération. |

L’historique est copié avant le transport, ordonné par séquences strictement croissantes, terminé par un message utilisateur et exclut les réponses en attente/en streaming. L’appelant choisit explicitement d’inclure ou non les anciennes réponses partielles annulées/échouées. Toute pièce jointe est refusée avec `UnsupportedCapability` : M6 n’a pas de préparation binaire et rien n’est ignoré silencieusement. ImageInput reste Unknown ; ni le nom du modèle ni la découverte Models ne prouvent la vision. Responses, Files, outils, audio et images ne sont pas implémentés.

Le streaming émet les fragments de texte dans l’ordre et exactement une mise à jour terminale. Un arrêt naturel termine la réponse ; l’absence de motif de fin, un SSE malformé ou une déconnexion échoue en préservant le texte déjà émis. Une fin par longueur donne ContextLimit et conserve le texte ; refus/filtrage de contenu/appels d’outils échouent de manière sûre. Les extensions de raisonnement du fournisseur ne sont pas exposées comme texte de réponse. L’annulation de l’appelant lève une exception et la libération de l’itérateur libère le transport ; l’appelant persiste séparément l’annulation avec un token actif. L’annulation ferme la réponse cliente, mais l’arrêt du calcul côté serveur dépend de celui-ci.

| Erreur | Traduction |
| --- | --- |
| Code structuré `context_length_exceeded` ou fin par longueur | ContextLimit |
| HTTP 4xx hors 408/429, dont secret refusé ou modèle inconnu | Rejected |
| Erreur réseau, HTTP 408/429/5xx ou délai dépassé avant texte | Unavailable |
| Erreur de transport/protocole ou délai dépassé après texte progressif | Interrupted |
| Réponse non progressive malformée ou terminaison inconnue | Unknown |

Aucun corps fournisseur brut, message d’exception, secret ou texte de conversation n’est renvoyé dans les erreurs. Aucun retry automatique, même pour HTTP 429/500/503. Recharger/réconcilier l’état persisté avant une nouvelle tentative explicite de l’utilisateur. Une inférence réussie n’est pas encore écrite automatiquement dans les conversations.

## Vérification et compatibilité restante

Les tests HTTP contrôlés exécutent le véritable SDK avec des réponses synthétiques, sans clé payante ni serveur personnel. Ils couvrent chemins, historique/options, secrets facultatifs et renouvelés, isolation utilisateur/profil, découverte/saisie manuelle, motifs de fin, erreurs sûres, absence de retries, flux malformés/tronqués, annulation, échéances totales, libération anticipée, expiration des capacités et rejet des pièces jointes.

Le test optionnel utilise un serveur explicitement configuré. Après restauration et compilation Release habituelles :

```sh
MARMOTTE_RUN_MODEL_TESTS=1 \
MARMOTTE_MODEL_ENDPOINT=http://127.0.0.1:1234/v1 \
MARMOTTE_MODEL_ID=lfm3-1.7b \
dotnet test --project tests/Marmotte.ChatUi.OpenAI.Tests --configuration Release --no-build --no-restore --filter-class '*LiveServerTests'
```

Pour un serveur authentifié, fournir `MARMOTTE_MODEL_KEY` dans l’environnement secret du processus, pas dans un fichier versionné ni une commande contenant la vraie clé. Le test n’affiche ni réponses ni clés. La CI courante l’ignore. Ce test d’intégration exige la découverte ; les tests contrôlés couvrent séparément son absence.

| Serveur/modèle | Résultat consigné, 2026-11-26 |
| --- | --- |
| LM Studio, version installée `0.4.28+2` ; `lfm3-1.7b` ; boucle locale hôte `/v1` ; sans authentification | Découverte, texte non progressif, streaming texte et annulation cliente contrôlés par le test SDK optionnel. Température 0 et `max_completion_tokens` acceptés. |
| LM Studio avec authentification activée | Non testé sur le serveur réel ; seulement tests contrôlés Bearer/refus d’authentification. |
| Capacité image/vision | Unknown ; aucune image envoyée ni prise en charge de vision revendiquée. Préparation/transport restent pour M9. |
| Unsloth Desktop | Différé à la demande de l’utilisateur ; aucune compatibilité d’exécution revendiquée. |

Le modèle à raisonnement épuisait la sonde initiale de 32 tokens avant toute réponse visible ; son plafond vaut désormais 256. Le scénario réel d’annulation autorise 1024 tokens afin que le raisonnement ne consomme pas tout le budget de réponse. Ces limites n’établissent pas une fenêtre de contexte vérifiée. La version installée est lue dans le bundle applicatif ; aucune empreinte du build serveur ou des poids du modèle n’a été capturée. Le réseau hôte/conteneurs est décrit dans [exploitation](operations.fr.md) ; le test modèle tourne sur l’hôte, pas dans un conteneur. La tâche M6 6.8 reste ouverte pour la matrice réelle restante.
