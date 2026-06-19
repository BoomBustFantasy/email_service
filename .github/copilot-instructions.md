# Agent Instructions for BOOM Fantasy Football App

## 🎯 **Core Requirements**

**🚨 CRITICAL: Always use the available MCP servers:**
- **Nuxt UI MCP Server** (`https://ui.nuxt.com/mcp`) - Use `mcp_nuxt-ui_*` tools to discover and understand available components.
- **Supabase MCP Server** (`https://mcp.supabase.com/mcp`) - Use `mcp_supabase_*` tools for database operations and schema exploration.

**🛡️ Type Safety Requirements:**
- **Strict TypeScript Only** - The `any` type is strictly forbidden.
- **Define Interfaces** - Create proper interfaces in `app/models/` for all data structures.
- **Use `unknown`** - If a type is truly not known, use `unknown` and narrow it.

**🎨 UI Framework Requirements:**
- **ALWAYS use Nuxt UI primitives** - Use `UCard`, `UButton`, `UInput`, `UTable`, etc. as building blocks.
- **Create domain-specific components** that compose Nuxt UI primitives with business logic.
- **NEVER re-implement UI primitives** - Don't create custom buttons, cards, or form inputs when Nuxt UI provides them.
- **Follow the established theme** - Primary color: `indigo`, Neutral: `stone` (defined in `app/app.config.ts`).
- **Use consistent styling patterns** found in existing components.
- **Semantic color usage** - ONLY use `success` color for success states and `warning`/`error` colors for warnings/errors. Use `primary`, `secondary`, `info`, or `neutral` for non-status information.

## 🏗️ **Project Architecture**

**Stack:**
- **Frontend:** Nuxt 4 + Vue 3 + TypeScript
- **UI:** Nuxt UI v4 with Tailwind CSS
- **Database:** Supabase PostgreSQL
- **Authentication:** Supabase Auth (OAuth: Google, Discord)
- **External APIs:** Sleeper (via Composables)
- **Icons:** Lucide icons (`@iconify-json/lucide`)
- **Charts:** Chart.js + vue-chartjs for data visualization

## 🏈 **Sleeper API Integration**

**Base URL:** `https://api.sleeper.app/v1`

**Key Endpoints:**
- `/user/{username}` - Get user by username (returns `user_id`)
- `/user/{user_id}/leagues/nfl/{year}` - Get user's leagues for a season
- `/league/{league_id}` - Get league details
- `/league/{league_id}/rosters` - Get all rosters in a league
- `/league/{league_id}/users` - Get all users in a league
- `/league/{league_id}/traded_picks` - Get traded draft picks
- `/league/{league_id}/drafts` - Get draft information

**🚨 CRITICAL: Sleeper Data Structure:**

**User Object (`SleeperUser`):**
```typescript
{
  user_id: string           // Primary identifier
  username: string          // Display username
  display_name: string      // User's display name
  avatar: string | null
  metadata: {
    team_name: string       // 🔥 TEAM NAME IS HERE - user.metadata.team_name
    allow_pn: string
    mention_pn: string
    avatar: string
  }
}
```

**Roster Object (`SleeperRoster`):**
```typescript
{
  roster_id: number         // Roster identifier (1-12)
  owner_id: string          // References user.user_id
  league_id: string
  players: string[]         // Array of sleeper_id strings
  starters: string[]
  reserve?: string[]
  taxi?: string[]
  settings: {
    wins: number
    losses: number
    fpts: number
    fpts_against: number
    // ... other settings
  }
  metadata: {
    // ❌ NO team_name here - only player nicknames, record, streak
    p_nick_{player_id}: string  // Player nicknames
    record: string
    streak: string
  }
}
```

![alt text](image.png)**🔥 CRITICAL PATTERN: League Context**

The unified `useSleeperLeagueContext(leagueId)` composable owns the full picture
of a Sleeper league — enriched rosters (with `owner`, `teamName`, `picks`,
resolved `players`), a users map, transactions, and `myRoster` (the viewer's
own roster). Pages and components must consume league shape through this
composable; the league store now only holds identity (`sleeperUsername`,
`sleeperUserId`, `selectedLeagueId`).

```typescript
const { league, rosters, users, myRoster, loading } = useSleeperLeagueContext(leagueId)
// Team name is already resolved on enriched rosters:
const teamName = myRoster.value?.teamName ?? `Team ${myRoster.value?.roster_id}`
```

**User Identification Flow:**
1. User enters Sleeper username on profile page
2. Call `/user/{username}` to get `user_id`
3. Save `user_id` to `leagueStore.sleeperUserId`
4. When viewing a league, the context derives `myRoster` from `sleeperUserId`

