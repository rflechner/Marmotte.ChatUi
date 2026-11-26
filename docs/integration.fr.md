# Intégration des composants

[English](integration.md) | Français

## Interface disponible

M7 implémente une Razor Class Library `net10.0` ne référençant que `Marmotte.ChatUi.Contracts` et le package `Microsoft.AspNetCore.Components.Web`, compatible navigateur. Les composants reçoivent des instantanés, paramètres, callbacks et templates ; ils n’injectent aucun service applicatif et n’accèdent ni à la configuration, ni à l’identité, au stockage ou aux clients de modèles. L’hôte Web complet utilise le même widget dans son espace protégé, relié au [flux de chat](chat.fr.md) M8.

[L’exemple indépendant](../samples/Marmotte.ChatUi.Sample/Marmotte.ChatUi.Sample.csproj) ne référence que Components. Il substitue un service de réponse déterministe, démontre les thèmes hérités et par instance, et utilise la mascotte originale via une copie de déploiement inchangée. Il ne nécessite ni Keycloak, PostgreSQL, Aspire, SDK OpenAI, identifiants ou appel réseau à un modèle. L’état dure le temps du circuit de la page ; un rechargement le perd. Démarrer après restauration et compilation de la solution :

```sh
dotnet run --project samples/Marmotte.ChatUi.Sample --configuration Release --no-build
```

