---
name: implement-ticket
description: implement the ticket that the user has asked for
---

# Implement Ticket

The user will point you to a github issue. Find the next available ticket(issue) to work on.

## Process

### 1. Gather context

Work from whatever is already in the conversation context. If the user passes an issue reference (issue number, URL, or path) as an argument, fetch it from the issue tracker and read its full body and comments.

If no specific ticket is given, find the open ticket with the **lowest issue number** that is not currently blocked by another open ticket in the project (i.e. has no unresolved "blocked by" dependency). Fetch it and read its full body and comments.

If the ticket description is unclear, has ambiguous acceptance criteria, or you are unsure how to proceed at any point, **stop and ask the user clarifying questions** before writing any code.

### 2. Explore the codebase

If you have not already explored the codebase, do so to understand the current state of the code. Issue titles and descriptions should use the project's domain glossary vocabulary, and respect ADRs in the area you're touching.

### 3. Create a branch and make the needed changes

Branch names are **only** the prefix and issue number — no title slug or description:
- `FEAT-{issue number}` — for feature work
- `COW-{issue number}` — for code changes that do not add a feature (refactors, fixes, chores)

Examples: `FEAT-107`, `COW-42`. Never append a slash, title, or description after the number.

Look at the parent issue's branch name to confirm the correct prefix before creating the branch.

Make the needed changes to implement the ticket. Follow the coding standards and best practices of the project.

### 4. Verify before committing

Before committing, run `npx nuxi typecheck` and confirm there are no new TypeScript errors introduced by your changes. Fix any errors before proceeding.

### 5. Commit, push, and open a PR

When the work is done, commit your changes using the GitHub CLI and push the branch to the remote repository. Then, create a pull request and ask the user to review your changes. The pull request should reference the parent issue and include a clear description of the changes made.

Do NOT close or modify any parent issue.
