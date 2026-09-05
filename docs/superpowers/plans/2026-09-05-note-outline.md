# Note chapters, folding and navigation implementation plan

**Goal:** Implement the eight features approved in this conversation: heading levels, section folding, outline navigation, fold/unfold all, bookmarks, search reveal, persistence, and confirmed numbered-heading conversion.

**Architecture:** Preserve existing version-1 JSON with optional paragraph metadata. Folded bodies are independent document snapshots behind editor placeholders; saving and text extraction traverse their complete logical content. Group projection changes into WPF undo units. Prevent cut/copy/delete from treating hidden bodies as empty placeholders. UI lives in a focused editor partial class.

**Tech stack:** .NET 10, WPF RichTextBox, existing JSON serializer, xUnit STA tests.

**Constraints:** No commits, pushes or PRs. No edits to the running user's data. Build Release because the running app locks Debug output. Keep original font and line-spacing behavior.

- [x] Core: write failing tests for hierarchy, metadata roundtrip, numbered candidates, hidden content/media; implement DocumentOutline and serializer extensions. Delegated under subagent-driven-development; root handles UI independently.
- [x] Editor: add fold snapshots, heading gutter, outline/info tabs, commands, bookmarks, numbered-candidate checklist and document search. Preserve full content in save, clipboard and undo paths.
- [x] Integration: exercise fold/save/load, nested headings, navigation/search reveal, selection edits, undo/redo, candidate confirmation and legacy editor regressions.
- [x] Review and verification: run Release tests, inspect rendered WPF UI, fix findings, update README and report uncommitted working-tree status.

Ruling: Work in the current checkout as requested by the user's implementation context; no commit or worktree integration is needed. Core and editor files have separate owners.

Verification: full Release test suite passed (173 tests); git diff --check passed. WPF control rendered with the application theme and inspected at 1400x820. Fold button interaction, reopening nested folds, metadata undo, fold undo/redo, numbered confirmation, Enter behavior, search isolation and select-all coverage are included. Final independent review found no remaining material blocker. Changes left uncommitted.

