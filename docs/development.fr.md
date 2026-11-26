# Développement

[English](development.md) | Français

## Prérequis et versions

La solution utilise .NET 10 LTS, C# 14 et `net10.0` dans toute la solution. [global.json](../global.json) sélectionne le SDK `10.0.401`, autorise les correctifs de la bande `10.0.4xx` et exclut les préversions. Installer uniquement .NET 11 ne satisfait pas cette sélection. Consulter la [politique de support .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

| Dépendance | Version retenue | Rôle |
| --- | --- | --- |
| Aspire.AppHost.Sdk / Aspire.Hosting.AppHost | 13.6.5 | Orchestration de l’infrastructure locale |
| Aspire.Hosting.PostgreSQL / Aspire.Hosting.Testing | 13.6.5 | Ressources PostgreSQL et tests d’intégration du véritable AppHost |
| Aspire.Hosting.Keycloak | 13.6.5-preview.1.26559.7 | Intégration d’hébergement officielle en préversion correspondant à Aspire 13.6.5 |
| Microsoft.Extensions.ServiceDiscovery | 10.11.1 | Résolution des services HTTP de l’hôte |
| Exporter/hosting OpenTelemetry et instrumentation ASP.NET Core/HTTP/runtime | 1.21.0 | Métriques et traces optionnelles |
| Microsoft.AspNetCore.Components.Web | 10.0.12 | Références de la Razor Class Library compatibles avec le navigateur |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | Tests de l’hôte web en mémoire |
| xunit.v3 | 4.0.3 | Tests avec Microsoft.Testing.Platform |

Les versions directes des packages figurent dans [Directory.Packages.props](../Directory.Packages.props). La version du SDK Aspire figure dans `global.json` ; mettre à jour le SDK et le package d’hébergement ensemble. Les versions transitives et empreintes sont enregistrées dans les fichiers de verrouillage versionnés. [NuGet.Config](../NuGet.Config) sélectionne uniquement nuget.org, pour éviter une dépendance accidentelle aux flux privés d’un développeur.

L’AppHost utilise le tableau de bord et l’orchestrateur distribués par le SDK via NuGet : la compilation ne nécessite ni CLI Aspire installée séparément, ni workload. `AspireUseCliBundle=false` est volontaire ; seul le diagnostic `ASPIRE010` est supprimé pour ce choix. Les fonctions propres à la CLI sont hors M1. Aspire sélectionne ses outils natifs selon l’hôte de compilation ; AppHost et Infrastructure.Tests utilisent donc `packages.<RID>.lock.json`. Des fichiers existent pour `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-x64`, `osx-arm64`, `win-x64` et `win-arm64`. Leur restauration ne constitue pas une validation d’exécution sur chaque plateforme.

## Vérifications reproductibles

Depuis la racine du dépôt :

```sh
dotnet restore Marmotte.ChatUi.slnx --locked-mode
dotnet build Marmotte.ChatUi.slnx --configuration Release --no-restore
dotnet format Marmotte.ChatUi.slnx --verify-no-changes --no-restore
dotnet test --solution Marmotte.ChatUi.slnx --configuration Release --no-build --no-restore
```

L’option `--solution` est nécessaire avec le mode Microsoft.Testing.Platform retenu. Consulter [l’intégration MTP de xUnit](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform). Docker, PostgreSQL, Keycloak, un serveur de modèles personnel et des identifiants payants ne sont pas nécessaires à ces vérifications. La première restauration nécessite un accès réseau à NuGet.

La [CI](../.github/workflows/ci.yml) exécute les mêmes commandes sur Ubuntu et possède un job d’infrastructure Docker distinct. Configurer le workflow ne signifie pas qu’il a déjà été exécuté sur GitHub Actions.

Les tests d’architecture et d’hébergement contrôlent les références autorisées entre projets, l’indépendance du cœur envers les packages externes, l’isolation des dépendances des composants, le prérendu de l’accueil et ses marqueurs Interactive Server, les ressources statiques et les réponses HTTP 404. Les tests de l’hôte utilisent un fournisseur de protection des données éphémère et ne nécessitent pas de secrets de développement. M3 contrôle également la santé/disponibilité/vivacité l’absence des endpoints de santé en Production et l’absence d’un fournisseur de journaux OpenTelemetry lorsque OTLP est activé. Les interactions navigateur, les repositories métier, la connexion OIDC, la compatibilité des modèles et la consommation NuGet externe relèvent des milestones suivants.

