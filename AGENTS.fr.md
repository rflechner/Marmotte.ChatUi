# Consignes de contribution — Marmotte.ChatUi

[English](AGENTS.md) | Français

## Portée et documents de référence

Ces consignes s’appliquent à tout le dépôt, sauf précision d’un fichier AGENTS.md situé plus près du code concerné.

Lire [ARCHITECTURE.fr.md](ARCHITECTURE.fr.md) avant une modification structurelle. Ce fichier décrit les choix techniques, les frontières et les étapes du produit ; le présent document décrit la manière de concevoir, écrire, documenter et vérifier le code.

Le projet débute. Ne pas présenter les composants, intégrations ou commandes prévues comme déjà disponibles. Mettre la documentation à jour avec l’implémentation réelle.

## SOLID et conception

- Donner à chaque classe, service et composant une responsabilité explicite. Séparer rendu, orchestration métier, transport et persistance.
- Isoler les variations derrière des contrats ciblés : fournisseur de modèle, stockage, identité ou préparation de pièces jointes.
- Préserver le comportement des contrats dans chaque implémentation, notamment les erreurs, l’annulation et les garanties d’accès aux données.
- Préférer plusieurs petites interfaces cohérentes à une interface universelle.
- Faire dépendre les cas d’usage des abstractions internes. Les adaptateurs implémentent ces contrats ; l’application hôte compose les implémentations.
- Introduire les abstractions pour des responsabilités concrètes. Éviter les couches mécaniques, les services génériques et les interfaces ajoutées sans raison.

## Modélisation inspirée du DDD

Utiliser un vocabulaire cohérent autour des conversations, messages, pièces jointes, profils de connexion et préférences utilisateur. Distinguer explicitement le modèle métier du modèle d’IA.

Exprimer les invariants dans les objets qui en sont responsables. Employer agrégats, objets-valeurs et services de domaine lorsqu’ils rendent ces règles plus claires. Garder les règles métier hors des composants Razor, des mappings de base de données et des clients de fournisseurs.

Le domaine reste indépendant de Blazor, du SDK OpenAI, de PostgreSQL, de l’ORM, de Keycloak et d’Aspire. Les cas d’usage orchestrent le domaine et les ports ; ils ne connaissent pas les implémentations techniques.

Ne pas imposer CQRS, médiateur, événements de domaine ou microservices par principe. Les assemblies séparées servent les frontières décrites dans l’architecture et n’impliquent pas des services déployés séparément.

## Frontières et dépendances

- Placer les contrats de repository et les autres ports sortants dans le cœur applicatif, indépendamment des fournisseurs.
- Garder l’implémentation PostgreSQL et ses migrations dans une assembly de persistance distincte.
- Ne pas faire sortir de DbContext, IQueryable, entité ORM, connexion Npgsql ou type pgvector des adaptateurs de persistance.
- Le package OpenAI, s’il est retenu après validation, reste dans l’adaptateur dédié. Aucun type du SDK ne traverse ses contrats publics.
- Les composants Blazor distribués en NuGet n’imposent ni PostgreSQL, ni Keycloak, ni Aspire, ni le SDK OpenAI à l’application qui les consomme.
- Les composants reçoivent des paramètres, événements et contrats de présentation explicites. Ils ne lisent pas directement des secrets ou une configuration globale de l’application complète.
- Enregistrer et assembler les services à la frontière de composition de l’application hôte.

## Style de code

Écrire du code aéré, lisible et facile à parcourir. Employer des noms précis, des fonctions centrées sur une intention et des retours anticipés lorsque cela réduit l’imbrication. Séparer les étapes logiques par des lignes vides ; éviter les expressions compactes qui masquent le comportement.

Conserver un vocabulaire et des conventions C# cohérents dans la solution. Préférer les noms techniques en anglais et respecter la convention Markdown bilingue ci-dessous pour la documentation. Utiliser les annotations de nullabilité et traiter explicitement les cas absents ou invalides.

Pour les entrées-sorties, utiliser les API asynchrones et propager CancellationToken jusqu’aux adaptateurs. Éviter .Result, .Wait() et les opérations longues bloquant le rendu Blazor. Définir les délais et le comportement d’annulation.

Ne pas mélanger des refactorings sans lien avec la fonctionnalité modifiée. Limiter les dépendances aux besoins réels et expliquer leur rôle.

## Documentation détaillée et utile

### Langues et nommage des fichiers Markdown

Chaque document Markdown doit avoir une version principale en anglais nommée `[fichier].md` et une traduction française nommée `[fichier].fr.md` dans le même dossier. Cela inclut README, ARCHITECTURE, AGENTS et la documentation des sous-dossiers.

