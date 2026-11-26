# Visual identity

English | [Français](README.fr.md)

The Marmotte.ChatUi mascot reuses the character supplied by the user: turquoise fur, a cream muzzle, large incisors, and a red scarf with white patterns.

## Original references

The local files below are unmodified copies of the images attached by the user. The GitHub links record the supplied provenance; product names visible in these images still refer to their original projects.

- [MarmotteDB banner](references/marmottedb-banner.png) — [GitHub source](https://github.com/rflechner/MarmotteDB/raw/develop/docs/images/github_title_with_mascotte.png).
- [Experimental mascot](references/experimental-proof-of-concept.png) — [GitHub source](https://github.com/rflechner/CodeSemantic4Llm/blob/main/docs/images/experimental-proof-of-concept.png).

## Marmotte.ChatUi variants

Two variants were created from the references using the built-in imagegen tool:

- [Marmotte.ChatUi banner](marmotte-chatui-banner.png), used in the README: 1774 × 887 pixels, PNG with transparency.
- [Compact mascot](marmotte-chatui-mascot.png), used by the M1 web host: 1254 × 1254 pixels, PNG with transparency. An unchanged deployment copy lives in `src/Marmotte.ChatUi.Web/wwwroot/images/`; keep it synchronized with this source. An unchanged sample copy also lives in `samples/Marmotte.ChatUi.Sample/wwwroot/images/`; an architecture test checks both deployment copies against this source.

The [generation prompts](generation-prompts.md) are preserved so the visual intent can be reproduced or refined.

The variants preserve the mascot's identity. Check their readability in light and dark themes when integrating them into the UI. Branding must remain replaceable in widgets distributed through NuGet.

Generating and integrating a variant does not replace the reference files.
