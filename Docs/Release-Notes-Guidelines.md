# Release notes writing guidelines

Use the owner's revised v1.4.0 notes as the approved editorial reference: a benefit-led
summary, descriptive feature headings, concise related bullets, then upgrade guidance and
organized distribution disclosures. Reuse its style, not its release-specific claims.
These rules apply to new application release notes; do not rewrite published history or
immutable package notes. Catalog publication has its own workflow and approval gates.

## Write for the person using the app

Start with `# Earnings Tracker v{{version}}`, immediately followed by a short **bold summary**
of the main benefits. For substantial releases, add a brief introduction explaining what
the update helps users do. A patch may need only the summary and a few bullets.

Use these sections in order, omitting empty or unnecessary sections:

1. What's New — significant new functionality, with descriptive subheadings for major features.
2. Improvements — refinements to existing features, grouped into concise related bullets.
3. Bug Fixes — only meaningful, verified fixes.
4. Upgrade Information — relevant migration or upgrade guidance and user-impacting data changes.
5. Installation and Distribution Notes — required metadata and disclosures.

Use professional, approachable language and concrete descriptions of what users can do and
why it matters. Explain necessary technical terms. Use bold feature names selectively and
occasional relevant emojis on major feature headings, as in “🎮 Multiplayer Game Filtering”
and “📋 In-App Release History” in the approved rewrite. Do not decorate every section.
Group minor changes without forcing feature subheadings or an introductory paragraph.

Exclude test counts, build logs, internal issue identifiers, commit hashes, invisible
architectural refactors, explanations of unchanged features, repetition, filler, speculative
roadmaps and unsupported performance or reliability claims. Preserve qualifications such as
unknown multiplayer support being excluded from a filter. Never infer migration safety from
wording in an older release; check the actual implementation and verification evidence.

## Authoring and generation

Read this document before preparing the entry in `Resources/ReleaseHistory.json`. That file
remains the factual source; generation formats reviewed prose rather than rewriting it with
an external service. Do not expect formatting checks to establish factual accuracy.

- `summary` supplies the bold opening. An optional `introduction` supplies the following paragraph.
- Sections use `heading` and string-array `changes`, as before. `Added`, `Improved` and `Fixed`
  map to What's New, Improvements and Bug Fixes. Canonical headings also work.
- For a major feature, use a descriptive `heading` and optional `category` set to one of those
  three canonical headings. It becomes a third-level heading inside that category. Sections
  without a recognized category are preserved under Improvements, with their original heading.
- Put migration and backup guidance in `upgradeNotes`; it renders after feature descriptions.
  Explain the user impact and give a backup reminder once, without promising unverified safety.
- `introduction` and `category` are optional editorial metadata. The existing in-app reader
  ignores them; its summary, section headings, changes and upgrade display remain unchanged.
  No source-schema or application change is required for existing entries.

For example, a substantial future entry can include:

```json
{
  "unreleased": true,
  "summary": "A short, verified user benefit.",
  "introduction": "A brief explanation of the substantial changes.",
  "sections": [
    {
      "category": "What's New",
      "heading": "A descriptive feature name",
      "changes": ["Explain what users can do and any important limitation."]
    }
  ]
}
```

Keep `{{version}}`, `{{configuration}}` and `{{changes}}` in
`RELEASE_NOTES_TEMPLATE.md` compatible with the generator. `Tools/Release.ps1` calls the same
`Get-ReleaseNotes` function used by offline previews. It requires this guidance to exist,
checks the title and unresolved placeholders, and emits an editorial-review reminder before
packaging. There is no application-release GitHub Actions workflow; do not add publication
automation to enforce prose style.

## Required disclosures

Keep technical metadata below feature and upgrade descriptions. Use the template's Windows
Security, Installation and Data Preservation, and Verification and Licensing subsections.
Preserve unsigned status, possible unknown-publisher/SmartScreen warnings or security-policy
blocks, enabled Windows protections, and the distinction between checksums and publisher identity.
Keep exact-release publication approval separate from local preparation and the standing
unsigned-release permission.

Retain installation/data separation, legacy-identity migration warnings, verified backups,
exact-candidate verification limitations, accessibility/DPI limitations, third-party licensing,
owner-source rights, catalog/artwork redistribution uncertainty and native-notice completeness
limitations. See [Release.md](Release.md), [Distribution.md](Distribution.md) and
[Database.md](Database.md) for policy and recovery details. Do not remove an internal-sounding
statement from public notes without first verifying that policy permits omission. The approved
v1.4.0 rewrite retains these disclosures; this style change authorizes no policy omissions.
Update candidate-specific verification statements only with evidence and applicable approval.

## Editorial review and offline preview

Before asking the owner to check facts and approve publication, the author or generating agent
must review the complete rendered Markdown and correct the draft:

- **Accuracy:** every claim matches the release scope, implementation and evidence; no invented
  guarantees, features, performance results or verification status.
- **Clarity:** benefit-led summary, plain language, descriptive major-feature headings, detail
  proportional to release size.
- **Redundancy:** no duplicate changes or repeated general backup warnings; omit empty sections.
- **Formatting:** correct version, bold summary immediately below it, ordered sections, working
  Markdown headings/bullets, selective emphasis, no unresolved placeholders.
- **Disclosures:** all applicable security, migration, approval, licensing, redistribution and
  verification notices retained. Preparation does not authorize publishing.

Run from the repository root; this is the complete notes path without building, packaging,
network access, user-data access, tags or publication:

```powershell
. ./Tools/ReleaseHistory.ps1
Get-ReleaseNotes -RepositoryPath (Get-Location).Path -Version 1.4.0 -Configuration Production
```

Use the intended version/configuration for future drafts. Record editorial review and any
verification limits in local preparation evidence under `.local-docs/`. Existing packages and
public notes remain immutable; generating a preview is not a release announcement.