Ouvrir [le studio de composants](http://127.0.0.1:5190). Le profil de lancement sélectionne Development pour résoudre les ressources web référencées depuis le dépôt. Arrêter avec Ctrl+C. Il s’agit d’une démonstration locale, pas d’un déploiement applicatif.

Aucun package NuGet n’est publié. Les projets restent non empaquetables ; identité du package, métadonnées, empaquetage de Contracts et consommation de packages externes relèvent de M10. Utiliser pour l’instant une référence de projet.

## Ajouter la bibliothèque à un hôte

Référencer `src/Marmotte.ChatUi.Components/Marmotte.ChatUi.Components.csproj` depuis l’application et importer `Marmotte.ChatUi.Components` et `Marmotte.ChatUi.Contracts` dans `_Imports.razor`. Charger une fois cette feuille de style dans le document de l’hôte :

```html
<link rel="stylesheet" href="_content/Marmotte.ChatUi.Components/marmotte-chatui.css" />
```

Pour les Blazor Web Apps, l’exemple utilise `@Assets["_content/Marmotte.ChatUi.Components/marmotte-chatui.css"]` avec `MapStaticAssets()` pour les ressources avec empreinte. Cela suit la [convention de ressources RCL Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/class-libraries?view=aspnetcore-10.0). La bibliothèque ne nécessite aucun enregistrement de service et n’impose aucun `@rendermode`. L’hôte doit établir l’interactivité pour les événements ; l’exemple configure Interactive Server sur Routes et HeadOutlet.

Une petite intégration contrôlée par l’hôte :

```razor
<ChatWidget Title="Notes" Messages="_messages"
            @bind-Draft="_draft" @bind-Theme="_theme"
            Status="_status" OnSend="AcceptAsync" OnCancel="StopAsync">
    <Branding><span aria-hidden="true">✦</span></Branding>
    <EmptyContent><h3>Commencez par une question.</h3></EmptyContent>
</ChatWidget>
```

`_messages` est un `IReadOnlyList<MessageView>`, `_draft` un `string`, `_theme` un `ThemePreference` et `_status` un `GenerationStatus` nullable. L’exemple complet et exécutable de callbacks est dans [Home.razor.cs](../samples/Marmotte.ChatUi.Sample/Components/Pages/Home.razor.cs). Il attend l’acceptation, démarre un flux appartenant à l’hôte, remplace les instantanés à chaque mise à jour et annule/attend le flux à la destruction. Adapter ces callbacks aux cas d’usage autorisés de l’application ; la RCL n’enregistre ni n’appelle elle-même `IConversationService`. Voir les [contrats du cœur](core.fr.md).

## Composants publics

Tous les types appartiennent à `Marmotte.ChatUi.Components`. Placer les composants autonomes dans `ChatTheme` ; `ChatWidget` en fournit déjà un. Chaque instance doit avoir des identifiants uniques de messages/pièces jointes. Préserver l’ordre des messages dans la liste fournie.

| Composant | Entrées et comportement |
| --- | --- |
| `ChatTheme` | `Theme` vaut `System` par défaut ; `Class`, `Style`, `ChildContent` définissent le périmètre de thème de l’hôte. |
| `ChatWidget` | `Title`, `Messages`, `Draft`/`DraftChanged`, `Theme`/`ThemeChanged` ; `Status`, `Sending`, `Cancelling`, `Error`, `BlockedReason` ; `Attachments` ; `OnSend`, `OnCancel`, `OnRemoveAttachment`. `ShowThemeSelector` vaut true par défaut. `Class`/`Style` personnalisent la racine. |
| `MessageList` | `Messages`, nom accessible `Label`, `EmptyContent`, `MessageTemplate`, `AttachmentTemplate`. Région défilable avec focus et liste ordonnée ; aucun défilement automatique ni annonce de chaque token. |
| `ChatMessage` | `Message` requis, `ContentTemplate` et `AttachmentTemplate` optionnels. Texte brut encodé, locuteur, heure UTC, métadonnées de préparation et état des réponses partielles. |
| `MessageComposer` | `Draft`/`DraftChanged`, `OnSend`, `Busy`, `BlockedReason`. Texte multiligne avec envoi explicite, garde locale contre les doublons et retour d’erreur sûr. |
| `GenerationIndicator` | `Status`, `Sending`, `Cancelling`, `Error`. Région dynamique polie et atomique, plus alerte pour les erreurs sûres. |
| `AttachmentPreview` | `Attachment` requis, `PreviewTemplate`, `OnRemove`, `Disabled`. Métadonnées et annonces polies de préparation ; ni URL, récupération, upload ou support de modèle supposé. Omettre la suppression dans l’historique. |

`ChatWidget` propose `Branding`, `EmptyContent`, `Footer`, `RenderFragment<MessageView> MessageTemplate` et `RenderFragment<AttachmentView> AttachmentTemplate`. Les templates de message remplacent uniquement le contenu, préservant locuteur et état. Le widget n’impose ni mascotte ni marque par défaut ; Web et l’exemple fournissent eux-mêmes la mascotte préservée. Templates, `Class` et `Style` sont des entrées de confiance de l’hôte. Celui-ci doit assainir tout rendu HTML/Markdown personnalisé et autoriser la récupération des pièces jointes ; il gère aussi les URL d’objets et leur nettoyage. Le rendu par défaut n’interprète jamais le texte du modèle ou les noms de fichiers comme HTML, Markdown, CSS ou liens.

`BlockedReason` explique facultativement une configuration ou sélection hôte manquante et bloque l’envoi, sans annoncer un envoi en cours.

## Contrat des callbacks et des états

- Lier `Draft` et `DraftChanged`. `OnSend(string)` reçoit le brouillon exact non vide, retours à la ligne compris. Il retourne une `Task` lorsque l’acceptation réussit, **pas lorsque toute la réponse est terminée**. Seule une acceptation réussie efface le brouillon. Les exceptions le préservent ; `ChatOperationException` expose son message de catégorie sûr, l’annulation a un retour dédié et les autres erreurs affichent un message générique sans journaliser leur contenu. Le composant attend les callbacks et bloque les doublons pendant leur attente.
- L’hôte attribue les identifiants de soumission, gère autorisation et idempotence, définit `Sending` pendant l’acceptation et fournit le dernier `Status`. `Pending`/`Streaming`, `Sending` et `Cancelling` bloquent les nouvelles soumissions. Les callbacks gèrent délais et durées de vie des `CancellationTokenSource` ; les composants ne démarrent ni ne détruisent les opérations applicatives.
- `OnCancel()` est attendu une seule fois à la fois et affiche « Stopping response… ». L’hôte doit réellement annuler le travail et publier `Cancelled` après acquittement. Un échec expose une erreur sûre sans déclarer de succès. Le texte partiel reste dans les instantanés. Arrêter uniquement le flux ne constitue pas une annulation durable ; utiliser l’opération applicative appropriée dans un hôte intégré.
- `ThemeChanged(ThemePreference)` demande un choix ; l’hôte actualise `Theme` et décide de le persister avec les préférences. Aucun stockage local ou état global du navigateur n’est modifié. `OnRemoveAttachment(Guid)` demande la suppression des métadonnées en attente. L’hôte remplace ensuite `Attachments`.
- Les pièces jointes en attente bloquent toujours cet éditeur **textuel uniquement**, même à l’état `Ready`. Cet état signifie que la préparation est terminée, pas que le fournisseur les accepte ou les a reçues. La suppression est disponible avant envoi ; les véritables uploads/vision arrivent en M9.
- Après succès ou échec d’envoi, le focus revient à l’éditeur. Entrée insère un retour à la ligne ; Tab atteint Send message, puis Entrée/Espace active le bouton. Aucun gestionnaire clavier global, focus initial forcé ou défilement automatique pendant le streaming.

Les actions sont désactivées pendant SSR statique/prérendu. Les callbacks absents désactivent aussi les contrôles correspondants. Les libellés par défaut sont anglais ; leur localisation arbitraire et le rendu Markdown ne sont pas implémentés. Les templates peuvent remplacer le contenu, mais une API complète de localisation reste à venir.

## Tokens sémantiques et thèmes

Tous les sélecteurs sont limités à `.mcui-theme` ; la feuille ne cible ni `body`, `html` ou les contrôles non concernés de l’hôte. `Light` et `Dark` sélectionnent une palette explicite ; `System` réagit à `prefers-color-scheme`, y compris aux changements du système, sans JavaScript. Les valeurs privées `--_mcui-*` se réinitialisent à chaque frontière de thème. Les propriétés publiques `--mcui-*` ne sont **pas** réinitialisées : les surcharges héritent de l’application ou d’un conteneur et survivent aux changements de thème. Ne pas dépendre des tokens privés.

| Tokens publics (chaque nom est préfixé par `--mcui-`) | Valeurs par défaut / rôle |
| --- | --- |
| `surface`, `surface-muted`, `surface-user` | Surfaces principale, assistant et utilisateur selon le thème |
| `text`, `text-muted` | Textes principal et secondaire |
| `accent`, `accent-hover`, `on-accent` | Fond d’action, survol et texte contrasté |
| `border`, `focus`, `error`, `success` | Limites de contrôles et états sémantiques |
| `space-1` … `space-6` | `0.25rem`, `0.5rem`, `0.75rem`, `1rem`, `1.5rem`, `2rem` |
| `font-family`, `font-size`, `font-small`, `font-heading` | `system-ui, sans-serif`, `1rem`, `0.875rem`, `1.25rem` |
| `font-weight-strong`, `line-height` | `650`, `1.6` |
| `radius-sm`, `radius-md`, `radius-lg`, `shadow` | `0.5rem`, `0.875rem`, `1.25rem`, ombre selon le thème |
| `focus-width`, `focus-offset`, `control-height`, `disabled-opacity` | `3px`, `3px`, `2.75rem`, `0.6` |
| `transcript-min-height`, `transcript-max-height` | `18rem`, `32rem` ; le défilement reste dans la conversation |

Les surcharges applicatives et par instance peuvent coexister :

```css
/* Feuille de l’hôte ; héritée par tous les widgets sous ce conteneur. */
.my-application {
    --mcui-font-family: "Atkinson Hyperlegible", system-ui, sans-serif;
    --mcui-radius-lg: 0.75rem;
}
```

```razor
<ChatWidget Title="Palette personnalisée" Theme="ThemePreference.Light"
            Style="--mcui-accent: #6440a0; --mcui-accent-hover: #4e2d83; --mcui-on-accent: #ffffff;" />
```

Surcharger ensemble les couleurs liées et revérifier le contraste dans chaque thème. Les surcharges publiques priment intentionnellement même en mode sombre. Les valeurs de la bibliothèque fournissent focus visible, libellés et contrôles natifs, hauteur minimale de 44px, retour à la ligne, espacements sur petit écran et texte désactivé en couleurs forcées. La conversation est défilable au clavier ; les changements d’état sont séparés du contenu afin de ne pas annoncer toute la conversation à chaque token.

## Vérification et limites

Les tests bUnit couvrent encodage, contenu partiel, callbacks, doublons, annulation, conservation du brouillon, blocage des pièces jointes, libellés, templates, thèmes indépendants et actions SSR désactivées. Les tests d’architecture imposent l’indépendance de l’exemple. La suite navigateur exécute le véritable exemple Interactive Server : clavier/focus, envoi/streaming/annulation/échec, changements de thème/système, tokens hérités et par instance, formats 360px/1280px et contrôles d’accessibilité axe. Consulter [le développement](development.fr.md) pour les commandes et limites. Les contrôles automatisés ne constituent pas un audit d’usage avec lecteur d’écran.

SSR statique et Interactive Server sont vérifiés. WebAssembly et Auto gardent des dépendances compatibles navigateur, sans hôte ou validation de consommation dédiés. M8 implémente l’orchestration complète authentifiée modèle/historique ; les uploads restent pour M9 et NuGet pour M10. Aucune publication de package, mise en production ou compatibilité entre navigateurs n’est annoncée.
