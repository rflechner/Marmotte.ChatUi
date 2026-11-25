# Exploitation locale

[English](operations.md) | Français

## Périmètre actuel

M1 fournit un hôte web local et un AppHost Aspire contenant uniquement la ressource `web`. Aucune base de données, identité, connexion aux modèles, migration, gestion des fichiers ou configuration de santé/télémétrie n’est encore implémentée. ServiceDefaults est une assembly vide réservée à M3. Aucun déploiement de production n’est configuré.

## Démarrer et arrêter

Exécuter d’abord les commandes de restauration et de compilation Release du [guide de développement](development.fr.md). Pour démarrer directement l’hôte web :

```sh
dotnet run --project src/Marmotte.ChatUi.Web --configuration Release --no-build
```

Le profil de développement utilise [http://localhost:5180](http://localhost:5180). Il affiche une page de présentation du socle et la mascotte existante. Ctrl+C arrête l’hôte. Aucune donnée applicative n’est à conserver à ce stade.

Pour démarrer le même hôte web via Aspire :

```sh
dotnet dev-certs https --check
dotnet run --project src/Marmotte.ChatUi.AppHost --configuration Release --no-build
```

Aspire nécessite un certificat HTTPS de développement valide. S’il manque ou n’est pas approuvé, suivre les [instructions des certificats de développement .NET](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs) pour votre plateforme. Le profil HTTPS de l’AppHost utilise le port `17291` pour le tableau de bord, `21291` pour OTLP et `22291` pour le service de ressources. Utiliser le lien authentifié du tableau de bord affiché dans le terminal ; ne pas partager son jeton de connexion. Le point d’accès web reste sur le port `5180`. Ces ports sont des valeurs de développement propres au dépôt, pas des ports supposés de serveurs de modèles.

Ctrl+C dans le terminal AppHost arrête l’orchestration et le processus web enfant. Ne pas lancer simultanément l’hôte web direct et l’AppHost sur le même port web. Docker n’est pas nécessaire avant l’ajout de ressources conteneurisées dans M3.

## Configuration et secrets de développement

L’hôte web lit ses paramètres ordinaires dans `appsettings.json`, les surcharges d’environnement ASP.NET Core standard et la configuration en ligne de commande. `Logging:LogLevel` contrôle les diagnostics et `AllowedHosts` restreint les en-têtes d’hôte acceptés aux noms locaux. Les profils de lancement définissent `Development` et les adresses locales. Les variables d’environnement utilisent `__` à la place de `:` pour les clés imbriquées, par exemple `Logging__LogLevel__Default`.

Les deux hôtes ont des valeurs `UserSecretsId` stables dans leurs projets ; aucune initialisation n’est nécessaire. ASP.NET Core/Aspire chargent leurs magasins respectifs en développement. Un exemple sans valeur sensible ni usage applicatif illustre la syntaxe :

```sh
dotnet user-secrets set "Development:ExampleSecret" "local-example-only" --project src/Marmotte.ChatUi.Web
dotnet user-secrets remove "Development:ExampleSecret" --project src/Marmotte.ChatUi.Web
```

Utiliser le chemin du projet AppHost pour les futurs secrets d’orchestration. Les deux magasins sont séparés ; un secret d’AppHost n’est pas automatiquement transmis à Web. Aucun secret ni clé d’API n’est nécessaire dans M1, et `Development:ExampleSecret` n’est pas un paramètre applicatif.

Les secrets utilisateur constituent un stockage local de développement, pas un stockage de production chiffré. Ne pas mettre d’identifiants dans `appsettings.json`, les profils de lancement, les fichiers versionnés, les journaux ou les valeurs d’exemple. Les futurs identifiants OIDC et ceux des serveurs de modèles sont distincts et seront documentés avec leurs adaptateurs.

## Diagnostic

- SDK introuvable : installer le SDK `10.0.401` ou un correctif `10.0.4xx` ultérieur ; vérifier `dotnet --info` depuis la racine du dépôt.
- Échec de restauration : vérifier l’accès réseau à NuGet et le SDK sélectionné. Ne pas utiliser de flux personnels ni désactiver l’audit des dépendances pour masquer les erreurs. Les changements volontaires de dépendances nécessitent une mise à jour revue des fichiers de verrouillage.
- Port occupé : arrêter le processus précédent ou ajuster ensemble les adresses de développement dans les fichiers `Properties/launchSettings.json` concernés.
- Erreur de certificat Aspire : vérifier le certificat et la configuration de confiance de la plateforme ; l’hôte HTTP autonome reste disponible pour le développement M1.
- Les tests signalent une incompatibilité VSTest/MTP : exécuter depuis le dépôt pour trouver `global.json` et utiliser `dotnet test --solution ...` comme documenté.
- Un paramètre de modèle, base de données, identité ou migration paraît sans effet : ces fonctions ne sont pas implémentées dans M1. Suivre la [roadmap](../roadmap.fr.md) plutôt que d’ajouter des clés de configuration spéculatives.
