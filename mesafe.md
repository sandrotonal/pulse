# PULSE — PRECISION UI/UX LAYOUT REFINEMENT

Act as a senior UI/UX engineer specializing in premium Apple-inspired desktop interfaces and native WinUI 3 applications.

The current Pulse Dynamic Island is functional, but the expanded player has layout, spacing, alignment, and visual hierarchy problems.

Use the attached screenshots as evidence of the current implementation.

Your task is to refine the existing layout into a polished, cohesive, premium interface inspired by Apple's design principles.

IMPORTANT: This is a targeted layout refinement, not a complete redesign.

## 1. PRESERVE THE EXISTING APPLICATION

Before making changes, inspect the current XAML, styles, control templates, layout containers, and window dimensions.

Preserve:

- The existing Dynamic Island interaction.
- Compact, hover, and expanded states.
- The existing pointer tracking and collapse behavior.
- Media-session integration.
- Play/pause, previous, and next controls.
- Seek functionality.
- Artwork and track metadata.
- Existing working animations.

Do not rewrite the animation engine or media services.

Do not change unrelated files.

Do not replace the current implementation with a new mockup.

## 2. FIX THE EXPANDED PLAYER LAYOUT

The expanded player currently has inconsistent spacing and an unbalanced visual hierarchy.

Reorganize the existing layout into four clear sections:

A. Header
B. Track information and artwork
C. Playback progress
D. Playback controls

Each section must have a clear alignment system and predictable spacing.

Use a consistent layout grid rather than manually positioning every control.

### A. Header

Align the Spotify icon and label on the left.

Align the collapse button, audio-related control, and settings button on the right.

Requirements:

- Vertically center all header elements.
- Use consistent icon-button dimensions.
- Use equal spacing between header controls.
- Keep balanced horizontal padding.
- Prevent icons from appearing too close to the window edge.
- Avoid unnecessary empty space between the brand and controls.

### B. Artwork and track information

Create a balanced horizontal composition.

Place the album artwork on the left and the title and artist information on the right.

Requirements:

- Keep artwork square.
- Use a consistent corner radius.
- Align artwork and metadata vertically.
- Reduce excessive empty space around the artwork.
- Maintain sufficient distance between artwork and text.
- Use a strong title hierarchy and muted secondary artist text.
- Prevent title and artist labels from clipping or overlapping.
- Handle missing artwork using a deliberate placeholder design.

Do not stretch artwork to fit a rectangular container.

### C. Seek bar and timestamps

The progress bar must be visually connected to the track information.

Requirements:

- Align the start and end of the seek bar consistently with the metadata column.
- Place elapsed time and total duration directly beneath the track.
- Keep both timestamps aligned with the progress bar's endpoints.
- Use consistent vertical spacing.
- Keep the progress thumb centered on its track.
- Preserve the existing seek functionality.
- Avoid excessive gaps between the progress bar and timestamps.

### D. Playback controls

Create one balanced horizontal control row.

Order:

Shuffle, backward 15 seconds, previous, play/pause, next, forward 15 seconds, favorite.

Only include controls already supported by the application.

Requirements:

- Keep all controls vertically centered.
- Establish consistent spacing between adjacent controls.
- Give play/pause the strongest visual emphasis.
- Keep secondary controls visually quieter.
- Make the primary play/pause button proportionate to the other controls.
- Ensure the row has balanced left and right margins.
- Prevent buttons from touching the panel edges.
- Ensure all controls have comfortable hit areas.
- Avoid inconsistent circular button sizes.

The layout must remain balanced even when optional controls are unavailable.

## 3. ESTABLISH A CONSISTENT SPACING SYSTEM

Audit all margins, paddings, row gaps, column gaps, and control dimensions.

Replace arbitrary values with a small, consistent spacing scale.

Use approximately 4, 8, 12, 16, 20, and 24 logical-pixel increments where appropriate.

These are design guidelines, not mandatory values for every element.

Avoid unnecessary nested padding that creates excessive whitespace.

Do not compress everything simply to make the player smaller.

The objective is balanced proportions, not maximum density.

## 4. REFINE THE COMPACT ISLAND

The compact state must use the same visual design language as the expanded player.

Requirements:

- Consistent capsule geometry.
- Consistent background and border treatment.
- Balanced artwork-to-text proportions.
- Vertically centered metadata.
- Correctly aligned play/pause control.
- Predictable horizontal padding.
- Graceful handling of long track titles.
- A proper placeholder when artwork is unavailable.

Ensure the compact state does not look like a different application.

## 5. PRESERVE THE COLLAPSE AND EXPANSION ANIMATION

The existing pointer-driven Dynamic Island behavior must remain intact.

Do not introduce new windows or duplicate player components.

Do not alter the existing animation timings unless a specific layout dependency requires it.

Ensure the refined layout supports smooth transitions between compact and expanded states.

The artwork, text, seek bar, and controls must not jump unexpectedly during expansion or collapse.

If layout changes reveal an existing transition problem, identify it separately before modifying animation code.

## 6. RESPONSIVE BEHAVIOR

Use WinUI 3 layout containers and appropriate sizing rules.

Avoid hardcoded coordinates for every element.

Keep the layout stable under Windows display scaling.

The expanded player should maintain its intended proportions without clipping or overlap.

Verify the actual application at its existing window dimensions before changing the overall size.

Do not enlarge the window just to hide spacing problems.

## 7. IMPLEMENTATION RULES

1. Inspect the current implementation first.
2. Identify the exact layout defects in the screenshots.
3. List the files that require modification.
4. Change only the relevant XAML and styling, plus minimal supporting code if required.
5. Avoid broad refactoring.
6. Build the solution.
7. Inspect the rendered result.
8. Compare it against the screenshots and verify that existing interactions still work.

Do not claim that the UI is fixed based only on code inspection.

If the application cannot be run, report that limitation.

## 8. ACCEPTANCE CRITERIA

- Header controls are consistently aligned.
- Artwork and metadata form a balanced composition.
- Seek bar and timestamps have consistent spacing.
- Playback controls share one clear alignment axis.
- Primary and secondary controls have appropriate visual hierarchy.
- No overlapping, clipping, or unnecessary whitespace.
- Compact and expanded states share a coherent visual identity.
- Existing media functionality remains intact.
- Existing pointer-driven transitions remain functional.
- The project builds successfully.
- Actual runtime behavior is verified when possible.

FINAL REQUIREMENT:

Make the smallest set of changes necessary to achieve a polished, Apple-inspired layout.

Do not add new features.

Do not redesign unrelated components.

Do not sacrifice working functionality for visual changes.

Prioritize alignment, spacing, proportions, hierarchy, and consistency.