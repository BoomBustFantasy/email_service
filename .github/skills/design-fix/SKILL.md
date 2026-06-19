---
name: design-fix
description: Clean up the design of a given page using Nuxt UI components and adhering to the app's design rules. Use when a page has design inconsistencies, hardcoded styles, or doesn't follow the design system.
---

Look at all of the comopnents of the page and determine if they break any of the following design rules.

Create a list of all design violations and then ask me about them one by one.

## Design Rules
1. Always use Nuxt UI primitives — never re-implement what Nuxt UI provides. If a custom button, card, input, badge, or modal exists that duplicates a Nuxt UI component, replace it.

2. Never use the card header or footer slots of `<UCard>`

3. Never use raw Tailwind palette colors — always use semantic color tokens. All text elements must carry an explicit semantic color class (`text-default`, `text-muted`, `text-tinted`, etc.). A bare `<p>` or `<span>` with no color class will inherit the browser default black and become invisible on dark backgrounds.

4. Each status color has exactly one permitted use case. Ask: *"Did something just happen to trigger this color?"* If no, it's wrong.

   | Color | Only use when... |
   |---|---|
   | `success` | Confirming a completed user action (toast, alert, form saved, purchase confirmed) |
   | `error` | Reporting a failure or destructive action |
   | `warning` | Alerting the user to a recoverable problem requiring attention |
   | `info` | Displaying a system-generated informational alert or banner |

   Badges, labels, and decorative elements that don't describe a system event must use `primary`, `secondary`, or `neutral`.

5. Position colors must use the app's custom colors (qb, rb, wr, te, picks)

6. Do not use icons decoratively. An icon is only justified when it is:
   - In a navigation element, tab, or icon-only button (where it replaces a text label)
   - A status indicator that conveys meaning independent of adjacent text (e.g. a checkmark on a success alert)
   - Part of an empty state illustration

   If removing the icon would leave the UI equally clear, remove it. Never prefix headings, card titles, badge labels, or plain text rows with icons just for visual texture.

7. Always use `variant="subtle"` on `<UCard>`. The default (no variant) renders an elevated surface that clashes with the page background and adjacent subtle cards. Every card on a page must use the same variant to maintain consistent surface contrast.