Mettre à jour les deux versions dans le même changement et conserver le même sens, le même périmètre et les mêmes décisions techniques. Traduire le texte, les titres, les descriptions d’images et les libellés des diagrammes ; préserver les identifiants, commandes, chemins de fichiers, URL et noms exacts des produits.

Ajouter un lien vers l’autre langue près du début de chaque document. Les liens locaux vers d’autres documents Markdown doivent cibler la même langue ; les deux versions partagent les mêmes fichiers de code et images.

### Contenu et maintenance

Documenter les choix, leurs raisons, les invariants et les limites. Les commentaires expliquent une intention ou une contrainte ; ils ne reformulent pas chaque ligne de code.

Documenter les API publiques réutilisables : rôle, paramètres, valeurs de retour, erreurs, annulation, ownership et durée de vie si ces notions s’appliquent. Pour les composants Blazor, décrire paramètres, événements, templates, états, accessibilité et exemples d’intégration.

Maintenir les documents suivants au fil du code :

- README : état réel du projet, prérequis, démarrage et points d’entrée documentaires.
- ARCHITECTURE : responsabilités, dépendances, flux, décisions et limitations.
- Documentation d’intégration : installation du NuGet, personnalisation des thèmes et raccordement aux services de l’hôte.
- Documentation d’exploitation : configuration, migrations, identité, stockage et diagnostic.

Documenter les formats et valeurs de configuration sans publier de secrets. Vérifier les exemples de commandes avant de les présenter comme exécutables.

## UI et design system

Utiliser les tokens sémantiques du design system pour les couleurs, espacements, typographie, rayons, ombres et états interactifs. Éviter les couleurs dispersées dans les composants et les sélecteurs globaux qui modifient l’application consommatrice.

Préserver les modes clair, sombre et système ainsi que les substitutions de tokens par l’hôte, idéalement au niveau d’une instance de widget. Vérifier les contrastes, le clavier, les libellés, le focus et les annonces accessibles des états utiles.

Présenter explicitement les états d’envoi, de réception progressive, d’annulation, de préparation de pièce jointe et d’erreur. Ne pas afficher une pièce jointe comme transmise si le modèle ou l’adaptateur ne la prend pas en charge.

Réutiliser la mascotte fournie pour l’identité du produit. Conserver les sources originales et leur provenance ; préserver les couleurs et détails caractéristiques lors des déclinaisons.

## Connexions, données et fichiers

Valider les adresses, paramètres de modèle et capacités réellement prises en charge. Ne pas assimiler « compatible OpenAI » à une prise en charge intégrale de toutes les API OpenAI. Ne pas figer un port, une version ou une capacité sans vérification.

Distinguer l’authentification OIDC des utilisateurs et les identifiants d’accès aux serveurs de modèles. Une clé n’est obligatoire dans l’UI que si le serveur en exige une ; gérer les contraintes éventuelles du SDK dans l’adaptateur et les documenter.

Vérifier côté serveur que chaque conversation, préférence et pièce jointe appartient à l’utilisateur autorisé. Ne pas faire confiance à un identifiant de propriétaire reçu du navigateur.

Ne pas versionner ou journaliser les secrets. Ne pas journaliser par défaut le texte des conversations, le contenu des pièces jointes ou leurs représentations base64. Les paramètres ordinaires et les secrets suivent des traitements distincts.

Traiter les fichiers importés comme des données non fiables. Vérifier taille, format réel, dimensions et droits d’accès. Borner le rendu PDF en pages, pixels, mémoire, durée et concurrence. Propager l’annulation et nettoyer les fichiers temporaires.

## Vérifications et livraison

Adapter les vérifications au changement : invariants et autorisations dans le cœur, tests de contrat pour les adaptateurs, intégration PostgreSQL/pgvector, vérifications Blazor et tests de consommation du NuGet lorsque ces éléments existent.

Tester les comportements observables et les cas limites importants. Éviter les tests qui recopient l’implémentation. Les tests courants ne doivent pas dépendre d’un serveur personnel ou d’une clé payante.

Pour les changements purement documentaires, relire le contenu, vérifier les liens locaux et les paires linguistiques, puis inspecter le diff suffit. Pour le code, exécuter les vérifications pertinentes disponibles et signaler les vérifications impossibles.

Ne pas annoncer de compilation, de compatibilité, de publication NuGet ou de déploiement qui n’a pas été effectué. Décrire ce qui a changé, pourquoi, ce qui a été vérifié et les limites restantes.
