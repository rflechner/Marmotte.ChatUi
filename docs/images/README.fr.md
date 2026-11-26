# Identité visuelle

[English](README.md) | Français

La mascotte de Marmotte.ChatUi reprend le personnage fourni par l’utilisateur : pelage turquoise, museau crème, grandes incisives et écharpe rouge à motifs blancs.

## Références originales

Les fichiers locaux ci-dessous sont des copies non modifiées des images jointes par l’utilisateur. Les liens GitHub indiquent la provenance fournie ; les noms de produits visibles dans ces images restent ceux des projets d’origine.

- [Bannière MarmotteDB](references/marmottedb-banner.png) — [source GitHub](https://github.com/rflechner/MarmotteDB/raw/develop/docs/images/github_title_with_mascotte.png).
- [Mascotte expérimentale](references/experimental-proof-of-concept.png) — [source GitHub](https://github.com/rflechner/CodeSemantic4Llm/blob/main/docs/images/experimental-proof-of-concept.png).

## Déclinaisons Marmotte.ChatUi

Deux déclinaisons ont été produites avec l’outil imagegen intégré à partir des références :

- [Bannière Marmotte.ChatUi](marmotte-chatui-banner.png), utilisée dans le README : 1774 × 887 pixels, PNG avec transparence.
- [Mascotte compacte](marmotte-chatui-mascot.png), utilisée par l’hôte web M1 : 1254 × 1254 pixels, PNG avec transparence. Une copie de déploiement inchangée se trouve dans `src/Marmotte.ChatUi.Web/wwwroot/images/` ; la maintenir synchronisée avec cette source. Une copie inchangée pour l’exemple existe aussi dans `samples/Marmotte.ChatUi.Sample/wwwroot/images/` ; un test d’architecture compare les deux copies de déploiement à cette source.

Les [prompts de génération](generation-prompts.fr.md) sont conservés pour permettre de reproduire ou faire évoluer l’intention graphique.

Les déclinaisons conservent l’identité de la mascotte. Vérifier leur lisibilité dans les thèmes clair et sombre lors de leur intégration à l’UI. Le branding doit rester remplaçable dans les widgets distribués en NuGet.

La génération et l’intégration d’une déclinaison ne remplacent pas les fichiers de référence.