M2 ajoute `Marmotte.ChatUi.Core.Tests` : instantanés métier immuables, restauration d’historique, cycle de vie/réponses partielles, doublons, concurrence optimiste, isolation utilisateur, pagination bornée, validation des paramètres et annulation. Ses repositories de test ne nécessitent aucune infrastructure. Consulter la [référence du cœur](core.fr.md) pour les garanties des adaptateurs et les limites.

## Vérifications de l’infrastructure Docker

Avec Docker démarré, après restauration/compilation :

```sh
MARMOTTE_RUN_INFRASTRUCTURE_TESTS=1 dotnet test --project tests/Marmotte.ChatUi.Infrastructure.Tests --configuration Release --no-build --no-restore
```

Cette commande pour shell POSIX active le test de conteneurs ignoré par défaut ; dans PowerShell, définir `$env:MARMOTTE_RUN_INFRASTRUCTURE_TESTS = "1"` avant la même commande `dotnet test`. Le véritable AppHost s’exécute avec son tableau de bord, de nouveaux secrets/ports aléatoires et un volume unique supprimé ensuite. Trois démarrages vérifient l’activation initiale, l’idempotence, la réparation après suppression de l’extension, la conservation de données SQL et d’un utilisateur Keycloak, la disponibilité, la découverte OIDC visible au navigateur et les restrictions du client importé. Le délai maximal est de cinq minutes ; aucun serveur personnel ni clé payante n’est nécessaire. Le test référence Npgsql transitivement via Aspire uniquement pour ses sondes SQL ; aucun choix d’accès aux données n’est introduit dans le cœur applicatif ou l’adaptateur de persistance.

Les tags/empreintes des images, les secrets locaux et le cycle de vie sont documentés dans [l’exploitation](operations.fr.md). Les tests n’utilisent ni ne suppriment le volume normal de développement. Un arrêt forcé du processus peut empêcher le nettoyage ; consulter le diagnostic avant de supprimer un volume de test résiduel. La configuration CI ne constitue pas une preuve d’exécution réussie sur GitHub Actions.

## Mettre à jour les dépendances

Modifier délibérément les versions centrales, lancer `dotnet restore Marmotte.ChatUi.slnx` pour actualiser les fichiers de verrouillage, examiner les changements de dépendances, puis relancer les vérifications verrouillées. Pour une mise à jour Aspire, régénérer tous les fichiers d’AppHost et Infrastructure.Tests avec chaque RID indiqué ci-dessus ; par exemple :

```sh
dotnet restore tests/Marmotte.ChatUi.Infrastructure.Tests/Marmotte.ChatUi.Infrastructure.Tests.csproj -p:NETCoreSdkRuntimeIdentifier=linux-x64
dotnet restore Marmotte.ChatUi.slnx --locked-mode
```

La première commande résout les packages pour cet hôte de compilation ; elle ne réalise ni compilation croisée ni validation de l’exécution sur cet hôte. La seconde restaure les ressources de votre propre hôte avant compilation. Ne pas résoudre un échec de restauration verrouillée en désactivant silencieusement ce mode dans la CI.

## Conventions C# et Razor

- Suivre [AGENTS.md](../AGENTS.fr.md) et les frontières de dépendance d’[ARCHITECTURE.md](../ARCHITECTURE.fr.md).
- `.editorconfig` définit UTF-8, LF, les retours à la ligne finaux, une indentation de quatre espaces en C#/Razor et deux espaces pour les projets et la configuration. Utiliser des espaces de noms C# limités au fichier, une accessibilité explicite, des accolades et un espacement lisible. L’indentation Razor est aussi revue manuellement ; `dotnet format` ne valide pas toutes les règles Razor ou CSS.
- Les références nullables, les analyseurs recommandés .NET 10, les règles de style et les avertissements traités comme erreurs s’appliquent à toute la solution. Éviter les suppressions globales d’avertissements. Les noms des méthodes de test peuvent utiliser des underscores.
- Garder les composants centrés sur la présentation. Exclure de la RCL les appels aux fournisseurs, la persistance, les secrets et toute dépendance implicite à un circuit serveur. L’hôte choisit le mode de rendu.
- Ajouter des tests comportementaux ciblés avec chaque cas d’usage. Les adaptateurs de test restent dans le projet de tests ; les assemblies d’infrastructure vides restent réservées à leurs milestones.
- Toute modification Markdown inclut les versions anglaise et française correspondantes et des liens locaux dans la même langue.

Consulter [l’exploitation](operations.fr.md) pour le démarrage local et les secrets, et [l’intégration](integration.fr.md) pour le contrat de rendu.
