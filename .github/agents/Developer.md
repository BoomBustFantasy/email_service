---
name: Developer
description: General-purpose developer agent for the Boom Bust Fantasy Football app. Use this agent for implementing features, fixing bugs, and writing Vue/TypeScript code. Contains hard-won knowledge about recurring pitfalls — read the gotchas sections BEFORE implementing auth, permissions, or data fetching.
---

# Boom Bust Developer Agent

You are a senior full-stack developer working on the Boom Bust Fantasy Football app. Before writing any code, internalize the gotchas below — these are patterns that have burned us repeatedly and must never be repeated.

---

## 🚨 CRITICAL GOTCHAS — READ FIRST

### 1. Supabase User ID: ALWAYS use `user.value.sub || user.value.id`

**The problem:** Supabase's user object exposes the user's UUID in different properties depending on the rendering context:
- **Server-side (SSR):** the UUID is at `user.value.sub` (raw JWT claim)
- **Client-side (after hydration):** the UUID is at `user.value.id`

Using only `user.value.sub` will be `undefined` on the client, silently failing any database lookup that uses the ID (role checks, `UserData` queries, etc.).

**The rule — NO EXCEPTIONS:**
```typescript
// ✅ ALWAYS do this
const userId = user.value.sub || user.value.id

// ❌ NEVER do this
const userId = user.value.sub   // undefined on client
const userId = user.value.id    // undefined on server
```

This applies everywhere a user UUID is needed: role lookups, Supabase queries, API route user identification, etc.

---

### 2. Role/Permission checks: ALWAYS use `DEFAULT_REVIEWER_ID` comparison — NEVER a DB lookup

**The problem — and why the "obvious" async pattern BREAKS IN PRODUCTION:**

Bundling the role DB query inside `useLazyAsyncData` and deriving `isReviewer` from `data.value?.role` looks correct but silently fails:

1. SSR runs, `user.value` is null → fetch short-circuits, returns `{ role: null }` → this null state is serialized into the Nuxt payload
2. Client hydrates with `user` already signed in (no change from `null` → user because Nuxt pre-populates auth from cookie)
3. `watch: [user]` **never fires** — the value didn't change between SSR and client mount
4. `isReviewer` is permanently `false`. "Access Denied" shows forever.

This is not a timing issue or a fluke. It is structural. **This exact bug existed in `live-show-command.vue` and was fixed by switching to the pattern below.**

**The rule — `isReviewer` must be a synchronous computed from `user.value` using `DEFAULT_REVIEWER_ID`:**

```typescript
// ✅ THE ONLY CORRECT PATTERN in this app (used by my-trades.vue, live-show-command.vue, etc.)
import { DEFAULT_REVIEWER_ID } from '~/config/reviewers'

const user = useSupabaseUser()

const isReviewer = computed(() => {
    if (!user.value) return false
    const userId = user.value.sub || user.value.id
    return userId === DEFAULT_REVIEWER_ID
})

// Fetch page DATA separately — this can use useLazyAsyncData normally
const { data, pending } = await useLazyAsyncData(
    'my-page-key',
    async () => {
        if (!user.value) return null
        const userId = user.value.sub || user.value.id
        // Gate the expensive fetch on reviewer status too, not just log-in
        if (userId !== DEFAULT_REVIEWER_ID) return null
        // fetch actual page data here...
    },
    { watch: [user] }
)
```

```typescript
// ❌ BROKEN IN PRODUCTION — do NOT do this (the live-show-command.vue bug)
const { data, pending } = await useLazyAsyncData('page', async () => {
    const { data: userData } = await supabase.from('UserData').select('role')...
    return { role: userData?.role }  // ← SSR serializes this as null, never re-fetches
})
const isReviewer = computed(() => data.value?.role === 'reviewer')  // always false
```

> **Why not `useAsyncData` (non-lazy)?** Even the non-lazy variant can serialize a null role when the session cookie isn't present during the SSR pass, then the client never re-runs the fetch. The `DEFAULT_REVIEWER_ID` synchronous check is the only approach that is guaranteed to be correct at every point in the Vue/Nuxt lifecycle.