**File Structure:**
```
app/
├── components/          # Reusable Vue components (PascalCase)
├── composables/         # Shared logic & API clients (useSleeper)
├── models/             # TypeScript interfaces
│   ├── supa/          # Supabase-specific types
│   └── Sleeper.ts     # External API types
├── pages/             # File-based routing (kebab-case, [...] for dynamic)
│   ├── player/[id].vue
│   ├── game/[id].vue
│   └── schedule/[year]/[week].vue
└── types/             # Global type definitions (database.types.ts)
```

## 🗄️ **Database Schema Overview**

**Core Tables:**
- `Players` - Player information with fantasy values (KTC, Fantasy Calc), linked to Teams & Positions.
- `PlayerStats` - Game-by-game statistics with JSON fields: `passing`, `rushing`, `receiving`.
- `Teams` - NFL team data (`full_name`, `abbreviation`).
- `Positions` - Player positions (QB, RB, WR, TE).
- `Schedule` - NFL game schedule with `espn_game_id`, betting lines, implied points.
- `LeagueInfo` - Sleeper league configurations with scoring settings.
- `Trades` / `TradeDetails` - Trade evaluations and player assignments.

**Critical Data Patterns:**
- **PlayerStats JSON fields** require parsing helpers (see `app/utils/playerStatHelpers.ts`):
  - `passing`: `{ passingyards, passingtouchdowns, interceptions, completions, ... }`
  - `rushing`: `{ rushingyards, rushingtouchdowns, rushingattempts, ... }`
  - `receiving`: `{ receivingyards, receivingtouchdowns, receptions, receivingtargets, ... }`
- **Schedule relationships**: `home_team_id` and `away_team_id` foreign keys to Teams.
- **Game linking**: `PlayerStats.espn_game_id` → `Schedule.espn_game_id`.

## 🛠️ **Development Patterns**

### **1. Component Development**
```vue
<script setup>
// ALWAYS use defineOptions for component name
defineOptions({
    name: 'ComponentName'
})

// Props with TypeScript
const props = defineProps({
    playerStats: {
        type: Array,
        default: () => []
    },
    loading: {
        type: Boolean,
        default: false
    }
})
</script>

<template>
    <!-- Use Nuxt UI components exclusively -->
    <UCard variant="subtle">
        <template #header>
            <h3 class="text-lg font-semibold">Title</h3>
        </template>
        
        <!-- Loading state with USkeleton -->
        <div v-if="loading" class="space-y-3">
            <USkeleton v-for="i in 3" :key="i" class="h-16 w-full" />
        </div>
        
        <!-- Content -->
        <div v-else class="space-y-4">
            <!-- ... -->
        </div>
    </UCard>
</template>
```

### **2. Data Fetching**
- **Supabase**: Use `useSupabaseClient` for direct DB access.
- **External APIs**: Encapsulate in composables (`useSleeper.ts`) and use `$fetch`.
- **Caching**: Use `useLazyAsyncData` for SSR-friendly caching.
- **Joins**: Always include proper joins using foreign key relationship syntax (e.g., `Teams!Schedule_home_team_id_fkey`).

### **3. External API Integration**
- **Pattern**: Create a composable (`useX.ts`) and a model file (`models/X.ts`).
- **Implementation**: Use `$fetch` with a base URL.
- **Example**: `useSleeper.ts` for Sleeper API interactions.

### **4. Parsing PlayerStats JSON Fields**
- **ALWAYS use helper functions** from `app/utils/playerStatHelpers.ts` (`getPassingStat`, `getRushingStat`, `getReceivingStat`).
- **Handle both string and object formats** - database has mixed data.
- **Check for placeholder data**: `{ ValueKind: 1 }` means no stats.

### **5. Image Handling**
- **Use `usePlayerImage` composable** for fetching player images.
- **Avoid Supabase image transforms** to prevent extra costs (see `STORAGE_CONFIG` in `app/composables/usePlayerImage.ts`).
- **Handle fallbacks** gracefully using the composable's logic.

### **6. Navigation & Routing**
- Use `navigateTo` for programmatic navigation.
- Use `useRoute` to access params.
- Create clickable links in `UTable` cells using `h('a', ...)` render functions.

### **7. Draft Picks System**

**📋 Understanding Draft Picks:**

Draft picks are fetched from Sleeper's API and managed through a combination of default picks and traded picks.

