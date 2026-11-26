# Intégration des composants

[English](integration.md) | Français

## Ce qui existe dans M1–M6

`Marmotte.ChatUi.Components` est une Razor Class Library `net10.0` référençant `Marmotte.ChatUi.Contracts` et `Microsoft.AspNetCore.Components.Web`. Elle ne fournit pas encore de composants publics de chat. Contracts fournit désormais `IConversationService`, `IConnectionProfileService`, `IUserPreferencesService`, les DTO, les mises à jour de génération et les erreurs sûres ; consulter la [référence des API du cœur](core.fr.md). Les consommateurs fournissent ces services sans importer Application ni les adaptateurs serveur. Son graphe de packages est contrôlé pour rester indépendant des adaptateurs PostgreSQL, Keycloak, Aspire et OpenAI. Le projet web référence déjà la RCL, et affiche ses propres pages d’accueil et d’espace protégé. M3 ajoute l’infrastructure et ServiceDefaults, et M4 l’identité OIDC uniquement à l’hôte de l’application complète ; les consommateurs de composants n’acquièrent aucune nouvelle dépendance serveur. M5 compose les services PostgreSQL dans l’hôte complet lorsqu’il est configuré ; voir [persistance](persistence.fr.md).

M6 compose l’adaptateur de modèles uniquement dans l’hôte complet ; voir [intégration des modèles](models.fr.md). Les consommateurs de composants ne nécessitent toujours aucun SDK.

Il n’existe ni package NuGet publié ni commande d’installation prise en charge. Tous les projets sont actuellement non empaquetables ; l’identité du package, ses métadonnées, la distribution de Contracts et les tests de consommation externe relèvent de M10. M7 ajoutera les vrais exemples de composants, paramètres, callbacks, templates, états, consignes d’accessibilité et API de personnalisation des thèmes.

## Contrat de rendu

L’application complète utilise Interactive Server avec prérendu pour les pages authentifiées. Elle enregistre `AddInteractiveServerComponents`, configure `AddInteractiveServerRenderMode` et sélectionne `InteractiveServer` sur Routes et HeadOutlet si authentifié. Les pages publiques d’accueil/erreur utilisent le SSR statique ; l’endpoint de circuit exige une session active. Les tests de l’hôte vérifient les deux modes, l’autorisation des routes et l’expiration/révocation avec le véritable adaptateur d’utilisateur courant. Ils ne simulent ni JavaScript navigateur ni circuit WebSocket actif.

Les composants réutilisables doivent hériter du mode de leur hôte, sans `@rendermode` imposé, service injecté propre au serveur ou accès implicite à `HttpContext`, aux secrets et à la configuration globale de l’application. Consulter les [modes de rendu Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0).

| Mode | Contrat et périmètre de validation |
| --- | --- |
| SSR statique / prérendu | L’affichage doit fonctionner sans risque avant l’interactivité ; l’envoi et les autres callbacks nécessitent un hôte interactif. |
| Interactive Server | Première cible d’intégration interactive, configurée dans l’hôte web M1. Les vrais composants de chat seront testés dans M7. |
| Interactive WebAssembly / Auto | Préserver la compatibilité dans la conception de la bibliothèque. Aucun hôte client ni test de consommation n’existe encore ; la prise en charge n’est pas vérifiée. Les futurs services devront être fournis par l’hôte consommateur. |

Le CSS de Web constitue seulement une présentation minimale de l’hôte avec apparence claire/sombre selon le système. Il ne s’agit ni de l’API publique de tokens ni du sélecteur complet de thème prévu pour M7. La mascotte appartient à l’identité de l’hôte ; les consommateurs des futurs widgets doivent pouvoir la remplacer ou l’omettre.

Consulter [l’architecture](../ARCHITECTURE.fr.md) pour le sens des dépendances et [le développement](development.fr.md) pour les contrôles protégeant ces frontières.
