# Marmotte.ChatUi

[English](README.md) | Français

![Marmotte.ChatUi : la mascotte turquoise à écharpe rouge derrière une enseigne en bois](docs/images/marmotte-chatui-banner.png)

Une application de chat en .NET et Blazor, et des composants réutilisables pour intégrer conversations et widgets de chat dans d’autres applications.

## État du projet

> **Preuve de concept (POC) expérimentale — non prête pour la production.**
> Ce projet est destiné à l’expérimentation et à l’évaluation, pas à un usage en production.

<img src="docs/images/references/experimental-proof-of-concept.png" alt="La mascotte marmotte avec une pancarte Experimental Proof of Concept" width="320" />

Le projet est en phase de cadrage. Ce dépôt contient l’architecture, les consignes de contribution et les ressources visuelles. L’application, les intégrations et le package NuGet ne sont pas encore implémentés ou publiés.

## Direction du produit

- Composants Blazor destinés à être distribués en NuGet.
- Chat et paramètres de connexion aux API compatibles OpenAI, dont LM Studio et Unsloth Desktop.
- SDK .NET OpenAI envisagé dans un adaptateur indépendant.
- Historique et préférences utilisateur dans PostgreSQL, avec pgvector disponible.
- Application complète avec authentification OIDC/Keycloak et orchestration .NET Aspire.
- Design system personnalisable, modes clair et sombre, uploads et glisser-déposer d’images.
- Préparation de PDF pour les modèles de vision prévue dans une étape ultérieure.

## Documentation

- [Architecture et périmètre](ARCHITECTURE.fr.md)
- [Consignes de contribution](AGENTS.fr.md)
- [Mascotte et provenance des images](docs/images/README.fr.md)

Les instructions de lancement seront ajoutées avec une application exécutable.
