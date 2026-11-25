# Marmotte.ChatUi

[English](README.md) | Français

![Marmotte.ChatUi : la mascotte turquoise à écharpe rouge derrière une enseigne en bois](docs/images/marmotte-chatui-banner.png)

Une application de chat en .NET et Blazor, et des composants réutilisables pour intégrer conversations et widgets de chat dans d’autres applications.

## État du projet

> **Preuve de concept (POC) expérimentale — non prête pour la production.**
> Ce projet est destiné à l’expérimentation et à l’évaluation, pas à un usage en production.

<img src="docs/images/references/experimental-proof-of-concept.png" alt="La mascotte marmotte avec une pancarte Experimental Proof of Concept" width="320" />

Le milestone 1 est implémenté : une solution .NET 10 avec neuf projets sources, un hôte Blazor minimal en interactivité serveur, un AppHost Aspire qui démarre l’hôte web, des tests d’architecture et d’hébergement, et une configuration CI. Le domaine, les contrats applicatifs, les adaptateurs, les composants de chat réutilisables et le comportement de ServiceDefaults restent pour les milestones suivants. Aucun package NuGet n’est publié.

## Direction du produit

- Composants Blazor destinés à être distribués en NuGet.
- Chat et paramètres de connexion aux API compatibles OpenAI, dont LM Studio et Unsloth Desktop.
- SDK .NET OpenAI envisagé dans un adaptateur indépendant.
- Historique et préférences utilisateur dans PostgreSQL, avec pgvector disponible.
- Application complète avec authentification OIDC/Keycloak et orchestration .NET Aspire.
- Design system personnalisable, modes clair et sombre, uploads et glisser-déposer d’images.
- Préparation de PDF pour les modèles de vision prévue dans une étape ultérieure.

## Documentation

- [Roadmap et tâches par milestone](roadmap.fr.md)
- [Architecture et périmètre](ARCHITECTURE.fr.md)
- [Consignes de contribution](AGENTS.fr.md)
- [Mascotte et provenance des images](docs/images/README.fr.md)

## Compiler et démarrer

Installer le SDK .NET sélectionné par [global.json](global.json) : `10.0.401` ou un correctif ultérieur de la même bande `10.0.4xx`. La première restauration nécessite un accès à NuGet. Docker, un serveur de modèles et des identifiants ne sont pas nécessaires pour M1.

Depuis la racine du dépôt :

```sh
dotnet restore Marmotte.ChatUi.slnx --locked-mode
dotnet build Marmotte.ChatUi.slnx --configuration Release --no-restore
dotnet format Marmotte.ChatUi.slnx --verify-no-changes --no-restore
dotnet test --solution Marmotte.ChatUi.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Marmotte.ChatUi.Web --configuration Release --no-build
```

Ouvrir [l’hôte local](http://localhost:5180). La page affiche l’état du projet et la mascotte ; elle ne propose pas encore de chat. L’arrêter avec Ctrl+C.

Consulter le [guide de développement](docs/development.fr.md) pour les versions, conventions et tests ; [l’exploitation](docs/operations.fr.md) pour le démarrage Aspire et les secrets de développement ; et [l’intégration](docs/integration.fr.md) pour les frontières des composants et les choix de rendu.
