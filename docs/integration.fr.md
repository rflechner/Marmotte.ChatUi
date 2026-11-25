# Intégration des composants

[English](integration.md) | Français

## Ce qui existe dans M1

`Marmotte.ChatUi.Components` est une Razor Class Library `net10.0` référençant `Marmotte.ChatUi.Contracts` et `Microsoft.AspNetCore.Components.Web`. Elle ne fournit pas encore de composants publics de chat ni de contrats de services. Son graphe de packages est contrôlé pour rester indépendant des adaptateurs PostgreSQL, Keycloak, Aspire et OpenAI. Le projet web référence déjà la RCL, mais affiche actuellement sa propre page de présentation du socle.

Il n’existe ni package NuGet publié ni commande d’installation prise en charge. Tous les projets sont actuellement non empaquetables ; l’identité du package, ses métadonnées, la distribution de Contracts et les tests de consommation externe relèvent de M10. M7 ajoutera les vrais exemples de composants, paramètres, callbacks, templates, états, consignes d’accessibilité et API de personnalisation des thèmes.

## Contrat de rendu

L’application complète utilise Interactive Server avec prérendu. Elle enregistre `AddInteractiveServerComponents`, configure `AddInteractiveServerRenderMode` et définit `InteractiveServer` sur Routes et HeadOutlet. Le premier test de l’hôte vérifie le contenu prérendu et les marqueurs d’interactivité serveur ; il ne simule pas de circuit navigateur.

Les composants réutilisables doivent hériter du mode de leur hôte, sans `@rendermode` imposé, service injecté propre au serveur ou accès implicite à `HttpContext`, aux secrets et à la configuration globale de l’application. Consulter les [modes de rendu Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0).

| Mode | Contrat et périmètre de validation |
| --- | --- |
| SSR statique / prérendu | L’affichage doit fonctionner sans risque avant l’interactivité ; l’envoi et les autres callbacks nécessitent un hôte interactif. |
| Interactive Server | Première cible d’intégration interactive, configurée dans l’hôte web M1. Les vrais composants de chat seront testés dans M7. |
| Interactive WebAssembly / Auto | Préserver la compatibilité dans la conception de la bibliothèque. Aucun hôte client ni test de consommation n’existe encore ; la prise en charge n’est pas vérifiée. Les futurs services devront être fournis par l’hôte consommateur. |

Le CSS de Web constitue seulement une présentation minimale de l’hôte avec apparence claire/sombre selon le système. Il ne s’agit ni de l’API publique de tokens ni du sélecteur complet de thème prévu pour M7. La mascotte appartient à l’identité de l’hôte ; les consommateurs des futurs widgets doivent pouvoir la remplacer ou l’omettre.

Consulter [l’architecture](../ARCHITECTURE.fr.md) pour le sens des dépendances et [le développement](development.fr.md) pour les contrôles protégeant ces frontières.
