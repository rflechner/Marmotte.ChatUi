# Roadmap — Marmotte.ChatUi

[English](roadmap.md) | Français

> **POC expérimental — non prêt pour la production.** La livraison du POC ou d’un package NuGet ne change pas ce statut.

Cette feuille de route décline les décisions de [l’architecture](ARCHITECTURE.fr.md) en tâches réalisables, selon les [consignes de contribution](AGENTS.fr.md).

## Point de départ

Les README, documents d’architecture, consignes bilingues et ressources de la mascotte existent déjà. L’application, l’infrastructure exécutable et les packages ne sont pas encore implémentés ou publiés. Les 82 tâches ci-dessous restent donc à réaliser.

## Périmètre et suivi

- Les milestones M1 à M10 constituent la première livraison du POC : chat, paramètres, identité, persistance, composants réutilisables et images pour la vision.
- M11 est une itération ultérieure consacrée aux PDF ; il ne bloque pas la première livraison.
- Chaque tâche possède un identifiant stable au format `milestone.tâche`, identique dans les deux langues. Conserver les identifiants existants lors des mises à jour.
- Cocher une tâche uniquement lorsque son résultat est réalisé et vérifié, et que la documentation concernée est à jour dans les deux langues.
- Les dépendances indiquent les prérequis de validation d’un milestone. Les travaux indépendants peuvent avancer en parallèle une fois leurs prérequis disponibles.
- Aucune date ni estimation n’est engagée dans ce document.

## Vue d’ensemble