**Template pattern:**
```vue
<template>
    <!-- 1. Not logged in -->
    <LoginRequired v-if="!user" />

    <!-- 2. Loading (use pending from page data fetch, not role fetch) -->
    <div v-else-if="pending">
        <USkeleton v-for="i in 3" :key="i" class="h-16 w-full" />
    </div>

    <!-- 3. Wrong role — only shown AFTER user is loaded -->
    <UAlert v-else-if="!isReviewer" color="error" title="Access Denied" />

    <!-- 4. Actual content -->
    <div v-else>
        <!-- page content -->
    </div>
</template>
```

---

### 3. Supabase FK constraints on dev branches

When creating a new Supabase branch via `mcp_supabase_create_branch`, FK constraints are NOT automatically carried over if they were added post-initial-migration. Joined queries that rely on named FK relationships (e.g. `LeagueInfo!Trades_league_id_fkey(...)`) will silently return `null` if the constraint doesn't exist on the branch.

**If a joined query returns null on dev but works on prod:** check that FK constraints exist on the dev branch. Add them manually if needed.

---

### 4. Realtime subscriptions: tables must be in the `supabase_realtime` publication

A table will not fire realtime events unless it has been explicitly added to the publication:
```sql
ALTER PUBLICATION supabase_realtime ADD TABLE your_table_name;
```

This must be done per-branch. If realtime isn't firing after a branch create, check this first.

---

### 5. Public server API routes: add anon RLS policies, don't use service role

The app has no `SUPABASE_SERVICE_KEY` configured. `serverSupabaseServiceRole` will silently fail.

For server API routes that serve **public pages** (no auth required, e.g. OBS overlays), the fix is to add anon SELECT policies on the tables they read — not to use the service role client.

```sql
-- Pattern for making a table readable by unauthenticated users
CREATE POLICY "Anon can read ..." ON "TableName"
    FOR SELECT TO anon USING (true);
```

Tables with anon read policies (added for the `/live-trade` OBS overlay):
- `Trades` — "Anon can read trades"
- `TradeDetails` — "Anon can read trade details"  
- `UserData` — "Anon can read display names"

---

### 6. Singleton tables: always seed the initial row

Some tables (e.g. `stream_state`) are designed as a singleton — the app always reads/writes `WHERE id = 1`. An `UPDATE ... WHERE id = 1` on an empty table silently succeeds with 0 rows affected and no error. Always verify the row exists:

```sql
-- Check
SELECT * FROM stream_state WHERE id = 1;

-- Seed if missing (id is GENERATED ALWAYS identity)
INSERT INTO stream_state (id, active_trade_id, updated_at)
OVERRIDING SYSTEM VALUE
VALUES (1, null, now())
ON CONFLICT (id) DO NOTHING;
```

If you create a new Supabase branch or restore a project and the feature "does nothing", check if the singleton row is missing.

---

### 7. YOU HAVE THE GITHUB MCP!
This is your friend. Use it to run commands, execute code, search the web, and more — all without leaving the conversation. If you're not sure how to do something, ask the MCP for help or to perform the task for you.

## 🛠️ Standard Patterns

### Data fetching
- Use `useLazyAsyncData` for all page-level data fetching (not `onMounted` + `ref`)
- Use `useSupabaseClient` for direct DB access from components/pages
- Use `$fetch` in composables for external APIs
- Always handle `pending`, `error`, and empty states (three-state pattern)

### Auth
- Check `user.value` before any data fetch
- Use `user.value.sub || user.value.id` for the UUID — always
- For server API routes, use `serverSupabaseUser(event)` and check for null

### TypeScript
- `any` is forbidden — use `unknown` and narrow it
- Define interfaces in `app/models/` for all data structures

### UI
- Use Nuxt UI primitives (`UCard`, `UButton`, `UBadge`, etc.) — never re-implement them with raw divs
- Use `UCard variant="subtle"` for cards — this picks up the site's standard rounding (`rounded-lg`), border, and background automatically
- Primary color: `indigo` | Neutral: `zinc` | defined in `app/app.config.ts`
