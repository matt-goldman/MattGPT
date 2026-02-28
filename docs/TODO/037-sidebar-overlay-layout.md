# 037 — Sidebar Should Overlay Chat Area, Not Push It

**Status:** TODO  
**Sequence:** 37  
**Dependencies:** 022 (chat history sidebar), 012 (chat UI)

## Summary

When the sidebar slides in and out, it pushes the main chat content area, causing the layout to reflow and disturbing the chat experience. The sidebar should instead overlay the chat area (higher z-index, absolute/fixed positioning) so it slides in and out independently without affecting the main chat layout.

## Background

The current sidebar implementation in `Chat.razor` uses a width transition:

```html
<div class="@(_sidebarOpen ? "w-72" : "w-0") transition-all duration-200 overflow-hidden shrink-0">
```

When `_sidebarOpen` toggles, the sidebar's width animates from `0` to `w-72` (18rem). Because the sidebar and chat area are flex siblings, expanding the sidebar **shrinks** the chat area, causing:

- Message text to reflow and re-wrap.
- The chat input area to resize.
- A jarring visual shift during the 200ms transition.
- Potential scroll position changes as content height changes with the reflow.

## Requirements

1. **Overlay behaviour** — The sidebar should slide in on top of the chat area using absolute or fixed positioning with a higher `z-index`, rather than being a flex sibling that affects the chat area's width.

2. **No layout disturbance** — The main chat content area (messages, input, header) should not move, resize, or reflow when the sidebar opens or closes.

3. **Dismiss behaviour** — Consider adding a click-outside-to-close or a semi-transparent backdrop overlay so users can easily dismiss the sidebar. The existing toggle button should still work.

4. **Responsive** — On narrow viewports, the sidebar should overlay the full width. On wider viewports, it should overlay only its own width (e.g. `w-72`).

5. **Smooth animation** — Keep the existing slide transition but apply it via `transform: translateX()` (GPU-accelerated) rather than width animation for smoother performance.

## Acceptance Criteria

- [ ] Opening/closing the sidebar does not cause the chat message area to resize or reflow.
- [ ] The sidebar visually overlays the chat area with a higher z-index.
- [ ] The sidebar can be dismissed by the existing toggle button.
- [ ] The slide animation is smooth and performant.
- [ ] The chat area remains fully usable (scrolling, input) when the sidebar is closed.

## Notes

- A common pattern is `position: fixed; left: 0; top: 0; height: 100%; z-index: 40;` with a `transform: translateX(-100%)` → `translateX(0)` transition.
- Tailwind classes: `fixed inset-y-0 left-0 z-40 w-72 transform -translate-x-full transition-transform duration-200` when closed, removing `-translate-x-full` when open.
- Consider whether the sidebar should push the header/toggle button or only overlay the message area. Keeping the toggle button always visible (outside the overlay) is important for UX.
- A backdrop (`<div class="fixed inset-0 bg-black/20 z-30">`) when the sidebar is open provides good visual affordance and a click-to-close target.