| Milestone | Résultat | Dépendances | Étape |
| --- | --- | --- | --- |
| [M1](#milestone-1) | Socle de la solution | Aucune | POC |
| [M2](#milestone-2) | Modèle métier et ports applicatifs | [M1](#milestone-1) | POC |
| [M3](#milestone-3) | Infrastructure Aspire, Keycloak et PostgreSQL | [M1](#milestone-1) | POC |
| [M4](#milestone-4) | Identité OIDC et isolation des utilisateurs | [M2](#milestone-2), [M3](#milestone-3) | POC |
| [M5](#milestone-5) | Persistance PostgreSQL et paramètres utilisateur | [M2](#milestone-2), [M3](#milestone-3), [M4](#milestone-4) | POC |
| [M6](#milestone-6) | Adaptateur de modèles compatibles OpenAI | [M2](#milestone-2), [M5](#milestone-5) | POC |
| [M7](#milestone-7) | UI réutilisable et design system | [M1](#milestone-1), [M2](#milestone-2) | POC |
| [M8](#milestone-8) | POC complet de chat textuel | [M4](#milestone-4), [M5](#milestone-5), [M6](#milestone-6), [M7](#milestone-7) | POC |
| [M9](#milestone-9) | Import de pièces jointes et vision sur images | [M8](#milestone-8) | POC |
| [M10](#milestone-10) | Livraison NuGet et validation du POC | [M9](#milestone-9) | POC |
| [M11](#milestone-11) | Préparation des PDF pour la vision — itération suivante | [M10](#milestone-10) | Après le premier POC |

<a id="milestone-1"></a>

## Milestone 1 — Socle de la solution

**Objectif:** Créer une solution .NET compilable avec des frontières d’assemblies explicites et un environnement de développement reproductible.

**Dépendances:** Aucune.

- [ ] **1.1** — Choisir des versions compatibles de .NET, Aspire et des dépendances ; documenter les prérequis et fixer les versions du SDK et des packages.
- [ ] **1.2** — Confirmer le mode de rendu Blazor de l’application hôte, à partir de la proposition d’interactivité serveur, et définir les modes pris en charge par les composants réutilisables.
- [ ] **1.3** — Créer Domain, Contracts, Application, Components, Infrastructure.OpenAI, Persistence.PostgreSql, Web, AppHost et ServiceDefaults avec le sens de dépendance prévu.
- [ ] **1.4** — Configurer la nullabilité, le formatage, les analyseurs et la gestion des dépendances ; documenter les conventions C# et Razor.
- [ ] **1.5** — Créer les projets de tests et un workflow CI pour la restauration, la compilation et les vérifications pertinentes, sans serveur de modèles personnel ni identifiant payant.
- [ ] **1.6** — Documenter en anglais et en français les commandes de compilation et de test vérifiées, ainsi que la configuration des secrets de développement.

**Critère de validation:** Un clone neuf compile et passe les premières vérifications ; les références entre projets respectent l’architecture et la bibliothèque de composants ne dépend pas des adaptateurs serveur.

<a id="milestone-2"></a>

## Milestone 2 — Modèle métier et ports applicatifs

**Objectif:** Définir les comportements métier et les contrats avant de les relier à l’UI ou à l’infrastructure.

**Dépendances:** [M1](#milestone-1).

- [ ] **2.1** — Préciser le vocabulaire et les limites d’agrégats de Conversation, Message, Attachment, ConnectionProfile et UserPreferences.
- [ ] **2.2** — Définir les identifiants, la propriété, l’ordre des messages, les horodatages et les invariants nécessaires pour créer et reprendre une conversation.
- [ ] **2.3** — Spécifier les états de génération des messages : réception progressive, fin, annulation et échec, avec les règles de gestion des réponses partielles et des envois en double.
- [ ] **2.4** — Définir des ports ciblés pour les repositories de conversations, les préférences, les profils de connexion, l’identité courante, les capacités des modèles et la génération ; exclure les types de fournisseurs de leurs signatures.
- [ ] **2.5** — Implémenter les premiers cas d’usage de création, liste, chargement et suppression des conversations, d’enregistrement des messages et de gestion des préférences et profils, avec des frontières d’autorisation explicites.
- [ ] **2.6** — Définir les contrats de présentation exposés à la bibliothèque de composants, notamment les mises à jour progressives, erreurs, annulations et métadonnées des pièces jointes.
- [ ] **2.7** — Tester les invariants métier, les contrôles de propriété, la pagination et le cycle de vie des messages à travers les contrats applicatifs.

**Critère de validation:** Les cas d’usage et invariants du cœur sont vérifiables avec des adaptateurs de test, sans Blazor, PostgreSQL, Keycloak ni SDK OpenAI.

<a id="milestone-3"></a>

## Milestone 3 — Infrastructure Aspire, Keycloak et PostgreSQL

**Objectif:** Démarrer l’infrastructure locale de manière reproductible, avec une base disposant de pgvector.

**Dépendances:** [M1](#milestone-1).

- [ ] **3.1** — Déclarer les ressources de l’application web, PostgreSQL et Keycloak dans AppHost, avec leurs dépendances et contrôles de disponibilité.
- [ ] **3.2** — Choisir et fixer une image PostgreSQL incluant pgvector ; configurer les volumes persistants et des bases distinctes pour l’application et Keycloak.
- [ ] **3.3** — Prévoir un processus idempotent d’activation de l’extension vector et le vérifier sur une base vide et un volume existant.
- [ ] **3.4** — Configurer l’initialisation reproductible du realm et du client Keycloak, la persistance des identités et des adresses d’autorité et de redirection accessibles au navigateur.
- [ ] **3.5** — Injecter les informations de connexion et secrets dans les services hôtes concernés ; garder la configuration des ressources hors du domaine et du package de composants.
- [ ] **3.6** — Configurer ServiceDefaults, les contrôles de santé et les diagnostics utiles sans journaliser les conversations, le contenu des fichiers ni les identifiants d’accès.
- [ ] **3.7** — Documenter et vérifier le démarrage, l’arrêt, le redémarrage et le diagnostic locaux, notamment l’accès aux serveurs de modèles locaux depuis l’hôte ou les conteneurs.

**Critère de validation:** Aspire démarre les ressources nécessaires, expose leur disponibilité, conserve les données entre redémarrages et rend l’extension vector disponible dans la base applicative.

<a id="milestone-4"></a>

## Milestone 4 — Identité OIDC et isolation des utilisateurs

**Objectif:** Authentifier les utilisateurs de l’application complète et faire respecter la propriété des données côté serveur.

**Dépendances:** [M2](#milestone-2), [M3](#milestone-3).

- [ ] **4.1** — Intégrer la connexion et la déconnexion OIDC avec Keycloak dans l’hôte web, avec gestion de l’expiration des sessions et des échecs d’authentification.
- [ ] **4.2** — Implémenter l’adaptateur d’utilisateur courant et relier l’identité applicative stable au couple émetteur/sujet OIDC plutôt qu’à l’e-mail.
- [ ] **4.3** — Protéger les routes applicatives et les points d’entrée serveur, en gardant Keycloak et les détails d’authentification hors du package d’UI réutilisable.
- [ ] **4.4** — Appliquer les contrôles de propriété dans les opérations applicatives et l’accès aux données ; ne pas utiliser une identité de propriétaire envoyée par le navigateur comme autorisation.
- [ ] **4.5** — Séparer l’authentification de l’application des identifiants d’accès aux serveurs de modèles et empêcher l’exposition des secrets des profils aux autres utilisateurs.
- [ ] **4.6** — Vérifier l’accès anonyme, les sessions expirées, la déconnexion et le refus des accès entre utilisateurs ; documenter la configuration locale de l’identité et ses limites pour la production.

**Critère de validation:** Deux utilisateurs peuvent se connecter indépendamment et les tests d’autorisation refusent l’accès aux données d’un autre utilisateur à travers les contrats applicatifs. L’application de ces contrôles aux données réellement persistées et aux pièces jointes stockées sera de nouveau vérifiée dans M5 et M9.

<a id="milestone-5"></a>

## Milestone 5 — Persistance PostgreSQL et paramètres utilisateur

**Objectif:** Persister les conversations et paramètres derrière les ports applicatifs, entièrement dans l’adaptateur de persistance dédié.

**Dépendances:** [M2](#milestone-2), [M3](#milestone-3), [M4](#milestone-4).

- [ ] **5.1** — Choisir et documenter l’approche d’accès aux données, en confinant les dépendances ORM ou SQL à Persistence.PostgreSql.
- [ ] **5.2** — Définir les mappings, clés, index et migrations des conversations, messages, profils, préférences et métadonnées de pièces jointes, sans tables d’embeddings spéculatives.
- [ ] **5.3** — Implémenter les repositories avec filtrage par utilisateur, pagination, annulation et mapping vers le domaine ou les DTO internes ; n’exposer aucun type propre à la base.
- [ ] **5.4** — Définir les transactions et la gestion de concurrence pour l’ordre des messages, les générations partielles, les envois répétés et les références de pièces jointes.
- [ ] **5.5** — Persister les profils de connexion et préférences d’UI, notamment le thème et le modèle ou profil sélectionné, avec validation des valeurs.
- [ ] **5.6** — Choisir et implémenter la protection et la récupération des identifiants d’accès aux modèles séparément des paramètres ordinaires ; exclure les secrets des données renvoyées à l’UI et des logs.
- [ ] **5.7** — Implémenter la séquence de migration au démarrage et vérifier l’initialisation neuve, les mises à niveau d’une base existante et la persistance entre redémarrages.
- [ ] **5.8** — Exécuter des tests d’intégration PostgreSQL sur les repositories, la propriété, l’ordre, la concurrence et les règles de suppression ou conservation.

**Critère de validation:** L’historique et les paramètres d’un utilisateur authentifié survivent à un redémarrage, avec migrations, pagination et isolation vérifiées sur PostgreSQL.

<a id="milestone-6"></a>

## Milestone 6 — Adaptateur de modèles compatibles OpenAI

**Objectif:** Relier les contrats internes de chat à LM Studio, Unsloth Desktop et aux serveurs compatibles configurables.

**Dépendances:** [M2](#milestone-2), [M5](#milestone-5).

- [ ] **6.1** — Évaluer le package NuGet OpenAI sur les endpoints et comportements d’authentification nécessaires ; confirmer son adoption et sa version ou documenter les incompatibilités bloquantes.
- [ ] **6.2** — Implémenter les endpoints configurables et traduire les requêtes, réponses et exceptions du SDK en contrats internes dans Infrastructure.OpenAI.
- [ ] **6.3** — Implémenter le streaming, l’annulation, les délais et les erreurs après réponse partielle ; éviter les tentatives automatiques dupliquant un message ou une génération.
- [ ] **6.4** — Implémenter le test de connexion et la découverte des modèles, avec saisie manuelle lorsque la découverte est indisponible.
- [ ] **6.5** — Valider les adresses, identifiants de modèles et options des profils ; prendre en charge les serveurs avec ou sans authentification sans imposer de clé fictive à l’utilisateur.
- [ ] **6.6** — Représenter les capacités vérifiées des serveurs et modèles, notamment la vision, sans supposer que tout endpoint compatible OpenAI prend en charge toutes les API.
- [ ] **6.7** — Associer correctement les clients et identifiants d’accès à leur utilisateur ou profil et documenter les endpoints selon l’exécution sur l’hôte ou dans un conteneur.
- [ ] **6.8** — Ajouter des tests de contrat avec des réponses contrôlées, puis consigner les vérifications réelles du texte, du streaming, de l’authentification, de la découverte et de la vision sur LM Studio et Unsloth Desktop.

**Critère de validation:** L’adaptateur échange des messages avec les deux serveurs cibles à partir de profils configurables, tandis que le cœur et les composants restent indépendants du SDK.

<a id="milestone-7"></a>

## Milestone 7 — UI réutilisable et design system

**Objectif:** Construire l’interface de chat Blazor réutilisable et son API de thème indépendamment de l’application complète.

**Dépendances:** [M1](#milestone-1), [M2](#milestone-2).

- [ ] **7.1** — Définir les tokens CSS sémantiques des surfaces, textes, accents, bordures, focus, états, espacements, typographie, rayons et ombres.
- [ ] **7.2** — Implémenter les thèmes clair, sombre et système, avec choix explicite de l’utilisateur et substitutions des tokens à l’échelle de l’application ou d’un widget.
- [ ] **7.3** — Construire les composants réutilisables de liste de messages, message, saisie, état de génération et aperçu de pièces jointes à partir des contrats de présentation.
- [ ] **7.4** — Composer un widget de chat intégrable avec paramètres, callbacks et templates documentés ; éviter les dépendances à une configuration globale ou à un circuit serveur.
- [ ] **7.5** — Gérer les états vide, envoi, réception progressive, annulation et erreur, et afficher de manière sûre le contenu fourni par le modèle.
- [ ] **7.6** — Intégrer la mascotte existante à l’identité visuelle de l’hôte tout en permettant aux consommateurs de remplacer ou d’omettre la marque.
- [ ] **7.7** — Vérifier l’adaptation aux tailles d’écran, la navigation clavier, le focus, les libellés, les contrastes et les annonces accessibles des états dans chaque thème.
- [ ] **7.8** — Ajouter un exemple minimal et documenter dans les deux langues l’intégration, les API publiques, les assets et la personnalisation des tokens.

**Critère de validation:** Une application d’exemple affiche les composants de chat avec des services et tokens de thème substitués, sans Keycloak, PostgreSQL, Aspire ni SDK OpenAI.

<a id="milestone-8"></a>

## Milestone 8 — POC complet de chat textuel

**Objectif:** Assembler l’identité, la persistance, les connexions aux modèles et les composants réutilisables dans une application de chat fonctionnelle.

**Dépendances:** [M4](#milestone-4), [M5](#milestone-5), [M6](#milestone-6), [M7](#milestone-7).

- [ ] **8.1** — Assembler les services et adaptateurs dans l’hôte web et relier les composants réutilisables aux cas d’usage.
- [ ] **8.2** — Créer les écrans de paramètres des profils de connexion, endpoints, authentification, sélection du modèle, test de connexion et préférences de thème.
- [ ] **8.3** — Implémenter la navigation, la création, le chargement, la suppression des conversations et l’historique paginé propres à l’utilisateur connecté.
- [ ] **8.4** — Relier l’envoi des messages et les mises à jour progressives à la persistance, avec un statut clair pour les générations annulées ou interrompues.
- [ ] **8.5** — Gérer les serveurs inaccessibles, authentifications refusées, modèles indisponibles, dépassements de contexte et coupures de connexion avec des retours UI exploitables.
- [ ] **8.6** — Utiliser la bannière et la mascotte existantes et conserver la mention POC expérimental, non prêt pour la production, dans l’application et sa documentation.
- [ ] **8.7** — Vérifier le parcours complet avec deux utilisateurs et les deux serveurs cibles, notamment le rechargement, le redémarrage, l’annulation et la reprise après échec.

**Critère de validation:** Un utilisateur peut se connecter, configurer un modèle local, discuter avec réception progressive, arrêter une réponse et rouvrir ses conversations persistées après un redémarrage.

<a id="milestone-9"></a>

## Milestone 9 — Import de pièces jointes et vision sur images

**Objectif:** Prendre en charge les images via un flux d’import borné et les transmettre uniquement aux modèles de vision compatibles.

**Dépendances:** [M8](#milestone-8).

- [ ] **9.1** — Choisir l’adaptateur initial de stockage binaire et la politique de conservation derrière un port dédié ; séparer originaux, variantes préparées et métadonnées.
- [ ] **9.2** — Implémenter l’import, la récupération et la suppression avec autorisation, cohérence des références en base et nettoyage des fichiers temporaires abandonnés.
- [ ] **9.3** — Unifier la sélection et le glisser-déposer avec aperçus, progression, retrait avant envoi, annulation et erreurs explicites.
- [ ] **9.4** — Valider côté serveur les formats réels, tailles, dimensions et totaux par message ; rendre les limites configurables.
- [ ] **9.5** — Préparer les images avec des dimensions et un encodage adaptés, en préservant leur lisibilité et l’original lorsque la politique de stockage le prévoit.
- [ ] **9.6** — Intégrer les images préparées aux requêtes multimodales de l’adaptateur et respecter les capacités vérifiées du modèle ; ne pas annoncer le support PDF avant l’existence de son traitement.
- [ ] **9.7** — Tester les formats non pris en charge, images trop volumineuses ou invalides, modèles textuels, annulations et accès aux pièces jointes d’autres utilisateurs.
- [ ] **9.8** — Vérifier le chat avec images sur des modèles compatibles dans LM Studio et Unsloth Desktop, et documenter les limites observées et la consommation de ressources.

**Critère de validation:** Un utilisateur peut sélectionner ou déposer des images, les prévisualiser et les retirer, les transmettre à un modèle de vision vérifié et accéder aux pièces jointes persistées uniquement s’il y est autorisé.

<a id="milestone-10"></a>

## Milestone 10 — Livraison NuGet et validation du POC

**Objectif:** Rendre le premier POC reproductible pour les évaluateurs et distribuer les composants réutilisables sous forme de package expérimental.

**Dépendances:** [M9](#milestone-9).

- [ ] **10.1** — Choisir et vérifier l’identité du package NuGet et le versionnement de préversion ; définir la livraison de l’assembly Contracts pour que l’installation du package de composants suffise.
- [ ] **10.2** — Configurer les métadonnées, la licence, la documentation, les symboles, les styles et assets statiques ; vérifier l’absence de secrets et d’adaptateurs serveur dans le package.
- [ ] **10.3** — Créer le package et l’installer dans une application d’exemple indépendante ; vérifier les services personnalisés, substitutions de tokens, remplacement de marque et modes de rendu pris en charge.
- [ ] **10.4** — Étendre la CI aux vérifications pertinentes des unités, adaptateurs, base de données, composants et consommation du package, avec une infrastructure contrôlée.
- [ ] **10.5** — Préparer le workflow de livraison et publier la première préversion NuGet expérimentale uniquement après les vérifications du packaging et les tâches 10.6 à 10.8.
- [ ] **10.6** — Rédiger en anglais et en français des instructions vérifiées de démarrage, configuration, identité, migration, import, intégration, personnalisation des thèmes et diagnostic.
- [ ] **10.7** — Consigner une matrice de compatibilité des versions de serveurs et modèles testées ainsi que les limites connues ; conserver les mentions POC et non prêt pour la production.
- [ ] **10.8** — Exécuter et documenter une validation en environnement neuf : démarrage Aspire, OIDC, persistance, chat texte/vision, isolation, thèmes et consommation externe du NuGet ; mettre à jour les statuts de la roadmap à partir de ces résultats.

**Critère de validation:** Le POC est documenté et validé de façon reproductible, et un package expérimental de composants est publié et consommé par un exemple indépendant. Ce jalon n’établit pas une aptitude à la production.

<a id="milestone-11"></a>

## Milestone 11 — Préparation des PDF pour la vision — itération suivante

**Objectif:** Préparer les pages PDF sélectionnées pour la vision sans consommation mémoire ni taille de requête incontrôlées.

**Dépendances:** [M10](#milestone-10).

- [ ] **11.1** — Évaluer les bibliothèques de rendu selon les plateformes et conteneurs, la licence, les ressources et la qualité ; documenter le moteur retenu et ses prérequis de déploiement.
- [ ] **11.2** — Introduire le port de préparation documentaire et son assembly d’adaptation dédiée sans exposer les types du moteur au domaine ni à l’UI.
- [ ] **11.3** — Valider les PDF, inspecter le nombre de pages et gérer les fichiers corrompus ou protégés par mot de passe avant le rendu.
- [ ] **11.4** — Effectuer le rendu progressivement avec des limites configurables de pages, pixels, dimensions, mémoire, durée et concurrence ; gérer l’annulation et le nettoyage des fichiers temporaires.
- [ ] **11.5** — Ajouter les aperçus et la sélection des pages en préservant leur ordre et numéro ; afficher la progression et expliquer les limites empêchant le traitement.
- [ ] **11.6** — Redimensionner et encoder les pages sélectionnées pour équilibrer lisibilité du texte, taille du transfert et contraintes du modèle ; ne pas confondre poids du fichier et consommation de contexte.
- [ ] **11.7** — Évaluer l’extraction des PDF contenant du texte et définir quand utiliser le texte, les images ou les deux ; garder le RAG et l’indexation hors de ce jalon.
- [ ] **11.8** — Relier les pages préparées aux flux existants de pièces jointes et de vision, avec traçabilité vers le document original et accès autorisé au stockage.
- [ ] **11.9** — Tester les PDF scannés, textuels, volumineux, malformés et protégés ; vérifier la lisibilité et les limites de ressources avec les modèles cibles, puis mettre à jour la documentation dans les deux langues.

**Critère de validation:** Les PDF scannés peuvent être prévisualisés et envoyés sous forme d’images de pages sélectionnées et lisibles, dans les limites configurées ; les PDF textuels, fichiers protégés, échecs et annulations ont un comportement explicite.

## Hors du périmètre engagé

RavenDB et les autres fournisseurs de persistance ne font pas partie de cette roadmap. L’abstraction des repositories préserve la possibilité d’une évolution sans prévoir aujourd’hui d’adaptateur supplémentaire.

Le RAG, l’indexation documentaire, la génération d’embeddings et la recherche vectorielle ne sont pas des engagements de ces milestones. pgvector est installé et activé pour préparer une évolution éventuelle, sans construire ces fonctionnalités à l’avance.

Le choix d’une cible de production et les travaux nécessaires à son exploitation feront l’objet d’un périmètre distinct avant tout déploiement de production. Cette roadmap reste celle d’un POC expérimental.
