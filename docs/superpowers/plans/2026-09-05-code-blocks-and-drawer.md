# Code content and navigation drawer responsiveness

User-approved scope: code blocks with C# syntax highlighting, preserved indentation, copy and wrapping; inline code; diagnose and fix main navigation drawer lag. No history/sync/other proposed features. No commits or pushes.

- [x] Persist CodeBlock text/language/wrap and InlineCode semantics; clone and fold without serializing UI editors. Include code in plain-text search/backup. Owner: code_persistence.
- [x] Reproduce unnecessary heading layout work while sibling drawer animates, cache/coalesce only valid updates, verify scroll/resize/zoom still align. Owner: drawer_performance.
- [x] Integrate AvalonEdit code editor, insertion/conversion, toolbar actions, block copy/wrap, keyboard routing and code search. Owner: root.
- [x] Run saved-content/undo/fold/search and actual WPF UI regression tests, inspect render, independent review. Build configuration CodeBlocks avoids locking the running Release/Zoom app. Leave all changes uncommitted.

Design: CodeBlock is a DP-backed BlockUIContainer with no view serialized; views attach only to visible code blocks. Syntax highlighting is presentation-only through AvalonEdit, rather than repeated rich-text formatting edits. Existing rich-text document remains authoritative for chapter structure. InlineCode is a semantic Span with monospace styling. Code text is never executed.

Verification: CodeBlocks build succeeded. All 189 tests passed with test-collection parallelism disabled in the ignored build-output xunit.runner.json. The existing titlebar animation test fails when competing WPF tests run concurrently and passes isolated and serially. RenderTargetBitmap preview inspected at TestResults/code-preview.png. Independent review fixes cover nested input routing, toolbar code undo/redo, and inline font restoration. Drawer regression measured 3,000 redundant position writes reduced to zero across 30 unrelated layout updates for 100 headings; no FPS measurement claimed. git diff --check passed. Changes remain uncommitted.
