# Pelican Desktop Pet Implementation Plan

**Goal:** Ship the approved red-scarf, green-bicycle pelican with six animated actions and desktop interaction.

**Architecture:** Five independent DesktopPet layer projects in the existing UI/src layout. Pure motion state in Domain; settings use cases and storage port in Application; atomic JSON settings in Infrastructure; public settings contract in Contracts. UI owns vector drawing, animation, native positioning, interaction and the settings page. Host composes and restores the feature.

**Approved design:** Cycling pelican reference and six-action concept sheet from this conversation. Recreate articulated vector parts for real wheel/pedal/scarf motion, not a sliding static concept image. No AI chat, feeding economy, or todo integration in this release.

**Behavior:** Slow riding, temporary boost, braking/edge reversal, click reaction, dragging/landing and idle sleeping. Right-click menu, scale controls, visibility and location persistence. Starts disabled until enabled. App exit closes the pet. No focus stealing; transparent background. Recover invalid/offscreen saved positions.

**Execution:** Continue in the current workspace; preserve existing changes and user data. No commits or pushes authorized.

- [x] Domain/Application/Infrastructure: add failing behavior and persistence tests, implement state transitions and validated settings with safe writes.
- [x] UI: articulated pelican renderer, transparent pet window, native screen bounds, settings/preview page. Verify six poses and real interactions in an isolated smoke harness.
- [x] Host: navigation registration, startup restore and shutdown cleanup; update architecture checks for fifth module.
- [x] Validate: module and full regression suites, isolated startup/UI smoke, render poses for visual inspection; document entry and limitations.

## Verification results
- Final full suite: 289 passed, no failures or skips.
- Normal desktop build succeeded; two transient MSBuild copy-lock warnings resolved on retry.
- Isolated startup smoke: nine routes, all six rendered poses, actual pet show/animation/resize/shutdown/restore/hide passed.
- WPF tests: transparent articulated frames and native no-activate style/offscreen recovery passed.
- Mixed-DPI multi-monitor dragging still needs manual hardware acceptance.
- No commits or pushes; user live data was not used for validation.