**Data Structures:**
```typescript
// Sleeper API response (from getTradedPicks)
interface SleeperTradedPick {
  season: string           // Year as string (e.g., "2026")
  round: number           // Draft round (1, 2, 3, etc.)
  roster_id: number       // Original owner's roster_id
  owner_id: number        // Current owner's roster_id
  previous_owner_id: number
}

// Internal representation
interface RosterPick {
  season: number          // Year as number (e.g., 2026)
  round: number          // Draft round (1, 2, 3, etc.)
  original_owner_id: number  // Original owner's roster_id
  owner_id: number          // Current owner's roster_id
  pick_number?: number      // Absolute pick number (e.g., 1, 13, 25)
}
```

**Calculating Roster Picks:**

1. **Generate default picks** for each roster (current year + next 2 years)
2. **Remove traded away picks** where `SleeperTradedPick.roster_id` matches the roster
3. **Add received picks** where `SleeperTradedPick.owner_id` matches the roster
4. **Calculate pick_number** for current season only (when draft order is available)

**Pick Number Calculation:**
```typescript
// Formula: pick_number = (round - 1) * total_rosters + draft_position
// Example: Round 2, Pick 5 in a 12-team league = (2-1) * 12 + 5 = 17

// Get draft order from current season's draft object
const currentDraft = drafts.find(d => d.season === league.season)
const draftPosition = currentDraft?.draft_order?.[roster.owner_id]

if (season === currentSeason && draftPosition) {
  pick.pick_number = (round - 1) * league.total_rosters + draftPosition
}
```

**Displaying Picks:**

- **Assigned picks (has `pick_number`)**: Show as "Round.Pick" format
  - Calculate: `round = Math.floor((pick_number - 1) / total_rosters) + 1`
  - Calculate: `pickInRound = ((pick_number - 1) % total_rosters) + 1`
  - Display: `"1.01"`, `"2.05"`, `"3.12"` (pad pick number to 2 digits)
  
- **Unassigned picks (no `pick_number`)**: Show as "Round X"
  - Display: `"Round 1"`, `"Round 2"`, `"Round 3"`

**Color Coding:**
- Use `primary` color for original picks (`original_owner_id === roster_id`)
- Use `neutral` color for acquired picks (`original_owner_id !== roster_id`)

**Example Implementation:**
```vue
<template>
  <!-- For assigned picks (2026 with draft order) -->
  <UBadge v-if="pick.pick_number" color="primary">
    {{ Math.floor((pick.pick_number - 1) / totalRosters) + 1 }}.{{ 
      String(((pick.pick_number - 1) % totalRosters) + 1).padStart(2, '0') 
    }}
  </UBadge>
  
  <!-- For unassigned picks (future years) -->
  <UBadge v-else color="neutral">
    Round {{ pick.round }}
  </UBadge>
</template>
```

**Reference Implementation:**
See `app/pages/league/[id].vue` (lines 163-235) for the complete pick calculation logic.

## 📝 **Specific Guidelines**

### **Adding New Pages**
1. Create in `app/pages/` following file-based routing.
2. Include authentication check: `if (!user.value) await navigateTo('/login')`.
3. Use `useHead()` for SEO metadata.
4. Implement loading states with `USkeleton` components.
5. Handle error states with `UAlert` or custom empty states.

### **Creating Components**
1. **Check Nuxt UI first** - Use `mcp_nuxt-ui_list_components` to find primitives.
2. **File naming**: PascalCase (e.g., `PlayerStatsTable.vue`).
3. **Always use `defineOptions({ name: 'ComponentName' })`**.
4. **Props**: Use TypeScript-style prop definitions with defaults.

### **Database Operations**
1. **Use Supabase MCP tools** for schema exploration (`mcp_supabase_list_tables`).
2. **RLS is enabled** - all queries respect Row Level Security.
3. **Error handling**: Always check `error` from Supabase queries.

## 🔧 **Development Environment**

**⚡ Development Server:**
- **THE APP IS ALWAYS RUNNING** - Assume `http://localhost:3000` is already active.
- **NEVER run `npm run dev` or start the server** - The developer manages this manually.
- **Hot reload is enabled** - All changes are automatically reflected in the browser.

## ⚠️ **Critical Reminders**

- **THE DEV SERVER IS ALWAYS RUNNING** - Never try to start it.
- **NEVER re-implement Nuxt UI primitives** - Use provided components as building blocks.
- **ALWAYS use MCP servers** for component/database discovery.
- **ALWAYS parse PlayerStats JSON fields** with helper functions.
- **ALWAYS handle loading/error/empty states** (three-state pattern).
- **ALWAYS use TypeScript** with proper types from `app/models/` and `app/types/`. **NEVER use the `any` type.**
